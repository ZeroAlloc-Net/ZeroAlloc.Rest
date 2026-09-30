using System.Collections.Generic;

namespace ZeroAlloc.Rest.Generator.Models;

internal record ClientModel(
    string Namespace,
    string InterfaceName,
    string HintNameStem,
    string ClassName,
    // How generated code at namespace level names the interface: its simple name for a top-level
    // interface, as it always was, and global::Ns.Outer.IApi for a nested one.
    string InterfaceReference,
    // The interface's name within its namespace, for messages and span names: IUserApi, or
    // Outer.IApi for a nested interface.
    string InterfaceDisplayName,
    // The containing types' names, each followed by '_', or empty for a top-level interface. It
    // starts the client's class name and follows "Add" in the registration method's name, so two
    // nested interfaces with one name get distinct ones: Orders_ApiClient and AddOrders_IApi.
    string NestedPrefix,
    // False when no client can be generated for the interface; Diagnostics then says why (ZRA006).
    bool IsSupported,
    EquatableArray<MethodModel> Methods,
    string? SerializerTypeName,
    bool IsPublic,
    int MaxErrorBodyBytes,
    EquatableArray<ErrorMapperModel> ErrorMappers,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    // The generated types follow the interface: a public client over an internal interface
    // would leak it, and fails to compile when the interface uses internal DTOs.
    internal string Accessibility => IsPublic ? "public" : "internal";

    // Partial declarations must agree on accessibility, so internal clients get their own class.
    internal string ExtensionsClassName => IsPublic ? "GeneratedRestClientExtensions" : "InternalGeneratedRestClientExtensions";

    // The Add{I} registration method: AddIUserApi, or AddOrders_IApi for Orders.IApi.
    internal string RegistrationMethodName => "Add" + NestedPrefix + InterfaceName;

    // The named HttpClient Add{I} registers. A top-level interface keeps nameof(IUserApi), which
    // is "IUserApi"; a nested one is named with its containing types, "Orders.IApi", because
    // nameof would give two nested IApi interfaces the same name and the same client settings.
    internal string HttpClientNameExpression => NestedPrefix.Length == 0
        ? $"nameof({InterfaceName})"
        : $"\"{InterfaceDisplayName}\"";

    // Method-level serializers that need their own constructor parameter. A method override equal to
    // the interface-level type reuses the main serializer instead of injecting it twice.
    internal IReadOnlyList<string> GetOverrideSerializerTypes()
    {
        var result = new List<string>();
        foreach (var m in Methods)
        {
            if (m.SerializerTypeName != null
                && m.SerializerTypeName != SerializerTypeName
                && !result.Contains(m.SerializerTypeName))
                result.Add(m.SerializerTypeName);
        }
        return result.AsReadOnly();
    }

    // The error types methods use that have a usable mapper, each with that mapper and the error type
    // as the mapper declares it, in order of first use. Each gets one constructor parameter. A mapper
    // for an error type no method uses is only registered.
    internal IReadOnlyList<(string ErrorTypeName, string MapperTypeName, string MapperErrorTypeName)> GetUsedErrorMappings()
    {
        var result = new List<(string ErrorTypeName, string MapperTypeName, string MapperErrorTypeName)>();
        foreach (var m in Methods)
        {
            if (!m.MapsError || m.ErrorMapperTypeName is null)
                continue;
            var seen = false;
            foreach (var (errorType, _, _) in result)
            {
                if (errorType == m.ErrorTypeName) { seen = true; break; }
            }
            if (!seen)
                result.Add((m.ErrorTypeName!, m.ErrorMapperTypeName, m.MapperErrorTypeName!));
        }
        return result.AsReadOnly();
    }
}
