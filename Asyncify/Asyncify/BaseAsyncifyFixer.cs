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
            var newMethod = this.ApplyFix(method, nodeToFix, syntaxRoot);

            var lambda = nodeToFix.FirstAncestorOrSelf<LambdaExpressionSyntax>();
            Solution newSolution = document.Project.Solution;
            if (lambda == null)
            {

                document = newSolution.GetDocument(document.Id);
                var matchingMembers = await FindImplementedInterfaceMembers(document, method);
                foreach (var matchingMember in matchingMembers)
                {
                    var interfaceDoc = document.Project.Solution.GetDocument(matchingMember.SyntaxTree);
                    newSolution = await FixSignatureAndCallers(matchingMember, interfaceDoc, returnTypeSymbol, cancellationToken);
                }

                document = newSolution.GetDocument(document.Id);
                syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                method = RefindMethod(method, syntaxRoot);
                syntaxRoot = syntaxRoot.ReplaceNode(method, newMethod);
                newSolution = newSolution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);

                document = newSolution.GetDocument(document.Id);
                syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
                method = RefindMethod(method, syntaxRoot);
                newSolution = await FixSignatureAndCallers(method, document, returnTypeSymbol, cancellationToken);
            }
            else
            {
                syntaxRoot = syntaxRoot.ReplaceNode(method, newMethod);
                newSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);
            }

            return newSolution;
        }

        private static async Task<MethodDeclarationSyntax[]> FindImplementedInterfaceMembers(Document document,
            MethodDeclarationSyntax method)
        {
            var semanticModel = await document.GetSemanticModelAsync();
            var syntaxRoot = await document.GetSyntaxRootAsync();

            method = RefindMethod(method, syntaxRoot);
            var symbol = semanticModel.GetDeclaredSymbol(method);
            var type = symbol.ContainingType;
            var matchingMembers = type.AllInterfaces
                .SelectMany(x => x.GetMembers(method.Identifier.ValueText).OfType<IMethodSymbol>())
                .Select(x =>
                {
                    var implementedSymbol = type.FindImplementationForInterfaceMember(x);
                    if (implementedSymbol != null)
                    {
                        return x;
                    }
                    return null;
                })
                .Where(x => x != null)
                .Select(x => x.DeclaringSyntaxReferences.First().GetSyntax() as MethodDeclarationSyntax)
                .ToArray();
            return matchingMembers;
        }

        private static async Task<Solution> FixSignatureAndCallers(MethodDeclarationSyntax method, Document document, ITypeSymbol returnTypeSymbol, CancellationToken cancellationToken)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);
            method = RefindMethod(method, syntaxRoot);

            IEnumerable<IGrouping<DocumentId, Location>> callersByDocumentId = 
                await GetCallerSites(document.Project.Solution, document, method, cancellationToken);

            syntaxRoot = FixMethodSignature(ref method, returnTypeSymbol, syntaxRoot);
            
            var newSolution = document.Project.Solution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);
                
            newSolution = await FixCallingMembersAsync(newSolution, callersByDocumentId, cancellationToken);
            return newSolution;
        }

        private static async Task<IEnumerable<IGrouping<DocumentId, Location>>> GetCallerSites(Solution solution, Document newDocument, MethodDeclarationSyntax method, CancellationToken cancellationToken)
        {
            var methodSymbol = await FindMethodSymbolInSolution(newDocument, method, cancellationToken);

            var callers = await SymbolFinder.FindCallersAsync(methodSymbol, solution, cancellationToken);
            var callersByDocumentId = callers.SelectMany(x => x.Locations).GroupBy(x => solution.GetDocumentId(x.SourceTree));
            return callersByDocumentId;
        }

        private static async Task<Solution> FixCallingMembersAsync(Solution solution, IEnumerable<IGrouping<DocumentId, Location>> callersByDocumentId, CancellationToken cancellationToken)
        {
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

                    var invocation = callingNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                    if (invocation == null)
                        continue;//Broken code case

                    //If it's already in an await expression, leave it
                    if (invocation.FirstAncestorOrSelf<AwaitExpressionSyntax>() == null)
                    {
                        var fixProvider = new InvocationFixProvider();
                        var lambda = invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>();
                        var tempMethod = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                        var hasOutOrRefParameters = tempMethod.HasOutOrRefParameters();
                        if (hasOutOrRefParameters)
                        {
                            callerRoot = WrapInvocationInResultCall(invocation, trackedRoot);
                        }
                        else
                        {
                            var newMethod = fixProvider.ApplyFix(tempMethod, invocation, trackedRoot);
                            callerRoot = trackedRoot.ReplaceNode(tempMethod, newMethod);
                            tempMethod = RefindMethod(tempMethod, callerRoot);
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
                IEnumerable<IGrouping<DocumentId, Location>> callersByDocumentId =
                    await GetCallerSites(solution, solution.GetDocument(document.Id), refactoredMethod, cancellationToken);

                solution = await FixCallingMembersAsync(solution, callersByDocumentId, cancellationToken);
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

        private static ParameterSyntax[] _cancellationToken = ParseParameterList("(CancellationToken cancellationToken = default)").Parameters.ToArray();

        private static SyntaxNode FixMethodSignature(ref MethodDeclarationSyntax method, ITypeSymbol returnTypeSymbol, SyntaxNode syntaxRoot)
        {
            if (method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                return syntaxRoot;
            }

            TypeSyntax typeSyntax;
            if (returnTypeSymbol.SpecialType == SpecialType.System_Void)
            {
                typeSyntax = ParseTypeName(typeof(Task).Name);
            }
            else
            {
                typeSyntax = ParseTypeName(typeof(Task).Name + "<" + method.ReturnType.WithoutTrivia().ToFullString() + ">");
            }

            var newMethod = method
                    .WithReturnType(typeSyntax.WithTrailingTrivia(Space))
                    .AddParameterListParameters(_cancellationToken);

            if (method.FirstAncestorOrSelf<InterfaceDeclarationSyntax>() == null)
            {
                newMethod = newMethod.AddModifiers(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space));
            }

            //var trackedRoot = syntaxRoot.TrackNodes(method);
            //var tempRoot = trackedRoot.ReplaceNode(typeSyntax, typeSyntax.WithTrailingTrivia(Space));
            //var temp = tempRoot.GetCurrentNode(method);

            syntaxRoot = syntaxRoot.ReplaceNode(method, newMethod);
            
            // [iouris] - Adding this breaks refactoring of callers
            //    syntaxRoot = EnsureUsing((CompilationUnitSyntax)syntaxRoot, "System.Threading");
            method = RefindMethod(method, syntaxRoot);
            return syntaxRoot;
        }

        static SyntaxNode EnsureUsing(CompilationUnitSyntax syntaxRoot, string @namespace)
        {

            // Iterate through our usings to see if we've got what we need...
            if (!syntaxRoot.Usings.Any(u => u.Name.ToString() == @namespace))
            {
                // Create and add the using statement
                // UNDONE [iouris] - hardcoding the namespace
                var usingStatement = UsingDirective(QualifiedName(IdentifierName("System"), IdentifierName("Threading")));
                syntaxRoot = syntaxRoot.AddUsings(usingStatement);
            }

            return syntaxRoot;

        }

        private static MethodDeclarationSyntax RefindMethod(MethodDeclarationSyntax method, SyntaxNode syntaxRoot)
        {
            return syntaxRoot
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Single(x =>
                    x.Identifier.ValueText == method.Identifier.ValueText &&
                    SameParameters(method, x));
        }

        private static bool SameParameters(MethodDeclarationSyntax originalMethod, MethodDeclarationSyntax newMethod)
        {
            return newMethod.ParameterList == originalMethod.ParameterList || newMethod.ParameterList.ToFullString() == originalMethod.ParameterList.ToFullString()
                || newMethod.ParameterList.ToFullString() == originalMethod.AddParameterListParameters(_cancellationToken).ParameterList.ToFullString();
        }

        protected abstract SyntaxNode ApplyFix(MethodDeclarationSyntax method, TSyntaxType node, SyntaxNode syntaxRoot);
    }
}