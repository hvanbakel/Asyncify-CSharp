using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Asyncify.Test
{
    [TestClass]
    public class InvocationAnalyzerFixTest : BaseAnalyzerFixTest
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

        //No diagnostics expected to show up
        [TestMethod]
        public void DoesNotViolateOnNonTapUseWithinLock()
        {
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var obj = new object();
        lock(obj)
        {
            var result = CallAsync().Result;
        }
    }", EmptyExpectedResults);
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var obj = new object();
        lock(obj)
        {
            CallAsync();
        }
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void CanFindMethodNotUsingTap()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", GetResultWithLocation(10, 22));
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = CallAsync().Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var result = await CallAsync();
    }", GetResultWithLocation(10, 22));
        }

        [TestMethod]
        public void CanFindViolationInMethodUsingTap()
        {
            VerifyCodeWithReturn(@"
    public async Task Test()
    {
        var temp = await CallAsync();
        var result = CallAsync().Result;
    }", GetResultWithLocation(11, 22));
        }

        [TestMethod]
        public void DoesNotViolateOnMethodsWithOutOrRef()
        {
            VerifyCodeWithReturn(@"
    public void Test(out string test)
    {
        test = string.Empty;
        var result = CallAsync().Result;
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void DoesNotAddAwaitToVoidMethods()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        CallAsync().Wait();
    }", EmptyExpectedResults);
        }

        [TestMethod]
        public void CanFindMethodWhenUsingBraces()
        {
            VerifyCodeWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", GetResultWithLocation(10, 23));
            VerifyCodeFixWithReturn(@"
    public void Test()
    {
        var result = (CallAsync()).Result;
    }", @"
    public async System.Threading.Tasks.Task Test()
    {
        var result = (await CallAsync());
    }", GetResultWithLocation(10, 23));
        }

        [TestMethod]
        public void NoViolationOnAsyncMethodsWrappedInVoidCall()
        {
            VerifyCSharpDiagnostic(string.Format(FormatCode, @"
public void FirstLevelUp()
{
    Test().Wait();
}

public Task Test()
{
    return Task.FromResult(0);
}", string.Empty), EmptyExpectedResults);
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
        public void FixIsAppliedUpCallTreeStopsAtOutRefParams()
        {
            var oldSource = string.Format(FormatCode, @"
public int SecondLevelUp(out string test)
{
    test = string.Empty;
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
public int SecondLevelUp(out string test)
{
    test = string.Empty;
    return FirstLevelUp().Result;
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
        public void TestCodeFixWithinLambda()
        {
            var oldSource = string.Format(FormatCode, @"
public void Test()
{
    int[] bla = null;
    bla.Select(x => Task.FromResult(100).Result);
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public void Test()
{
    int[] bla = null;
    bla.Select(async x => await Task.FromResult(100));
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }

        [TestMethod]
        public void TestCodeFixWithinParenthesizedLambda()
        {
            var oldSource = string.Format(FormatCode, @"
public void Test()
{
    System.Action a = () => CallAsync();
}

public int CallAsync()
{
    return Task.FromResult(0).Result;
}
", string.Empty);
            var newSource = string.Format(FormatCode, @"
public void Test()
{
    System.Action a = async () => await CallAsync();
}

public async System.Threading.Tasks.Task<int> CallAsync()
{
    return await Task.FromResult(0);
}
", string.Empty);
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

        [TestMethod]
        public void WillAddAsyncToVoidMethodInCodefix()
        {
            var oldSource = string.Format(FormatCode, @"
public void VoidCallingMethod()
{
    Test();
}

public void Test()
{
    var test = new AsyncClass();
    var result = test.Call().Result;
}", string.Empty);
            var newSource = string.Format(FormatCode, @"
public async System.Threading.Tasks.Task VoidCallingMethod()
{
        await Test();
}

public async System.Threading.Tasks.Task Test()
{
    var test = new AsyncClass();
    var result = await test.Call();
}", string.Empty);
            VerifyCSharpFix(oldSource, newSource);
        }
        
        [TestMethod]
        public void TestRefactoringOverInterfaces()
        {
            VerifyCSharpFix(@"
using System.Threading.Tasks;

public class ConsumingClass
{
    public int Test(IInterface i)
    {
        return i.Call();
    }
}

public interface IInterface
{
    int Call();
}


public class DerivedClass : IInterface
{
    public int Call()
    {
        return AsyncMethod().Result;
    }

    public Task<int> AsyncMethod()
    {
        return Task.FromResult(0);
    }
}
", @"
using System.Threading.Tasks;

public class ConsumingClass
{
    public async System.Threading.Tasks.Task<int> Test(IInterface i)
    {
        return await i.Call();
    }
}

public interface IInterface
{
System.Threading.Tasks.Task<int> Call();
}


public class DerivedClass : IInterface
{
    public async System.Threading.Tasks.Task<int> Call()
    {
        return await AsyncMethod();
    }

    public Task<int> AsyncMethod()
    {
        return Task.FromResult(0);
    }
}
");
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