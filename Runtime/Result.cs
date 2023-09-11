using System;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    [PublicAPI]
    public readonly struct Result<TErrKind> where TErrKind : struct, Enum
    {
        public Err<TErrKind> err { get; }

        // 因为struct始终可以使用零参数构造，所以为了防止非法状态，不使用独立的isOK变量
        public bool isOK => err == null;

        private Result(Err<TErrKind> err)
        {
            this.err = err;
        }

        public static Result<TErrKind> OK() => new Result<TErrKind>();
        public static Result<TErrKind> Fail(Err<TErrKind> err) => new Result<TErrKind>(err);

        // Err => Result<T>
        public static implicit operator Result<TErrKind>(Err<TErrKind> err) => Fail(err);

        public void UnWarp([CanBeNull] string msg = null)
        {
            if (!isOK) throw new ManualPanicException(msg);
        }

        [NotNull]
        public override string ToString() =>
            isOK
                ? $"Result<{typeof(TErrKind).Name}>.OK()"
                : $"Result<{typeof(TErrKind).Name}>.Fail:\n{err}";
    }

    [PublicAPI]
    public readonly struct Result<T, TErrKind> where TErrKind : struct, Enum
    {
        private readonly T m_Value;

        public T value => isOK
            ? m_Value
            : throw new InvalidOperationException("Access Value From A Failed Result");

        public Err<TErrKind> err { get; }

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

        public static Result<T, TErrKind> OK(T value) => new Result<T, TErrKind>(value);
        public static Result<T, TErrKind> Fail(Err<TErrKind> err) => new Result<T, TErrKind>(err);

        // T => Result<T>
        public static implicit operator Result<T, TErrKind>(T value) => OK(value);

        // Err => Result<T>
        public static implicit operator Result<T, TErrKind>(Err<TErrKind> err) => Fail(err);

        [NotNull]
        public override string ToString() =>
            isOK
                ? $"Result<{typeof(T).Name}, {typeof(TErrKind).Name}>.OK({value.ToString()})"
                : $"Result<{typeof(T).Name}, {typeof(TErrKind).Name}>.Fail:\n{err}";

        #region Fluent

        public T UnWarp([CanBeNull] string msg = null) => isOK
            ? value
            : throw new ManualPanicException(msg);

        [CanBeNull]
        public T ValueOrDefault([CanBeNull] T @default = default) => isOK ? value : default;

        public T ValueOr(Func<Err<TErrKind>, T> whenFailed) => isOK ? value : whenFailed.Invoke(err);

        public Result<TR, TErrKind> Cast<TR>() where TR : T => isOK
            ? Result<TR, TErrKind>.OK((TR)value)
            : Result<TR, TErrKind>.Fail(err);

        public Result<TR, TErrKind> Map<TR>(Func<T, TR> mapFunc) => isOK
            ? Result<TR, TErrKind>.OK(mapFunc.Invoke(value))
            : Result<TR, TErrKind>.Fail(err);

        #endregion
    }
}