using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayPalCreateRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExternalCreateAttemptedAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalCreateOutcome",
                table: "Payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRequestFingerprint",
                table: "Payments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalRequestId",
                table: "Payments",
                type: "character varying(38)",
                maxLength: 38,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_PayPalCreate",
                table: "Payments",
                sql: "(\"ExternalCreateOutcome\" IS NULL AND \"ExternalRequestId\" IS NULL AND \"ExternalRequestFingerprint\" IS NULL AND \"ExternalCreateAttemptedAt\" IS NULL)\r\nOR (\"Provider\" = 'PayPal' AND \"ExternalCreateOutcome\" IN ('Attempted', 'Uncertain') AND \"ExternalOperationId\" IS NULL AND \"ExternalRequestId\" IS NOT NULL AND \"ExternalRequestFingerprint\" IS NOT NULL AND \"ExternalCreateAttemptedAt\" IS NOT NULL)\r\nOR (\"Provider\" = 'PayPal' AND \"ExternalCreateOutcome\" = 'Identified' AND \"ExternalOperationId\" IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_PayPalCreate",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalCreateAttemptedAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalCreateOutcome",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalRequestFingerprint",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalRequestId",
                table: "Payments");
        }
    }
}
