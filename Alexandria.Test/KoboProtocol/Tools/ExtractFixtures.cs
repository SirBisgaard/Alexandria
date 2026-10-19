using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Alexandria.Tests.KoboProtocol.Fixtures;
using Microsoft.Data.Sqlite;

namespace Alexandria.Tests.KoboProtocol.Tools;

/// <summary>
/// One-shot tool that turns a production Alexandria <c>KoboTrafficLogs</c> table into scrubbed
/// JSON fixtures under <c>Alexandria.Test/TestData/KoboProtocol/</c>. Not part of the regular
/// test run — the class is <c>[Ignore]</c>'d by default.
/// </summary>
/// <remarks>
/// <para>
/// <b>To re-generate fixtures:</b> point <c>ALEXANDRIA_PROD_DB_PATH</c> at your production DB
/// (default <c>/path/to/alexandria.db</c>), comment out the <c>[Ignore]</c>
/// attribute on the class, and run <c>dotnet test --filter FullyQualifiedName~ExtractFixtures</c>.
/// Re-add <c>[Ignore]</c> before committing. Re-running the tool with the same DB must produce
/// byte-identical output — deterministic ordering is enforced via <c>ORDER BY Id</c> and
/// stable fixture names.
/// </para>
/// <para>
/// <b>Scrubbing rules.</b> Fixtures ship in a public repository, so everything that could
/// identify the operator or reveal their library is replaced before writing:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>Identifying headers: <c>Authorization</c>, <c>Cookie</c>,
///     <c>x-forwarded-for</c>, <c>Referer</c>, <c>Origin</c>, <c>Host</c> are dropped.
///     <c>x-kobo-deviceid</c> → <c>TEST-SERIAL</c>. <c>x-kobo-synctoken</c> →
///     <c>SYNC-TOKEN-PLACEHOLDER</c>. JWT-shaped and <c>kobo_user_id</c> leaks are regex-replaced
///     with <c>REDACTED</c>. Body fields <c>AccessToken</c>, <c>RefreshToken</c>, <c>UserKey</c> and
///     <c>SerialNumber</c> are replaced with placeholders in requests and responses alike.</description>
///   </item>
///   <item>
///     <description>UUIDs: the recorded device UUID maps to the fixed synthetic
///     <see cref="SyntheticDeviceUuid"/>. Every other UUID found anywhere in the exchange is
///     remapped per-fixture via deterministic SHA-256, so the relationships inside one fixture
///     (download URL UUID = entitlement ID = cover image ID) are preserved but the values are
///     indistinguishable from random — you can't tell which prod book a fixture came from.
///     Well-known Kobo constants (<c>00000000-0000-0000-0000-000000000001</c>, <c>Guid.Empty</c>)
///     are preserved so enum-shape checks keep working.</description>
///   </item>
///   <item>
///     <description>Host: the operator's personal hostname in recorded URLs is replaced with
///     <see cref="SyntheticHost"/>.</description>
///   </item>
///   <item>
///     <description>Library content in response bodies: <c>Title</c>, <c>Subtitle</c>,
///     <c>Description</c>, <c>Publisher.Name</c>, <c>PublisherName</c>, <c>Imprint</c>,
///     <c>SeriesName</c>, <c>Contributors</c>, <c>ContributorRoles[].Name</c>, <c>Isbn</c>/<c>ISBN</c>
///     are replaced with fixed synthetic sentinels. The shape matcher only cares about types and
///     keys, so the replacement preserves everything the tests actually check.</description>
///   </item>
///   <item>
///     <description>Responses from <c>/v1/user/*</c> endpoints are dropped entirely — those
///     payloads contain user profile info no scrubbing rule can safely cover.</description>
///   </item>
/// </list>
/// </remarks>
[Ignore("One-shot extraction tool. Un-ignore, run, re-ignore, commit the generated fixtures.")]
[TestClass]
public sealed class ExtractFixtures
{
    private const string DefaultDbPath = "/path/to/alexandria.db";

    /// <summary>
    /// Must match <see cref="KoboProtocolTestBase.RecordedDeviceUuid"/>. Replaces the real
    /// production device UUID everywhere it appears in fixtures.
    /// </summary>
    public static readonly Guid SyntheticDeviceUuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    /// <summary>Replacement host for the operator's production hostname in recorded URLs.</summary>
    public const string SyntheticHost = "test.example.com";

    private static readonly Regex JwtRegex = new(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.Compiled);
    private static readonly Regex KoboUserIdRegex = new(@"""kobo_user_id""\s*:\s*""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex BearerLikeRegex = new(@"""Bearer\s+[A-Za-z0-9._-]+""", RegexOptions.Compiled);
    private static readonly Regex UuidRegex = new(@"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>UUIDs that must not be rewritten — these are Kobo protocol constants, not identifiers.</summary>
    private static readonly HashSet<Guid> PreservedUuids =
    [
        Guid.Empty,
        Guid.Parse("00000000-0000-0000-0000-000000000001")
    ];

    private static readonly HashSet<string> HeadersToDrop = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "x-forwarded-for",
        "Referer",
        "Origin",
        "Host"
    };

    [TestMethod]
    public void Extract()
    {
        var dbPath = Environment.GetEnvironmentVariable("ALEXANDRIA_PROD_DB_PATH") ?? DefaultDbPath;
        Assert.IsTrue(File.Exists(dbPath), $"Source DB not found at '{dbPath}'. Set ALEXANDRIA_PROD_DB_PATH.");

        var outputRoot = Path.Combine(FindTestProjectRoot(), "TestData", "KoboProtocol");
        Directory.CreateDirectory(outputRoot);

        var rows = ReadTrafficLogs(dbPath);
        Console.WriteLine($"Read {rows.Count} traffic rows from {dbPath}");

        var (registeredDeviceUuid, unregisteredDeviceUuid, realHost) = IdentifyOperatorIdentity(rows);
        Console.WriteLine($"Detected registered device {registeredDeviceUuid} and host {realHost}");
        var globalReplacements = new GlobalReplacements(registeredDeviceUuid, unregisteredDeviceUuid, realHost);

        var buckets = rows.Select(Classify).Where(c => c.folder is not null).ToList();
        Console.WriteLine($"Classified {buckets.Count} rows into endpoint families");

        var grouped = buckets.GroupBy(b => b.folder!).ToDictionary(g => g.Key, g => g.ToList());
        var written = 0;

        foreach (var (folder, entries) in grouped.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var endpointDir = Path.Combine(outputRoot, folder);
            if (Directory.Exists(endpointDir))
                Directory.Delete(endpointDir, recursive: true);
            Directory.CreateDirectory(endpointDir);

            var limit = LimitFor(folder);
            var selected = entries.Take(limit).ToList();

            var index = 1;
            foreach (var (row, _) in selected)
            {
                var fixture = BuildFixture(row, name: $"{folder}-{index:00}", globalReplacements);
                var fileName = $"{index:00}.json";
                var json = JsonSerializer.Serialize(fixture, FixtureJson.Options);
                File.WriteAllText(Path.Combine(endpointDir, fileName), json);
                index++;
                written++;
            }
            Console.WriteLine($"  {folder}: wrote {selected.Count} fixture(s)");
        }

        Console.WriteLine($"Done. {written} fixtures written under {outputRoot}");
    }

    /// <summary>Decide which endpoint folder a row belongs to. Rows returning <see langword="null"/> are skipped.</summary>
    private static (TrafficRow row, string? folder) Classify(TrafficRow row)
    {
        var path = StripQuery(row.Url);
        var afterDevice = StripDevicePrefix(path);

        // Covers: /{device}/{uuid}/{w}/{h}/[{q}/]{tp}/image.jpg
        if (afterDevice.EndsWith("/image.jpg", StringComparison.OrdinalIgnoreCase) && row.Method == "GET")
            return (row, "cover");

        if (afterDevice.Equals("/v1/initialization", StringComparison.OrdinalIgnoreCase) && row.Method == "GET")
            return (row, row.StatusCode == 200 ? "initialization-200" : "initialization-401");

        if (afterDevice.Equals("/v1/library/sync", StringComparison.OrdinalIgnoreCase) && row.Method == "GET" && row.StatusCode == 200)
            return (row, "library-sync");

        if (afterDevice.StartsWith("/v1/library/", StringComparison.OrdinalIgnoreCase))
        {
            if (afterDevice.EndsWith("/metadata", StringComparison.OrdinalIgnoreCase) && row.Method == "GET" && row.StatusCode == 200)
                return (row, "library-metadata");
            if (afterDevice.EndsWith("/state", StringComparison.OrdinalIgnoreCase) && row.StatusCode == 200)
                return (row, row.Method == "GET" ? "library-state-get" : "library-state-put");
            if (row.Method == "DELETE")
                return (row, row.StatusCode == 204 ? "library-delete-204" : "library-delete-401");
        }

        if (afterDevice.StartsWith("/v1/products/books/series/", StringComparison.OrdinalIgnoreCase) && row.Method == "GET" && row.StatusCode == 200)
            return (row, "products-series");

        if (afterDevice.StartsWith("/v1/products/books/", StringComparison.OrdinalIgnoreCase) && row.Method == "GET")
            return (row, "products-book");

        if (afterDevice.Contains("/prices", StringComparison.OrdinalIgnoreCase) && row.Method == "GET" && row.StatusCode == 200)
            return (row, "products-prices");

        if (afterDevice.StartsWith("/v1/products/", StringComparison.OrdinalIgnoreCase) && row.Method == "GET")
            return (row, "products-other");

        if (afterDevice.Equals("/v1/analytics/event", StringComparison.OrdinalIgnoreCase) && row.Method == "POST" && row.StatusCode == 200)
            return (row, "analytics-event");

        // Everything else with /v1/ that wasn't intercepted becomes a pass-through sample.
        if (afterDevice.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
            return (row, "passthrough");

        return (row, null);
    }

    private static int LimitFor(string folder) => folder switch
    {
        "library-sync" => 2,
        "library-metadata" => 3,
        "products-series" => 2,
        "analytics-event" => 2,
        "passthrough" => 12,
        _ => 1
    };

    private static RecordedFixture BuildFixture(TrafficRow row, string name, GlobalReplacements globals)
    {
        // Every fixture gets its own UUID map. Two fixtures that referenced the same production
        // book UUID will end up with two unrelated synthetic UUIDs — which is fine because the
        // tests don't reach across fixtures. Within one fixture, references stay consistent.
        var scrubber = new Scrubber(globals);

        var rawPath = StripQuery(row.Url);
        var path = scrubber.ScrubText(AlignPathToTestDevice(rawPath));
        var query = scrubber.ScrubText(ExtractQuery(row.Url));
        var requestHeaders = ParseRequestHeaders(row.RequestHeaders);
        var scrubbedHeaders = ScrubRequestHeaders(requestHeaders);
        var scrubbedRequestBody = scrubber.ScrubBodyText(RegexScrubKnownTokens(row.RequestBody));

        var responseHeaders = ParseResponseHeaders(row.ResponseHeaders);
        var isBinary = string.Equals(row.ResponseBody, "BINARY", StringComparison.Ordinal);
        var isUserEndpoint = rawPath.Contains("/v1/user/", StringComparison.OrdinalIgnoreCase);

        JsonElement? expectedBody = null;
        if (!isBinary && !isUserEndpoint && !string.IsNullOrWhiteSpace(row.ResponseBody))
        {
            var scrubbedResponseBody = scrubber.ScrubBodyText(RegexScrubKnownTokens(row.ResponseBody) ?? string.Empty) ?? string.Empty;
            try
            {
                using var doc = JsonDocument.Parse(scrubbedResponseBody);
                expectedBody = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                // Non-JSON body — keep it null, tests can fall back to status/content-type checks.
            }
        }

        return new RecordedFixture
        {
            Name = name,
            Request = new RecordedRequest
            {
                Method = row.Method,
                Path = path,
                Query = query,
                Headers = scrubbedHeaders,
                Body = scrubbedRequestBody ?? string.Empty
            },
            ExpectedStatus = row.StatusCode,
            ExpectedContentType = ExtractContentType(responseHeaders),
            ExpectedHeaders = null,
            ExpectedBody = expectedBody,
            IsBinary = isBinary
        };
    }

    /// <summary>
    /// Rewrite the stored absolute URL's path to the Alexandria-side relative path only, keeping
    /// the device UUID so tests replay against the same seeded device.
    /// </summary>
    private static string AlignPathToTestDevice(string storedUrl)
    {
        var slashSlash = storedUrl.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash < 0)
            return storedUrl;
        var afterScheme = storedUrl[(slashSlash + 2)..];
        var firstSlash = afterScheme.IndexOf('/');
        return firstSlash < 0 ? "/" : afterScheme[firstSlash..];
    }

    private static string StripQuery(string url)
    {
        var q = url.IndexOf('?');
        return q < 0 ? url : url[..q];
    }

    private static string ExtractQuery(string url)
    {
        var q = url.IndexOf('?');
        return q < 0 ? string.Empty : url[q..];
    }

    private static string StripDevicePrefix(string path)
    {
        // Path looks like http://host/api/kobo/{device}/v1/... — strip up to and including the device segment.
        var marker = "/api/kobo/";
        var idx = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return path;
        var afterMarker = path[(idx + marker.Length)..];
        var slash = afterMarker.IndexOf('/');
        return slash < 0 ? "/" : afterMarker[slash..];
    }

    private static Dictionary<string, string[]> ParseRequestHeaders(string? raw)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
            return headers;
        foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].TrimEnd();
            headers[key] = [value];
        }
        return headers;
    }

    private static Dictionary<string, string[]> ParseResponseHeaders(string? raw)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw))
            return headers;
        // Response headers are joined by ",\n" per the middleware's format string.
        foreach (var line in raw.Split(",\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            headers[key] = [value];
        }
        return headers;
    }

    private static string? ExtractContentType(Dictionary<string, string[]> headers)
    {
        return headers.TryGetValue("Content-Type", out var v) && v.Length > 0 ? v[0] : null;
    }

    private static Dictionary<string, string[]> ScrubRequestHeaders(Dictionary<string, string[]> headers)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in headers)
        {
            if (HeadersToDrop.Contains(key))
                continue;
            if (string.Equals(key, "x-kobo-deviceid", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = [KoboProtocolTestBase.DeviceSerial];
                continue;
            }
            if (string.Equals(key, "x-kobo-synctoken", StringComparison.OrdinalIgnoreCase))
            {
                // Alexandria's own wrapped sync token — opaque cursor, base64(JSON). Not PII, but
                // committed fixtures should be small and reviewer-friendly.
                result[key] = ["SYNC-TOKEN-PLACEHOLDER"];
                continue;
            }
            result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// Regex-level defensive scrub for tokens that could leak into body text even after
    /// UUID + field-aware scrubbing. Runs before any JSON parsing.
    /// </summary>
    private static string? RegexScrubKnownTokens(string? body)
    {
        if (string.IsNullOrEmpty(body))
            return body;
        var scrubbed = JwtRegex.Replace(body, "REDACTED");
        scrubbed = BearerLikeRegex.Replace(scrubbed, "\"REDACTED\"");
        scrubbed = KoboUserIdRegex.Replace(scrubbed, "\"kobo_user_id\":\"REDACTED\"");
        return scrubbed;
    }

    /// <summary>
    /// Scans all traffic rows to work out (a) which device UUID represents the operator's
    /// registered device (most frequent), (b) whether any secondary device UUID shows up
    /// (typically an old/stale device generating 401s — we need it distinct so tests can still
    /// exercise the unregistered-device path), and (c) the operator's real hostname.
    /// </summary>
    private static (Guid registered, Guid? unregistered, string? host) IdentifyOperatorIdentity(List<TrafficRow> rows)
    {
        var deviceCounts = new Dictionary<Guid, int>();
        string? host = null;
        foreach (var row in rows)
        {
            host ??= ExtractHost(row.Url);
            var deviceUuid = ExtractDeviceUuidFromUrl(row.Url);
            if (deviceUuid is not null)
            {
                deviceCounts[deviceUuid.Value] = deviceCounts.GetValueOrDefault(deviceUuid.Value) + 1;
            }
        }
        if (deviceCounts.Count == 0)
            throw new InvalidOperationException("No /api/kobo/{device}/ URLs found in traffic logs");
        var ordered = deviceCounts.OrderByDescending(kv => kv.Value).ToList();
        var registered = ordered[0].Key;
        Guid? unregistered = ordered.Count > 1 ? ordered[1].Key : null;
        return (registered, unregistered, host);
    }

    private static Guid? ExtractDeviceUuidFromUrl(string url)
    {
        var marker = "/api/kobo/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;
        var after = url[(idx + marker.Length)..];
        var slash = after.IndexOf('/');
        var segment = slash < 0 ? after : after[..slash];
        return Guid.TryParse(segment, out var uuid) ? uuid : null;
    }

    private static string? ExtractHost(string url)
    {
        var slashSlash = url.IndexOf("//", StringComparison.Ordinal);
        if (slashSlash < 0)
            return null;
        var after = url[(slashSlash + 2)..];
        var firstSlash = after.IndexOf('/');
        return firstSlash < 0 ? after : after[..firstSlash];
    }

    /// <summary>Identity substitutions applied identically across every fixture.</summary>
    private sealed record GlobalReplacements(Guid RegisteredDeviceUuid, Guid? UnregisteredDeviceUuid, string? RealHost)
    {
        public static readonly Guid SyntheticUnregisteredDeviceUuid = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    }

    /// <summary>
    /// Per-fixture scrubber. Holds a UUID-to-synthetic map so all references to the same
    /// production UUID within one fixture stay consistent (download URL UUID matches entitlement
    /// ID, etc.) while still being unlinkable from the prod source.
    /// </summary>
    private sealed class Scrubber
    {
        private readonly GlobalReplacements globals;
        private readonly Dictionary<Guid, Guid> uuidMap = new();

        public Scrubber(GlobalReplacements globals)
        {
            this.globals = globals;
        }

        public Guid MapUuid(Guid original)
        {
            if (original == globals.RegisteredDeviceUuid)
                return SyntheticDeviceUuid;
            if (globals.UnregisteredDeviceUuid is { } unreg && original == unreg)
                return GlobalReplacements.SyntheticUnregisteredDeviceUuid;
            if (PreservedUuids.Contains(original))
                return original;
            if (!uuidMap.TryGetValue(original, out var synthetic))
            {
                // Deterministic synthetic UUID: SHA-256 of the original bytes, first 16 bytes.
                // Stable across extractor runs (same source UUID → same synthetic), but the
                // mapping is one-way and not externally reversible.
                var hash = SHA256.HashData(original.ToByteArray());
                synthetic = new Guid(hash.AsSpan(0, 16));
                uuidMap[original] = synthetic;
            }
            return synthetic;
        }

        /// <summary>
        /// Substring-scrub for free text: replaces UUIDs and the operator's hostname. Leaves
        /// everything else intact.
        /// </summary>
        public string ScrubText(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return text ?? string.Empty;
            var result = UuidRegex.Replace(text, match =>
            {
                var parsed = Guid.Parse(match.Value);
                return MapUuid(parsed).ToString();
            });
            if (!string.IsNullOrEmpty(globals.RealHost))
                result = result.Replace(globals.RealHost, SyntheticHost, StringComparison.Ordinal);
            return result;
        }

        /// <summary>
        /// Full scrub of a JSON body string: parses into a mutable tree, applies field-aware
        /// replacements for library-content fields (Title, Description, author names, etc.),
        /// maps every UUID, substitutes hostnames, and re-serializes.
        /// </summary>
        public string? ScrubBodyText(string? body)
        {
            if (string.IsNullOrEmpty(body))
                return body;
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(body);
            }
            catch (JsonException)
            {
                // Not JSON — fall back to substring scrubbing.
                return ScrubText(body);
            }
            if (node is null)
                return body;
            var scrubbed = ScrubNode(node, currentFieldName: null, parentFieldName: null);
            return scrubbed?.ToJsonString() ?? body;
        }

        private JsonNode? ScrubNode(JsonNode? node, string? currentFieldName, string? parentFieldName)
        {
            switch (node)
            {
                case null:
                    return null;
                case JsonObject obj:
                    var newObj = new JsonObject();
                    foreach (var kvp in obj)
                    {
                        var childFieldName = kvp.Key;
                        if (TryFieldReplaceObject(childFieldName, kvp.Value, out var replacementObj))
                        {
                            newObj[childFieldName] = replacementObj;
                        }
                        else
                        {
                            newObj[childFieldName] = ScrubNode(kvp.Value, childFieldName, currentFieldName);
                        }
                    }
                    return newObj;
                case JsonArray arr:
                    var newArr = new JsonArray();
                    foreach (var elem in arr)
                    {
                        // Array elements inherit the array's field name — lets rules like
                        // "Contributors: [string, ...]" apply to each string element.
                        newArr.Add(ScrubNode(elem, currentFieldName, parentFieldName));
                    }
                    return newArr;
                case JsonValue value when value.TryGetValue<string>(out var str):
                    if (TryFieldReplaceString(currentFieldName, parentFieldName, str, out var replacedStr))
                        return JsonValue.Create(replacedStr);
                    return JsonValue.Create(ScrubText(str));
                default:
                    // Numbers, bools, nulls — unchanged.
                    return node.DeepClone();
            }
        }

        /// <summary>
        /// Replaces whole subtrees where the key alone determines the replacement (e.g. a
        /// <c>Publisher</c> object that carries <c>{Imprint, Name}</c> gets substituted wholesale).
        /// </summary>
        private bool TryFieldReplaceObject(string fieldName, JsonNode? value, out JsonNode? replacement)
        {
            replacement = null;
            if (value is null)
                return false;
            switch (fieldName)
            {
                case "Publisher" when value is JsonObject:
                    replacement = new JsonObject { ["Imprint"] = "", ["Name"] = "Scrubbed Publisher" };
                    return true;
                case "Contributors" when value is JsonArray:
                    replacement = new JsonArray { "Scrubbed Author" };
                    return true;
                case "ContributorRoles" when value is JsonArray arr:
                    var newArr = new JsonArray();
                    foreach (var elem in arr)
                    {
                        if (elem is JsonObject eo)
                        {
                            var o = new JsonObject { ["Name"] = "Scrubbed Author" };
                            if (eo.ContainsKey("Role"))
                                o["Role"] = eo["Role"]?.DeepClone();
                            newArr.Add(o);
                        }
                    }
                    replacement = newArr;
                    return true;
            }
            return false;
        }

        private static bool TryFieldReplaceString(string? fieldName, string? parentFieldName, string value, out string replaced)
        {
            replaced = string.Empty;
            return fieldName switch
            {
                "Title" => Replace(out replaced, "Scrubbed Title"),
                "Subtitle" => Replace(out replaced, "Scrubbed Subtitle"),
                "Description" => Replace(out replaced, "Scrubbed description."),
                "Imprint" => Replace(out replaced, ""),
                "PublisherName" => Replace(out replaced, "Scrubbed Publisher"),
                "SeriesName" => Replace(out replaced, "Scrubbed Series"),
                "Isbn" or "ISBN" => Replace(out replaced, "9780000000000"),
                "Contributors" => Replace(out replaced, "Scrubbed Author"),
                "Name" when parentFieldName == "Series" => Replace(out replaced, "Scrubbed Series"),
                "Name" when parentFieldName == "Tag" => Replace(out replaced, "Scrubbed Collection"),
                "AccessToken" => Replace(out replaced, "ACCESS-TOKEN-PLACEHOLDER"),
                "RefreshToken" => Replace(out replaced, "REFRESH-TOKEN-PLACEHOLDER"),
                "UserKey" => Replace(out replaced, "USER-KEY-PLACEHOLDER"),
                "SerialNumber" => Replace(out replaced, "TEST-SERIAL"),
                _ => false
            };
        }

        private static bool Replace(out string target, string value)
        {
            target = value;
            return true;
        }
    }

    private static List<TrafficRow> ReadTrafficLogs(string dbPath)
    {
        var rows = new List<TrafficRow>();
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Method, Url, RequestHeaders, RequestBody, StatusCode, ResponseHeaders, ResponseBody
            FROM KoboTrafficLogs
            ORDER BY Id
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new TrafficRow(
                Id: reader.GetInt64(0),
                Method: reader.GetString(1),
                Url: reader.GetString(2),
                RequestHeaders: reader.IsDBNull(3) ? null : reader.GetString(3),
                RequestBody: reader.IsDBNull(4) ? null : reader.GetString(4),
                StatusCode: reader.GetInt32(5),
                ResponseHeaders: reader.IsDBNull(6) ? null : reader.GetString(6),
                ResponseBody: reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return rows;
    }

    private static string FindTestProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Alexandria.Test.csproj")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Couldn't locate Alexandria.Test.csproj by walking up from AppContext.BaseDirectory");
        return dir.FullName;
    }

    private sealed record TrafficRow(long Id, string Method, string Url, string? RequestHeaders, string? RequestBody, int StatusCode, string? ResponseHeaders, string? ResponseBody)
    {
    }
}
