using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.Rest.Generator.Tests;

// A client for an interface nested in another type is a top-level class in the interface's
// namespace, named after the containing types and the interface, so a containing type need not
// be partial and two nested interfaces with one name do not collide. A generic interface, one
// declared inside a generic type, and one the namespace cannot reach get ZRA006 and no client
// (#394).
public class GeneratorNestedAndGenericClientTests
{
    private const string Members = """
        [ZeroAlloc.Rest.Attributes.Get("/users")]
        System.Threading.Tasks.Task<string> ListAsync(System.Threading.CancellationToken ct = default);
        """;

    [Fact]
    public void SameNamedNestedInterfaces_GetDistinctClients_AndCompile()
    {
        var source = $$"""
            using Microsoft.Extensions.DependencyInjection;
            namespace App
            {
                public class Orders { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } }
                public class Customers
                {
                    public class Inner { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} } }
                }
                [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IApi { {{Members}} }

                public static class Wiring
                {
                    public static void Register(IServiceCollection services)
                    {
                        services.AddOrders_IApi();
                        services.AddCustomers_Inner_IApi();
                        services.AddIApi();
                    }

                    public static object[] Construct(System.Net.Http.HttpClient http, ZeroAlloc.Rest.IRestSerializer serializer) =>
                    [
                        (Orders.IApi)new Orders_ApiClient(http, serializer),
                        (Customers.Inner.IApi)new Customers_Inner_ApiClient(http, serializer),
                        (IApi)new ApiClient(http, serializer),
                    ];
                }
            }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = Source(run, "App.Orders+IApi.g.cs");
        Assert.Contains("public sealed partial class Orders_ApiClient : global::App.Orders.IApi, global::ZeroAlloc.Rest.IGeneratedRestClient<Orders_ApiClient>", client);
        Assert.Contains("\"Orders.IApi.ListAsync\"", client);
        var di = Source(run, "App.Customers+Inner+IApi.DI.g.cs");
        Assert.Contains("public static IHttpClientBuilder AddCustomers_Inner_IApi(", di);
        Assert.Contains("services.AddHttpClient(\"Customers.Inner.IApi\", client =>", di);
        Assert.Contains(".AddTypedClient<global::App.Customers.Inner.IApi>(", di);
    }

    [Fact]
    public void NestedInterface_TakesItsAccessibilityFromTheWholeChain()
    {
        var source = $$"""
            namespace App
            {
                public class Open { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] internal interface IInternalApi { {{Members}} } }
                internal class Hidden { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IPublicApi { {{Members}} } }
                public class Family { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] protected internal interface IFamilyApi { {{Members}} } }
            }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        Assert.Contains("internal sealed partial class Open_InternalApiClient", Source(run, "App.Open+IInternalApi.g.cs"));
        Assert.Contains("internal sealed partial class Hidden_PublicApiClient", Source(run, "App.Hidden+IPublicApi.g.cs"));
        Assert.Contains("internal sealed partial class Family_FamilyApiClient", Source(run, "App.Family+IFamilyApi.g.cs"));
        Assert.Contains("internal static partial class InternalGeneratedRestClientExtensions", Source(run, "App.Open+IInternalApi.DI.g.cs"));
    }

    [Fact]
    public void NestedInterface_WithSerializerAndVerbatimNames_Compiles()
    {
        var source = $$"""
            namespace @class
            {
                public sealed class JevSerializer : ZeroAlloc.Rest.IRestSerializer
                {
                    public string ContentType => "application/x-jev";
                    public System.Threading.Tasks.ValueTask<T?> DeserializeAsync<T>(System.IO.Stream stream, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.ValueTask.FromResult<T?>(default);
                    public System.Threading.Tasks.ValueTask SerializeAsync<T>(System.IO.Stream stream, T value, System.Threading.CancellationToken ct = default)
                        => System.Threading.Tasks.ValueTask.CompletedTask;
                }

                public class @event
                {
                    public struct @static
                    {
                        [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient]
                        [ZeroAlloc.Rest.Attributes.Serializer(typeof(JevSerializer))]
                        public interface IApi { {{Members}} }
                    }
                }
            }
            """;

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Problems);
        var client = Source(run, "class.event+static+IApi.g.cs");
        Assert.Contains("sealed partial class event_static_ApiClient : global::@class.@event.@static.IApi", client);
        Assert.Contains("The REST client '@class.event.static.IApi' declares", client);
    }

    public static TheoryData<string, string, string> Unsupported() => new()
    {
        {
            $$"""[ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface [|IApi|]<T> { {{Members}} }""",
            "App.IApi<T>", "it is generic"
        },
        {
            $$"""[ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface [|IApi|]<TKey, TValue> { {{Members}} }""",
            "App.IApi<TKey, TValue>", "it is generic"
        },
        {
            $$"""public class Outer<T> { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface [|IApi|] { {{Members}} } }""",
            "App.Outer<T>.IApi", "it is declared inside the generic type 'App.Outer<T>'"
        },
        {
            $$"""public class Outer { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] private interface [|IApi|] { {{Members}} } }""",
            "App.Outer.IApi", "'App.Outer.IApi' is private, and the generated client, a class at namespace level, cannot reach it"
        },
        {
            $$"""public class Outer { protected class Mid { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface [|IApi|] { {{Members}} } } }""",
            "App.Outer.Mid.IApi", "'App.Outer.Mid' is protected, and the generated client, a class at namespace level, cannot reach it"
        },
        {
            $$"""public class Outer { [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] private protected interface [|IApi|] { {{Members}} } }""",
            "App.Outer.IApi", "'App.Outer.IApi' is private protected, and the generated client, a class at namespace level, cannot reach it"
        },
    };

    [Theory]
    [MemberData(nameof(Unsupported))]
    public void UnsupportedInterface_ReportsZra006_AndGeneratesNothingForIt(string declaration, string display, string reason)
    {
        var marked = $$"""
            namespace App
            {
                {{declaration}}
                [ZeroAlloc.Rest.Attributes.ZeroAllocRestClient] public interface IOtherApi { {{Members}} }
            }
            """;
        var start = marked.IndexOf("[|", System.StringComparison.Ordinal);
        var end = marked.IndexOf("|]", System.StringComparison.Ordinal) - 2;
        var source = marked.Replace("[|", "", System.StringComparison.Ordinal).Replace("|]", "", System.StringComparison.Ordinal);

        var run = GeneratorHarness.RunAll(source, GeneratorHarness.References);

        var diagnostic = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZRA006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            $"No REST client is generated for '{display}', because {reason}",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        GeneratorDiagnosticLocationTests.AssertAt(diagnostic.Location, source, TextSpan.FromBounds(start, end));
        Assert.Equal(["App.IOtherApi.DI.g.cs", "App.IOtherApi.g.cs"],
            run.GeneratedSources.Select(s => s.HintName).Order(System.StringComparer.Ordinal),
            System.StringComparer.Ordinal);
        Assert.Empty(run.Problems);
    }

    private static string Source(GeneratorHarness.GeneratorHarnessRunAll run, string hintName) =>
        run.GeneratedSources
            .Single(s => string.Equals(s.HintName, hintName, System.StringComparison.Ordinal))
            .SourceText.ToString();
}
