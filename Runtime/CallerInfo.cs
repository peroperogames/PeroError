using JetBrains.Annotations;

namespace PeroTools2.Error
{
    /// <summary>
    /// 保存调用者信息，也就是<br/>
    /// <see cref="System.Runtime.CompilerServices.CallerMemberNameAttribute"/>,<br/>
    /// <see cref="System.Runtime.CompilerServices.CallerFilePathAttribute"/>,<br/>
    /// <see cref="System.Runtime.CompilerServices.CallerLineNumberAttribute"/>.<br/>
    /// 使用一个结构体来传入主要是为了抑制一个ReSharper警告
    /// </summary>
    [PublicAPI]
    public readonly struct CallerInfo
    {
        public readonly string callerMemberName;
        public readonly string callerFilePath;
        public readonly int callerLineNumber;

        public CallerInfo(string callerMemberName, string callerFilePath, int callerLineNumber)
        {
            this.callerMemberName = callerMemberName;
            this.callerFilePath = callerFilePath;
            this.callerLineNumber = callerLineNumber;
        }
    }
}