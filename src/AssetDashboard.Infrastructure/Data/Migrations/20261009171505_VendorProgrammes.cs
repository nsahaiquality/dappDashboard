using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetDashboard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class VendorProgrammes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AnnualVolumeTarget",
                table: "Vendors",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OnboardedOn",
                table: "Vendors",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "ProgramType",
                table: "Vendors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Rating",
                table: "Vendors",
                type: "character varying(1)",
                maxLength: 1,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Recourse",
                table: "Vendors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnnualVolumeTarget",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "OnboardedOn",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "ProgramType",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Rating",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Recourse",
                table: "Vendors");
        }
    }
}
