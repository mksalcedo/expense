using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedsReviewToAmazonOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "needs_review",
                table: "amazon_order_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: existing rows created by the "simplified, no item detail" parser
            // path are identifiable by their exact placeholder title - flag them now so the
            // feature is immediately useful for data imported before this migration.
            migrationBuilder.Sql(
                "UPDATE amazon_order_items SET needs_review = true " +
                "WHERE item_title = '(Item details unavailable in email - check Amazon order page)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "needs_review",
                table: "amazon_order_items");
        }
    }
}
