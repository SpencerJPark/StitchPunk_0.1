// RuntimeWatchAttribute.cs
using System;
using UnityEngine;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
public sealed class RuntimeWatchAttribute : PropertyAttribute
{
    public readonly string Label;
    public RuntimeWatchAttribute(string label = null) => Label = label;
}
