namespace Expense.Domain.Services.Categories;

/// <summary>
/// The payment fields for an AccountPayment-strategy category's linked Account, edited
/// inline from Categories.razor. Writes through to the Account, never to a BudgetPeriod.
/// </summary>
public record AccountPaymentInput(decimal? MinPayment, decimal? ExtraPayment, int? PaymentDueDay, int? StatementCloseDay);
