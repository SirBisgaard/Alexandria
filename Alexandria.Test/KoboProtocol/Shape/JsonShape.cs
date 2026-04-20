using System.Text.Json;

namespace Alexandria.Tests.KoboProtocol.Shape;

/// <summary>
/// Structural comparison for JSON payloads. Use this to assert that a live controller response
/// has the same shape as a recorded Kobo response without failing on legitimately-drifting values
/// (timestamps, UUIDs, rotating bearer tokens, durations).
/// </summary>
/// <remarks>
/// <para>
/// "Shape" means: every property present in <c>expected</c> is present in <c>actual</c> with the
/// same <see cref="JsonValueKind"/>. Arrays match element-wise against the first element of the
/// expected array (arrays are assumed homogeneous in Kobo responses). Null expected values are
/// permissive — they only require the key to exist.
/// </para>
/// <para>
/// <paramref name="strictPaths"/> names dotted JSON paths where the value must match exactly,
/// not just type-wise. Use it for controller-produced invariants: <c>"OriginCategory"</c>,
/// <c>"DownloadUrls[].Format"</c>, <c>"DrmType"</c>, etc. Array elements are addressed with
/// <c>"[]"</c> (no index) because all elements share the same template.
/// </para>
/// </remarks>
public static class JsonShape
{
    public static void AssertMatches(JsonElement expected, JsonElement actual, params string[] strictPaths)
    {
        var strict = new HashSet<string>(strictPaths, StringComparer.Ordinal);
        AssertMatchesInternal(expected, actual, path: "", strict);
    }

    private static void AssertMatchesInternal(JsonElement expected, JsonElement actual, string path, HashSet<string> strict)
    {
        if (expected.ValueKind == JsonValueKind.Null || expected.ValueKind == JsonValueKind.Undefined)
            return;

        if (expected.ValueKind != actual.ValueKind)
        {
            if (actual.ValueKind == JsonValueKind.Null)
                return;
            Assert.Fail($"Shape mismatch at '{DisplayPath(path)}': expected {expected.ValueKind}, got {actual.ValueKind}");
        }

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in expected.EnumerateObject())
                {
                    var childPath = path.Length == 0 ? prop.Name : $"{path}.{prop.Name}";
                    if (!actual.TryGetProperty(prop.Name, out var actualChild))
                        Assert.Fail($"Missing key at '{childPath}'");
                    AssertMatchesInternal(prop.Value, actualChild, childPath, strict);
                }
                break;

            case JsonValueKind.Array:
                var expectedArr = expected.EnumerateArray().ToArray();
                if (expectedArr.Length == 0)
                    return;
                var template = expectedArr[0];
                var elementPath = $"{path}[]";
                foreach (var element in actual.EnumerateArray())
                    AssertMatchesInternal(template, element, elementPath, strict);
                break;

            default:
                if (strict.Contains(path))
                {
                    if (!JsonValuesEqual(expected, actual))
                        Assert.Fail($"Strict path '{DisplayPath(path)}' value mismatch: expected {expected.GetRawText()}, got {actual.GetRawText()}");
                }
                break;
        }
    }

    private static bool JsonValuesEqual(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
            return false;
        return a.ValueKind switch
        {
            JsonValueKind.String => string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => a.GetBoolean() == b.GetBoolean(),
            JsonValueKind.Null => true,
            _ => a.GetRawText() == b.GetRawText()
        };
    }

    private static string DisplayPath(string path) => path.Length == 0 ? "<root>" : path;
}
