using System.CommandLine;
using ZeroAlloc.Rest.Tools;

var specOption = new Option<string>("--spec", "Path or URL to OpenAPI spec") { IsRequired = true };
var nsOption = new Option<string>("--namespace", "C# namespace for generated interface") { IsRequired = true };
var outputOption = new Option<string>("--output", "Output .cs file path") { IsRequired = true };
var ifaceOption = new Option<string>("--interface", () => "IApiClient", "Interface name");

var generateCommand = new Command("generate", "Generate a ZeroAllocRestClient interface from an OpenAPI spec")
{
    specOption, nsOption, outputOption, ifaceOption
};

generateCommand.SetHandler(async (spec, ns, output, iface) =>
{
    string content;
    if (spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        content = await OpenApiInterfaceGenerator.GenerateFromUrlAsync(spec, ns, iface);
    else
        content = await OpenApiInterfaceGenerator.GenerateFromFileAsync(spec, ns, iface);

    var dir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(output, content);
    Console.WriteLine($"Generated: {output}");
}, specOption, nsOption, outputOption, ifaceOption);

var root = new RootCommand("ZeroAlloc.Rest code generation tools") { generateCommand };
return await root.InvokeAsync(args);
