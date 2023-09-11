using System;
using System.Collections.Generic;
using System.Text;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    [PublicAPI]
    public interface IErr
    {
        IErr causedBy { get; }
        string message { get; }
        Exception exception { get; }

        Lazy<IList<string>> notes { get; }

        Exception Panic();
        bool CausedByException(out Exception ex);
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