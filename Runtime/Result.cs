/*
 * Copyright (c) PeroPeroGames Co., Ltd.
 * Author: AidenMX
 * Created On: 2023/09/11
 * Description: Result Type For C#
 */

using System;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    /// <summary>
    /// 不带结果的Result类型
    /// </summary>
    /// <typeparam name="TErrKind">错误枚举类型</typeparam>
    [PublicAPI]
    public readonly struct Result<TErrKind> where TErrKind : struct, Enum
    {
        /// <summary>
        /// 如果失败, 存放错误信息(一定有). 如果成功则为null
        /// </summary>
        public Err<TErrKind> err { get; }

        /// <summary>
        /// Result的状态, 成功还是失败. 如果失败, err一定不为null
        /// </summary>
        public bool isOK => err == null; // 因为struct始终可以使用零参数构造，所以为了防止非法状态，不使用独立的isOK变量

        private Result(Err<TErrKind> err)
        {
            this.err = err;
        }

        /// <summary>创建一个成功的Result</summary>
        public static Result<TErrKind> OK() => new Result<TErrKind>();

        /// <summary>创建一个失败的Result</summary>
        public static Result<TErrKind> Fail(Err<TErrKind> err) => new Result<TErrKind>(err);

        // Err => Result<T>
        public static implicit operator Result<TErrKind>(Err<TErrKind> err) => Fail(err);

        /// <summary>
        /// 断言Result一定成功. 如果不成功则触发<see cref="IErr.Panic"/>
        /// </summary>
        /// <exception cref="ManualPanicException">当在一个失败的Result上调用时抛出</exception>
        public void UnWarp()
        {
            if (!isOK) throw err.Panic(); // 如果!isOK则err一定不为null
        }

        [NotNull]
        public override string ToString() => isOK
            ? $"Result<{typeof(TErrKind).Name}>.OK()"
            : $"Result<{typeof(TErrKind).Name}>.Fail({err.kind.ToString()})";
    }

    [PublicAPI]
    public readonly struct Result<T, TErrKind> where TErrKind : struct, Enum
    {
        /// <summary>
        /// 如果成功，存放结果，否则为default
        /// </summary>
        private readonly T m_Value;

        /// <summary>
        /// 如果失败, 存放错误信息(一定有). 如果成功则为null
        /// </summary>
        public Err<TErrKind> err { get; }

        /// <summary>
        /// Result的状态, 成功还是失败. 如果失败, err一定不为null
        /// </summary>
        public T value => isOK
            ? m_Value
            : throw new InvalidOperationException("Access Value From A Failed Result");

        // 因为struct始终可以使用零参数构造，所以为了防止非法状态，不使用独立的isOK变量
        public bool isOK => err == null;

        private Result(T value)
        {
            m_Value = value;
            err = null;
        }

        private Result(Err<TErrKind> err)
        {
            m_Value = default;
            this.err = err;
        }

        /// <summary>创建一个成功的Result</summary>
        public static Result<T, TErrKind> OK(T value) => new Result<T, TErrKind>(value);

        /// <summary>创建一个失败的Result</summary>
        public static Result<T, TErrKind> Fail(Err<TErrKind> err) => new Result<T, TErrKind>(err);

        // T => Result<T>
        public static implicit operator Result<T, TErrKind>(T value) => OK(value);

        // Err => Result<T>
        public static implicit operator Result<T, TErrKind>(Err<TErrKind> err) => Fail(err);

        [NotNull]
        public override string ToString() => isOK
            ? $"Result<{typeof(T).Name}, {typeof(TErrKind).Name}>.OK({value.ToString()})"
            : $"Result<{typeof(T).Name}, {typeof(TErrKind).Name}>.Fail({err.kind.ToString()})";

        #region Fluent

        /// <summary>
        /// 断言Result一定成功.
        /// 如果成功返回结果,
        /// 如果不成功则触发<see cref="IErr.Panic"/>
        /// </summary>
        /// <exception cref="ManualPanicException">当在一个失败的Result上调用时抛出</exception>
        public T UnWarp() => isOK
            ? value
            : throw err.Panic(); // 如果!isOK则err一定不为null

        /// <summary>
        /// Result成功时返回值, 否则返回默认值
        /// </summary>
        public T ValueOrDefault([CanBeNull] T @default = default) => isOK ? value : @default;

        /// <summary>
        /// Result成功时返回值, 调用函数来处理错误并返回值
        /// </summary>
        public T ValueOr(Func<Err<TErrKind>, T> whenFailed) => isOK ? value : whenFailed.Invoke(err);

        /// <summary>
        /// 将Result&lt;T, TErrKind>强转为Result&lt;TR, TErrKind&gt;
        /// </summary>
        public Result<TR, TErrKind> Cast<TR>() where TR : T => isOK
            ? Result<TR, TErrKind>.OK((TR)value)
            : Result<TR, TErrKind>.Fail(err);

        /// <summary>
        /// 将Result&lt;T, TErrKind>转换为Result&lt;TR, TErrKind&gt;
        /// </summary>
        public Result<TR, TErrKind> Map<TR>(Func<T, TR> mapFunc) => isOK
            ? Result<TR, TErrKind>.OK(mapFunc.Invoke(value))
            : Result<TR, TErrKind>.Fail(err);

        #endregion
    }
}