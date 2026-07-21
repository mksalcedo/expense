namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// One transaction row extracted from a user-provided screenshot (see
/// AmexScreenshotParsingService). Amount is always a positive magnitude, exactly as shown on
/// the card issuer's site - IsCredit says which direction it goes, since the caller (not this
/// extraction step) is responsible for converting to this app's own signed-amount convention.
/// </summary>
public record ExtractedChargeRow(DateOnly Date, string Description, decimal Amount, bool IsCredit);
