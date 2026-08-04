namespace Expense.Domain.Entities;

public enum AccountType
{
    Checking,
    ActiveSpending,
    Debt,
    // Purely informational - never read by the forecast engine (see [[project_expense_app_design]]
    // "savings buffer" note). Just a manually-updated balance shown alongside the lowest
    // projected balance, to offset how alarming a near-zero/negative checking projection reads.
    Savings
}
