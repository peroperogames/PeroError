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
        /// <param name="callerMemberName">不用填. 由编译器生成</param>
        /// <param name="callerFilePath">不用填. 由编译器生成</param>
        /// <param name="callerLineNumber">不用填. 由编译器生成</param>
        /// <remarks>
        /// 本方法应该用于直接创建新错误.
        /// 如果需要对Err的创建进行包装, 则应该使用<see cref="Err{TKind}"/>的构造函数并正确传入<see cref="CallerInfo"/>来确保调用方信息正确.
        /// </remarks>
        [NotNull]
        public static Err<TKind> New<TKind>(
            TKind kind, string message,
            [CallerMemberName] string callerMemberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        ) where TKind : struct, Enum =>
            new Err<TKind>(
                kind, message, null, null,
                new CallerInfo(callerMemberName, callerFilePath, callerLineNumber)
            );

        /// <summary>
        /// 将一个底层错误包装为新的错误.
        /// </summary>
        /// <typeparam name="TKind">错误枚举类型</typeparam>
        /// <param name="causedBy">被包装的底层错误</param>
        /// <param name="kind">错误枚举值</param>
        /// <param name="message">错误消息</param>
        /// <param name="callerMemberName">不用填. 由编译器生成</param>
        /// <param name="callerFilePath">不用填. 由编译器生成</param>
        /// <param name="callerLineNumber">不用填. 由编译器生成</param>
        public static Err<TKind> Warp<TKind>(
            this ErrBase causedBy, TKind kind, string message,
            [CallerMemberName] string callerMemberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        ) where TKind : struct, Enum =>
            new Err<TKind>(
                kind, message, causedBy, null,
                new CallerInfo(callerMemberName, callerFilePath, callerLineNumber)
            );

        /// <summary>
        /// 将一个底层错误包装为新的错误, 但依然使用旧的ErrKind和Message.
        /// </summary>
        /// <typeparam name="TKind">错误枚举类型</typeparam>
        /// <param name="causedBy">被包装的底层错误</param>
        /// <param name="callerMemberName">不用填. 由编译器生成</param>
        /// <param name="callerFilePath">不用填. 由编译器生成</param>
        /// <param name="callerLineNumber">不用填. 由编译器生成</param>
        public static Err<TKind> Warp<TKind>(
            this Err<TKind> causedBy,
            [CallerMemberName] string callerMemberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        ) where TKind : struct, Enum
        {
            causedBy.AddStackTrace(new CallerInfo(callerMemberName, callerFilePath, callerLineNumber));
            return causedBy;
        }

        // TODO 伴生类自动检测，自动构造，参数怎么传进来？

        /// <summary>
        /// 将一个Exception包装为新的错误.
        /// </summary>
        /// <typeparam name="TKind">错误枚举类型</typeparam>
        /// <param name="ex">被包装的底层异常</param>
        /// <param name="kind">错误枚举值</param>
        /// <param name="message">错误消息</param>
        /// <param name="callerMemberName">不用填. 由编译器生成</param>
        /// <param name="callerFilePath">不用填. 由编译器生成</param>
        /// <param name="callerLineNumber">不用填. 由编译器生成</param>
        public static Err<TKind> Warp<TKind>(
            this Exception ex, TKind kind, string message,
            [CallerMemberName] string callerMemberName = null,
            [CallerFilePath] string callerFilePath = null,
            [CallerLineNumber] int callerLineNumber = default
        ) where TKind : struct, Enum =>
            new Err<TKind>(
                kind, message, null, ex,
                new CallerInfo(callerMemberName, callerFilePath, callerLineNumber)
            );
    }

    [PublicAPI]
    public class Err<TKind> : ErrBase where TKind : struct, Enum
    {
        /// <summary>当前错误对应的错误枚举</summary>
        public virtual TKind kind { get; }

        /// <inheritdoc cref="ErrBase.causedBy"/>
        public override ErrBase causedBy { get; }

        /// <inheritdoc cref="ErrBase.message"/>
        public override string message { get; }

        /// <inheritdoc cref="ErrBase.exception"/>
        public override Exception exception { get; }

        private Lazy<IList<string>> notes { get; } = new Lazy<IList<string>>(() => new List<string>(5));

        /// <summary>使用CallerInfo模拟StackTrace</summary>
        protected readonly List<CallerInfo> stackTrace;

        /// <summary>
        /// 不应该直接使用.<br/>
        /// 当用于新建错误时，应当使用<see cref="Err.New{TKind}"/>代替
        /// </summary>
        protected internal Err(TKind kind, string message, ErrBase causedBy, Exception exception, CallerInfo callerInfo)
        {
            this.kind = kind;
            this.message = message;
            this.causedBy = causedBy;
            this.exception = exception;
            stackTrace = new List<CallerInfo>(1) { callerInfo };
        }

        protected internal void AddStackTrace(CallerInfo callerInfo)
        {
            stackTrace.Add(callerInfo);
        }

        /// <summary>
        /// 快捷的将<see cref="Err{TKind}"/>转换为错误枚举的对应伴生类(如果有)
        /// </summary>
        [NotNull]
        public TErr CastTo<TErr>() where TErr : Err<TKind> => (TErr)this;

        /// <inheritdoc cref="ErrBase.Panic"/>
        public override Exception Panic() => new ManualPanicException(ToString()); // 直接throw exception会导致堆栈丢失

        /// <inheritdoc cref="ErrBase.CausedByException"/>
        public override bool CausedByException(out Exception ex)
        {
            ErrBase current = this;
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

        /// <inheritdoc cref="ErrBase.AddNote"/>
        protected internal override void AddNote(string note) => notes.Value.Add(note);

        /// <inheritdoc cref="ErrBase.WriteToStringBuilder"/>
        protected internal override void WriteToStringBuilder([NotNull] StringBuilder sb)
        {
            // 后续可以替换成PeroString拼接
            sb.AppendFormat("{0}.{1}\n", typeof(TKind).FullName, kind);
            for (int i = stackTrace.Count - 1; i >= 0; i--)
            {
                var callerInfo = stackTrace[i];
                sb.AppendFormat("  At: {0} @ {1}:{2}\n", callerInfo.callerMemberName, callerInfo.callerFilePath, callerInfo.callerLineNumber.ToString());
            }
            sb.AppendFormat("  Message: {0}\n", message);
            // Notes
            if (notes.IsValueCreated)
            {
                sb.AppendLine("  Notes:");
                for (int i = 0; i < notes.Value.Count; i++)
                {
                    sb.AppendFormat("    {0}: {1}\n", i.ToString(), notes.Value[i]);
                }
            }
            // Exception
            if (exception != null)
            {
                sb.AppendLine("  Exception:");
                sb.AppendLine("========== Exception Start ==========");
                sb.AppendLine(exception.ToString());
                // Exception 内部可能有用的信息(Exception.ToString()会打印Message和StackTrace)：
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