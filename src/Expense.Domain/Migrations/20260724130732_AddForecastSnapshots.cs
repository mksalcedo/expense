using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddForecastSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "forecast_snapshots",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                    starting_balance = table.Column<decimal>(type: "numeric", nullable: false),
                    lowest_projected_balance = table.Column<decimal>(type: "numeric", nullable: false),
                    lowest_projected_balance_date = table.Column<DateOnly>(type: "date", nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "forecast_snapshot_lines",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    forecast_snapshot_id = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric", nullable: false),
                    running_balance = table.Column<decimal>(type: "numeric", nullable: false),
                    account_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_snapshot_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_forecast_snapshot_lines_forecast_snapshots_forecast_snapsho",
                        column: x => x.forecast_snapshot_id,
                        principalTable: "forecast_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshot_lines_forecast_snapshot_id",
                table: "forecast_snapshot_lines",
                column: "forecast_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_forecast_snapshots_as_of_date",
                table: "forecast_snapshots",
                column: "as_of_date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "forecast_snapshot_lines");

            migrationBuilder.DropTable(
                name: "forecast_snapshots");
        }
    }
}
