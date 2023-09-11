using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    [PublicAPI]
    public interface IErr
    {
        /// <summary>造成当前错误的错误.</summary>
        [CanBeNull]
        IErr causedBy { get; }

        /// <summary>获取描述当前异常的消息</summary>
        [NotNull]
        string message { get; }

        /// <summary>造成当前错误的异常</summary>
        [CanBeNull]
        Exception exception { get; }

        /// <summary>用于更好的描述当前错误的附加信息</summary>
        Lazy<IList<string>> notes { get; }

        /// <summary>将当前错误当作不可恢复错误抛出, 抛出<see cref="ManualPanicException"/></summary>
        Exception Panic();

        /// <summary>
        /// 查询错误链上是否有异常.
        /// </summary>
        /// <param name="ex">如果有异常, 则返回</param>
        bool CausedByException([CanBeNull] out Exception ex);

        /// <summary>
        /// 格式化自身并写入StringBuilder
        /// </summary>
        void WriteToStringBuilder(StringBuilder sb);
    }

    public static class ErrExtensions
    {
        /// <summary>向Err添加额外信息，会在打印该错误时一并输出</summary>
        /// <returns>返回自身来允许链式调用</returns>
        [PublicAPI]
        [NotNull]
        public static TErr AddNote<TErr>([NotNull] this TErr err, string note) where TErr : IErr
        {
            err.notes.Value.Add(note);
            return err;
        }
    }
}