using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddReconciledOccurrenceDateToBankTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "reconciled_occurrence_date",
                table: "bank_transactions",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reconciled_occurrence_date",
                table: "bank_transactions");
        }
    }
}
