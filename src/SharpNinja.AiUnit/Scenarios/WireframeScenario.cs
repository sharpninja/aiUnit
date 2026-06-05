using System;
using System.Collections.Generic;

namespace SharpNinja.AiUnit.Scenarios;

/// <summary>
/// Public data record for a wireframe-to-screenshot comparison scenario (the "aiUnit test" definition).
/// Moved to core per TR-AIUNITDESKTOP-LOADER-001 so tests and the Avalonia desktop tool can share the shape without duplication.
/// Loader (in desktop) populates Full* paths and merges sidecar HumanReview.
/// </summary>
public sealed record WireframeScenario
{
    public string SourcePath { get; set; } = string.Empty;

    public string RawYaml { get; set; } = string.Empty;

    public string ModelPayloadYaml { get; set; } = string.Empty;

    public string ScreenId { get; set; } = string.Empty;

    public string WireframeBaselineVersion { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Prompt { get; set; } = string.Empty;

    public List<WireframeRequirement> FunctionalRequirements { get; set; } = [];

    public List<WireframeRequirement> TechnicalRequirements { get; set; } = [];

    public string AgentsReadmeFirstPath { get; set; } = string.Empty;

    public string AgentsReadmeFirstFullPath { get; set; } = string.Empty;

    public string AgentsReadmeFirstContent { get; set; } = string.Empty;

    public string ActualScreenshotPath { get; set; } = string.Empty;

    public string ActualScreenshotFullPath { get; set; } = string.Empty;

    public string WireframeScreenshotPath { get; set; } = string.Empty;

    public string WireframeScreenshotFullPath { get; set; } = string.Empty;

    public string WireframeSvgPath { get; set; } = string.Empty;

    public string WireframeSvgFullPath { get; set; } = string.Empty;

    /// <summary>
    /// The result JSON schema (from YAML, expected to be literal block).
    /// </summary>
    public string ResultSchema { get; set; } = string.Empty;

    /// <summary>
    /// Optional human review verdict sidecar (merged by loader from screenId.review.yaml next to the scenario YAML).
    /// </summary>
    public HumanReview? HumanReview { get; set; }
}

/// <summary>
/// Requirement reference (FR- or TR-) used in scenario traceability (AC: Id starts with FR-/TR-, non-empty Text).
/// Class (not positional record) for easier YamlDotNet deserial of lists in loader.
/// </summary>
public sealed class WireframeRequirement
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Human review verdict persisted as sidecar (per FR-AIUNITDESKTOP-009, TEST-008 ACs).
/// Verdict values: "approved", "rejected", "needs-changes".
/// </summary>
public sealed record HumanReview(string Verdict, string? Reviewer = null, DateTimeOffset? ReviewedAt = null, string? Notes = null);
