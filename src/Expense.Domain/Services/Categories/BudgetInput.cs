using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categories;

/// <summary>
/// The fields needed to set a category's current BudgetPeriod, bundled as one unit. Direct
/// requires Anchor/AccountId (a single dated ledger line needs somewhere to land).
/// TrackedBudget's AccountId says which account this category is funded from (a pay-in-full
/// card pools it into that card's shared payment; any other account gets it its own
/// standalone line - see ForecastEngine) and is required for that; Anchor is only actually
/// used for the standalone-line case, but harmless to set either way. Direction is fixed at
/// Expense for TrackedBudget.
/// </summary>
public record BudgetInput(decimal Amount, Frequency Frequency, Direction Direction = Direction.Expense, DateOnly? Anchor = null, int? AccountId = null);
