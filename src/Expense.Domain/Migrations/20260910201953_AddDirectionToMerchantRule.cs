using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddDirectionToMerchantRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "merchant_rules",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "direction",
                table: "merchant_rules");
        }
    }
}
