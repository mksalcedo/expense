using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectionAnchorAccountToBudgetPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "account_id",
                table: "budget_periods",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "anchor",
                table: "budget_periods",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "budget_periods",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Expense");

            migrationBuilder.CreateIndex(
                name: "ix_budget_periods_account_id",
                table: "budget_periods",
                column: "account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_budget_periods_accounts_account_id",
                table: "budget_periods",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_budget_periods_accounts_account_id",
                table: "budget_periods");

            migrationBuilder.DropIndex(
                name: "ix_budget_periods_account_id",
                table: "budget_periods");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "budget_periods");

            migrationBuilder.DropColumn(
                name: "anchor",
                table: "budget_periods");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "budget_periods");
        }
    }
}
