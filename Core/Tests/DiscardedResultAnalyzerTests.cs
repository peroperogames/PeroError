/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Tests for the Result discard analyzer
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
}

public static class Helper
{
    public static Result<MyErr> DoWork() => default;
    public static Result<int, MyErr> DoWorkValue() => default;

    public static System.Threading.Tasks.Task<Result<MyErr>> DoWorkAsync()
        => System.Threading.Tasks.Task.FromResult(default(Result<MyErr>));

    public static System.Threading.Tasks.ValueTask<Result<MyErr>> DoWorkValueTask()
        => new System.Threading.Tasks.ValueTask<Result<MyErr>>(default(Result<MyErr>));

    public static void Handle(Result<MyErr> result) { }
    public static void HandleInt(Result<int, MyErr> result) { }
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
    public void ReportsDiscardedResultTest()
    {
        var diagnostics = Run(Source("Helper.DoWork();"));

        AssertIds(diagnostics, "PERR100");
        Assert.That(diagnostics[0].GetMessage(), Does.Contain("DoWork"));
    }

    [Test]
    public void ReportsDiscardedResultWithValueTest()
    {
        var diagnostics = Run(Source("Helper.DoWorkValue();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsFireAndForgetTaskTest()
    {
        // Calls an async method but neither awaits its result nor stores it
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
    public void ReportsDiscardedValueTaskAwaitTest()
    {
        var diagnostics = Run(SourceAsync("await Helper.DoWorkValueTask();"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsDiscardedUniTaskTest()
    {
        // The UniTask assembly is not referenced by the analyzer, so it is recognized by name
        var diagnostics = Run(prelude + uni_task_prelude + "public static class Subject { public static void Test() { UniHelper.GetUniTask(); } }");

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
    public void ReportsDiscardedAwaitOfVariableTest()
    {
        // Awaiting a variable discards the result just the same
        var diagnostics = Run(SourceAsync("var task = Helper.DoWorkAsync();\nawait task;"));

        AssertIds(diagnostics, "PERR100");
    }

    [Test]
    public void ReportsUnusedResultVariableTest()
    {
        var diagnostics = Run(Source("var result = Helper.DoWork();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsUnusedAwaitResultVariableTest()
    {
        var diagnostics = Run(SourceAsync("var result = await Helper.DoWorkAsync();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void ReportsOverwrittenResultVariableTest()
    {
        // The first Result is overwritten without ever being read
        var diagnostics = Run(Source("var r = Helper.DoWork();\nr = Helper.DoWork();"));

        AssertIds(diagnostics, "PERR101");
    }

    [Test]
    public void IgnoresOverwrittenButReadVariableTest()
    {
        var diagnostics = Run(Source("var r = Helper.DoWork();\nr = Helper.DoWork();\nif (!r.isOK) { }"));

        AssertIds(diagnostics);
    }

    #endregion

    #region Should not report

    [Test]
    public void IgnoresWhenCheckedTest()
    {
        var diagnostics = Run(Source(@"
var result = Helper.DoWork();
if (!result.isOK)
{
    System.Console.WriteLine(result.err);
}"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresWhenReturnedTest()
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
    public void IgnoresExplicitDiscardTest()
    {
        var diagnostics = Run(Source("_ = Helper.DoWork();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresUnderscorePrefixedLocalTest()
    {
        // Mirrors Rust: both let _ = ...; and let _x = ...; mean "intentionally unused"
        var diagnostics = Run(Source("var _ = Helper.DoWork();\nvar _ignored = Helper.DoWork();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresExplicitDiscardWithAwaitTest()
    {
        var diagnostics = Run(SourceAsync("_ = await Helper.DoWorkAsync();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresWhenPassedAsArgumentTest()
    {
        var diagnostics = Run(Source("Helper.Handle(Helper.DoWork());\nHelper.HandleInt(Helper.DoWorkValue());"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresWhenConditionCheckedTest()
    {
        var diagnostics = Run(Source("if (Helper.DoWork().isOK) { }\nswitch (Helper.DoWork()) { default: break; }"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresWhenUnwrappedTest()
    {
        var diagnostics = Run(Source("Helper.DoWork().UnWarp();"));

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresPropertyExpressionBodyTest()
    {
        var diagnostics = Run(prelude + "public static class Subject { public static Result<MyErr> Value => Helper.DoWork(); }");

        AssertIds(diagnostics);
    }

    [Test]
    public void IgnoresNonVoidLambdaTest()
    {
        var diagnostics = Run(Source("System.Func<Result<MyErr>> getter = () => Helper.DoWork();\nif (getter().isOK) { }"));

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
        // Projects that do not reference PeroTools2.PeroError must not be touched by these rules
        var compilation = CreateCompilation("public static class Subject { public static void Test() { System.Console.WriteLine(1); } }", referenceLibrary: false);
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
            Assert.That(errors, Is.Empty, "Test source failed to compile:\n" + string.Join("\n", errors));
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
            : s_References.Where(static r => !r.Display.EndsWith("PeroTools2.PeroError.dll", StringComparison.OrdinalIgnoreCase)).ToImmutableArray();

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