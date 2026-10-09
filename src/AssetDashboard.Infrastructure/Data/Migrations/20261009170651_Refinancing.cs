using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AssetDashboard.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Refinancing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "MaturityDate",
                table: "Contracts",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Existing contracts: maturity = start date + term.
            migrationBuilder.Sql("UPDATE \"Contracts\" SET \"MaturityDate\" = (\"StartDate\" + make_interval(months => \"TermMonths\"))::date;");

            migrationBuilder.CreateTable(
                name: "RefinancingRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContractId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RequestedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RequestedTermMonths = table.Column<int>(type: "integer", nullable: false),
                    CurrentRate = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: false),
                    ProposedRate = table.Column<decimal>(type: "numeric(6,3)", precision: 6, scale: 3, nullable: true),
                    LtvAtRequest = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ForcedSaleCoverAtRequest = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    RiskGradeAtRequest = table.Column<int>(type: "integer", nullable: false),
                    ExpectedLossAtRequest = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefinancingRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefinancingRequests_Contracts_ContractId",
                        column: x => x.ContractId,
                        principalTable: "Contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Contracts_MaturityDate",
                table: "Contracts",
                column: "MaturityDate");

            migrationBuilder.CreateIndex(
                name: "IX_RefinancingRequests_ContractId",
                table: "RefinancingRequests",
                column: "ContractId");

            migrationBuilder.CreateIndex(
                name: "IX_RefinancingRequests_RequestedAt",
                table: "RefinancingRequests",
                column: "RequestedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RefinancingRequests_Status",
                table: "RefinancingRequests",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RefinancingRequests");

            migrationBuilder.DropIndex(
                name: "IX_Contracts_MaturityDate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "MaturityDate",
                table: "Contracts");
        }
    }
}
