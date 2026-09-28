using System.Runtime.InteropServices;

namespace ZeroAlloc.Rest.MemoryPackAotSmoke;

// An unmanaged user struct: like SmokeColor, MemoryPack copies it as raw memory with no registration.
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SmokePoint(int X, int Y);
