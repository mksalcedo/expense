namespace Expense.Domain.Services.Dashboard;

/// <summary>Result of a manually-triggered Plaid backup import - not tracked as an ImportRun (see docs/plaid-import-utility-plan.md), just an immediate pass/fail message for the page that triggered it.</summary>
public record PlaidImportResult(bool Success, string Message);
