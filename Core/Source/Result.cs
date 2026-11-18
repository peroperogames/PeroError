/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX, Gatongone
 * Created On: 2023/09/11
 * Description: Result Type For C#
 */

#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace PeroTools2.Error;

/// <summary>
/// Result type that carries no value
/// </summary>
/// <typeparam name="TErrKind">Error enum type</typeparam>
#if NET11_0_OR_GREATER
[System.Runtime.CompilerServices.Union]
#endif
public readonly struct Result<TErrKind> : Result<TErrKind>.IUnionMembers, IDisposable where TErrKind : struct, Enum
{
    /// <summary>
    /// Holds the error info when failed (always present). Null when succeeded
    /// </summary>
    [Obsolete("Don't use err directly.")]
    public Err<TErrKind>? err { get; }

    /// <summary>
    /// State of the Result, succeeded or failed. When failed, err is always non-null
    /// </summary>
    [Obsolete("Don't use isOK directly.")]
    public bool isOK => err == null; // Zero-constructible struct: no isOK field, so illegal states cannot exist

    // The rent the error belongs to: a copy of this struct must not return an error that has already
    // been handed out to somebody else in the meantime
    private readonly int m_ErrVersion;

    object? IUnionMembers.Value => isOK ? Succeed.instance : err;

    /// <summary>Tries to get the error; returns true only when the Result failed</summary>
    /// <param name="value">Receives the error when the Result failed</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetValue([NotNullWhen(true)] out Err<TErrKind>? value)
    {
        value = err;
        return !isOK;
    }

    /// <summary>Tries to get the success marker; returns true only when the Result succeeded</summary>
    /// <param name="value">Receives the success marker when the Result succeeded</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetValue([NotNullWhen(true)] out Succeed? value)
    {
        value = isOK ? Succeed.instance : null;
        return isOK;
    }

    private Result(Err<TErrKind>? err)
    {
        if (err is null)
        {
            return;
        }
        this.err = err;
        m_ErrVersion = err.rentVersion;
    }

    /// <summary>Creates a successful Result</summary>
    public static Result<TErrKind> OK() => new();

    /// <summary>Creates a failed Result</summary>
    public static Result<TErrKind> Fail(Err<TErrKind> err) => new(err);

    // Err => Result<T>
    /// <summary>Treats an error directly as a failed Result</summary>
    /// <remarks>The Result takes ownership of the error: disposing it returns the error to the pool</remarks>
    public static implicit operator Result<TErrKind>(Err<TErrKind> err) => Fail(err);

    /// <summary>
    /// Asserts that the Result succeeded. Invokes <see cref="ErrBase.Panic"/> when it did not
    /// </summary>
    /// <exception cref="ManualPanicException">Thrown when called on a failed Result</exception>
    /// <remarks>
    /// This call takes the Result over: it is disposed on both paths. A failed Result hands its error
    /// back to the pool (the panic already holds the full text), a successful one has nothing to return.
    /// </remarks>
    public void Unwrap()
    {
        try
        {
            if (!isOK)
            {
                throw err!.Panic(); // When !isOK, err is always non-null; the panic snapshots the text
            }
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// Runs one of the handlers depending on the state of the Result and returns its result
    /// </summary>
    /// <typeparam name="TResult">Type produced by the handlers</typeparam>
    /// <param name="ok">Invoked when the Result succeeded</param>
    /// <param name="fail">Invoked when the Result failed; receives the error</param>
    /// <returns>The result of the invoked handler</returns>
    public TResult Match<TResult>(Func<TResult> ok, Func<Err<TErrKind>, TResult> fail) =>
        isOK ? ok.Invoke() : fail.Invoke(err!);

    /// <summary>
    /// Runs one of the handlers depending on the state of the Result
    /// </summary>
    /// <param name="ok">Invoked when the Result succeeded</param>
    /// <param name="fail">Invoked when the Result failed; receives the error</param>
    public void Switch(Action ok, Action<Err<TErrKind>> fail)
    {
        if (isOK) ok.Invoke();
        else fail.Invoke(err!);
    }

    /// <summary>Formats the state of the Result for output</summary>
    public override string ToString() => isOK
        ? $"Result<{typeof(TErrKind).Name}>.OK()"
        : $"Result<{typeof(TErrKind).Name}>.Fail({err!.kind.ToString()})";

    /// <summary>Members that the compiler-generated union pattern matching relies on</summary>
    public interface IUnionMembers
    {
        /// <summary>Creates a successful Result from the success marker</summary>
        static Result<TErrKind> Create(Succeed? _) => new(null);

        /// <summary>Creates a failed Result from an error</summary>
        static Result<TErrKind> Create(Err<TErrKind>? value) => new(value);

        /// <summary>Current state: the success marker or the error</summary>
        object? Value { get; }

        /// <summary>Tries to get the error; returns true only when the Result failed</summary>
        bool TryGetValue([NotNullWhen(true)] out Err<TErrKind>? value);

        /// <summary>Tries to get the success marker; returns true only when the Result succeeded</summary>
        bool TryGetValue([NotNullWhen(true)] out Succeed? value);
    }

    /// <summary>
    /// Mark this result has been fully resolved.
    /// </summary>
    public void Dispose()
    {
        if (err != null)
        {
            Err<TErrKind>.Return(err, m_ErrVersion);
        }
    }
}

/// <summary>Result type that carries a value</summary>
/// <typeparam name="T">Type of the value on success</typeparam>
/// <typeparam name="TErrKind">Error enum type</typeparam>
#if NET11_0_OR_GREATER
[System.Runtime.CompilerServices.Union]
#endif
public readonly struct Result<T, TErrKind> : Result<T, TErrKind>.IUnionMembers, IDisposable where TErrKind : struct, Enum
{
    /// <summary>
    /// Holds the value on success, otherwise default
    /// </summary>
    private readonly T? m_Value;

    /// <summary>
    /// Holds the error info when failed (always present). Null when succeeded
    /// </summary>
    [Obsolete("Don't use err directly.")]
    public Err<TErrKind>? err { get; }

    /// <summary>
    /// State of the Result, succeeded or failed. When failed, err is always non-null
    /// </summary>
    [Obsolete("Don't use value directly.")]
    public T value => isOK
        ? m_Value! // A successful Result created through OK() always carries a non-null value
        : throw new InvalidOperationException("Access Value From A Failed Result");

    /// <summary>State of the Result, succeeded or failed. When failed, err is always non-null</summary>
    // Structs are always zero-constructible, so no separate isOK field is used, to prevent illegal states
    [Obsolete("Don't use isOK directly.")]
    public bool isOK => err == null;

    // The rent the error belongs to: a copy of this struct must not return an error that has already
    // been handed out to somebody else in the meantime
    private readonly int m_ErrVersion;

    object? IUnionMembers.Value => isOK ? m_Value : err;

    /// <summary>Tries to get the value; returns true only when the Result succeeded</summary>
    /// <param name="result">Receives the value when the Result succeeded (it may be null)</param>
    /// <param name="error">Receives the error when the Result failed</param>
    public bool TryGetValue(out T? result, [NotNullWhen(false)] out Err<TErrKind>? error)
    {
        result = m_Value;
        error = err;
        return isOK;
    }

    /// <summary>Tries to get the value; returns true only when the Result succeeded</summary>
    /// <param name="result">Receives the value when the Result succeeded (it may be null)</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetValue(out T? result)
    {
        result = m_Value;
        return isOK;
    }

    /// <summary>Tries to get the error; returns true only when the Result failed</summary>
    /// <param name="error">Receives the error when the Result failed</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool TryGetValue([NotNullWhen(true)] out Err<TErrKind>? error)
    {
        error = err;
        return !isOK;
    }

    private Result(T? value)
    {
        // Even a null success value is a successful Result, so the state is recorded unconditionally.
        m_Value = value;
        err = null;
        m_ErrVersion = 0;
    }

    private Result(Err<TErrKind>? err)
    {
        if (err is not null)
        {
            m_Value = default;
            this.err = err;
            m_ErrVersion = err.rentVersion;
        }
    }

    /// <summary>Creates a successful Result</summary>
    public static Result<T, TErrKind> OK(T value) => new(value);

    /// <summary>Creates a failed Result</summary>
    public static Result<T, TErrKind> Fail(Err<TErrKind> err) => new(err);

    // T => Result<T>
    /// <summary>Treats a success value directly as a successful Result</summary>
    public static implicit operator Result<T, TErrKind>(T value) => OK(value);

    // Err => Result<T>
    /// <summary>Treats an error directly as a failed Result</summary>
    /// <remarks>The Result takes ownership of the error: disposing it returns the error to the pool</remarks>
    public static implicit operator Result<T, TErrKind>(Err<TErrKind> err) => Fail(err);

    /// <summary>Formats the state of the Result for output</summary>
    // m_Value is interpolated directly: a null success value must not throw from ToString()
    public override string ToString() => isOK
        ? $"Result<{typeof(T).Name}, {typeof(TErrKind).Name}>.OK({m_Value})"
        : $"Result<{typeof(T).Name}, {typeof(TErrKind).Name}>.Fail({err!.kind.ToString()})";

    /// <summary>
    /// Mark this result has been fully resolved.
    /// </summary>
    public void Dispose()
    {
        if (err != null)
        {
            Err<TErrKind>.Return(err, m_ErrVersion);
        }
    }

    #region Fluent

    /// <summary>
    /// Asserts that the Result succeeded.
    /// Returns the value on success,
    /// and invokes <see cref="ErrBase.Panic"/> when it did not succeed
    /// </summary>
    /// <exception cref="ManualPanicException">Thrown when called on a failed Result</exception>
    /// <remarks>
    /// This call takes the Result over: it is disposed on both paths. A failed Result hands its error
    /// back to the pool (the panic already holds the full text), a successful one has nothing to return.
    /// </remarks>
    public T Unwrap()
    {
        try
        {
            // When !isOK, err is always non-null; the panic snapshots the text before the dispose below
            return isOK ? value : throw err!.Panic();
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// Returns the value when the Result succeeded, otherwise returns the default value
    /// </summary>
    /// <param name="default">Value used when the Result failed</param>
    /// <remarks>
    /// This call takes the Result over: it is disposed on both paths, and a failed Result's error is
    /// dropped here instead of being handed on.
    /// </remarks>
    [return: MaybeNull]
    public T ValueOrDefault([AllowNull] T @default = default)
    {
        try
        {
            return isOK ? value : @default;
        }
        finally
        {
            Dispose();
        }
    }

    /// <summary>
    /// Returns the value when the Result succeeded; otherwise calls the given function to handle
    /// the error and produce a value
    /// </summary>
    public T ValueOr(Func<Err<TErrKind>, T> whenFailed) => isOK ? value : whenFailed.Invoke(err!);

    /// <summary>
    /// Runs one of the handlers depending on the state of the Result and returns its result
    /// </summary>
    /// <typeparam name="TResult">Type produced by the handlers</typeparam>
    /// <param name="ok">Invoked when the Result succeeded; receives the value</param>
    /// <param name="fail">Invoked when the Result failed; receives the error</param>
    /// <returns>The result of the invoked handler</returns>
    public TResult Match<TResult>(
        Func<T, TResult> ok,
        Func<Err<TErrKind>, TResult> fail)
        => isOK ? ok.Invoke(value) : fail.Invoke(err!);

    /// <summary>
    /// Runs one of the handlers depending on the state of the Result
    /// </summary>
    /// <param name="ok">Invoked when the Result succeeded; receives the value</param>
    /// <param name="fail">Invoked when the Result failed; receives the error</param>
    public void Switch(Action<T> ok, Action<Err<TErrKind>> fail)
    {
        if (isOK) ok.Invoke(value);
        else fail.Invoke(err!);
    }

    /// <summary>
    /// Casts Result&lt;T, TErrKind> to Result&lt;TR, TErrKind&gt;
    /// </summary>
    /// <remarks>
    /// On failure the returned Result takes over the error of this one, so this Result must not be
    /// consumed afterwards — the same error would be handed back to the pool twice.
    /// </remarks>
    public Result<TR, TErrKind> Cast<TR>() where TR : T => isOK
        ? Result<TR, TErrKind>.OK((TR)value!)
        : Result<TR, TErrKind>.Fail(err!);

    /// <summary>
    /// Converts Result&lt;T, TErrKind> to Result&lt;TR, TErrKind&gt;
    /// </summary>
    /// <remarks>
    /// On failure the returned Result takes over the error of this one, so this Result must not be
    /// consumed afterwards — the same error would be handed back to the pool twice.
    /// </remarks>
    public Result<TR, TErrKind> Map<TR>(Func<T, TR> mapFunc) => isOK
        ? Result<TR, TErrKind>.OK(mapFunc.Invoke(value))
        : Result<TR, TErrKind>.Fail(err!);

    #endregion

    /// <summary>Members that the compiler-generated union pattern matching relies on</summary>
    public interface IUnionMembers
    {
        /// <summary>Creates a successful Result from the value</summary>
        static Result<T, TErrKind> Create(T? value) => new(value);

        /// <summary>Creates a failed Result from an error</summary>
        static Result<T, TErrKind> Create(Err<TErrKind>? value) => new(value);

        /// <summary>Current state: the value or the error</summary>
        object? Value { get; }

        /// <summary>Tries to get the value; returns true only when the Result succeeded</summary>
        bool TryGetValue(out T? result);

        /// <summary>Tries to get the error; returns true only when the Result failed</summary>
        bool TryGetValue([NotNullWhen(true)] out Err<TErrKind>? error);
    }
}
#pragma warning restore CS0618 // Type or member is obsolete
