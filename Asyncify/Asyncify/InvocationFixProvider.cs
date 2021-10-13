using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Runtime.CompilerServices;
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

        protected override SyntaxNode ApplyFix(MethodDeclarationSyntax method, InvocationExpressionSyntax invocation, SyntaxNode syntaxRoot)
        {
            var lambda = invocation.FirstAncestorOrSelf<LambdaExpressionSyntax>();

            SyntaxNode oldNode = invocation;
            if (invocation.Expression is MemberAccessExpressionSyntax maInvocation &&
                !maInvocation.Name.ToString().EndsWith("Async"))
            {
                invocation = invocation.WithExpression(maInvocation.WithName(IdentifierName((invocation.Expression as MemberAccessExpressionSyntax).Name.ToString() + "Async")));
            }

            SyntaxNode newNode = AwaitExpression(invocation.WithLeadingTrivia(Space)
                .AddArgumentListArguments(ParseArgumentList("cancellationToken").Arguments.ToArray()));

            SyntaxNode node = oldNode.Parent;
            while (node != null)
            {
                var memberAccess = node as MemberAccessExpressionSyntax;
                var identifierName = memberAccess?.Name as IdentifierNameSyntax;
                if (identifierName != null &&
                    (identifierName.Identifier.ValueText == nameof(Task<int>.Result)
                     || identifierName.Identifier.ValueText == nameof(Task<int>.GetAwaiter)
                     || identifierName.Identifier.ValueText == nameof(TaskAwaiter<int>.GetResult)))
                {
                    //if (newNode == null)
                    //{
                    //    newNode = memberAccess.Expression.ReplaceNode(oldNode, newNode);

                    //    if (memberAccess.Parent is MemberAccessExpressionSyntax)
                    //    {
                    //        newNode = ParenthesizedExpression((ExpressionSyntax)newNode);
                    //    }
                    //}

                    oldNode = memberAccess;
                }
                else if (node is InvocationExpressionSyntax invocationExpression)
                {
                    oldNode = invocationExpression;
                }
                else 
                {
                    break;
                }

                node = node.Parent;

                //if (!(node is MemberAccessExpressionSyntax || node is InvocationExpressionSyntax))
                //{
                //    break;
                //}
            }

            if (lambda != null)
            {
                var newLambda = LambdaFixProvider.FixLambda(method, lambda, lambda.Body.ReplaceNode(oldNode, newNode));
                return method.ReplaceNode(method, newLambda);
            }
            else
            {
                return method.ReplaceNode(oldNode, newNode);
            }
        }
    }
}