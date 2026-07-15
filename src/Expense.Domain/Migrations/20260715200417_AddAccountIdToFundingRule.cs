using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountIdToFundingRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "account_id",
                table: "funding_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_funding_rules_account_id",
                table: "funding_rules",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_funding_rules_accounts_account_id",
                table: "funding_rules",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_funding_rules_accounts_account_id",
                table: "funding_rules");

            migrationBuilder.DropIndex(
                name: "ix_funding_rules_account_id",
                table: "funding_rules");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "funding_rules");
        }
    }
}
