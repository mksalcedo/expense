using Expense.Domain.Entities;

namespace Expense.Domain.Services.Categories;

/// <summary>
/// The 5 fields a Direct-strategy category's budget period needs, bundled as one
/// all-or-nothing unit rather than 5 separate nullable parameters - a Direct category
/// either has a complete budget period or none at all, never a partial one.
/// </summary>
public record DirectBudgetInput(decimal Amount, Frequency Frequency, Direction Direction, DateOnly Anchor, int AccountId);
