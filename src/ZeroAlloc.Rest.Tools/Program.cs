using System.CommandLine;
using ZeroAlloc.Rest.Tools;

var specOption = new Option<string>("--spec") { Description = "Path or URL to OpenAPI spec" };
var nsOption = new Option<string>("--namespace") { Description = "C# namespace for generated interface" };
var outputOption = new Option<string>("--output") { Description = "Output .cs file path" };
var ifaceOption = new Option<string>("--interface") { Description = "Interface name", DefaultValueFactory = _ => "IApiClient" };
var noWarnOption = new Option<string[]>("--nowarn")
{
    Description = "Warning codes to suppress, such as ZRT001; repeat the option or separate codes with ',' or ';'",
    AllowMultipleArgumentsPerToken = true,
};

specOption.Validators.Add(r => { if (r.GetValueOrDefault<string>() is null) r.AddError("--spec is required"); });
nsOption.Validators.Add(r => { if (r.GetValueOrDefault<string>() is null) r.AddError("--namespace is required"); });
outputOption.Validators.Add(r => { if (r.GetValueOrDefault<string>() is null) r.AddError("--output is required"); });

var generateCommand = new Command("generate", "Generate a ZeroAllocRestClient interface from an OpenAPI spec");
generateCommand.Options.Add(specOption);
generateCommand.Options.Add(nsOption);
generateCommand.Options.Add(outputOption);
generateCommand.Options.Add(ifaceOption);
generateCommand.Options.Add(noWarnOption);

generateCommand.SetAction(async (parseResult, ct) =>
{
    var spec = parseResult.GetValue(specOption)!;
    var ns = parseResult.GetValue(nsOption)!;
    var output = parseResult.GetValue(outputOption)!;
    var iface = parseResult.GetValue(ifaceOption)!;
    var noWarn = new HashSet<string>(
        (parseResult.GetValue(noWarnOption) ?? [])
            .SelectMany(v => v.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
        StringComparer.OrdinalIgnoreCase);

    string content;
    var warnings = new List<OpenApiWarning>();
    if (spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        content = await OpenApiInterfaceGenerator.GenerateFromUrlAsync(spec, ns, iface, warnings, ct).ConfigureAwait(false);
    else
        content = await OpenApiInterfaceGenerator.GenerateFromFileAsync(spec, ns, iface, warnings, ct).ConfigureAwait(false);

    // The canonical "file: warning CODE: message" form, which build logs and IDEs recognise.
    foreach (var warning in warnings.Where(w => !noWarn.Contains(w.Code)))
        await Console.Error.WriteLineAsync($"{spec}: warning {warning.Code}: {warning.Message}").ConfigureAwait(false);

    var dir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(output, content, ct).ConfigureAwait(false);
    Console.WriteLine($"Generated: {output}");
});

var root = new RootCommand("ZeroAlloc.Rest code generation tools");
root.Subcommands.Add(generateCommand);
return root.Parse(args).Invoke();
