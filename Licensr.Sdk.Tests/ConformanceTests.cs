using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Licensr.Sdk.Tests;

/// <summary>
/// C# conformance runner for <c>contract/conformance.yaml</c> — the third
/// of one per SDK (see <c>backend/scripts/seed_conformance_fixtures.py</c>'s
/// own doc comment), mirroring <c>sdks/js/test/conformance.test.ts</c> and
/// <c>sdks/cpp/test/test_conformance_live.cpp</c> case-for-case against a
/// *running* backend, using a raw <see cref="HttpClient"/> rather than
/// <see cref="LicensrClient"/> since a few cases exercise things the client
/// intentionally doesn't expose (a missing Authorization header, a
/// deprecated path). See <c>contract/README.md</c> for how to start the
/// backend and produce the fixtures file this reads.
/// </summary>
/// <remarks>
/// Skipped by default: requires <c>CONFORMANCE_BASE_URL</c> (the running
/// backend) and <c>CONFORMANCE_FIXTURES_PATH</c> (JSON from
/// <c>backend/scripts/seed_conformance_fixtures.py</c>), matching the other
/// two runners' own opt-in.
/// </remarks>
public class ConformanceTests
{
    private static readonly string? BaseUrl = Environment.GetEnvironmentVariable("CONFORMANCE_BASE_URL");
    private static readonly string? FixturesPath = Environment.GetEnvironmentVariable("CONFORMANCE_FIXTURES_PATH");
    private static readonly bool RunSlow = Environment.GetEnvironmentVariable("CONFORMANCE_RUN_SLOW") is not null;
    private static readonly HttpClient Client = new();

    private static readonly string ConformanceYamlPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "contract", "conformance.yaml"));

    public static IEnumerable<object[]> Cases() => LoadCases().Select(c => new object[] { c });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task RunsCase(ConformanceCase testCase)
    {
        if (BaseUrl is null || FixturesPath is null)
        {
            // Mirrors the other two runners' self-skip — see the class doc.
            return;
        }

        if (testCase.Id == "rate_limit_exceeded" && !RunSlow)
        {
            return;
        }

        var fixtures = LoadFixtures(FixturesPath);
        if (!fixtures.TryGetValue(testCase.Id, out var values))
        {
            throw new InvalidOperationException($"no seeded fixtures for case {testCase.Id} — re-run seed_conformance_fixtures.py");
        }

        var repeat = testCase.Request.Repeat ?? 1;
        HttpResponseMessage? response = null;
        for (var i = 0; i < repeat; i += 1)
        {
            response?.Dispose();
            response = await Send(BaseUrl, testCase.Request, values);
        }

        Assert.NotNull(response);
        var bodyText = await response!.Content.ReadAsStringAsync();
        var body = bodyText.Length > 0 ? JsonDocument.Parse(bodyText).RootElement : default;

        Assert.Equal(testCase.Expect.Status, (int)response.StatusCode);
        foreach (var (dottedPath, expected) in testCase.Expect.Json ?? new Dictionary<string, object?>())
        {
            var actual = GetPath(body, dottedPath);
            Assert.True(
                JsonNodesEqual(actual, ResolvePlaceholders(expected, values)),
                $"case {testCase.Id}: expected {ToJson(ResolvePlaceholders(expected, values))} at '{dottedPath}', got {actual}");
        }
    }

    private static async Task<HttpResponseMessage> Send(string baseUrl, ConformanceRequest request, Dictionary<string, string> values)
    {
        var path = (string)ResolvePlaceholders(request.Path, values)!;
        using var message = new HttpRequestMessage(
            string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) ? System.Net.Http.HttpMethod.Get : System.Net.Http.HttpMethod.Post,
            new Uri(new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/"), path));

        foreach (var (key, value) in AuthHeader(request.Auth, values))
        {
            message.Headers.TryAddWithoutValidation(key, value);
        }

        foreach (var (key, value) in OriginHeader(request.Origin, values))
        {
            message.Headers.TryAddWithoutValidation(key, value);
        }

        if (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedBody = ResolvePlaceholders(request.Body ?? new Dictionary<object, object>(), values);
            message.Content = new StringContent(ToJson(resolvedBody), Encoding.UTF8, "application/json");
        }

        return await Client.SendAsync(message);
    }

    private static Dictionary<string, string> AuthHeader(string auth, Dictionary<string, string> values) => auth switch
    {
        "missing" or "none" => new Dictionary<string, string>(),
        "invalid" => new Dictionary<string, string> { ["Authorization"] = "Bearer pk_live_00000000000000000000000000" },
        "valid_api_key" or "revoked" => new Dictionary<string, string> { ["Authorization"] = $"Bearer {values["api_key"]}" },
        _ => throw new ArgumentOutOfRangeException(nameof(auth), auth, "unknown auth kind"),
    };

    private static Dictionary<string, string> OriginHeader(string? origin, Dictionary<string, string> values) => origin switch
    {
        null or "omit" => new Dictionary<string, string>(),
        "allowed" => new Dictionary<string, string> { ["Origin"] = values["_allowed_origin"] },
        "disallowed" => new Dictionary<string, string> { ["Origin"] = "https://evil.example" },
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "unknown origin kind"),
    };

    /// <summary>Recursively substitutes <c>${name}</c> placeholders — mirrors <c>_resolve</c> in the Python runner.</summary>
    private static object? ResolvePlaceholders(object? value, Dictionary<string, string> values)
    {
        switch (value)
        {
            case string s:
                foreach (var (key, replacement) in values)
                {
                    s = s.Replace($"${{{key}}}", replacement);
                }

                return s;
            case IDictionary<object, object> map:
                return map.ToDictionary(kv => kv.Key, kv => ResolvePlaceholders(kv.Value, values));
            case IEnumerable<object> list:
                return list.Select(item => ResolvePlaceholders(item, values)).ToList();
            default:
                return value;
        }
    }

    /// <summary>Reads a dotted path like <c>detail.error</c> or <c>keys.0.kty</c> out of a parsed JSON body.</summary>
    private static JsonElement GetPath(JsonElement current, string dotted)
    {
        foreach (var part in dotted.Split('.'))
        {
            current = current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index)
                ? current[index]
                : current.GetProperty(part);
        }

        return current;
    }

    private static bool JsonNodesEqual(JsonElement actual, object? expected)
    {
        var expectedJson = ToJson(expected);
        using var expectedDoc = JsonDocument.Parse(expectedJson);
        return JsonElementDeepEquals(actual, expectedDoc.RootElement);
    }

    private static bool JsonElementDeepEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind)
        {
            return false;
        }

        return a.ValueKind switch
        {
            JsonValueKind.Object => a.EnumerateObject().Count() == b.EnumerateObject().Count()
                && a.EnumerateObject().All(prop => b.TryGetProperty(prop.Name, out var other) && JsonElementDeepEquals(prop.Value, other)),
            JsonValueKind.Array => a.GetArrayLength() == b.GetArrayLength()
                && a.EnumerateArray().Zip(b.EnumerateArray(), JsonElementDeepEquals).All(equal => equal),
            JsonValueKind.String => a.GetString() == b.GetString(),
            JsonValueKind.Number => a.GetRawText() == b.GetRawText(),
            _ => a.GetRawText() == b.GetRawText(),
        };
    }

    private static string ToJson(object? value)
    {
        var node = ToJsonNode(value);
        return node is null ? "null" : node.ToJsonString();
    }

    private static JsonNode? ToJsonNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        bool b => JsonValue.Create(b),
        string s => JsonValue.Create(s),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        IDictionary<object, object?> map => new JsonObject(map.Select(kv => KeyValuePair.Create(kv.Key.ToString()!, ToJsonNode(kv.Value)))),
        IDictionary<string, object?> map => new JsonObject(map.Select(kv => KeyValuePair.Create(kv.Key, ToJsonNode(kv.Value)))),
        IEnumerable<object?> list => new JsonArray(list.Select(ToJsonNode).ToArray()),
        _ => JsonValue.Create(value.ToString()),
    };

    private static List<ConformanceCase> LoadCases()
    {
        // YamlDotNet, unlike the `yaml` npm package (JS runner) and PyYAML
        // (Python runner), deserializes every untyped scalar as a plain
        // string rather than inferring bool/int/float from its plain
        // (unquoted) form — plug in the same YAML 1.1 core-schema
        // inference those two get for free, so `expect.json: {valid:
        // true}` compares as a JSON bool instead of the string "true".
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithNodeTypeResolver(new ScalarCoreSchemaTypeResolver())
            .IgnoreUnmatchedProperties()
            .Build();
        var document = deserializer.Deserialize<ConformanceDocument>(File.ReadAllText(ConformanceYamlPath));
        return document.Cases;
    }

    private sealed class ScalarCoreSchemaTypeResolver : INodeTypeResolver
    {
        private static readonly Regex BoolPattern = new("^(true|false)$", RegexOptions.Compiled);
        private static readonly Regex IntPattern = new(@"^-?(0|[1-9][0-9]*)$", RegexOptions.Compiled);
        private static readonly Regex DoublePattern = new(@"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][-+]?[0-9]+)?$", RegexOptions.Compiled);

        public bool Resolve(NodeEvent? nodeEvent, ref Type currentType)
        {
            if (currentType != typeof(object) || nodeEvent is not Scalar { IsPlainImplicit: true } scalar)
            {
                return false;
            }

            if (BoolPattern.IsMatch(scalar.Value))
            {
                currentType = typeof(bool);
            }
            else if (IntPattern.IsMatch(scalar.Value))
            {
                currentType = typeof(int);
            }
            else if (DoublePattern.IsMatch(scalar.Value) && scalar.Value.Contains('.'))
            {
                currentType = typeof(double);
            }
            else
            {
                return false;
            }

            return true;
        }
    }

    private static Dictionary<string, Dictionary<string, string>> LoadFixtures(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json) ?? new();
    }
}

public sealed class ConformanceDocument
{
    public List<ConformanceCase> Cases { get; set; } = new();
}

public sealed class ConformanceCase
{
    public string Id { get; set; } = "";

    public string Description { get; set; } = "";

    public ConformanceRequest Request { get; set; } = new();

    public ConformanceExpect Expect { get; set; } = new();

    public override string ToString() => Id;
}

public sealed class ConformanceRequest
{
    public string Method { get; set; } = "GET";

    public string Path { get; set; } = "";

    public string Auth { get; set; } = "none";

    public string? Origin { get; set; }

    public object? Body { get; set; }

    public int? Repeat { get; set; }
}

public sealed class ConformanceExpect
{
    public int Status { get; set; }

    public Dictionary<string, object?>? Json { get; set; }
}
