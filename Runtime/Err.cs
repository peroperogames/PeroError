/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: Err Class
 */

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
        /// <summary>
        /// 创建<see cref="Err{TKind}"/>实例.<br/>
        /// 如果错误枚举定义有伴生类, 则应该调用伴生类的构造方法而不是此方法
        /// </summary>
        /// <typeparam name="TKind">错误枚举类型</typeparam>
        /// <param name="kind">错误枚举值</param>
        /// <param name="message">错误消息</param>
        /// <param name="causedBy">造成本错误的错误. 如果本错误就是源头, 则填null</param>
        /// <param name="exception">造成本错误的异常. 如果本错误不是由异常造成的, 则填null</param>
        /// <param name="callerMemberName">不用填. 由编译器生成</param>
        /// <param name="callerFilePath">不用填. 由编译器生成</param>
        /// <param name="callerLineNumber">不用填. 由编译器生成</param>
        /// <remarks>
        /// 本方法应该用于直接创建新错误.
        /// 如果需要对Err的创建进行包装, 则应该使用<see cref="Err{TKind}"/>的构造函数并正确传入<see cref="CallerInfo"/>来确保调用方信息正确.
        /// </remarks>
        [NotNull]
        public static Err<TKind> New<TKind>(
            TKind kind, string message, IErr causedBy, Exception exception,
            [CallerMemberName] string callerMemberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        ) where TKind : struct, Enum =>
            new Err<TKind>(
                kind, message, causedBy, exception,
                new CallerInfo(callerMemberName, callerFilePath, callerLineNumber)
            );
    }

    [PublicAPI]
    public class Err<TKind> : IErr where TKind : struct, Enum
    {
        /// <summary>当前错误对应的错误枚举</summary>
        public virtual TKind kind { get; }

        /// <inheritdoc cref="IErr.causedBy"/>
        public virtual IErr causedBy { get; }

        /// <inheritdoc cref="IErr.message"/>
        public virtual string message { get; }

        /// <inheritdoc cref="IErr.exception"/>
        public virtual Exception exception { get; }

        /// <inheritdoc cref="IErr.notes"/>
        public Lazy<IList<string>> notes { get; } = new Lazy<IList<string>>(() => new List<string>(5));

        protected readonly string m_CallerMemberName;
        protected readonly string m_CallerFilePath;
        protected readonly int m_CallerLineNumber;

        /// <summary>
        /// 不应该直接使用.<br/>
        /// 当用于新建错误时，应当使用<see cref="Err.New{TKind}"/>代替
        /// </summary>
        public Err(TKind kind, string message, IErr causedBy, Exception exception, CallerInfo callerInfo)
        {
            this.kind = kind;
            this.message = message;
            this.causedBy = causedBy;
            this.exception = exception;

            m_CallerFilePath = callerInfo.callerFilePath;
            m_CallerLineNumber = callerInfo.callerLineNumber;
            m_CallerMemberName = callerInfo.callerMemberName;
        }

        /// <summary>
        /// 快捷的将<see cref="Err{TKind}"/>转换为错误枚举的对应伴生类(如果有)
        /// </summary>
        [NotNull]
        public TErr CastTo<TErr>() where TErr : Err<TKind> => (TErr)this;

        /// <inheritdoc cref="IErr.Panic"/>
        public virtual Exception Panic() // 直接throw exception会导致堆栈丢失
            => throw new ManualPanicException(ToString());

        /// <inheritdoc cref="IErr.CausedByException"/>
        public bool CausedByException(out Exception ex)
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

        /// <inheritdoc cref="IErr.WriteToStringBuilder"/>
        public virtual void WriteToStringBuilder([NotNull] StringBuilder sb)
        {
            // 后续可以替换成PeroString拼接
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
            if (exception != null)
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