/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: Tests that drive the companion class generator directly through Roslyn
 */

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using PeroTools2.Error.Generator;

namespace PeroTools2.Error.Tests;

[TestFixture]
[TestOf(typeof(PeroErrorGenerator))]
public sealed class ErrCompanionGeneratorTests
{
    private const string header = "using PeroTools2.Error;\n";

    private static readonly ImmutableArray<MetadataReference> s_References = CreateReferences();

    [Test]
    public void GeneratesCompanionClassTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""File {path} Do Not Exist"")]
    [ErrArg(""path"", typeof(string), ""Path that was not found"")]
    NotFound,
}");

        AssertNoErrors(run);
        Assert.That(run.generatorDiagnostics, Is.Empty);
        Assert.That(run.generated.Length, Is.EqualTo(1));
        Assert.That(run.generated[0].HintName, Is.EqualTo("IOErr.NotFound.g.cs"));

        string text = run.generated[0].SourceText.ToString();
        Assert.That(text, Does.Contain("public sealed class IOErr_NotFound : global::PeroTools2.Error.Err<global::IOErr>"));
        Assert.That(text, Does.Contain("public string path { get; private set; }"));
        Assert.That(text, Does.Contain("global::IOErr.NotFound,"));
        Assert.That(text, Does.Contain(@"msg ?? $""File {path} Do Not Exist"""));
        Assert.That(text, Does.Contain("Path that was not found"));
    }

    [Test]
    public void GeneratesInternalClassForInternalEnumTest()
    {
        var run = Run(header + @"
internal enum IOKind
{
    [ErrMsg(""io failed"")]
    Failed,
}");

        AssertNoErrors(run);
        Assert.That(run.generated[0].SourceText.ToString(), Does.Contain("internal sealed class IOKind_Failed"));
    }

    [Test]
    public void GeneratesOneFilePerMemberTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""a"")]
    A,

    [ErrMsg(""b {x}"")]
    [ErrArg(""x"", typeof(int))]
    B,

    Plain,
}");

        AssertNoErrors(run);
        Assert.That(run.generated.Length, Is.EqualTo(2));
        Assert.That(run.generated.Select(s => s.HintName), Is.EquivalentTo(new[] { "IOErr.A.g.cs", "IOErr.B.g.cs" }));
    }

    [Test]
    public void SupportsAlignmentAndFormatSpecifierTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""[{name,6}][{value:F3}]"")]
    [ErrArg(""name"", typeof(string))]
    [ErrArg(""value"", typeof(double))]
    Custom,
}");

        AssertNoErrors(run);
        var err = CreateError(Emit(run.outputCompilation), "IOErr_Custom", "ab", 1.25);
        Assert.That(GetProperty(err, "message"), Is.EqualTo($"[{"ab",6}][{1.25:F3}]"));
    }

    [Test]
    public void SupportsEscapedBracesTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""json {{ no close"")]
    Broken,
}");

        AssertNoErrors(run);
        var err = CreateError(Emit(run.outputCompilation), "IOErr_Broken");
        Assert.That(GetProperty(err, "message"), Is.EqualTo("json { no close"));
    }

    [Test]
    public void SupportsCustomClassNameTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""custom {v}"", name = ""MyCompanion"")]
    [ErrArg(""v"", typeof(int))]
    Custom,
}");

        AssertNoErrors(run);
        Assert.That(run.generated[0].HintName, Is.EqualTo("IOErr.Custom.g.cs"));
        Assert.That(run.generated[0].SourceText.ToString(), Does.Contain("sealed class MyCompanion"));
    }

    [Test]
    public void SupportsKeywordArgumentNameTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""event {event}"")]
    [ErrArg(""event"", typeof(string))]
    Keyword,
}");

        AssertNoErrors(run);
        string text = run.generated[0].SourceText.ToString();
        Assert.That(text, Does.Contain("public string @event { get; private set; }"));
        Assert.That(text, Does.Contain(@"msg ?? $""event {@event}"""));
    }

    [Test]
    public void SupportsGlobalNamespaceEnumTest()
    {
        var run = Run(header + @"
public enum GlobalEnum
{
    [ErrMsg(""g {v}"")]
    [ErrArg(""v"", typeof(string))]
    Item,
}");

        AssertNoErrors(run);
        string text = run.generated[0].SourceText.ToString();
        Assert.That(text, Does.Contain("sealed class GlobalEnum_Item"));
        Assert.That(text, Does.Not.Contain("namespace "));
    }

    [Test]
    public void GeneratedErrorBehavesLikeErrTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""File {path} Do Not Exist"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");
        AssertNoErrors(run);

        var assembly = Emit(run.outputCompilation);
        var err = CreateError(assembly, "IOErr_NotFound", "/tmp/a.txt");

        Assert.That(GetProperty(err, "message"), Is.EqualTo("File /tmp/a.txt Do Not Exist"));
        Assert.That(GetProperty(err, "path"), Is.EqualTo("/tmp/a.txt"));
        Assert.That(GetProperty(err, "kind").ToString(), Is.EqualTo("NotFound"));
        Assert.That(GetProperty(err, "causedBy"), Is.Null);
        Assert.That(GetProperty(err, "exception"), Is.Null);
        Assert.That(err.ToString(), Does.Contain("File /tmp/a.txt Do Not Exist"));
    }

    [Test]
    public void MessageOverrideAndCallerInfoTest()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""default {path}"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");
        AssertNoErrors(run);

        var assembly = Emit(run.outputCompilation);
        var type = assembly.GetType("IOErr_NotFound", throwOnError: true);
        var factory = type.GetMethod("New", BindingFlags.Public | BindingFlags.Static);
        var err = factory.Invoke(null, new object[] { "/tmp/a.txt", "overridden", null, null, "MyMember", "MyFile.cs", 42 });

        Assert.That(GetProperty(err, "message"), Is.EqualTo("overridden"));
        string text = err.ToString();
        Assert.That(text, Does.Contain("At: MyMember @ MyFile.cs:42"));
    }

    #region Diagnostics

    [Test]
    public void UnknownPlaceholderReportsPERR002Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""File {path} Do Not Exist"")]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR002" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void UnbalancedBraceReportsPERR001Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""File {path Do Not Exist"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR001" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void ExpressionInPlaceholderReportsPERR003Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""File {path.ToUpper()} Do Not Exist"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR003" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void DuplicateArgumentReportsPERR004Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""x {a}"")]
    [ErrArg(""a"", typeof(string))]
    [ErrArg(""a"", typeof(int))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR004" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void ReservedArgumentNameReportsPERR005Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""x {msg}"")]
    [ErrArg(""msg"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR005" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void InvalidArgumentNameReportsPERR006Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""x"")]
    [ErrArg(""1abc"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR006" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void DuplicateGeneratedClassNameReportsPERR015Test()
    {
        // Two members asking for the same class name must not turn into a CS0101 in generated files
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""a"", name = ""Same"")]
    A,

    [ErrMsg(""b"", name = ""Same"")]
    B,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR015" }));
        Assert.That(run.generated.Length, Is.EqualTo(1));
    }

    [Test]
    public void BadAlignmentReportsPERR001Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""{path,abc}"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR001" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void QuoteInFormatSpecifierReportsPERR001Test()
    {
        // The format tail is copied into the generated $"..." literal, so a quote must be rejected
        var run = Run(header + @"
public enum IOErr
{
    [ErrMsg(""len {path:\""x\""}"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR001" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void NameConflictReportsPERR008Test()
    {
        var run = Run(header + @"
public sealed class IOErr_NotFound { }

public enum IOErr
{
    [ErrMsg(""x"")]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR008" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void NestedEnumReportsPERR009Test()
    {
        var run = Run(header + @"
public static class Owner
{
    public enum IOErr
    {
        [ErrMsg(""x"")]
        NotFound,
    }
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR009" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void ArgumentWithoutMessageReportsPERR010Test()
    {
        var run = Run(header + @"
public enum IOErr
{
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR010" }));
        Assert.That(run.generatorDiagnostics.Single().Severity, Is.EqualTo(DiagnosticSeverity.Warning));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void MessageOnPlainFieldReportsPERR011Test()
    {
        var run = Run(header + @"
public sealed class NotAnEnum
{
    [ErrMsg(""x"")]
    public string field;
}");

        Assert.That(DiagnosticIds(run), Is.EqualTo(new[] { "PERR011" }));
        Assert.That(run.generated, Is.Empty);
    }

    [Test]
    public void RunsTwiceAndProducesSameResultTest()
    {
        // The generator is V1 (Unity's Roslyn 3.8 does not support incremental generators),
        // so this only confirms that repeated runs produce a stable result
        var compilation = CreateCompilation(header + @"
public enum IOErr
{
    [ErrMsg(""File {path}"")]
    [ErrArg(""path"", typeof(string))]
    NotFound,
}");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ISourceGenerator[] { new PeroErrorGenerator() });

        driver = driver.RunGenerators(compilation);
        string first = driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).Single().SourceText.ToString();

        driver = driver.RunGenerators(compilation);
        string second = driver.GetRunResult().Results.SelectMany(static r => r.GeneratedSources).Single().SourceText.ToString();

        Assert.That(second, Is.EqualTo(first));
    }

    #endregion

    #region Utils

    private sealed class GeneratorRun
    {
        public Compilation outputCompilation;
        public ImmutableArray<Diagnostic> generatorDiagnostics;
        public ImmutableArray<GeneratedSourceResult> generated;
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        return CSharpCompilation.Create(
            "CompanionGeneratorTestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions, path: "TestInput.cs") },
            s_References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GeneratorRun Run(string source)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CreateCompilation(source);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new PeroErrorGenerator() },
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var result = driver.GetRunResult();
        return new GeneratorRun
        {
            outputCompilation = outputCompilation,
            generatorDiagnostics = diagnostics,
            generated = result.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray(),
        };
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

    private static string[] DiagnosticIds(GeneratorRun run) =>
        run.generatorDiagnostics.Select(static d => d.Id).Distinct().OrderBy(static id => id, StringComparer.Ordinal).ToArray();

    private static void AssertNoErrors(GeneratorRun run)
    {
        var errors = run.outputCompilation.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.That(errors, Is.Empty, "Generated code failed to compile:\n" + string.Join("\n", errors));
    }

    private static Assembly Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        var errors = result.Diagnostics
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.That(result.Success, Is.True, string.Join("\n", errors));
        return Assembly.Load(stream.ToArray());
    }

    /// <summary>Builds a companion class through its generated New factory, defaulting omitted arguments by type</summary>
    private static object CreateError(Assembly assembly, string typeName, params object[] args)
    {
        var type = assembly.GetType(typeName, throwOnError: true);
        Assert.That(type, Is.Not.Null, "Could not find the generated type " + typeName);

        var factory = type.GetMethod("New", BindingFlags.Public | BindingFlags.Static);
        Assert.That(factory, Is.Not.Null, "The generated type has no static New factory: " + typeName);

        var parameters = factory.GetParameters();
        var all = new object[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            all[i] = i < args.Length
                ? args[i]
                : parameters[i].ParameterType.IsValueType
                    ? Activator.CreateInstance(parameters[i].ParameterType)
                    : null;
        }
        return factory.Invoke(null, all);
    }

    private static object GetProperty(object target, string name)
    {
        var property = target.GetType().GetProperty(name);
        Assert.That(property, Is.Not.Null, "Could not find property " + name);
        return property.GetValue(target);
    }

    #endregion
}