using Expense.Domain.Data;
using Expense.Domain.Entities;
using Expense.Domain.Services.Categorization;

namespace Expense.Domain.Services.Ingestion.ManualCharges;

/// <summary>
/// Turns a screenshot into a reviewable list of charges, then commits the ones the user
/// accepts as real BankTransactions - see docs/amex-pending-charges-plan.md.
/// </summary>
public class ManualChargeEntryService(
    AmexScreenshotParsingService parsingService, ManualChargeMatchingService matching, CategorizationService categorization)
{
    public async Task<List<ManualChargeReviewRow>> ReviewScreenshotAsync(
        ExpenseDbContext context, int accountId, byte[] imageBytes, string mediaType, DateOnly asOfDate, CancellationToken cancellationToken = default)
    {
        var extracted = await parsingService.ParseScreenshotAsync(imageBytes, mediaType, asOfDate, cancellationToken);

        var rows = new List<ManualChargeReviewRow>();
        foreach (var extractedRow in extracted)
        {
            // Charges reduce what's owed as a positive amount; credits/payments as negative
            // - the opposite of how the issuer's site shows them (charges plain, credits/
            // payments with a minus sign) - matching this app's own signed-amount convention.
            var signedAmount = extractedRow.IsCredit ? extractedRow.Amount : -extractedRow.Amount;
            var match = await matching.FindExistingMatchAsync(context, accountId, extractedRow.Date, signedAmount, cancellationToken);

            rows.Add(new ManualChargeReviewRow
            {
                Date = extractedRow.Date,
                Description = extractedRow.Description,
                Amount = signedAmount,
                IsDuplicate = match is not null,
                DuplicateReason = match is null ? null : DescribeMatch(match)
            });
        }

        return rows;
    }

    public async Task<int> AddChargesAsync(
        ExpenseDbContext context, int accountId, IReadOnlyList<ManualChargeReviewRow> rows, CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            var transaction = new BankTransaction
            {
                AccountId = accountId,
                TransactionDate = row.Date,
                PostedDate = null,
                Description = row.Description,
                Amount = row.Amount,
                ImportSource = ManualChargeMatchingService.ManualScreenshotImportSource,
                CreatedAt = DateTimeOffset.UtcNow
            };

            await categorization.ApplyMerchantRuleAsync(context, transaction);
            context.BankTransactions.Add(transaction);
        }

        await context.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    private static string DescribeMatch(BankTransaction match)
    {
        var when = match.PostedDate is { } posted ? $"posted {posted:MM/dd/yyyy}" : $"entered {match.TransactionDate:MM/dd/yyyy}";
        return $"Already in system - matches {match.Description}, ${Math.Abs(match.Amount):N2}, {when}";
    }
}
