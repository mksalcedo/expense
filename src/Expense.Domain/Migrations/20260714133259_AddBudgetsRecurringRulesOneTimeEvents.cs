using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddBudgetsRecurringRulesOneTimeEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_periods",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    effective_through = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_periods_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "one_time_events",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    account_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_one_time_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_one_time_events_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "recurring_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    frequency = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    anchor = table.Column<DateOnly>(type: "date", nullable: false),
                    account_id = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recurring_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_recurring_rules_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budget_periods_category_id_effective_from",
                table: "budget_periods",
                columns: new[] { "category_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ix_one_time_events_account_id",
                table: "one_time_events",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_one_time_events_date",
                table: "one_time_events",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_rules_account_id",
                table: "recurring_rules",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_recurring_rules_active",
                table: "recurring_rules",
                column: "active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budget_periods");

            migrationBuilder.DropTable(
                name: "one_time_events");

            migrationBuilder.DropTable(
                name: "recurring_rules");
        }
    }
}
