using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Asyncify
{
    internal class InvocationChecker
    {
        private readonly SemanticModel semanticModel;

        private Lazy<ITypeSymbol> taskSymbol;
        private Lazy<ITypeSymbol> taskOfTSymbol;

        public InvocationChecker(SemanticModel semanticModel)
        {
            this.semanticModel = semanticModel;
        }

        internal bool ShouldUseTap(InvocationExpressionSyntax invocation)
        {
            var method = invocation.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            if (invocation.IsWrappedInAwaitExpression() || invocation.IsWrappedInLock() || method == null || IsFollowedByCallReturningVoid(invocation))
            {
                return false;
            }

            taskSymbol = new Lazy<ITypeSymbol>(() => semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName));
            taskOfTSymbol = new Lazy<ITypeSymbol>(() => semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName + "`1"));

            if (method.HasOutOrRefParameters())
            {
                return false;
            }

            var symbolToCheck = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            if (symbolToCheck == null)
                return false;//Broken code case

            return IsAwaitableMethod(symbolToCheck) && this.InvocationCallsIsWrappedInResultCall(invocation);
        }

        private bool IsFollowedByCallReturningVoid(InvocationExpressionSyntax invocation)
        {
            var parentMemberAccess = invocation.Parent as MemberAccessExpressionSyntax;
            var parentIdentifier = parentMemberAccess?.Name as IdentifierNameSyntax;
            if (parentIdentifier == null)
            {
                return false;
            }

            var symbol = semanticModel.GetSymbolInfo(parentIdentifier).Symbol as IMethodSymbol;
            return symbol?.ReturnType.SpecialType == SpecialType.System_Void;
        }

        private bool InvocationCallsIsWrappedInResultCall(InvocationExpressionSyntax invocation)
        {
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
