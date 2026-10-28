using Xunit;
using ZeroAlloc.Rest.Anthropic.Recorder;

namespace ZeroAlloc.Rest.Anthropic.Tests;

public sealed class TranscriptPublisherTests : IDisposable
{
    private static readonly string[] Names = ["a", "b"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "publisher-" + Guid.NewGuid().ToString("N"));
    private readonly string _scratch;
    private readonly string _target;

    public TranscriptPublisherTests()
    {
        _scratch = Path.Combine(_root, "scratch");
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_scratch);
        Directory.CreateDirectory(_target);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_complete_recording_replaces_the_old_ones_and_keeps_other_files()
    {
        WriteSet(_scratch, "new");
        File.WriteAllText(Path.Combine(_target, "a.request.json"), "old");
        File.WriteAllText(Path.Combine(_target, "gone.meta.json"), "old");
        File.WriteAllText(Path.Combine(_target, "placeholder.txt"), "keep");

        var missing = TranscriptPublisher.Publish(_scratch, _target, Names);

        Assert.Empty(missing);
        // Red: skip File.Copy in Publish; the old content stays.
        Assert.Equal("new", File.ReadAllText(Path.Combine(_target, "a.request.json")));
        // Red: skip the deletion of old recordings in Publish; the stale file stays.
        Assert.False(File.Exists(Path.Combine(_target, "gone.meta.json")));
        // Red: delete every file in the target in Publish; the placeholder is then gone.
        Assert.Equal("keep", File.ReadAllText(Path.Combine(_target, "placeholder.txt")));
        // Red: copy only the request files in Publish; the sse response is then missing.
        Assert.True(File.Exists(Path.Combine(_target, "b.response.sse")));
    }

    [Fact]
    public void An_incomplete_recording_changes_nothing()
    {
        WriteSet(_scratch, "new");
        File.Delete(Path.Combine(_scratch, "b.meta.json"));
        File.WriteAllText(Path.Combine(_target, "a.request.json"), "old");

        var missing = TranscriptPublisher.Publish(_scratch, _target, Names);

        // Red: drop the completeness check in Publish; nothing is then reported missing.
        Assert.Equal(["b.meta.json"], missing);
        // Red: drop the completeness check in Publish; the old file is then overwritten.
        Assert.Equal("old", File.ReadAllText(Path.Combine(_target, "a.request.json")));
        // Red: drop the completeness check in Publish; the new file is then copied in.
        Assert.False(File.Exists(Path.Combine(_target, "b.request.json")));
    }

    private static void WriteSet(string directory, string content)
    {
        File.WriteAllText(Path.Combine(directory, "a.request.json"), content);
        File.WriteAllText(Path.Combine(directory, "a.response.json"), content);
        File.WriteAllText(Path.Combine(directory, "a.meta.json"), content);
        File.WriteAllText(Path.Combine(directory, "b.request.json"), content);
        File.WriteAllText(Path.Combine(directory, "b.response.sse"), content);
        File.WriteAllText(Path.Combine(directory, "b.meta.json"), content);
    }
}
