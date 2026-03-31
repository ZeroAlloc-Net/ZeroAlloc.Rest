using Microsoft.Build.Framework;
using ZeroAlloc.Rest.Tools;
using Task = Microsoft.Build.Utilities.Task;

namespace ZeroAlloc.Rest.Tools.MSBuild;

public sealed class GenerateRestClientTask : Task
{
    [Required] public string Spec    { get; set; } = "";
    [Required] public string Output  { get; set; } = "";
    [Required] public string Namespace { get; set; } = "";
    public string InterfaceName { get; set; } = "IApiClient";

    public override bool Execute()
    {
        if (string.IsNullOrWhiteSpace(Namespace))
        {
            Log.LogError("ZeroAlloc.Rest: Namespace is required and cannot be empty.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(Output))
        {
            Log.LogError("ZeroAlloc.Rest: Output is required and cannot be empty.");
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
            if (Spec.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || Spec.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                content = System.Threading.Tasks.Task.Run(() => OpenApiInterfaceGenerator.GenerateFromUrlAsync(Spec, Namespace, InterfaceName))
                    .GetAwaiter().GetResult();
            else
                content = System.Threading.Tasks.Task.Run(() => OpenApiInterfaceGenerator.GenerateFromFileAsync(Spec, Namespace, InterfaceName))
                    .GetAwaiter().GetResult();

            var dir = Path.GetDirectoryName(Output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(Output, content);
            Log.LogMessage(MessageImportance.Normal, $"ZeroAlloc.Rest: Generated {Output}");
            return true;
        }
        catch (Exception ex)
        {
            Log.LogError($"ZeroAlloc.Rest generation failed: {ex}");
            return false;
        }
    }
}
