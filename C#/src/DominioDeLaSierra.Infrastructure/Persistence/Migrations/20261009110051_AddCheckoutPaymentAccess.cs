using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckoutPaymentAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CheckoutExpiresAt",
                table: "Payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "Payments",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckoutAccessTokenHash",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                """UPDATE "Orders" SET "CheckoutAccessTokenHash" = encode(sha256(("Id"::text)::bytea), 'hex') WHERE "CheckoutAccessTokenHash" IS NULL;""");

            migrationBuilder.AlterColumn<string>(
                name: "CheckoutAccessTokenHash",
                table: "Orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_CheckoutSession",
                table: "Payments",
                sql: "(\"CheckoutUrl\" IS NULL AND \"CheckoutExpiresAt\" IS NULL)\r\nOR (\"StripeCheckoutSessionId\" IS NOT NULL AND \"CheckoutUrl\" IS NOT NULL AND \"CheckoutExpiresAt\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CheckoutAccessTokenHash",
                table: "Orders",
                column: "CheckoutAccessTokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_CheckoutSession",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CheckoutAccessTokenHash",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CheckoutExpiresAt",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CheckoutAccessTokenHash",
                table: "Orders");
        }
    }
}
