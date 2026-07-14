using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantRulesProductsFundingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "funding_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    strategy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_funding_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_funding_rules_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "merchant_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    merchant_pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merchant_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_merchant_rules_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_pattern = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_funding_rules_category_id",
                table: "funding_rules",
                column: "category_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_merchant_rules_category_id",
                table: "merchant_rules",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                table: "products",
                column: "category_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "funding_rules");

            migrationBuilder.DropTable(
                name: "merchant_rules");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
