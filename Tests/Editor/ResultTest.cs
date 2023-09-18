/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/12
 * Description: Tests For PeroTools2.Error.Result<>
 */

using System;
using NUnit.Framework;

namespace PeroTools2.Error.Tests.Editor
{
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
            m_FailedResult = Err.New(TestErrKind.HighLevel, failed_message, null, null);
        }

        [Test]
        public void Test_IsOK()
        {
            Assert.That(m_SuccessResult.isOK, Is.True);
            Assert.That(m_FailedResult.isOK, Is.False);
        }

        [Test]
        public void Test_GetValue()
        {
            Assert.That(m_SuccessResult.value, Is.EqualTo("Success"));
            Assert.Throws<InvalidOperationException>(() =>
            {
                string _ = m_FailedResult.value;
            });
        }

        [Test]
        public void Test_Error()
        {
            Assert.That(m_SuccessResult.err, Is.Null);
            Assert.That(m_FailedResult.err, Is.InstanceOf<Err<TestErrKind>>());
        }

        [Test]
        public void Test_ToString()
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
        public void Test_UnWarp()
        {
            // UnWarp
            Assert.That(m_SuccessResult.UnWarp(), Is.SameAs(success_message));
            Assert.Throws<ManualPanicException>(() =>
            {
                string _ = m_FailedResult.UnWarp();
            });
        }

        [TestCase("Default")]
        [TestCase(default(string))]
        public void Test_ValueOr(string defaultValue)
        {
            Assert.That(m_SuccessResult.ValueOrDefault(), Is.SameAs(success_message));
            Assert.That(m_FailedResult.ValueOrDefault(), Is.SameAs(default(string)));

            Assert.That(m_SuccessResult.ValueOrDefault(defaultValue), Is.SameAs(success_message));
            Assert.That(m_FailedResult.ValueOrDefault(defaultValue), Is.SameAs(defaultValue));

            Assert.That(m_SuccessResult.ValueOr(_ => defaultValue), Is.SameAs(success_message));
            Assert.That(m_FailedResult.ValueOr(_ => defaultValue), Is.SameAs(defaultValue));
        }
    }
}