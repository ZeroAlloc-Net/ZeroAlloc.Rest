using System.Globalization;
using ZeroAlloc.Rest.Anthropic;
using ZeroAlloc.Rest.Anthropic.Recorder;
using ZeroAlloc.Rest.Anthropic.Transcripts;

// Records the transcripts of the Anthropic client against the real API: ANTHROPIC_API_KEY in the environment,
// optionally the target directory as the only argument. Exit codes: 0 recorded and published, 1 a call or the cache
// check failed, 2 no key. The key is read once, handed to the client and never printed or written.
// Everything is recorded into a fresh scratch directory. Only a complete run replaces the recordings in the target;
// any other end deletes the scratch directory and leaves the target as it was.
const int MaxCacheAttempts = 3;

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("ANTHROPIC_API_KEY is not set.");
    return 2;
}

var directory = args.Length > 0 ? args[0] : DefaultDirectory();
if (directory is null)
{
    Console.Error.WriteLine("Could not find tests/ZeroAlloc.Rest.Anthropic.Tests/Transcripts; pass the directory as an argument.");
    return 1;
}

var scratch = Path.Combine(Path.GetTempPath(), "anthropic-transcripts-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    using var recorder = new RecordingHandler(scratch, new HttpClientHandler());
    using var http = new HttpClient(recorder);
    // No retries: one request is one call is one set of files, and a retry would also cost again.
    var client = AnthropicClient.Create(http, new AnthropicClientOptions { ApiKey = apiKey, MaxRetries = 0 });

    var code = await RecordAsync(client, recorder).ConfigureAwait(false);
    if (code != 0)
        return code;

    var missing = TranscriptPublisher.Publish(scratch, directory, TranscriptRequests.Names);
    if (missing.Count > 0)
    {
        Console.Error.WriteLine($"The recording is incomplete, nothing was published. Missing: {string.Join(", ", missing)}.");
        return 1;
    }

    Console.WriteLine($"Recorded {TranscriptRequests.Names.Count} transcripts in {directory}.");
    return 0;
}
catch (AnthropicApiException ex)
{
    Console.Error.WriteLine($"Failed: {DescribeError(ex.Error)}");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed with {ex.GetType().Name}.");
    return 1;
}
finally
{
    Directory.Delete(scratch, recursive: true);
}

static async Task<int> RecordAsync(AnthropicClient client, RecordingHandler recorder)
{
    recorder.Name = "text";
    if (await CreateAsync(client, TranscriptRequests.Text()).ConfigureAwait(false) is null)
        return 1;

    recorder.Name = "stream-text";
    if (!await StreamAsync(client, TranscriptRequests.StreamText(), requireToolUse: false).ConfigureAwait(false))
        return 1;

    recorder.Name = "tool-use";
    var toolUse = await CreateAsync(client, TranscriptRequests.ToolUse()).ConfigureAwait(false);
    if (toolUse is null)
        return 1;
    var call = toolUse.Content.OfType<ToolUseBlock>().FirstOrDefault();
    if (call is null || !string.Equals(toolUse.StopReason, StopReasons.ToolUse, StringComparison.Ordinal))
    {
        Console.Error.WriteLine($"tool-use: expected a tool_use block, stop_reason was {toolUse.StopReason}.");
        return 1;
    }

    recorder.Name = "tool-result";
    var answer = await CreateAsync(client, TranscriptRequests.ToolResult(call.Id, call.Name, call.Input)).ConfigureAwait(false);
    if (answer is null)
        return 1;
    if (!answer.Content.OfType<TextBlock>().Any())
    {
        Console.Error.WriteLine("tool-result: expected a text block.");
        return 1;
    }

    recorder.Name = "stream-tool-use";
    if (!await StreamAsync(client, TranscriptRequests.StreamToolUse(), requireToolUse: true).ConfigureAwait(false))
        return 1;

    var lines = TranscriptRequests.InitialPaddingLines;
    for (var attempt = 1; attempt <= MaxCacheAttempts; attempt++, lines *= 2)
    {
        Console.WriteLine($"cache attempt {attempt}: {lines} padding lines");
        recorder.Name = "cache-write";
        var write = await CreateAsync(client, TranscriptRequests.CacheWrite(lines)).ConfigureAwait(false);
        recorder.Name = "cache-read";
        var read = write is null ? null : await CreateAsync(client, TranscriptRequests.CacheRead(lines)).ConfigureAwait(false);
        if (write is null || read is null)
            return 1;

        if (write.Usage?.CacheCreationInputTokens > 0 && read.Usage?.CacheReadInputTokens > 0)
            return 0;

        Console.Error.WriteLine("The cache pair did not hold; raising the padding.");
    }

    Console.Error.WriteLine($"The cache pair did not hold after {MaxCacheAttempts} attempts. Not retrying.");
    return 1;
}

static async Task<MessageResponse?> CreateAsync(AnthropicClient client, MessageRequest request)
{
    var result = await client.CreateMessageAsync(request, CancellationToken.None).ConfigureAwait(false);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"Request failed: {DescribeError(result.Error)}");
        return null;
    }

    Console.WriteLine(DescribeUsage(result.Value.Usage));
    return result.Value;
}

static async Task<bool> StreamAsync(AnthropicClient client, MessageRequest request, bool requireToolUse)
{
    var events = 0;
    var sawToolUse = false;
    await foreach (var e in client.StreamMessageAsync(request, CancellationToken.None).ConfigureAwait(false))
    {
        events++;
        sawToolUse |= e is ContentBlockStartEvent { ContentBlock: ToolUseBlock };
    }

    Console.WriteLine($"{events} stream events");
    if (requireToolUse && !sawToolUse)
        Console.Error.WriteLine("stream-tool-use: no tool_use content block in the stream.");
    return events > 0 && (!requireToolUse || sawToolUse);
}

// The error's type and status. The message is the API's own text for a described failure; for a transport failure
// it comes from the exception, so it is left out.
static string DescribeError(AnthropicApiError error) =>
    error.Status > 0 ? $"{error.Type}, status {error.Status}: {error.Message}" : error.Type;

static string DescribeUsage(Usage? usage) => usage is null
    ? "no usage"
    : string.Create(CultureInfo.InvariantCulture,
        $"input {usage.InputTokens}, output {usage.OutputTokens}, cache write {usage.CacheCreationInputTokens}, cache read {usage.CacheReadInputTokens}");

static string? DefaultDirectory()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Rest.slnx")))
            return Path.Combine(dir.FullName, "tests", "ZeroAlloc.Rest.Anthropic.Tests", "Transcripts");
    }

    return null;
}
