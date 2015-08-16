using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Rename;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Asyncify
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AsyncifyCodeFixProvider)), Shared]
    public class AsyncifyCodeFixProvider : CodeFixProvider
    {
        private const string Title = "Asyncify Method";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(AsyncifyAnalyzer.DiagnosticId);

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
                var declaration = root.FindNode(diagnosticSpan) as MethodDeclarationSyntax;

                if (declaration?.Body == null || !declaration.Body.Statements.Any())
                    return;

                // Register a code action that will invoke the fix.
                context.RegisterCodeFix(
                    CodeAction.Create(
                        Title,
                        c => AsyncifyMethod(context.Document, declaration, c),
                        Title),
                    diagnostic);
            }
        }

        private async Task<Solution> AsyncifyMethod(Document document, MethodDeclarationSyntax method, CancellationToken cancellationToken)
        {
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken);

            var invocations = GetInvocationsToFix(method, semanticModel);
            var variableAcesses = GetVariableAcessesToFix(method, semanticModel);
            if (!invocations.Any() && !variableAcesses.Any())
            {
                return document.Project.Solution;
            }

            var returnTypeSymbol = semanticModel.GetDeclaredSymbol(method).ReturnType;

            syntaxRoot = FixInvocations(ref method, semanticModel, syntaxRoot);
            syntaxRoot = FixVariables(ref method, semanticModel, syntaxRoot);
            syntaxRoot = FixReturnTypeAndModifiers(ref method, returnTypeSymbol, syntaxRoot, semanticModel);

            return document.Project.Solution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);
        }

        private static SyntaxNode FixVariables(ref MethodDeclarationSyntax method, SemanticModel semanticModel, SyntaxNode syntaxRoot)
        {
            var variableAccessesToFix = GetVariableAcessesToFix(method, semanticModel);
            foreach (var variableAccess in variableAccessesToFix)
            {
                var trackedRoot = syntaxRoot.TrackNodes(method, variableAccess);
                var trackedVariableAccess = trackedRoot.GetCurrentNode(variableAccess);
                var newAccess = AwaitExpression(variableAccess.Expression.WithLeadingTrivia(Space));
                syntaxRoot = trackedRoot.ReplaceNode(trackedVariableAccess, newAccess);
                method = syntaxRoot.GetCurrentNode(method);
            }

            return syntaxRoot;
        }

        private static SyntaxNode FixInvocations(ref MethodDeclarationSyntax method, SemanticModel semanticModel, SyntaxNode syntaxRoot)
        {
            foreach (var invocation in GetInvocationsToFix(method, semanticModel))
            {
                var trackedRoot = syntaxRoot.TrackNodes(method, invocation);
                SyntaxNode oldNode = trackedRoot.GetCurrentNode(invocation);
                SyntaxNode newNode = AwaitExpression(invocation.WithLeadingTrivia(Space));

                SyntaxNode node = oldNode.Parent;
                while (node != null)
                {
                    var memberAccess = node as MemberAccessExpressionSyntax;
                    var identifierName = memberAccess?.Name as IdentifierNameSyntax;
                    if (identifierName != null && identifierName.Identifier.ValueText == nameof(Task<int>.Result))
                    {
                        newNode = memberAccess.Expression.ReplaceNode(oldNode, newNode);
                        oldNode = memberAccess;
                        break;
                    }

                    node = node.Parent;
                }

                syntaxRoot = trackedRoot.ReplaceNode(oldNode, newNode);
                method = syntaxRoot.GetCurrentNode(method);
            }
            return syntaxRoot;
        }

        private static SyntaxNode FixReturnTypeAndModifiers(ref MethodDeclarationSyntax method, ITypeSymbol returnTypeSymbol, SyntaxNode syntaxRoot, SemanticModel semanticModel)
        {
            TypeSyntax typeSyntax;
            if (returnTypeSymbol.SpecialType == SpecialType.System_Void)
            {
                typeSyntax = ParseTypeName(typeof (Task).FullName);
            }
            else
            {
                typeSyntax = ParseTypeName(typeof(Task).FullName + "<" + method.ReturnType.WithoutTrivia().ToFullString() + ">");
            }

            var newMethod = method
                    .WithReturnType(typeSyntax.WithTrailingTrivia(Space))
                    .AddModifiers(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space));

            syntaxRoot = syntaxRoot.ReplaceNode(method, newMethod);

            method = newMethod;
            return syntaxRoot;
        }

        private static IEnumerable<MemberAccessExpressionSyntax> GetVariableAcessesToFix(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            var variableAnalyzer = new VariableAccessAnalyzer(semanticModel);
            return method.Body
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Where(x => variableAnalyzer.ShouldUseTap(x));
        }

        private static IEnumerable<InvocationExpressionSyntax> GetInvocationsToFix(MethodDeclarationSyntax method, SemanticModel semanticModel)
        {
            var invocationAnalyzer = new InvocationAnalyzer(semanticModel);
            return method.Body
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(x => invocationAnalyzer.ShouldUseTap(x));
        }
    }
}