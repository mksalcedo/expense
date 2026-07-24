using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddNeedsReviewContextToAmazonOrderItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "needs_review_reason",
                table: "amazon_order_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_details_url",
                table: "amazon_order_items",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "raw_email_body",
                table: "amazon_order_items",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_message_id",
                table: "amazon_order_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "needs_review_reason",
                table: "amazon_order_items");

            migrationBuilder.DropColumn(
                name: "order_details_url",
                table: "amazon_order_items");

            migrationBuilder.DropColumn(
                name: "raw_email_body",
                table: "amazon_order_items");

            migrationBuilder.DropColumn(
                name: "source_message_id",
                table: "amazon_order_items");
        }
    }
}
