using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal class VariableAccessAnalyzer
    {
        private readonly SemanticModel semanticModel;

        public VariableAccessAnalyzer(SemanticModel semanticModel)
        {
            this.semanticModel = semanticModel;
        }

        public bool ShouldUseTap(MemberAccessExpressionSyntax memberAccessExpression)
        {
            if (memberAccessExpression.IsWrappedInAwaitExpression())
            {
                return false;
            }

            var identifierName = memberAccessExpression.Name as IdentifierNameSyntax;
            if (identifierName?.Identifier.ValueText != nameof(Task<int>.Result))
            {
                return false;
            }

            var symbol = FindSymbol(memberAccessExpression.Expression);
            if (symbol == null)
            {
                return false;
            }
            var taskSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName);
            var taskOfTSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName + "`1");

            return symbol.IsGenericType ?
                symbol.ConstructedFrom.Equals(taskOfTSymbol) :
                symbol.Equals(taskSymbol);
        }

        private INamedTypeSymbol FindSymbol(ExpressionSyntax expression)
        {
            while (true)
            {
                var parenthesizedExpression = expression as ParenthesizedExpressionSyntax;
                if (parenthesizedExpression != null)
                {
                    expression = parenthesizedExpression.Expression;
                    continue;
                }

                var castExpression = expression as CastExpressionSyntax;
                if (castExpression != null)
                {
                    return semanticModel.GetTypeInfo(castExpression.Type).Type as INamedTypeSymbol;
                }

                if (expression is InvocationExpressionSyntax)//Handled by invocationanalzyer
                {
                    return null;
                }

                return ((ILocalSymbol) semanticModel.GetSymbolInfo(expression).Symbol).Type as INamedTypeSymbol;
            }
        }
    }
}