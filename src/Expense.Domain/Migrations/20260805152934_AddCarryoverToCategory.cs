using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCarryoverToCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "carryover_anchor_date",
                table: "categories",
                type: "date",
                nullable: false,
                defaultValueSql: "CURRENT_DATE");

            migrationBuilder.AddColumn<decimal>(
                name: "carryover_cap_multiplier",
                table: "categories",
                type: "numeric",
                nullable: true,
                defaultValue: 1.0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "pending_carryover_anchor_date",
                table: "categories",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carryover_anchor_date",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "carryover_cap_multiplier",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "pending_carryover_anchor_date",
                table: "categories");
        }
    }
}
