using Microsoft.Build.Framework;
using ZeroAlloc.Rest.Tools;
using Task = Microsoft.Build.Utilities.Task;

namespace ZeroAlloc.Rest.Tools.MSBuild;

public sealed class GenerateRestClientTask : Task
{
    [Required] public string Spec { get; set; } = "";
    [Required] public string OutputPath { get; set; } = "";
    [Required] public string Namespace { get; set; } = "";
    public string InterfaceName { get; set; } = "IApiClient";

    // Metadata GenerateModels="false" keeps hand-written DTOs. MSBuild does not set a parameter
    // whose metadata is empty, so an item without it keeps the default.
    public bool GenerateModels { get; set; } = true;

    public override bool Execute()
    {
        if (string.IsNullOrWhiteSpace(Namespace))
        {
            Log.LogError("ZeroAlloc.Rest: Namespace is required and cannot be empty.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            Log.LogError("ZeroAlloc.Rest: OutputPath is required and cannot be empty.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(Spec))
        {
            Log.LogError("ZeroAlloc.Rest: Spec is required and cannot be empty.");
            return false;
        }

        try
        {
            string content;
            var diagnostics = new List<OpenApiDiagnostic>();
            var options = new GenerationOptions(GenerateModels);
            if (Spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                content = System.Threading.Tasks.Task.Run(() => OpenApiInterfaceGenerator.GenerateFromUrlAsync(Spec, Namespace, InterfaceName, diagnostics, options, CancellationToken.None))
                    .GetAwaiter().GetResult();
            else
                content = System.Threading.Tasks.Task.Run(() => OpenApiInterfaceGenerator.GenerateFromFileAsync(Spec, Namespace, InterfaceName, diagnostics, options, CancellationToken.None))
                    .GetAwaiter().GetResult();

            // An error means the generated code would be wrong, so nothing is written.
            if (!Report(diagnostics))
                return false;

            // The task runs before every compile. Rewriting an unchanged file would bump its timestamp
            // and make CoreCompile rerun on every build.
            if (File.Exists(OutputPath) && string.Equals(File.ReadAllText(OutputPath), content, StringComparison.Ordinal))
            {
                Log.LogMessage(MessageImportance.Low, $"ZeroAlloc.Rest: {OutputPath} is up to date");
                return true;
            }

            var dir = Path.GetDirectoryName(OutputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(OutputPath, content);
            Log.LogMessage(MessageImportance.Normal, $"ZeroAlloc.Rest: Generated {OutputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"ZeroAlloc.Rest generation failed: {ex}");
            return false;
        }
    }

    // Reported against the spec, the file to change to resolve them, under their ZRT code, which
    // NoWarn suppresses for a warning. Returns false when any is an error.
    private bool Report(List<OpenApiDiagnostic> diagnostics)
    {
        var succeeded = true;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == OpenApiSeverity.Error)
            {
                Log.LogError(null, diagnostic.Code, null, Spec, 0, 0, 0, 0, diagnostic.Message);
                succeeded = false;
            }
            else
            {
                Log.LogWarning(null, diagnostic.Code, null, Spec, 0, 0, 0, 0, diagnostic.Message);
            }
        }
        return succeeded;
    }
}
