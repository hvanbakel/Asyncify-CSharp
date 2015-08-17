using TestHelper;

namespace Asyncify.Test
{
    public abstract class BasAnalyzerFixTest : CodeFixVerifier
    {
        public abstract string DiagnosticId { get; }

        protected static readonly DiagnosticResult[] EmptyExpectedResults = new DiagnosticResult[0];
        protected const string AsyncTaskOfTMethod = @"
    public async Task<int> CallAsync()
    {
        await Task.Delay(1000);
        return 0;
    }";
        protected const string AsyncTaskMethod = @"
    public async Task CallAsync()
    {
        await Task.Delay(1000);
    }";
        protected const string SyncTaskOfTMethod = @"
    public Task<int> CallAsync()
    {
        return Task.FromResult(0);
    }";
        protected const string SyncTaskMethod = @"
    public Task CallAsync()
    {
        return Task.FromResult(0);
    }";

        protected const string FormatCode = @"
using System.Linq;
using System.Threading.Tasks;

public class TapTest
{{
    {0}

    {1}
}}

public class AsyncClass
{{
    public async Task<int> Call()
    {{
        await Task.Delay(100);
        return 0;
    }}
}}
";

        protected DiagnosticResult GetResultWithLocation(int line, int column)
        {
            var expected = new DiagnosticResult
            {
                Id = DiagnosticId,
                Locations =
                    new[]
                    {
                        new DiagnosticResultLocation("Test0.cs", line, column)
                    }
            };
            return expected;
        }

        protected void VerifyCodeFixWithReturn(string test, string fix, params DiagnosticResult[] expectedResults)
        {
            var oldSource = string.Format(FormatCode, test, AsyncTaskOfTMethod);
            VerifyCSharpDiagnostic(oldSource, expectedResults);
            var fixSource = string.Format(FormatCode, fix, AsyncTaskOfTMethod);
            VerifyCSharpFix(oldSource, fixSource);

            oldSource = string.Format(FormatCode, test, SyncTaskOfTMethod);
            VerifyCSharpDiagnostic(oldSource, expectedResults);
            fixSource = string.Format(FormatCode, fix, SyncTaskOfTMethod);
            VerifyCSharpFix(oldSource, fixSource);
        }

        protected void VerifyCodeFixNoReturn(string test, string fix, params DiagnosticResult[] expectedResults)
        {
            var oldSource = string.Format(FormatCode, test, AsyncTaskMethod);
            VerifyCSharpDiagnostic(oldSource, expectedResults);
            var fixSource = string.Format(FormatCode, fix, AsyncTaskMethod);
            VerifyCSharpFix(oldSource, fixSource);

            oldSource = string.Format(FormatCode, test, SyncTaskMethod);
            VerifyCSharpDiagnostic(oldSource, expectedResults);
            fixSource = string.Format(FormatCode, fix, SyncTaskMethod);
            VerifyCSharpFix(oldSource, fixSource);
        }

        protected void VerifyCodeWithReturn(string test, params DiagnosticResult[] expectedResults)
        {
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, AsyncTaskOfTMethod), expectedResults);
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, SyncTaskOfTMethod), expectedResults);
        }

        protected void VerifyCodeNoReturn(string test, params DiagnosticResult[] expectedResults)
        {
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, AsyncTaskMethod), expectedResults);
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, SyncTaskMethod), expectedResults);
        }

    }
}