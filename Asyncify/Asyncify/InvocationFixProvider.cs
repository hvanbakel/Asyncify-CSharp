using System.Collections.Immutable;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Asyncify
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InvocationFixProvider)), Shared]
    public class InvocationFixProvider : BaseAsyncifyFixer<InvocationExpressionSyntax>
    {
        protected override string Title => "Asyncify Method";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(InvocationAnalyzer.DiagnosticId);

        protected override SyntaxNode ApplyFix(ref MethodDeclarationSyntax method, InvocationExpressionSyntax invocation, SyntaxNode syntaxRoot)
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

                    if (memberAccess.Parent is MemberAccessExpressionSyntax)
                    {
                        newNode = ParenthesizedExpression((ExpressionSyntax) newNode);
                    }

                    oldNode = memberAccess;
                    break;
                }

                node = node.Parent;
            }

            syntaxRoot = trackedRoot.ReplaceNode(oldNode, newNode);
            method = syntaxRoot.GetCurrentNode(method);
            return syntaxRoot;
        }
    }
}