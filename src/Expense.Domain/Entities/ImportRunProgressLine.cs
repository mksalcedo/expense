namespace Expense.Domain.Entities;

/// <summary>One line of an ImportRun's detailed progress transcript (e.g. a per-message header/body/result block from an Amazon Gmail sync), persisted so it can be reviewed after an unattended run, not just live while the run is happening.</summary>
public class ImportRunProgressLine
{
    public int Id { get; set; }
    public int ImportRunId { get; set; }
    public ImportRun ImportRun { get; set; } = null!;
    public int Sequence { get; set; }
    public required string Text { get; set; }
    public bool IsError { get; set; }
}
