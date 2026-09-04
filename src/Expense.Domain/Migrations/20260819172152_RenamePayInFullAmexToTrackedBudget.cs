using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <summary>
    /// Data-only migration - no column/table changes, just: (1) the strategy string itself
    /// (funding_rules.strategy 'pay_in_full_amex' -> 'tracked_budget', matching the
    /// FundingStrategies.TrackedBudget rename), and (2) backfilling budget_periods.account_id
    /// for every existing TrackedBudget category - that column has always existed but was
    /// never populated for this strategy (only Direct ever set it), and every existing
    /// TrackedBudget category was, until now, implicitly funded by the one pay-in-full
    /// ("ActiveSpending") account. Without this backfill, the account-scoped budget
    /// calculations added alongside this migration would treat every one of these categories
    /// as belonging to no account at all, dropping them out of Amex's own forecast entirely.
    /// </summary>
    public partial class RenamePayInFullAmexToTrackedBudget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE funding_rules SET strategy = 'tracked_budget' WHERE strategy = 'pay_in_full_amex';");

            migrationBuilder.Sql("""
                UPDATE budget_periods bp
                SET account_id = (SELECT id FROM accounts WHERE type = 'ActiveSpending' AND is_active = true ORDER BY id LIMIT 1)
                FROM funding_rules fr
                WHERE fr.category_id = bp.category_id
                  AND fr.strategy = 'tracked_budget'
                  AND bp.account_id IS NULL;
                """);
        }

        /// <summary>
        /// Best-effort only - safe to run immediately after Up(), before any category has
        /// been manually switched to TrackedBudget on its own (e.g. Restaurants, moved off
        /// Direct once this ships). Once that's happened, this can no longer tell "backfilled
        /// by this migration" apart from "set independently afterward" and would wrongly wipe
        /// the latter too. The real safety net for this deployment is the fresh pg_dump taken
        /// immediately before it, not this method.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE budget_periods bp
                SET account_id = NULL
                FROM funding_rules fr
                WHERE fr.category_id = bp.category_id
                  AND fr.strategy = 'tracked_budget';
                """);

            migrationBuilder.Sql("UPDATE funding_rules SET strategy = 'pay_in_full_amex' WHERE strategy = 'tracked_budget';");
        }
    }
}
