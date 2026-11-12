namespace DistillForge.Bench;

/// <summary>One benchmark run: a (document, model, profile, repeat) cell.</summary>
public sealed class RunResult
{
    public string Document { get; set; } = "";
    public string Model { get; set; } = "";
    public string Profile { get; set; } = "";
    public int Repeat { get; set; }

    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public double Seconds { get; set; }

    public int Segments { get; set; }
    public int Windows { get; set; }
    public int FailedWindows { get; set; }
    public int WindowBudgetTokens { get; set; }
    public int FoundBeforeMerge { get; set; }
    public int AfterWindowMerge { get; set; }
    public bool ConsolidationRan { get; set; }
    public bool ConsolidationSkipped { get; set; }
    public bool ConsolidationFailed { get; set; }
    public int ConsolidationMerged { get; set; }

    public int Notes { get; set; }
    public int WithOwner { get; set; }
    public int WithDue { get; set; }
    public int EvidenceExact { get; set; }
    public int EvidenceNormalised { get; set; }
    public int EvidenceSegment { get; set; }
    public int Warnings { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }

    public List<NoteSummary> NoteList { get; set; } = [];
    public List<string> WarningList { get; set; } = [];

    public int EvidenceTotal => EvidenceExact + EvidenceNormalised + EvidenceSegment;
    public string Cell => $"{Model}/{Profile}";
}

public sealed class NoteSummary
{
    public string Title { get; set; } = "";
    public string? Owner { get; set; }
    public string? Due { get; set; }
    public string Quote { get; set; } = "";
    public string Quality { get; set; } = "";
}

public sealed class BenchReport
{
    public DateTimeOffset StartedAt { get; set; }
    public string SamplesDir { get; set; } = "";
    public List<RunResult> Runs { get; set; } = [];
}
