/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 * Description: ErrArg Attribute
 */

using System;

namespace PeroTools2.Error;

/// <summary>
/// Used on an error enum field to declare one piece of data that the companion error class
/// carries.<br/>
/// Each <see cref="ErrArgAttribute"/> becomes a read-only property of the companion class and a
/// required constructor parameter.<br/>
/// An error enum field may declare any number of <see cref="ErrArgAttribute"/>s; they appear in the
/// constructor parameter list in declaration order.<br/>
/// </summary>
/// <remarks>
/// Only error enum fields that also declare <see cref="ErrMsgAttribute"/> get a companion class;
/// declaring this attribute alone produces no code and yields a compiler warning.
/// </remarks>
/// <example>
/// <code>
/// public enum IOErr
/// {
///     [ErrMsg("File {path} Do Not Exist")]
///     [ErrArg("path", typeof(string), "Path that was not found")]
///     NotFound,
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
public sealed class ErrArgAttribute : Attribute
{
    /// <summary>Parameter name; also the companion property name and the placeholder name</summary>
    public string name { get; }

    /// <summary>Parameter type</summary>
    public Type type { get; }

    /// <summary>Parameter description, written to the property and constructor parameter docs</summary>
    public string comment { get; }

    /// <summary>Declares one piece of data that the companion class carries</summary>
    /// <param name="name">Parameter name; also the companion property name and the placeholder name</param>
    /// <param name="type">Parameter type</param>
    public ErrArgAttribute(string name, Type type) : this(name, type, null) { }

    /// <summary>Declares one piece of data that the companion class carries</summary>
    /// <param name="name">Parameter name; also the companion property name and the placeholder name</param>
    /// <param name="type">Parameter type</param>
    /// <param name="comment">Parameter description, written to the property and parameter docs</param>
    public ErrArgAttribute(string name, Type type, string comment)
    {
        this.name = name;
        this.type = type;
        this.comment = comment;
    }
}