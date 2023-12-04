/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: CallerInfo
 */

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
    /// <remarks>
    /// 每加一层调用栈都需要重新传一次调用方信息, 否则调用方信息会停留在最后一次传入时.
    /// 可以直接覆盖可选参数来覆盖调用方信息, 但会产生ReSharper警告(结果无影响). 为了防止警告, 可以直接抑制警告, 或者使用<see cref="CallerInfo"/>包装
    /// </remarks>
    /// <see href="https://learn.microsoft.com/zh-cn/dotnet/csharp/language-reference/attributes/caller-information"/>
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