/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/12
 * Description: Tests For PeroTools2.Error.Result<>
 */

using System;
using NUnit.Framework;

// These tests verify the deprecated direct accessors and cache Results in fields on purpose
#pragma warning disable PERR100 // Results are stored in fields on purpose here
#pragma warning disable CS0618 // Type or member is obsolete

namespace PeroTools2.Error.Tests;

[TestFixture]
[TestOf(typeof(Result<>))]
public class ResultTest
{
    private const string success_message = "Success";
    private const string failed_message = "Failed";
    private Result<string, TestErrKind> m_SuccessResult;
    private Result<string, TestErrKind> m_FailedResult;

    [SetUp]
    public void Setup()
    {
        m_SuccessResult = success_message;
        m_FailedResult = Err.New(TestErrKind.HighLevel, failed_message);
    }

    [Test]
    public void IsOKTest()
    {
        Assert.That(m_SuccessResult.isOK, Is.True);
        Assert.That(m_FailedResult.isOK, Is.False);
    }

    [Test]
    public void GetValueTest()
    {
        Assert.That(m_SuccessResult.value, Is.EqualTo("Success"));
        Assert.Throws<InvalidOperationException>(() =>
        {
            string _ = m_FailedResult.value;
        });
    }

    [Test]
    public void ErrorTest()
    {
        Assert.That(m_SuccessResult.err, Is.Null);
        Assert.That(m_FailedResult.err, Is.InstanceOf<Err<TestErrKind>>());
    }

    [Test]
    public void ToStringTest()
    {
        string successStr = m_SuccessResult.ToString();
        string failedStr = m_FailedResult.ToString();

        Assert.That(successStr, Does.Contain(nameof(String)));
        Assert.That(successStr, Does.Contain(nameof(TestErrKind)));
        Assert.That(successStr, Does.Contain("Success"));
        // ReSharper disable once PossibleNullReferenceException
        Assert.That(failedStr, Does.Contain(m_FailedResult.err.kind.ToString()));
    }

    [Test]
    public void UnwrapTest()
    {
        // Unwrap
        Assert.That(m_SuccessResult.Unwrap(), Is.SameAs(success_message));
        Assert.Throws<ManualPanicException>(() =>
        {
            string _ = m_FailedResult.Unwrap();
        });
    }

    [TestCase("Default")]
    [TestCase(default(string))]
    public void ValueOrTest(string defaultValue)
    {
        Assert.That(m_SuccessResult.ValueOrDefault(), Is.SameAs(success_message));
        Assert.That(m_FailedResult.ValueOrDefault(), Is.SameAs(default(string)));

        Assert.That(m_SuccessResult.ValueOrDefault(defaultValue), Is.SameAs(success_message));
        Assert.That(m_FailedResult.ValueOrDefault(defaultValue), Is.SameAs(defaultValue));

        Assert.That(m_SuccessResult.ValueOr(_ => defaultValue), Is.SameAs(success_message));
        Assert.That(m_FailedResult.ValueOr(_ => defaultValue), Is.SameAs(defaultValue));
    }

    [Test]
    public void MatchTest()
    {
        Assert.That(
            m_SuccessResult.Match(value => $"ok:{value}", error => $"fail:{error.kind}"),
            Is.EqualTo("ok:Success"));
        Assert.That(
            m_FailedResult.Match(value => $"ok:{value}", error => $"fail:{error.kind}"),
            Is.EqualTo("fail:HighLevel"));
    }

    [Test]
    public void SwitchTest()
    {
        string receivedValue = null;
        Err<TestErrKind> receivedError = null;

        m_SuccessResult.Switch(value => receivedValue = value, error => receivedError = error);

        Assert.That(receivedValue, Is.SameAs(success_message));
        Assert.That(receivedError, Is.Null);

        receivedValue = null;

        m_FailedResult.Switch(value => receivedValue = value, error => receivedError = error);

        Assert.That(receivedValue, Is.Null);
        Assert.That(receivedError, Is.SameAs(m_FailedResult.err));
    }

    [Test]
    public void TryGetValueTest()
    {
        Assert.That(m_SuccessResult.TryGetValue(out string successValue), Is.True);
        Assert.That(successValue, Is.SameAs(success_message));
        Assert.That(m_SuccessResult.TryGetValue(out Err<TestErrKind> successError), Is.False);
        Assert.That(successError, Is.Null);

        Assert.That(m_FailedResult.TryGetValue(out Err<TestErrKind> failedError), Is.True);
        Assert.That(failedError, Is.SameAs(m_FailedResult.err));
        Assert.That(m_FailedResult.TryGetValue(out string _), Is.False);
    }

    [Test]
    public void OKWithNullValueTest()
    {
        // A null success value is still a successful Result: isOK, TryGetValue and ToString must agree
        Result<string, TestErrKind> result = Result<string, TestErrKind>.OK(null);

        Assert.That(result.isOK, Is.True);
        Assert.That(result.value, Is.Null);
        Assert.That(result.TryGetValue(out string nullValue), Is.True);
        Assert.That(nullValue, Is.Null);
        Assert.That(result.TryGetValue(out Err<TestErrKind> error), Is.False);
        Assert.That(error, Is.Null);
        Assert.That(result.ToString(), Does.Contain("OK"));

        result.Dispose();
    }

    [Test]
    public void DefaultResultTest()
    {
        // Zero-constructed Results are successful ones: none of the members may throw or disagree
        Result<string, TestErrKind> result = default;

        Assert.That(result.isOK, Is.True);
        Assert.That(result.TryGetValue(out string nullValue), Is.True);
        Assert.That(nullValue, Is.Null);
        Assert.That(result.TryGetValue(out Err<TestErrKind> _), Is.False);
        Assert.That(result.ToString(), Does.Contain("OK"));

        result.Dispose();
    }
}

[TestFixture]
[TestOf(typeof(Result<>))]
public class ResultWithoutValueTest
{
    private const string failed_message = "Failed";
    private Result<TestErrKind> m_SuccessResult;
    private Result<TestErrKind> m_FailedResult;

    [SetUp]
    public void Setup()
    {
        m_SuccessResult = Result<TestErrKind>.OK();
        m_FailedResult = Result<TestErrKind>.Fail(Err.New(TestErrKind.HighLevel, failed_message));
    }

    [Test]
    public void MatchTest()
    {
        Assert.That(
            m_SuccessResult.Match(() => "ok", error => $"fail:{error.kind}"),
            Is.EqualTo("ok"));
        Assert.That(
            m_FailedResult.Match(() => "ok", error => $"fail:{error.kind}"),
            Is.EqualTo("fail:HighLevel"));
    }

    [Test]
    public void SwitchTest()
    {
        bool okInvoked = false;
        Err<TestErrKind> receivedError = null;

        m_SuccessResult.Switch(() => okInvoked = true, error => receivedError = error);

        Assert.That(okInvoked, Is.True);
        Assert.That(receivedError, Is.Null);

        okInvoked = false;

        m_FailedResult.Switch(() => okInvoked = true, error => receivedError = error);

        Assert.That(okInvoked, Is.False);
        Assert.That(receivedError, Is.SameAs(m_FailedResult.err));
    }

    [Test]
    public void TryGetValueTest()
    {
        Assert.That(m_SuccessResult.TryGetValue(out Succeed? _), Is.True);
        Assert.That(m_SuccessResult.TryGetValue(out Err<TestErrKind> successError), Is.False);
        Assert.That(successError, Is.Null);

        Assert.That(m_FailedResult.TryGetValue(out Err<TestErrKind> failedError), Is.True);
        Assert.That(failedError, Is.SameAs(m_FailedResult.err));
        Assert.That(m_FailedResult.TryGetValue(out Succeed? _), Is.False);
    }
}