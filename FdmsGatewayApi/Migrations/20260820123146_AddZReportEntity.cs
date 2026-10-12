using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FdmsGatewayApi.Migrations
{
    /// <inheritdoc />
    public partial class AddZReportEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ZReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    FiscalDayNo = table.Column<int>(type: "int", nullable: false),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartingReceiptGlobalNo = table.Column<int>(type: "int", nullable: false),
                    EndingReceiptGlobalNo = table.Column<int>(type: "int", nullable: false),
                    TotalFiscalizedDocuments = table.Column<int>(type: "int", nullable: false),
                    TotalUsdSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalZigSales = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BlockHashDigest = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Signature = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfFilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PdfFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZReports", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ZReports_DeviceId_FiscalDayNo",
                table: "ZReports",
                columns: new[] { "DeviceId", "FiscalDayNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ZReports");
        }
    }
}
