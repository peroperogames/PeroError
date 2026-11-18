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
public enum ConfigErr
{
    NotFound,
}

// a function that produces either a value or an error
static Result<string, IOErr> ReadFromFile(string filePath)
{
    // Usage 1: create the error directly
    if (!File.Exists(filePath))
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
static Result<string, ConfigErr> ReadConfig(string configPath)
{
    var readResult = ReadFromFile(configPath);
    string configContent;
    if (readResult.TryGetValue(out var value, out var err))
    {
        configContent = value;
        readResult.Dispose(); // released: we are done with it
    }
    else
    {
        switch (err.kind)
        {
            // Option 1: hand the error over to a new error (this consumes the Result)
            case IOErr.NotFound:
            {
                // wrap IOErr as ConfigErr
                return err.Wrap(ConfigErr.NotFound, message: "WhatEver");
            }
            // Option 2: handle the error in place, then release the Result
            case IOErr.Others:
            {
                Console.Error.WriteLine(err);
                configContent = "Default Value"; // provide a default value
                readResult.Dispose();
                break;
            }
            // Option 3: give up and turn it into an unrecoverable exception
            default:
            {
                var panic = err.Panic(); // snapshot the full error text first
                readResult.Dispose();
                throw panic;
            }
        }
    }
    // ...
    // the rest of the work
    return configContent;
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

### Propagating an error

`Wrap` re-classifies a failure: it creates a **new** error kind and keeps the current error as its `causedBy`. When nothing needs re-classifying and the method only passes the failure up, use `AddCallerFrame()` instead — it appends one stack frame to the **same** error and returns that very instance, so kind, message, notes and exception stay untouched while the trace still records every hop:

```csharp
public Result<string, IOErr> LoadConfig(string path)
{
    var readResult = ReadFromFile(path);
    if (!readResult.TryGetValue(out var content, out var err))
    {
        return err.AddCallerFrame(); // one more frame; ownership moves to the caller
    }

    return content;
}
```

### Printing an error

`ToString()` walks the whole chain already: the error kind, the simulated stack trace (outermost frame first), the message, the notes, the wrapped exception, and every `causedBy` recursively. Logging it puts the entire chain into one entry:

```csharp
var root = Err.New(IOErr.NotFound, "File /tmp/a.txt Do Not Exist").AddNote("retried with the default path");
var err = root.Wrap(ConfigErr.NotFound, "config not found");

Debug.LogError(err.ToString());
// Game.ConfigErr.NotFound
//   At: LoadConfig @ Assets/Config.cs:42
//   Message: config not found
// ========== CausedBy ==========
// Game.IOErr.NotFound
//   At: ReadFromFile @ Assets/Config.cs:21
//   Message: File /tmp/a.txt Do Not Exist
//   Notes:
//     0: retried with the default path
```

`CausedByException(out var ex)` returns the first exception found in the chain, and `Panic()` wraps that same text into a `ManualPanicException` when the error has to become an exception: `throw err.Panic();`.

### Result ownership

A `Result` owns an `Err` instance, and `Dispose()` hands it back to the internal pool. The analyzer therefore requires every `Result` to be consumed **exactly once**.

A `Result` counts as consumed when, and only when:

| # | How | Example |
| --- | --- | --- |
| 1 | it is disposed | `using var r = Load();` · `using (r) { }` · `r.Dispose();` |
| 2 | it is returned | `return r;` · `Result<string, IOErr> M() => Load();` |
| 3 | it is passed as an argument | `Handle(Load());` |
| 4 | its payload is taken out, or its error is handed over — wrapped into a new error, propagated with one more frame, or returned / passed on directly | `r.TryGetValue(out var value, out var err);` · `return err.Wrap(IOErr.NotFound, "not found");` · `return err.AddCallerFrame();` |

Reading `isOK`, inspecting the obsolete `err` / `value` accessors, calling `Unwrap()` / `Match()` / `Switch()`, or simply dropping the value does **not** consume it — the current method still owns it. `TryGetValue(...)` is different: it takes the value and the error out of the Result, so the Result is drained.

Consuming in two places that can never both run is not double consumption either: the two branches of one `if` / `switch` / `?:`, or the code after a branch that ends with `return` / `throw`.

Storing a Result in a field or property is not a consumption either — the error would sit in the member and never return to the pool. `yield return` does hand the Result over, and `ref` / `out` / `in` parameters stay owned by the caller, so they are not analyzed.

| ID | Reported when |
| --- | --- |
| `PERR100` | a produced Result is never consumed — `Load();`, `await LoadAsync();`, `Load().Unwrap();` |
| `PERR101` | a local Result is never consumed |
| `PERR102` | a Result is consumed more than once — `r.Dispose(); r.Dispose();`, `using var r = …; Handle(r);` |
| `PERR103` | a Result parameter is never consumed |
| `PERR104` | a Result is assigned over before it is consumed — `r = Load();` when `r` already held one |

Consuming twice is a real bug, not just a style issue: the error goes back to the pool while a wrapped error still references it as `causedBy`, so the chain can lose — or silently mix up — error info afterwards. A returned error is never pushed into the pool twice (the second `Dispose()` is a no-op), but the second owner is then looking at an instance that has already been handed out to somebody else.

```csharp
var readResult = ReadFromFile(path);
if (!readResult.TryGetValue(out var content, out var err))
{
    return err.Wrap(ConfigErr.NotFound, "config not found"); // ownership moves on with the new error
}

return content; // success: the value is out and there is no error left to release
```

The failure branch returns, so the two consumption points cannot both run on one path — that is exactly what the analyzer checks for.

> Turn a rule off with `dotnet_diagnostic.PERR102.severity = none` in `.editorconfig`, or with a `Default.ruleset` in Unity.

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

That is equivalent to hand-writing the following (the generator emits exactly this). The constructor stays **private**: instances always come from the pooled `New` factory, so a companion class costs no allocation once one instance is back in the pool:

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

    // `Dispose()` on the error — or on a Result that holds it — puts it back into this class's pool.
    // The argument values are dropped first, so the pool never keeps user data alive.
    protected override void OnReturn()
    {
        ClearArgs();
        s_Pool.Push(this);
    }

    private void ClearArgs()
    {
        this.path = default;
    }
}

// usage
var err = IOErr_NotFound.New("/tmp/a.txt");
err.message; // "File /tmp/a.txt Do Not Exist"
err.path;    // "/tmp/a.txt"

Result<string, IOErr> held = err; // the Result takes ownership again
held.Dispose();                   // ... and releases the error back to the pool

// an error knows how to hand itself back as well
using var other = IOErr_NotFound.New("/tmp/b.txt");
```

Every `Err<TKind>` is `IDisposable`, and returning an instance twice is safe: the second `Dispose()` is a no-op, so a struct copy of a `Result` can never push the same error into the pool twice.

The generated companion class *is* an `Err<TKind>`, so `Dispose` / `Wrap` / `AddNote` / `Result` / `CastTo<T>` all keep working, and callers do not have to pass `CallerInfo` by hand while the stack trace stays accurate.

## License

PeroError is released under the [MIT License](LICENSE). Copyright (c) 2026, PeroPeroGames.