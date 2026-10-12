using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FdmsGatewayApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: true),
                    InvoiceNo = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Direction = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Endpoint = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawRequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HttpStatusCode = table.Column<int>(type: "int", nullable: false),
                    ErrorCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceConfigs",
                columns: table => new
                {
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    TaxPayerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TaxPayerTIN = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    VatNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeviceSerialNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceBranchName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActivationKey = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceModelName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceModelVersionNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceOperatingMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EncryptedPrivateKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CertificatePem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ServerCertificatePem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QrUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CurrentFiscalDayNo = table.Column<int>(type: "int", nullable: false),
                    CurrentReceiptGlobalNo = table.Column<int>(type: "int", nullable: false),
                    CurrentReceiptCounter = table.Column<int>(type: "int", nullable: false),
                    LastReceiptHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FiscalDayStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FiscalDayOpenedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceConfigs", x => x.DeviceId);
                });

            migrationBuilder.CreateTable(
                name: "FiscalDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReceiptType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    FiscalDayNo = table.Column<int>(type: "int", nullable: false),
                    ReceiptCounter = table.Column<int>(type: "int", nullable: false),
                    ReceiptGlobalNo = table.Column<int>(type: "int", nullable: false),
                    DeviceHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DeviceSignature = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiptID = table.Column<long>(type: "bigint", nullable: true),
                    ServerDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FdmsSignature = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QrCodeUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OperationID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FiscalStatus = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RawPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OfflineQueue",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceId = table.Column<int>(type: "int", nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReceiptGlobalNo = table.Column<int>(type: "int", nullable: false),
                    ReceiptType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmitPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WebhookUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Secret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_InvoiceNo",
                table: "AuditLogs",
                column: "InvoiceNo");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_FiscalStatus",
                table: "FiscalDocuments",
                column: "FiscalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_InvoiceNo",
                table: "FiscalDocuments",
                column: "InvoiceNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflineQueue_DeviceId_Status_ReceiptGlobalNo",
                table: "OfflineQueue",
                columns: new[] { "DeviceId", "Status", "ReceiptGlobalNo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DeviceConfigs");

            migrationBuilder.DropTable(
                name: "FiscalDocuments");

            migrationBuilder.DropTable(
                name: "OfflineQueue");

            migrationBuilder.DropTable(
                name: "WebhookRegistrations");
        }
    }
}
