using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal static class ExtensionMethods
    {
        internal static bool IsWrappedInAwaitExpression(this SyntaxNode node)
        {
            return node?.FirstAncestorOrSelf<AwaitExpressionSyntax>() != null;
        }
    }
}
