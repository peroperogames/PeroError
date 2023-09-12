/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: ManualPanicException
 */

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
        public ManualPanicException([NotNull] string message) : base(message)
        {
        }
    }
}