using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal static class ExtensionMethods
    {
        internal static bool IsWrappedInAwaitExpression(this SyntaxNode node)
        {
            return node?.FirstAncestorOrSelf<AwaitExpressionSyntax>() != null;
        }

        internal static bool HasOutOrRefParameters(this MethodDeclarationSyntax node)
        {
            return node.ParameterList != null &&
                   node.ParameterList.Parameters.Any(x =>
                       x.Modifiers.Any(SyntaxKind.RefKeyword) ||
                       x.Modifiers.Any(SyntaxKind.OutKeyword)
                       );
        }
    }
}
