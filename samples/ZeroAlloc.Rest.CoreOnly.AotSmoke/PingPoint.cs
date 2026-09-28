using System.Runtime.InteropServices;

namespace ZeroAlloc.Rest.CoreOnly.AotSmoke;

[StructLayout(LayoutKind.Auto)]
public readonly record struct PingPoint(int X, int Y);
