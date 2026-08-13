namespace AiUsageMonitor.App.ViewModels;

/// <summary>A titled block of fields, plus optional free-text lines rendered below them.</summary>
public sealed class DiagnosticSection(
    string title,
    string? subtitle,
    IReadOnlyList<DiagnosticField> fields,
    IReadOnlyList<string> lines)
{
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    public IReadOnlyList<DiagnosticField> Fields { get; } = fields;
    public IReadOnlyList<string> Lines { get; } = lines;
}
