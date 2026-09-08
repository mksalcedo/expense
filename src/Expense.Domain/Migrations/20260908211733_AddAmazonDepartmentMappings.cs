using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAmazonDepartmentMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "department_hint",
                table: "amazon_order_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "amazon_department_mappings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    department_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_amazon_department_mappings", x => x.id);
                    table.ForeignKey(
                        name: "fk_amazon_department_mappings_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_amazon_department_mappings_category_id",
                table: "amazon_department_mappings",
                column: "category_id");

            // Case-insensitive uniqueness on the department name (EF's fluent API can't
            // express a functional index).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ix_amazon_department_mappings_department_name_lower " +
                "ON amazon_department_mappings (lower(department_name));");

            // Seed the departments the 2026-07..09 backtest proved map unambiguously for this
            // user (Amazon's "Nutrition & Wellness / Supplements / Vitamins / Health Care" all
            // mean the Supplements category here; "Grocery" means Groceries). Guarded so a
            // fresh or renamed DB where those categories don't exist just skips the seed
            // rather than failing the migration. Deliberately NOT seeding ambiguous Amazon
            // departments (Kitchen, Home, Exercise & Fitness) - those defer to review.
            migrationBuilder.Sql("""
                INSERT INTO amazon_department_mappings (department_name, category_id)
                SELECT d.name, c.id
                FROM (VALUES
                    ('Nutrition & Wellness', 'Supplements'),
                    ('Supplements',          'Supplements'),
                    ('Vitamins',             'Supplements'),
                    ('Health Care',          'Supplements'),
                    ('Grocery',              'Groceries')
                ) AS d(name, category_name)
                JOIN categories c ON c.name = d.category_name
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "amazon_department_mappings");

            migrationBuilder.DropColumn(
                name: "department_hint",
                table: "amazon_order_items");
        }
    }
}
