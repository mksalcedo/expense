using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAsOfTimestampToCheckingBalanceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nullable first, so existing rows can be backfilled from their real AsOfDate
            // (midnight UTC on that date) instead of silently defaulting to year 0001 -
            // that would still sort correctly relative to each other, but throws away real
            // historical ordering for no reason. New rows always populate this precisely.
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "as_of_timestamp",
                table: "checking_balance_snapshots",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE checking_balance_snapshots SET as_of_timestamp = as_of_date::timestamp AT TIME ZONE 'UTC' WHERE as_of_timestamp IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "as_of_timestamp",
                table: "checking_balance_snapshots",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_checking_balance_snapshots_as_of_timestamp",
                table: "checking_balance_snapshots",
                column: "as_of_timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_checking_balance_snapshots_as_of_timestamp",
                table: "checking_balance_snapshots");

            migrationBuilder.DropColumn(
                name: "as_of_timestamp",
                table: "checking_balance_snapshots");
        }
    }
}
