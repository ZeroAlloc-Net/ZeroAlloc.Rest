---
id: cookbook-custom-serializer
title: "03 — Custom Serializer"
slug: /cookbook/custom-serializer
sidebar_position: 103
description: Implement a custom IRestSerializer and wire it into ZeroAlloc.Rest.
---

# Recipe 03 — Custom Serializer

**Goal:** Implement a custom `IRestSerializer` (e.g., for XML or a proprietary binary format) and use it for selected API calls.

## 1. Implement IRestSerializer

```csharp
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using ZeroAlloc.Rest;

public sealed class XmlRestSerializer : IRestSerializer
{
    public string ContentType => "application/xml";

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "XML serialization may require dynamic code.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "XML serialization may require unreferenced code.")]
    public ValueTask<T?> DeserializeAsync<T>(Stream stream, CancellationToken ct = default)
    {
        var xs = new XmlSerializer(typeof(T));
        var result = (T?)xs.Deserialize(stream);
        return ValueTask.FromResult(result);
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode(
        "XML serialization may require dynamic code.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "XML serialization may require unreferenced code.")]
    public ValueTask SerializeAsync<T>(Stream stream, T value, CancellationToken ct = default)
    {
        var xs = new XmlSerializer(typeof(T));
        xs.Serialize(stream, value);
        return ValueTask.CompletedTask;
    }
}
```

## 2. Use as the default serializer

```csharp
builder.Services.AddILegacyApi(options =>
{
    options.BaseAddress = new Uri("https://legacy.example.com");
    options.UseSerializer<XmlRestSerializer>();
});
```

## 3. Use as a per-method override

If only one endpoint uses XML:

```csharp
[ZeroAllocRestClient]
public interface IHybridApi
{
    [Get("/json-data")]
    Task<DataDto> GetJsonAsync(CancellationToken ct = default);  // uses default (JSON)

    [Get("/xml-report")]
    [Serializer(typeof(XmlRestSerializer))]
    Task<ReportDto> GetXmlReportAsync(CancellationToken ct = default);  // uses XML
}
```

The DI emitter registers `XmlRestSerializer` as a singleton automatically.

## 4. Testing the custom serializer

Test it in isolation first:

```csharp
[Fact]
public async Task XmlSerializer_RoundTrip()
{
    var sut = new XmlRestSerializer();
    var original = new MyDto { Id = 1, Name = "Test" };

    using var stream = new MemoryStream();
    await sut.SerializeAsync(stream, original);
    stream.Position = 0;
    var result = await sut.DeserializeAsync<MyDto>(stream);

    Assert.Equal(original.Id, result!.Id);
    Assert.Equal(original.Name, result.Name);
}
```

Then test end-to-end with WireMock.Net using `WithHeader("Content-Type", "application/xml")` and an XML body.
