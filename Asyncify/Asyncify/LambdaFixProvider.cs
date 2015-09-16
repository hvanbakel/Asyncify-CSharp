using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Asyncify
{
    internal static class LambdaFixProvider
    {
        public static SyntaxNode FixLambda(SyntaxNode root, LambdaExpressionSyntax lambda, CSharpSyntaxNode newBody)
        {
            var simpleLambda = lambda as SimpleLambdaExpressionSyntax;
            var parenthesizedLambda = lambda as ParenthesizedLambdaExpressionSyntax;
            if (simpleLambda != null)
            {
                return root.ReplaceNode(lambda, simpleLambda
                    .WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space))
                    .WithBody(newBody));
            }
            else
            {
                return root.ReplaceNode(lambda, parenthesizedLambda
                    .WithAsyncKeyword(Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(Space))
                    .WithBody(newBody));
            }
        }
    }
}
