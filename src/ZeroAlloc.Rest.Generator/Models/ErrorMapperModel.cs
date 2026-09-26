using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

// A usable [ErrorMapper]: its global::-qualified type, and the error types it maps for this interface.
internal sealed record ErrorMapperModel(string MapperTypeName, IReadOnlyList<string> ErrorTypeNames);
