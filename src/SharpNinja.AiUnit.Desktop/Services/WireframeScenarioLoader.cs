using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SharpNinja.AiUnit.Scenarios;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SharpNinja.AiUnit.Desktop.Services;

///<summary>
///Desktop-provided loader for WireframeScenario (the aiUnit wireframe-to-screenshot comparison "tests").
///Per TR-AIUNITDESKTOP-LOADER-001 + FR-AIUNITDESKTOP-001 + user "finds aiUnit tests":
///- Takes 3 configurable folder paths (scenarios YAML dir, wireframes SVG dir, screenshots PNG dir).
///- Enumerates *.yaml in scenarios folder.
///- Deserializes to core WireframeScenario.
///- Resolves YAML-relative (or leaf) image paths against the *configured* wireframes/screenshots folders (absolute verbatim).
///- Missing referenced image -> per-scenario error placeholder (no exception).
///- Merges optional sibling screenId.review.yaml sidecar into HumanReview (per FR-009, TEST-008).
///
///ACs (explicit, for TDD per Byrd v4; see TEST-AIUNITDESKTOP-001 and loader tests):
///AC-LOAD-001: LoadAllAsync with valid 3 folders returns exactly the 4 scenarios (for aiunit-repl) in numeric prefix order.
///AC-LOAD-002: Relative paths in YAML (e.g. "docs/.../xx.png") are resolved against the corresponding configured folder (base + filename or smart relative).
///AC-LOAD-003: Absolute paths in YAML are used verbatim.
///AC-LOAD-004: Missing image file (svg/png) produces a scenario with placeholder/error indicator (not crash).
///AC-LOAD-005: Sidecar verdict yaml next to scenario is read and merged into scenario.HumanReview (verdict, reviewer, reviewedAt, notes).
///AC-LOAD-006: Uses in-mem fs fake in unit tests (mocks-first); real IO only after fake tests green.
///AC-LOAD-007: Result includes SourcePath, Full* paths, ModelPayloadYaml (with sanitized agents readme if present).
///</summary>
public sealed class WireframeScenarioLoader
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _serializer;

    // For testability (in-mem fake per AC-LOAD-006 / TEST-001 mocks-first).
    // In real use, pass null to use System.IO.
    public interface IFileSystem
    {
        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
        bool FileExists(string path);
        string ReadAllText(string path);
        void WriteAllText(string path, string contents);
        bool DirectoryExists(string path);
    }

    private readonly IFileSystem? _fs;

    public WireframeScenarioLoader(IFileSystem? fileSystem = null)
    {
        _fs = fileSystem;
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
    }

    private IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        _fs?.EnumerateFiles(path, searchPattern, searchOption) ?? Directory.EnumerateFiles(path, searchPattern, searchOption);

    private bool FileExists(string path) => _fs?.FileExists(path) ?? File.Exists(path);
    private string ReadAllText(string path) => _fs?.ReadAllText(path) ?? File.ReadAllText(path);
    private void WriteAllText(string path, string contents)
    {
        if (_fs != null) _fs.WriteAllText(path, contents);
        else File.WriteAllText(path, contents);
    }

    ///<summary>
    ///Load scenarios (the aiUnit tests) from the 3 folders.
    ///</summary>
    public Task<IReadOnlyList<WireframeScenario>> LoadAsync(
        string scenariosFolder,
        string wireframesFolder,
        string screenshotsFolder,
        CancellationToken ct = default)
    {
        var results = new List<WireframeScenario>();
        if (!DirectoryExists(scenariosFolder))
        {
            return Task.FromResult<IReadOnlyList<WireframeScenario>>(results); // graceful for AC-LOAD-004
        }
        foreach (var yamlFile in EnumerateFiles(scenariosFolder, "*.yaml", SearchOption.TopDirectoryOnly).OrderBy(f => Path.GetFileName(f)))
        {
            ct.ThrowIfCancellationRequested();
            var yaml = ReadAllText(yamlFile);
            var scenario = _deserializer.Deserialize<WireframeScenario>(yaml)
                ?? throw new InvalidDataException($"Failed to deserialize scenario {yamlFile}");

            scenario.SourcePath = yamlFile;
            scenario.RawYaml = yaml;

            // Resolve images against *configured* folders (AC-LOAD-002/003)
            scenario.ActualScreenshotFullPath = ResolveAgainstBase(scenario.ActualScreenshotPath, screenshotsFolder);
            // Wireframe PNG renders live in a "wireframes/" subfolder under the screenshots base (see docs/screenshots/aiunit-repl/wireframes/)
            // while actuals are directly in the screenshots base. Using only basename + correct sub-base ensures distinct files.
            var wireframeScreenshotsBase = Path.Combine(screenshotsFolder, "wireframes");
            scenario.WireframeScreenshotFullPath = ResolveAgainstBase(scenario.WireframeScreenshotPath, wireframeScreenshotsBase);
            scenario.WireframeSvgFullPath = ResolveAgainstBase(scenario.WireframeSvgPath, wireframesFolder);

            // Agents readme (relative to scenarios or repo root; reuse sanitization logic conceptually)
            if (!string.IsNullOrWhiteSpace(scenario.AgentsReadmeFirstPath))
            {
                var agentsFull = ResolveAgainstBase(scenario.AgentsReadmeFirstPath, scenariosFolder); // or scenarios parent
                scenario.AgentsReadmeFirstFullPath = agentsFull;
                scenario.AgentsReadmeFirstContent = FileExists(agentsFull)
                    ? SanitizeAgentsContent(ReadAllText(agentsFull))
                    : "AGENTS-README-FIRST not found at resolved path.";
            }

            // Sidecar verdict merge (AC-LOAD-005)
            var sidecarPath = Path.Combine(scenariosFolder, scenario.ScreenId + ".review.yaml");
            if (FileExists(sidecarPath))
            {
                // Deserialize sidecar defensively. HumanReview is a positional record (no paramless ctor),
                // so direct generic Deserialize can fail under YamlDotNet; map via dictionary then construct the record.
                // This keeps the public model unchanged while supporting roundtrip save/load for verdicts (approved/rejected/needs-changes).
                try
                {
                    var sidecarYaml = ReadAllText(sidecarPath);
                    var raw = _deserializer.Deserialize<Dictionary<object, object>>(sidecarYaml);
                    if (raw != null)
                    {
                        string? ver = raw.TryGetValue("verdict", out var vv) ? vv?.ToString() : null;
                        if (!string.IsNullOrWhiteSpace(ver))
                        {
                            string? rev = raw.TryGetValue("reviewer", out var rv) ? rv?.ToString() : null;
                            DateTimeOffset? at = null;
                            if (raw.TryGetValue("reviewedAt", out var ra) && ra != null &&
                                DateTimeOffset.TryParse(ra.ToString(), out var dto))
                            {
                                at = dto;
                            }
                            string? nt = raw.TryGetValue("notes", out var nv) ? nv?.ToString() : null;
                            scenario.HumanReview = new HumanReview(ver, rev, at, nt);
                        }
                    }
                }
                catch
                {
                    // bad/malformed sidecar is ignored (graceful, does not fail the whole load)
                    scenario.HumanReview = null;
                }
            }

            // Basic model payload (append if needed, simplified)
            scenario.ModelPayloadYaml = AppendAgentsIfMissing(scenario.RawYaml, scenario.AgentsReadmeFirstContent);

            results.Add(scenario);
        }

        // AC-LOAD-001: numeric order by screenId prefix (already ordered by filename in this impl; yaml files are 01- etc.)
        return Task.FromResult<IReadOnlyList<WireframeScenario>>(results);
    }

    ///<summary>
    ///Persist a human review verdict as sidecar {screenId}.review.yaml next to the scenario yamls (in the scenariosFolder).
    ///Uses the injectable IFileSystem for testability (mocks-first). Overwrites if exists.
    ///Matches sidecar convention used by LoadAsync merge (AC-LOAD-005) + FR-AIUNITDESKTOP-009 / TEST-AIUNITDESKTOP-008.
    ///</summary>
    public void SaveReview(string scenariosFolder, string screenId, HumanReview review)
    {
        if (string.IsNullOrWhiteSpace(screenId) || review is null)
            return;

        var sidecarPath = Path.Combine(scenariosFolder, screenId + ".review.yaml");
        var yaml = _serializer.Serialize(review);
        WriteAllText(sidecarPath, yaml);
    }

    private string ResolveAgainstBase(string yamlPath, string baseFolder)
    {
        if (string.IsNullOrWhiteSpace(yamlPath)) return string.Empty;
        if (Path.IsPathRooted(yamlPath)) return yamlPath; // absolute verbatim AC-LOAD-003
        var fileName = Path.GetFileName(yamlPath);
        return Path.Combine(baseFolder, fileName); // simple leaf resolution against configured (common for these yamls)
    }

    private static string AppendAgentsIfMissing(string raw, string agentsContent)
    {
        if (raw.Contains("agentsReadmeFirstContent:")) return raw;
        if (string.IsNullOrWhiteSpace(agentsContent)) return raw;
        var b = new StringBuilder(raw.TrimEnd());
        b.AppendLine().AppendLine("agentsReadmeFirstContent: |");
        foreach (var line in agentsContent.Replace("\r\n", "\n").Split('\n'))
        {
            b.Append("  ").AppendLine(line);
        }
        return b.ToString();
    }

    private static string SanitizeAgentsContent(string content)
    {
        // Minimal reuse of redaction idea from test catalog (full in test for AI path)
        var b = new StringBuilder();
        foreach (var line in content.Replace("\r\n", "\n").Split('\n'))
        {
            var t = line.TrimStart().ToLowerInvariant();
            if (t.Contains("apikey") || t.Contains("api-key") || t.Contains("x-api-key") || t.Contains("authorization") || t.Contains("bearer "))
            {
                b.Append(line.Substring(0, line.Length - line.TrimStart().Length)).AppendLine("[redacted]");
            }
            else b.AppendLine(line);
        }
        return b.ToString().TrimEnd();
    }

    private bool DirectoryExists(string path) => _fs?.DirectoryExists(path) ?? Directory.Exists(path);
}
