using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Asyncify.Test
{
    [TestClass]
    public class VariableAccessAnalyzerFixTest : BasAnalyzerFixTest
    {
        [TestMethod]
        public void CanFindMethodNotUsingTapWithVariable()
        {
            var expected = GetResultWithLocation(11, 22);
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
        }
        [TestMethod]
        public void WillWrapVariableInParenthesesIfNeeded()
        {
            var expected = GetResultWithLocation(11, 22);
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var temp = CallAsync();
        var result = temp.Result.ToString();
    }", expected);
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var temp = CallAsync();
        var result = temp.Result.ToString();
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var temp = CallAsync();
        var result = (await temp).ToString();
    }", expected);
        }

        [TestMethod]
        public void CanFindViolationInMethodUsingTap()
        {
            var expected = GetResultWithLocation(11, 22);
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var temp = CallAsync();
        var result = temp.Result;

        var temp2 = CallAsync();    
        var result2 = await temp2;
    }", expected);
        }

        [TestMethod]
        public void DoesNotViolateOnLackOfTapUseWithinLock()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var obj = new object();
        lock(obj)
        {
            var temp = CallAsync();
            var result = temp.Result;
        }
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void DoesNotViolateOnMethodsWithOutOrRef()
        {
            VerifyCodeWithReturn(@"
    public void Test(out string test)
    {
        test = string.Empty;
        var temp = CallAsync();
        var result = temp.Result;
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void CanFindMethodNotUsingTapWithVariableInBraces()
        {
            var expected = GetResultWithLocation(11, 22);
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
        }

        protected override CodeFixProvider GetCSharpCodeFixProvider()
        {
            return new VariableAccessFixProvider();
        }

        protected override DiagnosticAnalyzer GetCSharpDiagnosticAnalyzer()
        {
            return new VariableAccessAnalyzer();
        }

        public override string DiagnosticId => VariableAccessAnalyzer.DiagnosticId;
    }
}