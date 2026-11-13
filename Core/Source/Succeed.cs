/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: Gatongone
 * Created On: 2026/11/13
 */

namespace PeroTools2.Error;

/// <summary>
/// Success indication used for <see cref="Result{TErrKind}"/>.
/// </summary>
public readonly struct Succeed
{
    /// <summary>
    /// Singleton.
    /// </summary>
    internal static readonly Succeed instance = new();
}