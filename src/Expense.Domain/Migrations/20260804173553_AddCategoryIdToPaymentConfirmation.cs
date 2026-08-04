using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryIdToPaymentConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_confirmations_account_id_original_date",
                table: "payment_confirmations");

            migrationBuilder.AddColumn<int>(
                name: "category_id",
                table: "payment_confirmations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_confirmations_account_id_category_id_original_date",
                table: "payment_confirmations",
                columns: new[] { "account_id", "category_id", "original_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_confirmations_category_id",
                table: "payment_confirmations",
                column: "category_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_confirmations_categories_category_id",
                table: "payment_confirmations",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_confirmations_categories_category_id",
                table: "payment_confirmations");

            migrationBuilder.DropIndex(
                name: "ix_payment_confirmations_account_id_category_id_original_date",
                table: "payment_confirmations");

            migrationBuilder.DropIndex(
                name: "ix_payment_confirmations_category_id",
                table: "payment_confirmations");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "payment_confirmations");

            migrationBuilder.CreateIndex(
                name: "ix_payment_confirmations_account_id_original_date",
                table: "payment_confirmations",
                columns: new[] { "account_id", "original_date" },
                unique: true);
        }
    }
}
