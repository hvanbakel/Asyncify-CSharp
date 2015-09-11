using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Asyncify
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(VariableAccessFixProvider)), Shared]
    public class VariableAccessFixProvider : BaseAsyncifyFixer<MemberAccessExpressionSyntax>
    {
        protected override string Title => "Asyncify Variable Access";

        public sealed override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(VariableAccessAnalyzer.DiagnosticId);

        protected override SyntaxNode ApplyFix(ref MethodDeclarationSyntax method, MemberAccessExpressionSyntax variableAccess, SyntaxNode syntaxRoot)
        {
            ExpressionSyntax newAccess = AwaitExpression(variableAccess.Expression.WithLeadingTrivia(Space));
            if (variableAccess.Parent is MemberAccessExpressionSyntax)
            {
                newAccess = ParenthesizedExpression(newAccess);
            }

            var lambdaExpression = variableAccess.FirstAncestorOrSelf<LambdaExpressionSyntax>();
            if (lambdaExpression != null)
            {
                var newBody = lambdaExpression.Body.ReplaceNode(variableAccess, newAccess);
                syntaxRoot = LambdaFixProvider.FixLambda(syntaxRoot, lambdaExpression, newBody);

            }
            else
            {
                var trackedRoot = syntaxRoot.TrackNodes(method, variableAccess);
                var trackedVariableAccess = trackedRoot.GetCurrentNode(variableAccess);

                syntaxRoot = trackedRoot.ReplaceNode(trackedVariableAccess, newAccess);
                method = syntaxRoot.GetCurrentNode(method);
            }
            return syntaxRoot;
        }
    }
}