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
var modelsOption = new Option<bool>("--models")
{
    Description = "Generate a type for each schema the interface references; false keeps your own DTOs",
    DefaultValueFactory = _ => true,
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
generateCommand.Options.Add(modelsOption);

generateCommand.SetAction(async (parseResult, ct) =>
{
    var spec = parseResult.GetValue(specOption)
        ?? throw new InvalidOperationException("--spec is required");
    var ns = parseResult.GetValue(nsOption)
        ?? throw new InvalidOperationException("--namespace is required");
    var output = parseResult.GetValue(outputOption)
        ?? throw new InvalidOperationException("--output is required");
    var iface = parseResult.GetValue(ifaceOption)
        ?? throw new InvalidOperationException("--interface has a default value and cannot be null");
    var noWarn = new HashSet<string>(
        (parseResult.GetValue(noWarnOption) ?? [])
            .SelectMany(v => v.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
        StringComparer.OrdinalIgnoreCase);
    var options = new GenerationOptions(parseResult.GetValue(modelsOption));

    string content;
    var diagnostics = new List<OpenApiDiagnostic>();
    if (spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        content = await OpenApiInterfaceGenerator.GenerateFromUrlAsync(spec, ns, iface, diagnostics, options, ct).ConfigureAwait(false);
    else
        content = await OpenApiInterfaceGenerator.GenerateFromFileAsync(spec, ns, iface, diagnostics, options, ct).ConfigureAwait(false);

    // The canonical "file: warning CODE: message" form, which build logs and IDEs recognise. --nowarn
    // suppresses a warning, never an error.
    foreach (var diagnostic in diagnostics.Where(d => d.Severity == OpenApiSeverity.Error || !noWarn.Contains(d.Code)))
    {
        var severity = diagnostic.Severity == OpenApiSeverity.Error ? "error" : "warning";
        await Console.Error.WriteLineAsync($"{spec}: {severity} {diagnostic.Code}: {diagnostic.Message}").ConfigureAwait(false);
    }

    // An error means the generated code would be wrong, so nothing is written.
    if (diagnostics.Exists(d => d.Severity == OpenApiSeverity.Error))
        return 1;

    var dir = Path.GetDirectoryName(output);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    await File.WriteAllTextAsync(output, content, ct).ConfigureAwait(false);
    Console.WriteLine($"Generated: {output}");
    return 0;
});

var root = new RootCommand("ZeroAlloc.Rest code generation tools");
root.Subcommands.Add(generateCommand);
return root.Parse(args).Invoke();
