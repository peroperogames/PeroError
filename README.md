# PeroError

`Err<TKind>` / `Result<T>` style error handling for C#.

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
        return Err.New(IOErr.NotFound, msg: $"File {filePath} NotFound")
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
        // an error caused by an exception should be created with exception.Warp()
        return e.Warp(IOErr.NotFound, msg: $"File {filePath} NotFound");
    }
    catch (Exception e)
    {
        // handle the remaining error kinds...
    }
}

// a function that handles errors
public Result<Config, ConfigErr> ReadConfig(string configPath)
{
    Result<string, IOErr> readResult = ReadFromFile(configPath);
    string configContent;
    if(readResult.isOK)
    {
        configContent = readResult.value;
    }
    else
    {
        switch(readResult.err.kind)
        {
            // Option 1: wrap the error and keep going
            case IOErr.NotFound:
            {
                // wrap IOErr as ConfigErr
                return readResult.err.Warp(ConfigErr.NotFound, msg: "WhatEver");
            }
            // Option 2: handle the error in place and let the current function continue
            case IOErr.Others:
            {
                log.Error(readResult.err);
                configContent = "Default Value"; // provide a default value
                break;
            }
            // Option 3: give up and throw it as an unrecoverable exception
            default:
                throw readResult.err.Panic();
        }
    }
    ... // the rest of the work
}
```

If your project is built with .NET 11, you can use pattern matching with union types:

```csharp
internal enum IOErr
{
    NotFound,
    Timeout
}
static string DealWithIOError(Result<string, IOErr> result) => result switch
{
    string value => $"No error, the value is {value}",
    Err<IOErr> err => err.kind switch
    {
        IOErr.NotFound => "Not found",
        IOErr.Timeout => "Timeout"
    }
};
```

### Error companion classes

When an error needs to carry data, generate a **companion error class** for it. Declare the message template and the arguments on the error enum member and `PeroTools2.PeroError.Generator` generates the whole class:

```csharp
public enum IOErr
{
    [ErrMsg("File {path} Do Not Exist")]              // supports format strings
    [ErrArg("path", typeof(string), "Path that was not found")] // ErrArg can be repeated
    NotFound,
}
```

That is equivalent to hand-writing the following (the generator emits exactly this):

```csharp
public sealed class IOErr_NotFound : Err<IOErr>
{
    /// <summary>Path that was not found</summary>
    public string path { get; }

    public IOErr_NotFound(
        string path,
        string msg = null,
        ErrBase causedBy = null,
        Exception ex = null,
        // Caller info: omitting these three parameters makes the stack trace inaccurate
        [CallerMemberName] string callerMemberName = null,
        [CallerFilePath] string callerFilePath = null,
        [CallerLineNumber] int callerLineNumber = default
    ) : base(
        IOErr.NotFound,
        msg ?? $"File {path} Do Not Exist",
        causedBy,
        ex,
        new CallerInfo(callerMemberName, callerFilePath, callerLineNumber)
    )
    {
        this.path = path;
    }
}

// usage
var err = new IOErr_NotFound("/tmp/a.txt");
err.message; // "File /tmp/a.txt Do Not Exist"
err.path;    // "/tmp/a.txt"
```

The generated companion class *is* an `Err<TKind>`, so `Warp` / `AddNote` / `Result` / `CastTo<T>` all keep working, and callers do not have to pass `CallerInfo` by hand while the stack trace stays accurate.