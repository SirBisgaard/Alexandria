using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alexandria.Tests.KoboProtocol.Fixtures;

/// <summary>
/// On-disk representation of one captured Kobo protocol exchange. Written by the one-shot
/// extractor (<c>Tools/ExtractFixtures.cs</c>) and loaded by each endpoint test class through
/// <see cref="FixtureLoader"/>. Request headers and bodies are scrubbed of PII before being
/// committed — see the extractor for the scrubbing rules.
/// </summary>
public sealed class RecordedFixture
{
    /// <summary>Short label used in test output and fixture file names.</summary>
    public required string Name { get; init; }

    public required RecordedRequest Request { get; init; }

    public required int ExpectedStatus { get; init; }

    public string? ExpectedContentType { get; init; }

    /// <summary>Response headers worth asserting on. Only non-volatile headers (no dates, tokens).</summary>
    public Dictionary<string, string[]>? ExpectedHeaders { get; init; }

    /// <summary>The recorded response body, parsed as JSON. <see langword="null"/> for empty bodies (204, 307) and binary responses.</summary>
    public JsonElement? ExpectedBody { get; init; }

    /// <summary>True if the recorded body was <c>"BINARY"</c> in the logs (audio, KEPUB, image).</summary>
    public bool IsBinary { get; init; }
}

public sealed class RecordedRequest
{
    public required string Method { get; init; }

    /// <summary>The full Alexandria-side path, starting with <c>/api/kobo/{device}/…</c>.</summary>
    public required string Path { get; init; }

    public string Query { get; init; } = string.Empty;

    public Dictionary<string, string[]> Headers { get; init; } = new();

    /// <summary>UTF-8 request body. Empty for GETs.</summary>
    public string Body { get; init; } = string.Empty;
}

/// <summary>Shared JSON options so extractor and loader stay in sync.</summary>
public static class FixtureJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
}
