using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryIdToOneTimeEvent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "category_id",
                table: "one_time_events",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_one_time_events_category_id",
                table: "one_time_events",
                column: "category_id");

            migrationBuilder.AddForeignKey(
                name: "fk_one_time_events_categories_category_id",
                table: "one_time_events",
                column: "category_id",
                principalTable: "categories",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_one_time_events_categories_category_id",
                table: "one_time_events");

            migrationBuilder.DropIndex(
                name: "ix_one_time_events_category_id",
                table: "one_time_events");

            migrationBuilder.DropColumn(
                name: "category_id",
                table: "one_time_events");
        }
    }
}
