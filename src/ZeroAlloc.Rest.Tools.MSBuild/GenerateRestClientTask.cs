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
            var warnings = new List<OpenApiWarning>();
            var options = new GenerationOptions(GenerateModels);
            if (Spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                content = System.Threading.Tasks.Task.Run(() => OpenApiInterfaceGenerator.GenerateFromUrlAsync(Spec, Namespace, InterfaceName, warnings, options, CancellationToken.None))
                    .GetAwaiter().GetResult();
            else
                content = System.Threading.Tasks.Task.Run(() => OpenApiInterfaceGenerator.GenerateFromFileAsync(Spec, Namespace, InterfaceName, warnings, options, CancellationToken.None))
                    .GetAwaiter().GetResult();

            // Reported against the spec, the file to change to resolve them, under their ZRT code,
            // which NoWarn suppresses.
            foreach (var warning in warnings)
                Log.LogWarning(null, warning.Code, null, Spec, 0, 0, 0, 0, warning.Message);

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
}
