using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class MakeForecastSnapshotAsOfDateNonUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_forecast_snapshots_as_of_date",
                table: "forecast_snapshots");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_as_of_date",
                table: "forecast_snapshots",
                column: "as_of_date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_forecast_snapshots_as_of_date",
                table: "forecast_snapshots");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_as_of_date",
                table: "forecast_snapshots",
                column: "as_of_date",
                unique: true);
        }
    }
}
