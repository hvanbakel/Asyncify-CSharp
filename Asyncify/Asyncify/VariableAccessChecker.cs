using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal class VariableAccessChecker
    { 
        private readonly SemanticModel semanticModel;

        public VariableAccessChecker(SemanticModel semanticModel)
        {
            this.semanticModel = semanticModel;
        }

        public bool ShouldUseTap(MemberAccessExpressionSyntax memberAccessExpression)
        {
            if (memberAccessExpression.IsWrappedInAwaitExpression() || memberAccessExpression.IsWrappedInLock())
            {
                return false;
            }

            var identifierName = memberAccessExpression.Name as IdentifierNameSyntax;
            if (identifierName?.Identifier.ValueText != nameof(Task<int>.Result))
            {
                return false;
            }

            var lambdaExpression = memberAccessExpression.FirstAncestorOrSelf<LambdaExpressionSyntax>();
            if (lambdaExpression == null)
            {
                var methodDeclaration = memberAccessExpression.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                if (methodDeclaration == null || methodDeclaration.HasOutOrRefParameters())
                {
                    return false;
                }
            }

            var symbol = FindSymbol(memberAccessExpression.Expression);
            if (symbol == null)
            {
                return false;
            }
            var taskSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName);
            var taskOfTSymbol = semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName + "`1");

            return symbol.IsGenericType ?
                symbol.ConstructedFrom.Equals(taskOfTSymbol, SymbolEqualityComparer.Default) :
                symbol.Equals(taskSymbol, SymbolEqualityComparer.Default);
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

                var localSymbol = semanticModel.GetSymbolInfo(expression).Symbol as ILocalSymbol;
                return localSymbol?.Type as INamedTypeSymbol;
            }
        }
    }
}