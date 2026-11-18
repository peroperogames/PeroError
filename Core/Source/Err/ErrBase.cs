/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: Err BaseClass
 */

using System;
using System.Text;

namespace PeroTools2.Error;

/// <summary>Base class of all error types</summary>
public abstract class ErrBase
{
    /// <summary>The error that caused the current error.</summary>
    public abstract ErrBase? causedBy { get; }

    /// <summary>Gets the message that describes the current error</summary>
    public abstract string? message { get; }

    /// <summary>The exception that caused the current error</summary>
    public abstract Exception? exception { get; }

    /// <summary>
    /// Treats the current error as unrecoverable and returns a <see cref="ManualPanicException"/>,
    /// which has to be thrown manually<br/>
    /// Usage: <c>throw err.Panic();</c><br/>
    /// </summary>
    /// <remarks>Throwing it manually lets C# see the code path ends and saves one stack frame</remarks>
    public abstract Exception Panic();

    /// <summary>Checks whether the error chain contains an exception.</summary>
    /// <param name="ex">Receives the exception when there is one</param>
    public abstract bool CausedByException(out Exception? ex);

    /// <summary>Formats this instance and writes it into the StringBuilder</summary>
    protected internal abstract void WriteToStringBuilder(StringBuilder sb);

    /// <summary>Adds extra information to the current error to describe it better</summary>
    protected internal abstract void AddNote(string note);

    /// <summary>Reset error state.</summary>
    protected internal abstract void Reset();
}

/// <summary>Fluent extension methods for <see cref="ErrBase"/></summary>
public static class ErrBaseExtensions
{
    /// <summary>Adds extra information to the current error to describe it better</summary>
    public static TErr AddNote<TErr>(this TErr err, string note) where TErr : ErrBase
    {
        err.AddNote(note);
        return err;
    }
}