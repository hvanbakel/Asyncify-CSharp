# Asyncify-CSharp
Asyncify-CSharp is an analyzer and codefix that allows you to quickly update your code to use the [Task Asynchronous Programming model](https://msdn.microsoft.com/en-us/library/hh873175(v=vs.110).aspx). This model, introduced in C# 5, adds an intuitive way of handling asynchronous calls within C#. 

The analyzer allows large codebases to be easily modified to use the TAP model by finding violations and applying fixes up the call tree. 

## Analyzer
The analyzer will throw warnings on use cases like below:
```CSharp
public void AnotherMethod()
{
    var result = Test();
}

public int Test()
{
    var result = AsyncMethod().Result; // Warning
    var awaitable = AsyncMethod();
    var result2 = awaitable.Result.ToString(); // Warning
    (new int[0]).Select(x => AsyncMethod().Result); // Warning
    return 0;
}

public async Task<int> AsyncMethod()
{
    await Task.Delay(10);
    return 0;
}
```

## Code Fix
The code fix will fix the following things.
- Remove the call to `.Result`
- Wrap it in an `await` expression (potentially wrapping in parentheses
- Update the method signature to become `async` and either `Task` or `Task<T>`
- Recursively find calls to this method and refactor those to use `await` and update their signature.

So the given code sample above becomes:
```CSharp
public async Task AnotherMethod()
{
    var result = await Test();
}

public async Task<int> Test()
{
    var result = await AsyncMethod();
    var awaitable = AsyncMethod();
    var result2 = (await awaitable).ToString();
    (new int[0]).Select(async x => await AsyncMethod());
    return 0;
}

public async Task<int> AsyncMethod()
{
    await Task.Delay(10);
    return 0;
}
```

## Conditions
A violation will not be thrown if a refactoring is not possible if:
- The violation is within a lock statement
- The method contains out or ref parameters

## Caution
The model of analyzers in Visual Studio is constrained to a single project. As the analyzers hook into the compilation, they are only capable of refactoring within a project. 
However, calls from outside the project can be suffixed with `.Result` causing a new violation in that project to which the code fix can be applied.

## What about VB?
Currently only C# is supported, mostly because I only have tests running on C#. Also, one thing that is specific to C# is the refactoring of the return type. 
