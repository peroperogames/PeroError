/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: CallerInfo
 */


namespace PeroTools2.Error;

/// <summary>
/// Holds the caller info, namely<br/>
/// <see cref="System.Runtime.CompilerServices.CallerMemberNameAttribute"/>,<br/>
/// <see cref="System.Runtime.CompilerServices.CallerFilePathAttribute"/>,<br/>
/// <see cref="System.Runtime.CompilerServices.CallerLineNumberAttribute"/>.<br/>
/// A struct is used to pass it around mainly to suppress a ReSharper warning
/// </summary>
/// <remarks>
/// Every extra stack frame requires the caller info to be passed in again, otherwise the caller info
/// stays at the last value that was passed.
/// The optional parameters can be overridden directly to override the caller info, but that triggers a
/// ReSharper warning (with no effect on the result). To prevent the warning, suppress it directly, or
/// pass the values wrapped in <see cref="CallerInfo"/>.
/// </remarks>
/// <see href="https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/attributes/caller-information"/>
public readonly struct CallerInfo
{
    /// <summary>Name of the calling member</summary>
    public readonly string callerMemberName;

    /// <summary>Source file path of the caller</summary>
    public readonly string callerFilePath;

    /// <summary>Line number the caller is on</summary>
    public readonly int callerLineNumber;

    /// <summary>Constructs the caller info; the arguments are filled in by the compiler</summary>
    public CallerInfo(string callerMemberName, string callerFilePath, int callerLineNumber)
    {
        this.callerMemberName = callerMemberName;
        this.callerFilePath = callerFilePath;
        this.callerLineNumber = callerLineNumber;
    }
}