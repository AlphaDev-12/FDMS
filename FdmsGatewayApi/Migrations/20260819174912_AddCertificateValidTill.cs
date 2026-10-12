using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FdmsGatewayApi.Migrations
{
    /// <inheritdoc />
    public partial class AddCertificateValidTill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousDeviceHash",
                table: "FiscalDocuments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CertificateValidTill",
                table: "DeviceConfigs",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousDeviceHash",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "CertificateValidTill",
                table: "DeviceConfigs");
        }
    }
}
