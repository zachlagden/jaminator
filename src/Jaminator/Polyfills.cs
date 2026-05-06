// Polyfills required for modern C# features on .NET Framework 4.8.

namespace System.Runtime.CompilerServices
{
    /// <summary>Required by C# 9 init-only setters and records when targeting netstandard2.0/net48.</summary>
    internal static class IsExternalInit { }
}
