namespace AiUsageMonitor.App.ViewModels;

/// <summary>One label/value pair. Value is never null: an absent fact renders as EmptyValue.</summary>
public sealed record DiagnosticField(string Label, string Value);
