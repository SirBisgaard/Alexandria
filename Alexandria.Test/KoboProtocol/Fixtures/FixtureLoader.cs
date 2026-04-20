using System.Text.Json;

namespace Alexandria.Tests.KoboProtocol.Fixtures;

/// <summary>
/// Loads scrubbed Kobo protocol fixtures from <c>TestData/KoboProtocol/{endpoint}/*.json</c>
/// (copied next to the test assembly via the csproj's <c>Content</c> glob). Endpoint tests call
/// <see cref="LoadAll(string)"/> with their directory name (e.g. <c>"library-sync"</c>) and
/// iterate over the result. Returns an empty list when the directory doesn't exist so tests can
/// ship before their fixtures do.
/// </summary>
public static class FixtureLoader
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "TestData", "KoboProtocol");

    public static IReadOnlyList<RecordedFixture> LoadAll(string endpointFolder)
    {
        var dir = Path.Combine(Root, endpointFolder);
        if (!Directory.Exists(dir))
            return [];
        return Directory.GetFiles(dir, "*.json")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(Load)
            .ToArray();
    }

    public static RecordedFixture Load(string absolutePath)
    {
        using var stream = File.OpenRead(absolutePath);
        var fixture = JsonSerializer.Deserialize<RecordedFixture>(stream, FixtureJson.Options)
            ?? throw new InvalidOperationException($"Failed to deserialize fixture at {absolutePath}");
        return fixture;
    }
}
