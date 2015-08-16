using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestHelper;

namespace Asyncify.Test
{
    [TestClass]
    public class AnalyzerFixTest : CodeFixVerifier
    {
        private static readonly DiagnosticResult[] EmptyExpectedResults = new DiagnosticResult[0];
        private const string AsyncTaskOfTMethod = @"
    public async Task<int> CallAsync()
    {
        await Task.Delay(1000);
        return 0;
    }";
        private const string AsyncTaskMethod = @"
    public async Task CallAsync()
    {
        await Task.Delay(1000);
    }";
        private const string SyncTaskOfTMethod = @"
    public Task<int> CallAsync()
    {
        return Task.FromResult(0);
    }";
        private const string SyncTaskMethod = @"
    public Task CallAsync()
    {
        return Task.FromResult(0);
    }";

        private const string FormatCode = @"
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

        //No diagnostics expected to show up
        [TestMethod]
        public void DoesNotViolateOnCorrectUseOfTap()
        {
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        await CallAsync();
    }", EmptyExpectedResults);
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        CallAsync();
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void CanFindMethodNotUsingTap()
        {
            var expected = GetResultWithLocation(7, 17);
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", expected);
            VerifyCodeNoReturn(@"
    public void Test()
    {
        CallAsync();
    }", expected);

            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var result = await CallAsync();
    }", expected);
            VerifyCodeFixNoReturn(@"
    public void Test()
    {
        CallAsync();
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        await CallAsync();
    }", expected);
        }

        [TestMethod]
        public void CanFindMethodWhenUsingBraces()
        {
            var expected = GetResultWithLocation(7, 17);
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", expected);
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var result = (await CallAsync());
    }", expected);
        }

        
        [TestMethod]
        public void CanFindMethodNotUsingTapWithVariable()
        {
            var expected = GetResultWithLocation(7, 17);
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var temp = CallAsync();
        var result = temp.Result;
    }", expected);
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var temp = CallAsync();
        var result = temp.Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var temp = CallAsync();
        var result = await temp;
    }", expected);
            VerifyCodeNoReturn(@"
    public void Test()
    {
        var temp = CallAsync();
    }", expected);
        }

        [TestMethod]
        public void CanFindMethodNotUsingTapWithVariableInBraces()
        {
            var expected = GetResultWithLocation(7, 17);
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var temp = CallAsync();
        var result = ((Task<int>)temp).Result;
    }", expected);
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var temp = CallAsync();
        var result = ((Task<int>)temp).Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var temp = CallAsync();
        var result = await ((Task<int>)temp);
    }", expected);
            VerifyCodeNoReturn(@"
    public void Test()
    {
        CallAsync();
    }", expected);
        }

        [TestMethod]
        public void TestCodeFixWithReturnType()
        {
            var oldSource = string.Format(FormatCode, @"
public int Test()
{
    var test = new AsyncClass();
    return test.Call().Result;
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public async System.Threading.Tasks.Task<int> Test()
{
    var test = new AsyncClass();
    return await test.Call();
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void TestCodeFixWithInstanceCall()
        {
            var oldSource = string.Format(FormatCode, @"
public void Test()
{
    var test = new AsyncClass();
    var result = test.Call().Result;
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public async System.Threading.Tasks.Task Test()
{
    var test = new AsyncClass();
    var result = await test.Call();
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void FixWillWrapInParenthesesIfNeeded()
        {
            var oldSource = string.Format(FormatCode, @"
public void Test()
{
    var test = new AsyncClass();
    var result = test.Call().Result;
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public async System.Threading.Tasks.Task Test()
{
    var test = new AsyncClass();
    var result = await test.Call();
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            return new AsyncifyCodeFixProvider();
        }

        protected override MetadataReference[] AdditionalReferences => new MetadataReference[]
        {
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location)
        };

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new AsyncifyAnalyzer();
        }

        private static DiagnosticResult GetResultWithLocation(int line, int column)
        {
            var expected = new DiagnosticResult
            {
                Id = "Asyncify",
                Locations =
                    new[]
                    {
                        new DiagnosticResultLocation("Test0.cs", line, column)
                    }
            };
            return expected;
        }

        private void VerifyCodeFixWithReturn(string test, string fix, params DiagnosticResult[] expectedResults)
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

        private void VerifyCodeFixNoReturn(string test, string fix, params DiagnosticResult[] expectedResults)
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

        private void VerifyCodeWithReturn(string test, params DiagnosticResult[] expectedResults)
        {
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, AsyncTaskOfTMethod), expectedResults);
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, SyncTaskOfTMethod), expectedResults);
        }

        private void VerifyCodeNoReturn(string test, params DiagnosticResult[] expectedResults)
        {
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, AsyncTaskMethod), expectedResults);
            VerifyCSharpDiagnostic(string.Format(FormatCode, test, SyncTaskMethod), expectedResults);
        }
    }
}