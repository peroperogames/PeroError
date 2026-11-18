/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/12
 * Description: Tests For PeroTools2.Error.Err<T>
 */

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace PeroTools2.Error.Tests;

internal enum TestErrKind
{
    LowLevel,
    MidLevel,
    HighLevel
}

[TestFixture]
[TestOf(typeof(Err<>))]
public sealed class ErrTests
{
    private Err<TestErrKind> m_Err;

    private FieldInfo m_ExceptionField;
    private FieldInfo m_NotesField;
    private FieldInfo m_StackTrace;

    private (string memberName, string filePath, int lineNum) m_LineInfo; // Line info where m_Err is created

    [OneTimeSetUp]
    public void OneTimeSetup()
    {
        var type = typeof(Err<TestErrKind>);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        m_ExceptionField = type.GetField("m_Exception", flags);
        m_NotesField = type.GetField("<notes>k__BackingField", flags);
        m_StackTrace = type.GetField("stackTrace", flags);

        Assert.That(m_ExceptionField, Is.Not.Null);
        Assert.That(m_NotesField, Is.Not.Null);
        Assert.That(m_StackTrace, Is.Not.Null);
    }

    [SetUp]
    public void Setup()
    {
        // high => mid => low => Exception
        var exception = CreateExceptionWithStackTrace("PanicTest"); // Create an exception with a stack trace
        var lowErr = exception.Wrap(TestErrKind.LowLevel, "Root Cause");
        var midErr = lowErr.Wrap(TestErrKind.MidLevel, "Mid Level Err Occurred");
        var highErr = midErr.Wrap(TestErrKind.HighLevel, "High Level Err Occurred");
        m_LineInfo = GetCurrentLineInfo(); // Capture the line info of the line that creates highErr
        m_LineInfo.lineNum -= 1;
        m_Err = highErr;
    }

    [Test]
    public void KindAndCausedByTest()
    {
        Assert.That(m_Err.kind == TestErrKind.HighLevel);
        Assert.That(m_Err.causedBy is Err<TestErrKind> { kind: TestErrKind.MidLevel });
        Assert.That(m_Err.causedBy.causedBy is Err<TestErrKind> { kind: TestErrKind.LowLevel });
        Assert.That(m_Err.causedBy.causedBy.exception, Is.InstanceOf<ManualPanicException>());
        Assert.That(m_Err.causedBy.causedBy.causedBy, Is.Null);
    }

    [Test]
    public void CausedByExceptionTest()
    {
        Assert.That(m_Err.CausedByException(out var ex), Is.True);
        Assert.That(ex, Is.InstanceOf<ManualPanicException>());

        // ReSharper disable once PossibleNullReferenceException
        m_ExceptionField.SetValue(m_Err.causedBy.causedBy, null); // Set lowErr.exception To null
        Assert.That(m_Err.CausedByException(out _), Is.False);
    }

    [Test]
    public void ManualPanicTest()
    {
        Assert.Catch<ManualPanicException>(() => throw m_Err.Panic());
    }

    [Test]
    public void CallerInfoTest()
    {
        var stackTrace = (List<CallerInfo>)m_StackTrace.GetValue(m_Err);
        Assert.That(stackTrace, Has.Count.EqualTo(1));

        string callerMemberName = stackTrace[0].callerMemberName;
        string callerFilePath = stackTrace[0].callerFilePath;
        int callerLineNumber = stackTrace[0].callerLineNumber;

        Assert.That(callerMemberName == m_LineInfo.memberName && !string.IsNullOrWhiteSpace(callerMemberName));
        Assert.That(callerFilePath == m_LineInfo.filePath && !string.IsNullOrWhiteSpace(callerFilePath));
        Assert.That(callerLineNumber == m_LineInfo.lineNum && callerLineNumber != default);
    }

    [Test]
    public void NoteTest()
    {
        Assert.That(GetNotes(m_Err).IsValueCreated, Is.False);
        m_Err.AddNote("note1").AddNote("note2");
        Assert.That(GetNotes(m_Err).IsValueCreated, Is.True);
        Assert.That(GetNotes(m_Err).Value, Is.EquivalentTo(new[] { "note1", "note2" }));
        return;

        Lazy<List<string>> GetNotes(Err<TestErrKind> err) => (Lazy<List<string>>)m_NotesField.GetValue(err);
    }

    [Test]
    public void ToStringTest()
    {
        m_Err.AddNote("note1").AddNote("note2");
        string str = m_Err.ToString();
        Assert.That(str, Does.Contain(m_Err.kind.ToString()));

        // ReSharper disable PossibleNullReferenceException
        Assert.That(str, Does.Contain(m_Err.message));
        Assert.That(str, Does.Contain(m_Err.causedBy.message));
        Assert.That(str, Does.Contain(m_Err.causedBy.causedBy.message));

        Assert.That(str, Does.Contain("note1"));
        Assert.That(str, Does.Contain("note2"));

        Assert.That(str, Does.Contain(m_Err.causedBy.causedBy.exception.Message));
        // ReSharper restore PossibleNullReferenceException
    }

    [Test]
    public void AddCallerFrameTest()
    {
        // AddCallerFrame appends a stack frame to the same instance
        var err = Err.New(TestErrKind.MidLevel, "in place");
        Err<TestErrKind> wrapped = err.AddCallerFrame();

        Assert.That(wrapped, Is.SameAs(err));
        Assert.That(wrapped.kind, Is.EqualTo(TestErrKind.MidLevel));
        Assert.That(wrapped.message, Is.EqualTo("in place"));
        Assert.That(wrapped.causedBy, Is.Null);
    }

    [Test]
    public void WrapCreatesNewErrorTest()
    {
        // The kind/message Wrap overloads create a new error and leave the original one untouched
        var lowLevel = Err.New(TestErrKind.LowLevel, "root");
        Err<TestErrKind> wrapped = lowLevel.Wrap(TestErrKind.HighLevel, "high");

        Assert.That(wrapped, Is.Not.SameAs(lowLevel));
        Assert.That(wrapped.kind, Is.EqualTo(TestErrKind.HighLevel));
        Assert.That(wrapped.message, Is.EqualTo("high"));
        Assert.That(wrapped.causedBy, Is.SameAs(lowLevel));
        Assert.That(lowLevel.causedBy, Is.Null);
    }

    [Test]
    public void CastToKeepsStoredErrorInfoTest()
    {
        var exception = CreateExceptionWithStackTrace("CastToTest");
        var root = exception.Wrap(TestErrKind.LowLevel, "root cause");
        var source = root.Wrap(TestErrKind.MidLevel, "mid level").AddCallerFrame(); // two stack frames

        var casted = source.CastTo(TestErrKind.HighLevel, "high level");

        Assert.That(casted.kind, Is.EqualTo(TestErrKind.HighLevel));
        Assert.That(casted.message, Is.EqualTo("high level"));
        Assert.That(casted.causedBy, Is.SameAs(root));
        Assert.That(casted.CausedByException(out var found), Is.True);
        Assert.That(found, Is.SameAs(exception));
        Assert.That(casted.ToString(), Does.Contain("high level"));
    }

    [Test]
    public void CastToCopiesStackTraceTest()
    {
        // The two errors must not share one mutable frame list: clearing one would clear the other
        var source = Err.New(TestErrKind.MidLevel, "mid level").AddCallerFrame();
        var casted = source.CastTo(TestErrKind.HighLevel, "high level");

        var sourceFrames = (List<CallerInfo>)m_StackTrace.GetValue(source);
        var castedFrames = (List<CallerInfo>)m_StackTrace.GetValue(casted);

        Assert.That(castedFrames, Is.Not.SameAs(sourceFrames));
        Assert.That(castedFrames, Has.Count.EqualTo(sourceFrames.Count));
        Assert.That(castedFrames, Is.EqualTo(sourceFrames));
    }

    [Test]
    public void ErrorIsDisposableTest()
    {
        var err = Err.New(TestErrKind.LowLevel, "disposable");
        Assert.That(err, Is.InstanceOf<IDisposable>());

        err.Dispose();

        // Disposing hands the instance back to the pool, which clears it
        Assert.That(err.message, Is.Empty);
        Assert.That(err.causedBy, Is.Null);
    }

    [Test]
    public void DisposeTwiceReturnsToPoolOnceTest()
    {
        var err = Err.New(TestErrKind.LowLevel, "disposable");
        err.Dispose();
        err.Dispose(); // must not push the same instance into the pool a second time

        var first = Err.New(TestErrKind.LowLevel, "first");
        var second = Err.New(TestErrKind.LowLevel, "second");

        // A second push would hand the very same instance out twice
        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void TwoResultsSharingAnErrorReturnItOnceTest()
    {
        var err = Err.New(TestErrKind.LowLevel, "shared");
        Result<TestErrKind> first = err;
        Result<TestErrKind> second = first; // both Results hold the same error

        first.Dispose();
        second.Dispose(); // must be a no-op

        var rentedFirst = Err.New(TestErrKind.LowLevel, "first");
        var rentedSecond = Err.New(TestErrKind.LowLevel, "second");

        Assert.That(rentedSecond, Is.Not.SameAs(rentedFirst));
    }

    [Test]
    public void ReturnedErrorKeepsCausedByChainTest()
    {
        var root = Err.New(TestErrKind.LowLevel, "root");
        var wrapper = root.Wrap(TestErrKind.HighLevel, "high");

        wrapper.Dispose();

        // Only the returned instance is cleared; the errors it points at belong to someone else
        Assert.That(wrapper.causedBy, Is.Null);
        Assert.That(root.message, Is.EqualTo("root"));
        Assert.That(root.kind, Is.EqualTo(TestErrKind.LowLevel));
    }

    #region Utils

    // Only an exception thrown inside try/catch has a stack trace
    private static Exception CreateExceptionWithStackTrace(string message)
    {
        try
        {
            throw new ManualPanicException(message);
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static (string memberName, string filePath, int lineNum) GetCurrentLineInfo(
        [CallerMemberName] string memberName = null,
        [CallerFilePath] string filePath = null,
        [CallerLineNumber] int lineNum = default
    ) => (memberName, filePath, lineNum);

    #endregion
}