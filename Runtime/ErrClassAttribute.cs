using System;
using JetBrains.Annotations;

namespace PeroTools2.Error
{
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