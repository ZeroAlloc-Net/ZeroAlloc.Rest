using System.Reflection;
using Xunit;
using ZeroAlloc.Rest.Anthropic.Transcripts;

namespace ZeroAlloc.Rest.Anthropic.Tests;

/// <summary>
/// Guards the recorded transcripts under <c>Transcripts/</c>: no secret in any file, and every expected transcript
/// recorded in full. The folder is empty until the recorder has run against the real API.
/// </summary>
public sealed class TranscriptScrubTests
{
    private const string PendingRecording =
        "No transcripts are recorded yet. Task 8 Step 4 records them; then delete this Skip. "
        + "The test Recordings_lift_the_skip_on_the_three_files_check fails until it is gone.";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "Transcripts");

    [Fact]
    public void No_transcript_holds_a_secret()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var leaks = new List<string>();
        foreach (var file in Files())
        {
            foreach (var finding in Findings(File.ReadAllText(file), key))
                leaks.Add($"{Path.GetFileName(file)}: {finding}");
        }

        // Red: add "x-api-key":"x" to one meta file under Transcripts/; that file is then listed here.
        Assert.Empty(leaks);
    }

    [Fact]
    public void The_scrub_finds_each_kind_of_secret()
    {
        // Red: drop the sk-ant- check from Findings; the first line is then empty.
        Assert.Single(Findings("a sk-ant-api03-abc b", null));
        // Red: compare x-api-key case-sensitively; the mixed-case header is then missed.
        Assert.Single(Findings("{\"X-Api-Key\":\"x\"}", null));
        // Red: drop the authorization check from Findings; this is then empty.
        Assert.Single(Findings("Authorization: Bearer x", null));
        // Red: skip the key comparison in Findings; the key is then not found.
        Assert.Single(Findings("the value hunter2 here", "hunter2"));
        // Red: treat an empty key as a key to find; every text then matches.
        Assert.Empty(Findings("clean text", string.Empty));
        // Red: report a finding for clean text, for example always add one; this is then not empty.
        Assert.Empty(Findings("{\"status\":200,\"headers\":{\"request-id\":\"req_1\"}}", "hunter2"));
    }

    [Fact(Skip = PendingRecording)]
    public void Every_transcript_has_its_three_files()
    {
        var missing = new List<string>();
        foreach (var name in TranscriptRequests.Names)
        {
            // Red: delete one of the recorded request files; its name is then listed here.
            AddIfMissing(missing, $"{name}.request.json");
            // Red: delete one of the recorded meta files; its name is then listed here.
            AddIfMissing(missing, $"{name}.meta.json");
            // Red: delete one of the recorded response files; its name is then listed here.
            if (!File.Exists(Path.Combine(Folder, $"{name}.response.json")) && !File.Exists(Path.Combine(Folder, $"{name}.response.sse")))
                missing.Add($"{name}.response.json or .sse");
        }

        Assert.Empty(missing);
    }

    [Fact]
    public void Recordings_lift_the_skip_on_the_three_files_check()
    {
        var skip = typeof(TranscriptScrubTests).GetMethod(nameof(Every_transcript_has_its_three_files))!
            .GetCustomAttribute<FactAttribute>()!.Skip;
        var recorded = Files().Any();

        // Red: record a transcript while the Skip stays on Every_transcript_has_its_three_files; this then fails.
        Assert.True(!recorded || skip is null, "Transcripts are recorded: delete the Skip on Every_transcript_has_its_three_files.");
    }

    private static void AddIfMissing(List<string> missing, string file)
    {
        if (!File.Exists(Path.Combine(Folder, file)))
            missing.Add(file);
    }

    private static string[] Files() => Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*", SearchOption.AllDirectories) : [];

    private static List<string> Findings(string text, string? key)
    {
        var found = new List<string>();
        foreach (var forbidden in new[] { "sk-ant-", "x-api-key", "authorization" })
        {
            if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                found.Add(forbidden);
        }

        if (!string.IsNullOrEmpty(key) && text.Contains(key, StringComparison.Ordinal))
            found.Add("the value of ANTHROPIC_API_KEY");
        return found;
    }
}
