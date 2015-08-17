using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Asyncify.Test
{
    [TestClass]
    public class InvocationAnalyzerFixTest : BasAnalyzerFixTest
    {

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
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", GetResultWithLocation(9, 22));
            VerifyCodeNoReturn(@"
    public void Test()
    {
        CallAsync();
    }", GetResultWithLocation(9, 9));

            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var result = await CallAsync();
    }", GetResultWithLocation(9, 22));
            VerifyCodeFixNoReturn(@"
    public void Test()
    {
        CallAsync();
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        await CallAsync();
    }", GetResultWithLocation(9, 9));
        }

        [TestMethod]
        public void CanFindMethodWhenUsingBraces()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", GetResultWithLocation(9, 23));
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var result = (await CallAsync());
    }", GetResultWithLocation(9, 23));
        }

        [TestMethod]
        public void FixIsAppliedUpCallTree()
        {
            var oldSource = string.Format(FormatCode, @"
public int SecondLevelUp()
{
    return FirstLevelUp();
}

public int FirstLevelUp()
{
    return Test();
}

public int Test()
{
    var test = new AsyncClass();
    return test.Call().Result;
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public async System.Threading.Tasks.Task<int> SecondLevelUp()
{
    return await FirstLevelUp();
}

public async System.Threading.Tasks.Task<int> FirstLevelUp()
{
    return await Test();
}

public async System.Threading.Tasks.Task<int> Test()
{
    var test = new AsyncClass();
    return await test.Call();
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
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
    var result = test.Call().Result.ToString();
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public async System.Threading.Tasks.Task Test()
{
    var test = new AsyncClass();
    var result = (await test.Call()).ToString();
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            return new InvocationFixProvider();
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new InvocationAnalyzer();
        }

        public override string DiagnosticId => InvocationAnalyzer.DiagnosticId;
    }
}