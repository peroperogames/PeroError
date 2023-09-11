using System;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    /// <summary>
    /// 代表一次手动Panic
    /// </summary>
    [PublicAPI]
    public class ManualPanicException : Exception
    {
        public ManualPanicException(string msg, [CanBeNull] Exception innerEx = null)
            : base($"Manual Panic Occured By: {msg}", innerEx)
        {
        }
    }
}