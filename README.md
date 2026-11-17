# PeroError

`Err<TKind>` / `Result<T>` style error handling for C#.

## Install

### Nuget

Append following content to project file (`.csproj`):

```xml
<ItemGroup>
    <PackageReference Include="PeroTools2.PeroError" Version="0.2.0" />
</ItemGroup>
```

Or install via CLI:

```sh
dotnet add package PeroTools2.PeroError --version 0.2.0
```

### Unity

Append following content to `Packages/manifest.json`:

**From [GIT](https://github.com/peroperogames/PeroError):**

```json
{
  "dependencies":
  {
    "com.prpr.error": "https://github.com/peroperogames/PeroError.git#0.2.0"
  }
}
```

**From [NPMJS](https://www.npmjs.com/package/com.prpr.error/):**

```json
{
  "scopedRegistries":
  [
    {
      "name": "npmjs",
      "url": "https://registry.npmjs.org",
      "scopes": ["com.prpr"]
    }
  ],
  "dependencies":
  {
    "com.prpr.error": "0.2.0"
  }
}
```

**From [OpenUPM](https://openupm.com/packages/com.prpr.error/):**

```json
{
  "scopedRegistries":
  [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": ["com.prpr"]
    }
  ],
  "dependencies":
  {
    "com.prpr.error": "0.2.0"
  }
}
```

Or install via CLI:

```sh
openupm add com.prpr.error
```

## Features

### Quick start

```Csharp
public enum IOErr
{
    NotFound,         // maps to FileNotFoundException, DirectoryNotFoundException, DriveNotFoundException
    InvalidPath,      // maps to ArgumentException, PathTooLongException, NotSupportedException
    PermissionDenied, // maps to UnauthorizedAccessException, SecurityException
    EOF,              // maps to EndOfStreamException
    Others,           // maps to any other IOException, e.g. PipeException, FileLoadException
}

// a function that produces either a value or an error
private static Result<string, IOErr> ReadFromFile(string filePath)
{
    // Usage 1: create the error directly
    if(!File.Exists(filePath))
    {
       // creates an Err<IOErr>, implicitly converted to Result<string, IOErr>.Fail()
        return Err.New(IOErr.NotFound, message: $"File {filePath} NotFound")
            .AddNote("Note1")
            .AddNote("Note2"); // any error can carry extra descriptive notes
    }
    
    // Usage 2: create the error from an exception
    try
    {
        var content = File.ReadAllText(filePath);
        return content; // the string is implicitly converted to Result<string, IOErr>.Ok(content);
    }
    catch (Exception e) when (e is FileNotFoundException || e is DirectoryNotFoundException)
    {
        // an error caused by an exception should be created with exception.Wrap()
        return e.Wrap(IOErr.NotFound, message: $"File {filePath} NotFound");
    }
    catch (Exception e)
    {
        // handle the remaining error kinds...
    }
}

// a function that handles errors
public Result<Config, ConfigErr> ReadConfig(string configPath)
{
    var readResult = ReadFromFile(configPath);
    string configContent;
    if(readResult.isOK)
    {
        configContent = readResult.value;
        readResult.Dispose(); // released: we are done with it
    }
    else
    {
        switch(readResult.err!.kind)
        {
            // Option 1: hand the error over to a new error (this consumes the Result)
            case IOErr.NotFound:
            {
                // wrap IOErr as ConfigErr
                return readResult.err.Wrap(ConfigErr.NotFound, message: "WhatEver");
            }
            // Option 2: handle the error in place, then release the Result
            case IOErr.Others:
            {
                log.Error(readResult.err);
                configContent = "Default Value"; // provide a default value
                readResult.Dispose();
                break;
            }
            // Option 3: give up and turn it into an unrecoverable exception
            default:
            {
                var panic = readResult.err.Panic(); // snapshot the full error text first
                readResult.Dispose();
                throw panic;
            }
        }
    }
    ... // the rest of the work
}
```

Function style for railway oriented programming is also supported. A method that takes a `Result` owns it, so it releases it with `using`:

```csharp
enum IOErr
{
    NotFound,
    Timeout
}

static string DealWithIOError(Result<string, IOErr> result)
{
    using (result) // the callee owns the Result now and releases it when done
    {
        return result.Match(
            static value => $"No error, the value is {value}",
            static err => err.kind switch
            {
                IOErr.NotFound => "Not found",
                IOErr.Timeout => "Timeout"
            });
    }
}
```

If your project is built with .NET 11, you can use pattern matching with union types:

```csharp
static string DealWithIOError(Result<string, IOErr> result)
{
    using (result)
    {
        return result switch
        {
            string value => $"No error, the value is {value}",
            Err<IOErr> err => err.kind switch
            {
                IOErr.NotFound => "Not found",
                IOErr.Timeout => "Timeout"
            }
        };
    }
}
```

### Result ownership

A `Result` owns an `Err` instance, and `Dispose()` hands it back to the internal pool. The analyzer therefore requires every `Result` to be consumed **exactly once**.

A `Result` counts as consumed when, and only when:

| # | How | Example |
| --- | --- | --- |
| 1 | it is disposed | `using var r = Load();` · `using (r) { }` · `r.Dispose();` |
| 2 | it is returned | `return r;` · `Result<string, IOErr> M() => Load();` |
| 3 | it is passed as an argument | `Handle(Load());` |
| 4 | its error is wrapped into a new error | `return r.err.Wrap(IOErr.NotFound, "not found");` |

Reading `isOK` / `err` / `value`, calling `Unwrap()` / `Match()` / `Switch()`, or simply dropping the value does **not** consume it — the current method still owns it.

| ID | Reported when |
| --- | --- |
| `PERR100` | a produced Result is never consumed — `Load();`, `await LoadAsync();`, `Load().Unwrap();` |
| `PERR101` | a local Result is never consumed |
| `PERR102` | a Result is consumed more than once — `r.Dispose(); r.Dispose();`, `using var r = …; Handle(r);` |
| `PERR103` | a Result parameter is never consumed |

Consuming twice is a real bug, not just a style issue: `Dispose()` returns the error to the pool while a wrapped error still references it as `causedBy`, so the chain can lose — or silently mix up errors afterwards.

```csharp
var readResult = ReadFromFile(path);
if (!readResult.isOK)
{
    return readResult.err!.Wrap(ConfigErr.NotFound, "config not found"); // rule 4: ownership moves on
}

string content = readResult.value;
readResult.Dispose();                                                   // success path: release it
```

The analyzer ships inside the NuGet package (`analyzers/dotnet/cs`) and is picked up automatically. For Unity both Roslyn components live in `Unity/Plugins/` and carry the `RoslynAnalyzer` label. Turn a rule off with `dotnet_diagnostic.PERR102.severity = none` in `.editorconfig`, or with a `Default.ruleset` in Unity.

### Error companion classes

When an error needs to carry data, generate a **companion error class** for it. Declare the message template and the arguments on the error enum member and `PeroTools2.PeroError.Generator` generates the whole class:

```csharp
public enum IOErr
{
    [ErrMsg("File {path} Do Not Exist")]                        // supports format strings
    [ErrArg("path", typeof(string), "Path that was not found")] // ErrArg can be repeated
    NotFound,
}
```

That is equivalent to hand-writing the following (the generator emits exactly this). The constructor
stays **private**: instances always come from the pooled `New` factory, so a companion class costs no
allocation once one instance is back in the pool:

```csharp
public sealed class IOErr_NotFound : Err<IOErr>
{
    /// <summary>Path that was not found</summary>
    public string path { get; private set; }

    /// <summary>Pool of instances of this companion class.</summary>
    private static readonly ConcurrentStack<IOErr_NotFound> s_Pool = new();

    private IOErr_NotFound(
        string path,
        string message,
        ErrBase causedBy,
        Exception ex,
        CallerInfo callerInfo
    ) : base(IOErr.NotFound, message, causedBy, ex, callerInfo)
    {
        this.path = path;
    }

    public static IOErr_NotFound New(
        string path,
        string msg = null,
        ErrBase causedBy = null,
        Exception ex = null,
        // Caller info: omitting these three parameters makes the stack trace inaccurate
        [CallerMemberName] string callerMemberName = null,
        [CallerFilePath] string callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = default
    )
    {
        var message = msg ?? $"File {path} Do Not Exist";
        var callerInfo = new CallerInfo(callerMemberName, callerFilePath, callerLineNumber);
        if (s_Pool.TryPop(out var err))
        {
            err.Init(IOErr.NotFound, message, causedBy, ex, callerInfo);
            err.path = path;
            return err;
        }

        return new IOErr_NotFound(path, message, causedBy, ex, callerInfo);
    }

    // `Dispose()` on a Result that holds this error puts it back into this class's own pool
    protected override void OnReturn() => s_Pool.Push(this);
}

// usage
var err = IOErr_NotFound.New("/tmp/a.txt");
err.message; // "File /tmp/a.txt Do Not Exist"
err.path;    // "/tmp/a.txt"

Result<string, IOErr> held = err; // the Result takes ownership again
held.Dispose();                   // ... and releases the error back to the pool
```

The generated companion class *is* an `Err<TKind>`, so `Wrap` / `AddNote` / `Result` / `CastTo<T>` all keep working, and callers do not have to pass `CallerInfo` by hand while the stack trace stays accurate.

## License

PeroError is released under the [MIT License](LICENSE). Copyright (c) 2026, PeroPeroGames.