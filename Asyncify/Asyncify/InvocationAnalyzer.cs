using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal class InvocationAnalyzer
    {
        private readonly SemanticModel semanticModel;

        private Lazy<ITypeSymbol> taskSymbol;
        private Lazy<ITypeSymbol> taskOfTSymbol;

        public InvocationAnalyzer(SemanticModel semanticModel)
        {
            this.semanticModel = semanticModel;
        }

        internal bool ShouldUseTap(InvocationExpressionSyntax invocation)
        {
            var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (invocation.IsWrappedInAwaitExpression() || method == null)
            {
                return false;
            }

            taskSymbol = new Lazy<ITypeSymbol>(() => semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName));
            taskOfTSymbol = new Lazy<ITypeSymbol>(() => semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName + "`1"));

            if (MethodReturnsTask(method))
            {
                return false;
            }

            var symbolToCheck = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;

            return IsAwaitableMethod(symbolToCheck) && InvocationCallsIsWrappedInResultCall(invocation, symbolToCheck);
        }

        private bool InvocationCallsIsWrappedInResultCall(InvocationExpressionSyntax invocation, IMethodSymbol invokedMethodSymbol)
        {
            if (invokedMethodSymbol.ReturnType.Equals(taskSymbol.Value))
            {
                return true;
            }

            SyntaxNode node = invocation;
            while (node.Parent != null)
            {
                node = node.Parent;

                var memberAccess = node as MemberAccessExpressionSyntax;
                var identifierName = memberAccess?.Name as IdentifierNameSyntax;
                if (identifierName != null && identifierName.Identifier.ValueText == nameof(Task<int>.Result))
                {
                    return true;
                }
            }
            return false;
        }

        private bool MethodReturnsTask(MethodDeclarationSyntax method)
        {
            return IsTask(semanticModel.GetTypeInfo(method.ReturnType).Type as INamedTypeSymbol);
        }

        private bool IsAwaitableMethod(IMethodSymbol invokedSymbol)
        {
            return invokedSymbol.IsAsync || IsTask(invokedSymbol.ReturnType as INamedTypeSymbol);
        }

        private bool IsTask(INamedTypeSymbol returnType)
        {
            if (returnType == null)
            {
                return false;
            }

            return returnType.IsGenericType ?
                returnType.ConstructedFrom.Equals(taskOfTSymbol.Value) :
                returnType.Equals(taskSymbol.Value);
        }
    }
}
