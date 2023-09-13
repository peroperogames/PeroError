/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: IErr Interface
 */

using System;
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

        /// <summary>向当前错误添加附加信息，来更好的描述当前错误</summary>
        void AddNote(string note);

        /// <summary>
        /// 将当前错误当作不可恢复错误, 返回<see cref="ManualPanicException"/>, 需要手动抛出<br/>
        /// 使用方法：<c>throw err.Panic();</c><br/>
        /// </summary>
        /// <remarks>需要手动抛出主要是为了能让C#识别到代码路径中断，同时能让堆栈少一层</remarks>
        Exception Panic();

        /// <summary>查询错误链上是否有异常.</summary>
        /// <param name="ex">如果有异常, 则返回</param>
        bool CausedByException([CanBeNull] out Exception ex);

        /// <summary>格式化自身并写入StringBuilder</summary>
        void WriteToStringBuilder(StringBuilder sb);
    }
}