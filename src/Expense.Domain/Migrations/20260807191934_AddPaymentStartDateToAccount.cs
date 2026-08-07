using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStartDateToAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "payment_start_date",
                table: "accounts",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_start_date",
                table: "accounts");
        }
    }
}
