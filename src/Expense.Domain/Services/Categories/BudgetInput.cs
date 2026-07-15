using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categories;

/// <summary>
/// The fields needed to set a category's current BudgetPeriod, bundled as one unit.
/// Anchor/AccountId only apply to Direct-strategy categories (a dated ledger line needs
/// somewhere to land); PayInFullAmex categories are just an ongoing target - Amount and
/// Frequency only, Direction fixed at Expense, no Anchor/AccountId.
/// </summary>
public record BudgetInput(decimal Amount, Frequency Frequency, Direction Direction = Direction.Expense, DateOnly? Anchor = null, int? AccountId = null);
