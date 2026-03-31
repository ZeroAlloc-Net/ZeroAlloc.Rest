using System.Threading;
using Microsoft.CodeAnalysis;
using ZeroAlloc.Rest.Generator.Models;

namespace ZeroAlloc.Rest.Generator;

internal static class ModelExtractor
{
    internal static ClientModel? Extract(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct) => null; // implemented in Task 6
}
