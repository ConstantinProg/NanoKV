using System;

namespace NanoKV.SerializationGenerator
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class GenerateBinarySerializerAttribute : Attribute
    {
    }
}