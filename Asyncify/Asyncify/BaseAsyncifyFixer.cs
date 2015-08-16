using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
            syntaxRoot = FixReturnTypeAndModifiers(ref method, returnTypeSymbol, syntaxRoot);
            
            return document.Project.Solution.WithDocumentSyntaxRoot(document.Id, syntaxRoot);
        }

        private static SyntaxNode FixReturnTypeAndModifiers(ref MethodDeclarationSyntax method, ITypeSymbol returnTypeSymbol, SyntaxNode syntaxRoot)
        {
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

            method = newMethod;
            return syntaxRoot;
        }

        protected abstract SyntaxNode ApplyFix(ref MethodDeclarationSyntax method, TSyntaxType node, SyntaxNode syntaxRoot);
    }
}