using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddImportRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ran_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    success = table.Column<bool>(type: "boolean", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    error_message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_source_ran_at",
                table: "import_runs",
                columns: new[] { "source", "ran_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "import_runs");
        }
    }
}
