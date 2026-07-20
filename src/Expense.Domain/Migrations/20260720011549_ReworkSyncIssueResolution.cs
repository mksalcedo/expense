using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Expense.Domain.Migrations
{
    /// <inheritdoc />
    public partial class ReworkSyncIssueResolution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "dismissed",
                table: "sync_issues");

            migrationBuilder.AddColumn<string>(
                name: "body",
                table: "sync_issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "received_date",
                table: "sync_issues",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<int>(
                name: "resolution",
                table: "sync_issues",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "resolved_amazon_order_item_id",
                table: "sync_issues",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_sync_issues_resolved_amazon_order_item_id",
                table: "sync_issues",
                column: "resolved_amazon_order_item_id");

            migrationBuilder.AddForeignKey(
                name: "fk_sync_issues_amazon_order_items_resolved_amazon_order_item_id",
                table: "sync_issues",
                column: "resolved_amazon_order_item_id",
                principalTable: "amazon_order_items",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_sync_issues_amazon_order_items_resolved_amazon_order_item_id",
                table: "sync_issues");

            migrationBuilder.DropIndex(
                name: "ix_sync_issues_resolved_amazon_order_item_id",
                table: "sync_issues");

            migrationBuilder.DropColumn(
                name: "body",
                table: "sync_issues");

            migrationBuilder.DropColumn(
                name: "received_date",
                table: "sync_issues");

            migrationBuilder.DropColumn(
                name: "resolution",
                table: "sync_issues");

            migrationBuilder.DropColumn(
                name: "resolved_amazon_order_item_id",
                table: "sync_issues");

            migrationBuilder.AddColumn<bool>(
                name: "dismissed",
                table: "sync_issues",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
