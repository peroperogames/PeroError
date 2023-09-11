using System;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
    /// <summary>
    /// 用在错误枚举字段上, 用于指明该错误枚举字段对应的伴生错误类.<br/>
    /// 目前只是给人看的, 没有实际作用
    /// </summary>
    [PublicAPI]
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ErrClassAttribute : Attribute
    {
        public Type bindTo { get; }

        public ErrClassAttribute(Type bindTo)
        {
            this.bindTo = bindTo;
        }
    }
}