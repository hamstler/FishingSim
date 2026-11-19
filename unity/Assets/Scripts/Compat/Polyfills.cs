// Unity's .NET Standard 2.1 profile lacks the marker types newer C# features need (records, init-only setters).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
