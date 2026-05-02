// Required polyfill: C# 9+ init-only properties and records use IsExternalInit,
// which is not present in netstandard2.0. Declaring it here makes it available.
// This file is internal and not shipped as part of any public API.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
