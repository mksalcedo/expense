using Expense.Domain.Data;
using Microsoft.EntityFrameworkCore;

namespace Expense.Domain.Services.Categorization;

/// <summary>Thin DI-composition wiring (like ForecastResultProvider) - all real logic lives in CategorizationService.</summary>
public class ReviewQueueProvider(IDbContextFactory<ExpenseDbContext> contextFactory, CategorizationService categorization) : IReviewQueueProvider
{
    public async Task<ReviewQueueData> GetReviewQueueAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return new ReviewQueueData
        {
            TransactionGroups = await categorization.GetPendingTransactionGroupsAsync(context),
            AmazonItemGroups = await categorization.GetPendingAmazonItemGroupsAsync(context),
            Categories = await context.Categories.OrderBy(c => c.Name).ToListAsync(cancellationToken)
        };
    }

    public async Task<int> CategorizeTransactionAsync(
        int transactionId, int categoryId, string? merchantPatternToCreate, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await categorization.CategorizeTransactionAsync(context, transactionId, categoryId, merchantPatternToCreate);
    }

    public async Task<int> CategorizeAmazonItemAsync(
        int itemId, int categoryId, string? productPatternToCreate, CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await categorization.CategorizeAmazonItemAsync(context, itemId, categoryId, productPatternToCreate);
    }

    public async Task<ReapplyRulesResult> ReapplyRulesAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await categorization.ReapplyRulesToPendingAsync(context);
    }
}
