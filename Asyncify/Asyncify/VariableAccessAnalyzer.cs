using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Asyncify
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class VariableAccessAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "AsyncifyVariable";

        private static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.AsyncifyVariableAccessTitle), Resources.ResourceManager, typeof (Resources));
        private static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.AsyncifyVariableAccessMessageFormat), Resources.ResourceManager, typeof (Resources));
        private static readonly LocalizableString Description = new LocalizableResourceString(nameof(Resources.AsyncifyVariableAccessDescription), Resources.ResourceManager, typeof (Resources));

        private const string HelpUrl = "https://msdn.microsoft.com/en-us/library/hh873175(v=vs.110).aspx";
        private const string Category = "Async";

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat,
            Category, DiagnosticSeverity.Warning, true, Description, HelpUrl);

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSyntaxNodeAction(CheckMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void CheckMemberAccess(SyntaxNodeAnalysisContext context)
        {
            var memberAccessExpression = context.Node as MemberAccessExpressionSyntax;

            if (memberAccessExpression == null)
            {
                return;
            }

            var memberAccessAnalyzer = new VariableAccessChecker(context.SemanticModel);
            if (memberAccessAnalyzer.ShouldUseTap(memberAccessExpression))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccessExpression.GetLocation()));
            }
        }
    }
}