// Polyfill required to use C# records on netstandard2.0
// The compiler emits init-only setters that reference this type.
using System.ComponentModel;

namespace System.Runtime.CompilerServices;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit { }
