/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/12
 * Description: Tests For PeroTools2.Error.Err<T>
 */

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace PeroTools2.Error.Test.Editor
{
    internal enum TestErrKind
    {
        LowLevel,
        MidLevel,
        HighLevel,
    }

    [TestFixture]
    [TestOf(typeof(Err<>))]
    public sealed class ErrTests
    {
        private Err<TestErrKind> m_Err;

        private FieldInfo m_CallerMemberNameField;
        private FieldInfo m_CallerFilePathField;
        private FieldInfo m_CallerLineNumberField;
        private FieldInfo m_ExceptionField;

        private (string memberName, string filePath, int lineNum) m_LineInfo; // 创建m_Err那一行的行信息

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            var type = typeof(Err<TestErrKind>);
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            m_CallerMemberNameField = type.GetField("m_CallerMemberName", flags);
            m_CallerFilePathField = type.GetField("m_CallerFilePath", flags);
            m_CallerLineNumberField = type.GetField("m_CallerLineNumber", flags);
            m_ExceptionField = type.GetField("<exception>k__BackingField", flags);

            Assert.That(m_CallerMemberNameField, Is.Not.Null);
            Assert.That(m_CallerFilePathField, Is.Not.Null);
            Assert.That(m_CallerLineNumberField, Is.Not.Null);
            Assert.That(m_ExceptionField, Is.Not.Null);
        }

        [SetUp]
        public void Setup()
        {
            // high => mid => low => Exception
            var exception = CreateExceptionWithStackTrace("PanicTest"); // 创建带堆栈的Ex
            var lowErr = Err.New(TestErrKind.LowLevel, "Root Cause", null, exception);
            var midErr = Err.New(TestErrKind.MidLevel, "Mid Level Err Occurred", lowErr, null);
            var highErr = Err.New(TestErrKind.HighLevel, "High Level Err Occurred", midErr, null);
            m_LineInfo = GetCurrentLineInfo(); // 拿到highErr创建的那行的信息
            m_LineInfo.lineNum -= 1;
            m_Err = highErr;
        }

        [Test]
        public void Test_KindAndCausedBy()
        {
            Assert.That(m_Err.kind == TestErrKind.HighLevel);
            Assert.That(m_Err.causedBy is Err<TestErrKind> midErr && midErr.kind == TestErrKind.MidLevel);
            Assert.That(m_Err.causedBy.causedBy is Err<TestErrKind> lowErr && lowErr.kind == TestErrKind.LowLevel);
            Assert.That(m_Err.causedBy.causedBy.exception, Is.InstanceOf<ManualPanicException>());
            Assert.That(m_Err.causedBy.causedBy.causedBy, Is.Null);
        }

        [Test]
        public void Test_CausedByException()
        {
            Assert.That(m_Err.CausedByException(out var ex), Is.True);
            Assert.That(ex, Is.InstanceOf<ManualPanicException>());

            // ReSharper disable once PossibleNullReferenceException
            m_ExceptionField.SetValue(m_Err.causedBy.causedBy, null); // Set lowErr.exception To null
            Assert.That(m_Err.CausedByException(out _), Is.False);
        }

        [Test]
        public void Test_ManualPanic()
        {
            Assert.Catch<ManualPanicException>(() => throw m_Err.Panic());
            Assert.Catch<ManualPanicException>(() => m_Err.Panic());
        }

        [Test]
        public void Test_CallerInfo()
        {
            string callerMemberName = (string)m_CallerMemberNameField.GetValue(m_Err);
            string callerFilePath = (string)m_CallerFilePathField.GetValue(m_Err);
            int callerLineNumber = (int)m_CallerLineNumberField.GetValue(m_Err);

            Assert.That(callerMemberName == m_LineInfo.memberName && !string.IsNullOrWhiteSpace(callerMemberName));
            Assert.That(callerFilePath == m_LineInfo.filePath && !string.IsNullOrWhiteSpace(callerFilePath));
            Assert.That(callerLineNumber == m_LineInfo.lineNum && callerLineNumber != default);
        }

        [Test]
        public void Test_Note()
        {
            Assert.That(m_Err.notes.IsValueCreated, Is.False);
            m_Err.AddNote("note1").AddNote("note2");
            Assert.That(m_Err.notes.IsValueCreated, Is.True);
            Assert.That(m_Err.notes.Value, Is.EquivalentTo(new[] { "note1", "note2" }));
        }

        [Test]
        public void Test_ToString()
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

        #region Utils

        // 使用TryCatch创建的异常才有堆栈
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
}