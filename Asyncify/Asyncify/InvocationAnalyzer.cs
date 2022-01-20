using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Asyncify
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class InvocationAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AsyncifyInvocation";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AsyncifyInvocationTitle), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AsyncifyInvocationMessageFormat), Resources.ResourceManager, typeof(Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AsyncifyInvocationDescription), Resources.ResourceManager, typeof(Resources));
        private const string HelpUrl = "https://msdn.microsoft.com/en-us/library/hh873175(v=vs.110).aspx";
        private const string Category = "Async";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category, DiagnosticSeverity.Warning, true, Description, HelpUrl);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(CheckInvocation, SyntaxKind.InvocationExpression);
        }

        private void CheckInvocation(SyntaxNodeAnalysisContext context)
        {
            var invocationExpression = context.Node as InvocationExpressionSyntax;

            if (invocationExpression == null)
            {
                return;
            }

            var invocationAnalyzer = new InvocationChecker(context.SemanticModel);

            if (invocationAnalyzer.ShouldUseTap(invocationExpression))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocationExpression.GetLocation()));
            }
        }
    }
}
