using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssetDashboard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Remarketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AskingPrice",
                table: "RemarketingCases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AuctionId",
                table: "RemarketingCases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Bids",
                table: "RemarketingCases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DailyStorageRate",
                table: "RemarketingCases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "PriceReductions",
                table: "RemarketingCases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RefurbishmentCost",
                table: "RemarketingCases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SellingFees",
                table: "RemarketingCases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StorageCost",
                table: "RemarketingCases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TimesPassedIn",
                table: "RemarketingCases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TransportCost",
                table: "RemarketingCases",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Yard",
                table: "RemarketingCases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Auctions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LotsPassedIn = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Auctions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RemarketingCases_AuctionId",
                table: "RemarketingCases",
                column: "AuctionId");

            migrationBuilder.CreateIndex(
                name: "IX_Auctions_Status_Date",
                table: "Auctions",
                columns: new[] { "Status", "Date" });

            migrationBuilder.AddForeignKey(
                name: "FK_RemarketingCases_Auctions_AuctionId",
                table: "RemarketingCases",
                column: "AuctionId",
                principalTable: "Auctions",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RemarketingCases_Auctions_AuctionId",
                table: "RemarketingCases");

            migrationBuilder.DropTable(
                name: "Auctions");

            migrationBuilder.DropIndex(
                name: "IX_RemarketingCases_AuctionId",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "AskingPrice",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "AuctionId",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "Bids",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "DailyStorageRate",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "PriceReductions",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "RefurbishmentCost",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "SellingFees",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "StorageCost",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "TimesPassedIn",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "TransportCost",
                table: "RemarketingCases");

            migrationBuilder.DropColumn(
                name: "Yard",
                table: "RemarketingCases");
        }
    }
}
