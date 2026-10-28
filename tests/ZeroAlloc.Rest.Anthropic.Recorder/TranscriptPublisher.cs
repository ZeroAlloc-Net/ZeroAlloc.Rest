namespace ZeroAlloc.Rest.Anthropic.Recorder;

/// <summary>
/// Moves a finished recording from its scratch directory into the committed Transcripts folder. Compiled into the
/// recorder and, by link, into the tests.
/// </summary>
internal static class TranscriptPublisher
{
    private static readonly string[] RecordingSuffixes = [".request.json", ".response.json", ".response.sse", ".meta.json"];

    /// <summary>
    /// Replaces the recordings in <paramref name="target"/> with those in <paramref name="scratch"/>. Nothing
    /// changes unless the scratch directory holds every transcript in full. Files that are not recordings, such as
    /// a placeholder, stay.
    /// </summary>
    /// <param name="scratch">The directory the recorder wrote into.</param>
    /// <param name="target">The committed Transcripts folder.</param>
    /// <param name="names">The transcripts that must be present.</param>
    /// <returns>The files that were missing from the scratch directory; empty when it was published.</returns>
    public static IReadOnlyList<string> Publish(string scratch, string target, IReadOnlyList<string> names)
    {
        var missing = new List<string>();
        foreach (var name in names)
        {
            AddIfMissing(missing, scratch, $"{name}.request.json");
            AddIfMissing(missing, scratch, $"{name}.meta.json");
            if (!File.Exists(Path.Combine(scratch, $"{name}.response.json")) && !File.Exists(Path.Combine(scratch, $"{name}.response.sse")))
                missing.Add($"{name}.response.json or .sse");
        }

        if (missing.Count > 0)
            return missing;

        Directory.CreateDirectory(target);
        foreach (var old in Directory.GetFiles(target).Where(IsRecording))
            File.Delete(old);
        foreach (var file in Directory.GetFiles(scratch).Where(IsRecording))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        return missing;
    }

    private static bool IsRecording(string path) =>
        RecordingSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.Ordinal));

    private static void AddIfMissing(List<string> missing, string directory, string file)
    {
        if (!File.Exists(Path.Combine(directory, file)))
            missing.Add(file);
    }
}
