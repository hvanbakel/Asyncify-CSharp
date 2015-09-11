using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Asyncify
{
    public abstract class BaseAsyncifyFixer<TSyntaxType> : CodeFixProvider
        where TSyntaxType : SyntaxNode
    {
        protected abstract string Title { get; }

        public sealed override FixAllProvider GetFixAllProvider()
        {
            return WellKnownFixAllProviders.BatchFixer;
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);

            foreach (var diagnostic in context.Diagnostics)
            {
                var diagnosticSpan = diagnostic.Location.SourceSpan;
                var nodeToFix = root.FindNode(diagnosticSpan) as TSyntaxType;

                // Register a code action that will invoke the fix.
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        c => AsyncifyMethod(context.Document, nodeToFix, c),
                        Title),
                    diagnostic);
            }
        }

        private async Task<Solution> AsyncifyMethod(Document document, TSyntaxType nodeToFix, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            
            var method = nodeToFix.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            var returnTypeSymbol = semanticModel.GetDeclaredSymbol(method).ReturnType;

            syntaxRoot = ApplyFix(ref method, nodeToFix, syntaxRoot);

            var lambda = nodeToFix.FirstAncestorOrSelf<LambdaExpressionSyntax>();
            Solution newSolution;
            if (lambda == null)
            {
                syntaxRoot = FixMethodSignature(ref method, returnTypeSymbol, syntaxRoot);

                newSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);

                var newDocument = newSolution.GetDocument(document.Id);
                newSolution = await FixCallingMembersAsync(newSolution, newDocument, method, cancellationToken);
            }
            else
            {
                newSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);
            }

            return newSolution;
        }

        private static async Task<Solution> FixCallingMembersAsync(Solution solution, Document newDocument, MethodDeclarationSyntax method, CancellationToken cancellationToken)
        {
            var methodSymbol = await FindMethodSymbolInSolution(newDocument, method, cancellationToken);

            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, solution, cancellationToken);
            var callersByDocumentId = callers.SelectMany(x => x.Locations).GroupBy(x => solution.GetDocumentId(x.SourceTree));

            foreach (var documentId in callersByDocumentId)
            {
                var document = solution.GetDocument(documentId.Key);
                if (document == null)
                {
                    continue;
                }

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var callerRoot = await document.GetSyntaxRootAsync(cancellationToken);
                var referencesInDocument = documentId.Select(x => callerRoot.FindNode(x.SourceSpan)).ToArray();
                var returnTypeSymbols = referencesInDocument
                    .Select(x => x.FirstAncestorOrSelf<MethodDeclarationSyntax>())
                    .Select(x => semanticModel.GetDeclaredSymbol(x).ReturnType).ToArray();

                var numInvocations = referencesInDocument.Length;

                //Iterate the invocations
                for (var i = 0; i < numInvocations; i++)
                {
                    //Track all nodes in use
                    var trackedRoot = callerRoot.TrackNodes(referencesInDocument);
                    //Get the current node from the tracking system
                    var callingNode = trackedRoot.GetCurrentNode(referencesInDocument[i]);

                    var invocation = callingNode.Parent as InvocationExpressionSyntax;
                    if (invocation == null)
                        continue;//Broken code case

                    //If it's already in an await expression, leave it
                    if (invocation.FirstAncestorOrSelf<AwaitExpressionSyntax>() == null)
                    {
                        var fixProvider = new InvocationFixProvider();
                        var lambda = invocation.FirstAncestorOrSelf<SimpleLambdaExpressionSyntax>();
                        var tempMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                        var hasOutOrRefParameters = tempMethod.HasOutOrRefParameters();
                        if (hasOutOrRefParameters)
                        {
                            callerRoot = WrapInvocationInResultCall(invocation, trackedRoot);
                        }
                        else
                        {
                            callerRoot = fixProvider.ApplyFix(ref tempMethod, invocation, trackedRoot);
                        }

                        //Check for a lambda, if we're refactoring a lambda, we don't need to update the signature of the method
                        if (lambda == null && !hasOutOrRefParameters)
                        {
                            callerRoot = FixMethodSignature(ref tempMethod, returnTypeSymbols[i], callerRoot);
                        }

                        referencesInDocument = callerRoot
                            .GetCurrentNodes<SyntaxNode>(referencesInDocument)
                            .ToArray();
                    }
                }

                solution = solution.WithDocumentSyntaxRoot(document.Id, callerRoot);

                solution = await RecurseUpCallTree(solution, referencesInDocument, document, cancellationToken);
            }


            return solution;
        }

        private static SyntaxNode WrapInvocationInResultCall(InvocationExpressionSyntax invocation, SyntaxNode trackedRoot)
        {
            var newMemberAccess = MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, invocation,
                IdentifierName("Result"));

            return trackedRoot.ReplaceNode(invocation, newMemberAccess);
        }

        private static async Task<Solution> RecurseUpCallTree(Solution solution, SyntaxNode[] referencesInDocument,
            Document document, CancellationToken cancellationToken)
        {
            var refactoredMethods = referencesInDocument.Select(x => x.FirstAncestorOrSelf<MethodDeclarationSyntax>()).ToArray();
            foreach (var refactoredMethod in refactoredMethods.Where(x => !x.HasOutOrRefParameters()))
            {
                solution = await FixCallingMembersAsync(solution, solution.GetDocument(document.Id), refactoredMethod, cancellationToken);
            }
            return solution;
        }

        private static async Task<IMethodSymbol> FindMethodSymbolInSolution(Document newDocument, MethodDeclarationSyntax method, CancellationToken cancellationToken)
        {
            var syntaxTree = await newDocument.GetSyntaxTreeAsync(cancellationToken);
            var compilation = await newDocument.Project.GetCompilationAsync(cancellationToken);
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = await syntaxTree.GetRootAsync(cancellationToken);
            var node = root.FindNode(method.GetLocation().SourceSpan);
            var methodSymbol = semanticModel.GetDeclaredSymbol(node) as IMethodSymbol;
            return methodSymbol;
        }

        private static SyntaxNode FixMethodSignature(ref MethodDeclarationSyntax method, ITypeSymbol returnTypeSymbol, SyntaxNode syntaxRoot)
        {
            if (method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                return syntaxRoot;
            }

            TypeSyntax typeSyntax;
            if (returnTypeSymbol.SpecialType == SpecialType.System_Void)
            {
                typeSyntax = ParseTypeName(typeof(Task).FullName);
            }
            else
            {
                typeSyntax = ParseTypeName(typeof(Task).FullName + "<" + method.ReturnType.WithoutTrivia().ToFullString() + ">");
            }

            var newMethod = method
                    .WithReturnType(typeSyntax.WithTrailingTrivia(Space))
                    .AddModifiers(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space));


            syntaxRoot = syntaxRoot.ReplaceNode(method, newMethod);
            method = syntaxRoot.FindNode(method.GetLocation().SourceSpan) as MethodDeclarationSyntax;
            return syntaxRoot;
        }

        protected abstract SyntaxNode ApplyFix(ref MethodDeclarationSyntax method, TSyntaxType node, SyntaxNode syntaxRoot);
    }
}