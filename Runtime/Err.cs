using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    [PublicAPI]
    public static class Err
    {
        [NotNull]
        public static Err<TKind> New<TKind>(
            TKind kind, string msg, IErr causedBy, Exception exception,
            [CallerMemberName] [CanBeNull] string callerMemberName = null,
            [CallerFilePath] [CanBeNull] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        ) where TKind : struct, Enum =>
            new Err<TKind>(
                kind, msg, causedBy, exception,
                new CallerInfo(callerMemberName, callerFilePath, callerLineNumber)
            );
    }

    [PublicAPI]
    public class Err<TKind> : IErr where TKind : struct, Enum
    {
        public virtual TKind kind { get; }
        public virtual IErr causedBy { get; }
        public virtual string message { get; }
        public virtual Exception exception { get; }

        public Lazy<IList<string>> notes { get; } = new Lazy<IList<string>>(() => new List<string>(5));

        protected string m_CallerMemberName;
        protected string m_CallerFilePath;
        protected int m_CallerLineNumber;

        public Err(
            TKind kind, string msg, IErr causedBy, Exception exception,
            [CallerMemberName] [CanBeNull] string callerMemberName = null,
            [CallerFilePath] [CanBeNull] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        )
        {
            this.kind = kind;
            message = msg;
            this.causedBy = causedBy;
            this.exception = exception;

            m_CallerFilePath = callerFilePath;
            m_CallerLineNumber = callerLineNumber;
            m_CallerMemberName = callerMemberName;
        }

        public Err(TKind kind, string msg, IErr causedBy, Exception exception, CallerInfo callerInfo)
            : this(
                kind, msg, causedBy, exception,
                // ReSharper disable ExplicitCallerInfoArgument
                callerInfo.callerMemberName,
                callerInfo.callerFilePath,
                callerInfo.callerLineNumber
                // ReSharper restore ExplicitCallerInfoArgument
            )
        {
        }

        [NotNull]
        public TErr CastTo<TErr>() where TErr : Err<TKind> => (TErr)this;

        /// <summary>
        /// 将该Err当作无法恢复的错误抛出(模仿Panic)
        /// </summary>
        /// <remarks>直接throw exception会导致堆栈丢失</remarks>
        public virtual Exception Panic() => throw new ManualPanicException(message, exception);

        /// <summary>
        /// 检查该错误是否是由异常引发的
        /// </summary>
        public bool CausedByException([CanBeNull] out Exception ex)
        {
            IErr current = this;
            while (current != null)
            {
                if (current.exception != null)
                {
                    ex = current.exception;
                    return true;
                }
                current = current.causedBy;
            }
            ex = null;
            return false;
        }

        public virtual void WriteToStringBuilder([NotNull] StringBuilder sb)
        {
            sb.AppendFormat("{0}.{1}\n", typeof(TKind).FullName, kind);
            sb.AppendFormat("  At: {0} @ {1}:{2}\n", m_CallerMemberName, m_CallerFilePath, m_CallerLineNumber);
            sb.AppendFormat("  Message: {0}\n", message);
            // Notes
            if (notes.IsValueCreated)
            {
                sb.AppendLine("  Notes:");
                for (int i = 0; i < notes.Value.Count; i++)
                {
                    sb.AppendFormat("    {0}: {1}\n", i, notes.Value[i]);
                }
            }
            // Exception
            if (exception == null)
            {
                sb.AppendLine("  Exception: null");
            }
            else
            {
                sb.AppendLine("  Exception:");
                sb.AppendLine("========== Exception Start ==========");
                sb.AppendLine(exception.ToString());
                // sb.AppendFormat("  ExMessage: {0}\n", exception.Message);
                // sb.AppendFormat("  TargetSite: {0}\n", exception.TargetSite);
                // sb.AppendFormat("  Source: {0}\n", exception.Source);
                // sb.AppendLine("  StackTrace:");
                // sb.AppendLine(exception.StackTrace);
                sb.AppendLine("========== Exception End ==========");
            }
            // CausedBy
            if (causedBy != null)
            {
                sb.AppendLine("========== CausedBy ==========");
                causedBy.WriteToStringBuilder(sb);
            }
        }

        [NotNull]
        public override string ToString()
        {
            // 递归调用ToString会New多个SB, 所以传入SB可以节约性能
            var sb = new StringBuilder();
            WriteToStringBuilder(sb);
            return sb.ToString();
        }
    }
}