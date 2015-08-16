using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Asyncify
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class AsyncifyAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "Asyncify";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));
        private const string HelpUrl = "https://msdn.microsoft.com/en-us/library/hh873175(v=vs.110).aspx";
        private const string Category = "Async";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, HelpUrl);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(CheckInvocation, SyntaxKind.InvocationExpression);
            context.RegisterSyntaxNodeAction(CheckMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void CheckMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccessExpression = context.Node as MemberAccessExpressionSyntax;

            if (memberAccessExpression == null)
            {
                return;
            }

            var memberAccessAnalyzer = new VariableAccessAnalyzer(context.SemanticModel);
            if (memberAccessAnalyzer.ShouldUseTap(memberAccessExpression))
            {
                ReportDiagnostic(context);
            }
        }

        private void CheckInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocationExpression = context.Node as InvocationExpressionSyntax;

            if (invocationExpression == null)
            {
                return;
            }

            var invocationAnalyzer = new InvocationAnalyzer(context.SemanticModel);

            if (invocationAnalyzer.ShouldUseTap(invocationExpression))
            {
                ReportDiagnostic(context);
            }
        }

        private static void ReportDiagnostic(SyntaxNodeAnalysisContext context)
        {
            var method = context.Node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
            context.ReportDiagnostic(Diagnostic.Create(Rule, method.Identifier.GetLocation()));
        }
    }
}
