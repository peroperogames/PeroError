/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Tests for the Result consumption analyzer
 */

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PeroTools2.Error.Analyzers;

namespace PeroTools2.Error.Tests;

[TestFixture]
[TestOf(typeof(DiscardedResultAnalyzer))]
public sealed class DiscardedResultAnalyzerTests
{
    private const string prelude = @"using PeroTools2.Error;

public enum MyErr
{
    A,
    B,
}

public static class Helper
{
    public static Result<MyErr> DoWork() => default;
    public static Result<int, MyErr> DoWorkValue() => default;

    public static System.Threading.Tasks.Task<Result<MyErr>> DoWorkAsync()
        => System.Threading.Tasks.Task.FromResult(default(Result<MyErr>));

    public static System.Threading.Tasks.ValueTask<Result<MyErr>> DoWorkValueTask()
        => new System.Threading.Tasks.ValueTask<Result<MyErr>>(default(Result<MyErr>));

    public static void Handle(Result<MyErr> result) => result.Dispose();
    public static void HandleInt(Result<int, MyErr> result) => result.Dispose();
}
";

    private const string uni_task_prelude = @"
namespace Cysharp.Threading.Tasks
{
    public struct UniTask<T>
    {
        public T GetResult() => default;
    }
}

public static class UniHelper
{
    public static Cysharp.Threading.Tasks.UniTask<PeroTools2.Error.Result<MyErr>> GetUniTask() => default;
}
";

    private static readonly ImmutableArray<MetadataReference> s_References = CreateReferences();

    #region Should report

    [Test]
    public void ReportsUnconsumedCallTest()
    {
        var diagnostics = Run(Source("Helper.DoWork();"));

        AssertIds(diagnostics, "PERR100");
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("DoWork"));
    }

    [Test]
    public void ReportsUnconsumedResultWithValueTest()
    {
        var diagnostics = Run(Source("Helper.DoWorkValue();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsFireAndForgetTaskTest()
    {
        var diagnostics = Run(Source("Helper.DoWorkAsync();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsDiscardedAwaitTest()
    {
        var diagnostics = Run(SourceAsync("await Helper.DoWorkAsync();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsDiscardedAwaitOfVariableTest()
    {
        var diagnostics = Run(SourceAsync("var task = Helper.DoWorkAsync();\nawait task;"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsDiscardedValueTaskAwaitTest()
    {
        var diagnostics = Run(SourceAsync("await Helper.DoWorkValueTask();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsUniTaskTest()
    {
        // The UniTask assembly is not referenced by the analyzer, the wrapper is recognized by name
        var diagnostics = Run(
            prelude + uni_task_prelude
                    + "public static class Subject { public static void Test() { UniHelper.GetUniTask(); } }");

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsInspectionOnlyTest()
    {
        // Reading isOK / calling Unwrap() does not consume the Result
        var diagnostics = Run(Source("Helper.DoWork().Unwrap();\nif (!Helper.DoWork().isOK) { }"));

        AssertIds(diagnostics, "PERR100", "PERR100");
    }

    [Test]
    public void ReportsExplicitDiscardTest()
    {
        // `_ = ...` no longer silences the analyzer: the Result is still not consumed
        var diagnostics = Run(Source("_ = Helper.DoWork();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsVoidExpressionBodiedMethodTest()
    {
        var diagnostics = Run(prelude + "public static class Subject { public static void Test() => Helper.DoWork(); }");

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsVoidExpressionBodiedLambdaTest()
    {
        var diagnostics = Run(Source("System.Action action = () => Helper.DoWork();\naction();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsUnconsumedLocalTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsLocalInspectedOnlyTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nif (!result.isOK) { }"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsLocalUnwrappedTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nresult.Unwrap();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsLocalErrorReadOnlyTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nvar error = result.err;"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsUnderscoreLocalTest()
    {
        // A leading underscore is not a consumption marker any more
        var diagnostics = Run(Source("var _ = Helper.DoWork();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsOverwrittenLocalTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nresult = Helper.DoWork();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsDisposedTwiceTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nresult.Dispose();\nresult.Dispose();"));

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void ReportsUsingAndWrappedTest()
    {
        // `using` disposes the Result, and wrapping its error hands the error over a second time:
        // the disposed error is returned to the pool while the new error still refers to it
        var diagnostics = Run(Source(
            "using var source = Helper.DoWork();\n"
            + "var wrapped = Result<MyErr>.Fail(source.err!.Wrap(MyErr.A, \"wrapped\"));\n"
            + "Helper.Handle(wrapped);"));

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void ReportsUsingAndPassedTest()
    {
        // Passing a using variable on hands ownership to the callee as well
        var diagnostics = Run(Source("using var result = Helper.DoWork();\nHelper.Handle(result);"));

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void ReportsUnconsumedParameterTest()
    {
        // A Result passed in as a parameter is owned by the method from then on
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static void Test(Result<MyErr> value)
    {
        if (!value.isOK) { }
    }
}");

        AssertIds(diagnostics, "PERR103");
    }

    [Test]
    public void ReportsParameterConsumedTwiceTest()
    {
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static void Test(Result<MyErr> value)
    {
        value.Dispose();
        Helper.Handle(value);
    }
}");

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void ReportsSameBranchConsumedTwiceTest()
    {
        var diagnostics = Run(Source(@"
var result = Helper.DoWork();
if (result.isOK)
{
    result.Dispose();
    Helper.Handle(result);
}"));

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void ReportsOverwrittenConsumedLocalTest()
    {
        // The first Result is silently lost when the local is assigned over
        var diagnostics = Run(Source("var result = Helper.DoWork();\nresult = Helper.DoWork();\nHelper.Handle(result);"));

        AssertIds(diagnostics, "PERR104");
    }

    [Test]
    public void ReportsNeverAwaitedTaskVariableTest()
    {
        // The task is stored and dropped: neither awaited nor handed on, so the Result never surfaces
        var diagnostics = Run(Source("var task = Helper.DoWorkAsync();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsLocalHoldingImplicitlyConvertedErrorTest()
    {
        // Result<T> r = Err.New(...) holds a Result even though the initializer is an Err
        var diagnostics = Run(Source("Result<MyErr> result = Err.New(MyErr.A, \"boom\");"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsResultStoredInFieldTest()
    {
        // Storing a Result in a member does not hand the error anywhere: it never returns to the pool
        var diagnostics = Run(prelude + @"
public sealed class Subject
{
    private Result<MyErr> m_Result = Helper.DoWork();
}");

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsResultAssignedToFieldTest()
    {
        var diagnostics = Run(prelude + @"
public sealed class Subject
{
    private Result<MyErr> m_Result;

    public void Refresh() => m_Result = Helper.DoWork();
}");

        AssertIds(diagnostics, "PERR100");
    }
    [Test]
    public void ReportsOverwrittenExtractedLocalTest()
    {
        // The payload was taken out, then a fresh Result overwrote the local without being consumed
        var diagnostics = Run(Source(
            "var result = Helper.DoWorkValue();\n"
            + "result.TryGetValue(out var value, out var err);\n"
            + "result = Helper.DoWorkValue();"));

        AssertIds(diagnostics, "PERR104");
    }
    [Test]
    public void ReportsConsumedTwiceWhenBranchDoesNotExitTest()
    {
        // Both places can run: the if branch falls through instead of leaving the method
        var diagnostics = Run(Source(@"
var result = Helper.DoWork();
if (!result.isOK)
{
    Helper.Handle(result);
}

result.Dispose();"));

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void ReportsConsumedTwiceInLoopTest()
    {
        // `continue` only leaves the current iteration, so the next one consumes the Result again
        var diagnostics = Run(Source(@"
foreach (var item in new[] { 1, 2 })
{
    var result = Helper.DoWork();
    if (item == 1)
    {
        result.Dispose();
        continue;
    }

    Helper.Handle(result);
}"));

        AssertIds(diagnostics, "PERR102");
    }

    [Test]
    public void IgnoresExtractedPayloadTest()
    {
        // TryGetValue drains the Result: the value and the error leave through the out parameters
        var diagnostics = Run(Source(@"
var result = Helper.DoWorkValue();
if (result.TryGetValue(out var value, out var err))
{
    System.Console.WriteLine(value);
}
else
{
    System.Console.WriteLine(err.kind);
}"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresExtractedAndDisposedTest()
    {
        // The shape the README uses: extract, release on success, hand the error on when failed
        var diagnostics = Run(Source(@"
var result = Helper.DoWorkValue();
if (result.TryGetValue(out var value, out var err))
{
    result.Dispose();
    System.Console.WriteLine(value);
}
else
{
    err.Dispose();
}"));

        AssertIds(diagnostics);
    }
    [Test]
    public void IgnoresConsumedInSeparateBranchesTest()
    {
        // Only one branch runs, so the Result is consumed exactly once on every path
        var diagnostics = Run(Source(@"
var result = Helper.DoWork();
if (result.isOK)
{
    result.Dispose();
}
else
{
    Helper.Handle(result);
}"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresConsumedInConditionalBranchesTest()
    {
        // Both branches of one ?: consume the Result: only one of them evaluates
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static int Test(bool flag)
    {
        var result = Helper.DoWork();
        return flag ? Consume(result) : Consume(result);
    }

    private static int Consume(Result<MyErr> result)
    {
        result.Dispose();
        return 1;
    }
}");

        AssertIds(diagnostics);
    }
    [Test]
    public void IgnoresDisposedAndWrappedInSeparateBranchesTest()
    {
        // The shape the README recommends: dispose on success, wrap the error on failure
        var diagnostics = Run(Source(@"
var result = Helper.DoWork();
if (result.isOK)
{
    result.Dispose();
}
else
{
    var wrapped = result.err!.Wrap(MyErr.A, ""wrapped"");
    Helper.Handle(wrapped);
}"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresDisposedAfterEarlyReturnTest()
    {
        // The shape the README recommends: the failure branch hands the error over and returns,
        // so the code after the if only runs on the success path
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static Result<MyErr> Test()
    {
        var result = Helper.DoWork();
        if (!result.isOK)
        {
            return result.err!.Wrap(MyErr.B, ""config not found"");
        }

        result.Dispose();
        return Result<MyErr>.OK();
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresDisposedAfterEarlyThrowTest()
    {
        var diagnostics = Run(Source(@"
var result = Helper.DoWork();
if (!result.isOK)
{
    throw result.err!.Panic();
}

result.Dispose();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresMemberWithoutBodyTest()
    {
        // Interface members and abstract methods have no implementation that could consume anything
        var diagnostics = Run(prelude + @"
public interface ISubject
{
    void Handle(Result<MyErr> value);

    Result<int, MyErr> Load(Result<MyErr> value);
}

public abstract class SubjectBase
{
    public abstract void Handle(Result<int, MyErr> value);
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresPartialMethodDeclarationTest()
    {
        // A partial method's declaration half has no body; only the implementation is analyzed
        var diagnostics = Run(prelude + @"
public partial class Subject
{
    partial void Handle(Result<MyErr> value);

    partial void Handle(Result<MyErr> value) => value.Dispose();
}");

        AssertIds(diagnostics);
    }
    [Test]
    public void IgnoresConsumedParameterTest()
    {
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static void Test(Result<MyErr> value) => value.Dispose();

    public static void Test2(Result<MyErr> value) => Helper.Handle(value);

    public static void Test3(Result<MyErr> value) => value.err!.Wrap(MyErr.A, ""wrapped"");
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void ReportsPassedAndReturnedTest()
    {
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static Result<MyErr> Test()
    {
        var result = Helper.DoWork();
        Helper.Handle(result);
        return result;
    }
}");

        AssertIds(diagnostics, "PERR102");
    }

    #endregion

    #region Should not report

    [Test]
    public void IgnoresPassedAsArgumentTest()
    {
        var diagnostics = Run(Source("Helper.Handle(Helper.DoWork());\nHelper.HandleInt(Helper.DoWorkValue());"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresReturnedTest()
    {
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static Result<MyErr> Test()
    {
        return Helper.DoWork();
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresLocalReturnedTest()
    {
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static Result<MyErr> Test()
    {
        var result = Helper.DoWork();
        return result;
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresDisposedTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nresult.Dispose();\nHelper.DoWork().Dispose();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresUsingDeclarationTest()
    {
        var diagnostics = Run(Source("using var result = Helper.DoWork();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresUsingStatementTest()
    {
        var diagnostics = Run(Source("using (var result = Helper.DoWork()) { }"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresPassedLocalTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();\nif (!result.isOK) { Helper.Handle(result); }"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresWrappedErrorTest()
    {
        // Rule 4: wrapping the error hands it over to the new error
        var diagnostics = Run(Source("var result = Helper.DoWork();\nresult.err.Wrap(MyErr.A, \"wrapped\");"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresReturnedErrorTest()
    {
        // Returning the error hands it over just like returning the Result itself
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static Err<MyErr> Test()
    {
        var result = Helper.DoWork();
        return result.err!;
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresAddCallerFrameTest()
    {
        // The README pattern for pure propagation: same error, one more stack frame
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static Result<MyErr> Test()
    {
        var result = Helper.DoWork();
        if (!result.isOK)
        {
            return result.err!.AddCallerFrame();
        }

        result.Dispose();
        return Result<MyErr>.OK();
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresYieldReturnedTest()
    {
        // `yield return` hands the value to whoever enumerates the iterator
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static System.Collections.Generic.IEnumerable<Result<MyErr>> LoadAll(string[] paths)
    {
        foreach (var path in paths)
        {
            yield return Helper.DoWork();
        }
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresOutParameterTest()
    {
        // ref / out / in parameters stay owned by the caller (Try-pattern)
        var diagnostics = Run(prelude + @"
public static class Subject
{
    public static bool TryLoad(string path, out Result<MyErr> result)
    {
        result = Helper.DoWork();
        return true;
    }
}");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresResultHoldingAnErrorTest()
    {
        // The README way of returning an error to the pool through a Result
        var diagnostics = Run(Source("var err = Err.New(MyErr.A, \"boom\");\nResult<MyErr> held = err;\nheld.Dispose();"));

        AssertIds(diagnostics);
    }
    [Test]
    public void IgnoresResultPropertyExpressionBodyTest()
    {
        var diagnostics = Run(prelude + "public static class Subject { public static Result<MyErr> Value => Helper.DoWork(); }");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresNonVoidLambdaTest()
    {
        var diagnostics = Run(Source(
            "System.Func<Result<MyErr>> getter = () => Helper.DoWork();\nHelper.Handle(getter());"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresOtherTypesTest()
    {
        var diagnostics = Run(Source(@"
System.Console.WriteLine(""hello"");
int x = 1;
x.ToString();
var err = Err.New(MyErr.A, ""boom"");
err.AddNote(""note"");"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresWithoutPeroErrorReferenceTest()
    {
        // Projects that do not reference PeroTools2.PeroError are not affected
        var compilation = CreateCompilation(
            "public static class Subject { public static void Test() { System.Console.WriteLine(1); } }",
            referenceLibrary: false);
        var diagnostics = RunAnalyzer(compilation, assertNoErrors: false);

        AssertIds(diagnostics);
    }

    #endregion

    #region Utils

    private static string Source(string body) =>
        prelude + @"
public static class Subject
{
    public static void Test()
    {
" + body + @"
    }
}";

    private static string SourceAsync(string body) =>
        prelude + @"
public static class Subject
{
    public static async System.Threading.Tasks.Task Test()
    {
" + body + @"
    }
}";

    private static ImmutableArray<Diagnostic> Run(string source) =>
        RunAnalyzer(CreateCompilation(source, referenceLibrary: true), assertNoErrors: true);

    private static ImmutableArray<Diagnostic> RunAnalyzer(Compilation compilation, bool assertNoErrors)
    {
        if (assertNoErrors)
        {
            var errors = compilation.GetDiagnostics()
                .Where(static d => d.Severity == DiagnosticSeverity.Error)
                .Select(static d => d.ToString())
                .ToArray();
            Assert.That(errors, Is.Empty, "The test source does not compile:\n" + string.Join("\n", errors));
        }

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new DiscardedResultAnalyzer()));
        return withAnalyzers.GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    private static CSharpCompilation CreateCompilation(string source, bool referenceLibrary)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var references = referenceLibrary
            ? s_References
            : s_References
                .Where(static r => !r.Display.EndsWith("PeroTools2.PeroError.dll", StringComparison.OrdinalIgnoreCase))
                .ToImmutableArray();

        return CSharpCompilation.Create(
            "DiscardedResultAnalyzerTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions, path: "TestInput.cs") },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (string path in tpa.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrEmpty(path))
                {
                    builder.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }
        builder.Add(MetadataReference.CreateFromFile(typeof(Err<>).Assembly.Location));
        return builder.ToImmutable();
    }

    private static void AssertIds(ImmutableArray<Diagnostic> diagnostics, params string[] expectedIds)
    {
        var actual = diagnostics.Select(static d => d.Id).OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        var expected = expectedIds.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        Assert.That(actual, Is.EqualTo(expected), "Actual diagnostics: " + string.Join(", ", diagnostics.Select(static d => d.ToString())));
    }

    #endregion
}