using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueNeedsReviewPlaceholderPerOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_amazon_order_items_order_id_needs_review_placeholder",
                table: "amazon_order_items",
                column: "order_id",
                unique: true,
                filter: "needs_review = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_amazon_order_items_order_id_needs_review_placeholder",
                table: "amazon_order_items");
        }
    }
}
