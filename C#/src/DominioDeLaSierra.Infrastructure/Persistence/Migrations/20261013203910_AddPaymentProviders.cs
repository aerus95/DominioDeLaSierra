using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_CheckoutSession",
                table: "Payments");

            migrationBuilder.AddColumn<string>(
                name: "ExternalCaptureId",
                table: "Payments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalOperationId",
                table: "Payments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Provider",
                table: "Payments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Stripe");

            migrationBuilder.AddColumn<string>(
                name: "RedsysOrderNumber",
                table: "Payments",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ExternalOperationId",
                table: "Payments",
                columns: new[] { "Provider", "ExternalOperationId" },
                unique: true,
                filter: "\"ExternalOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_RedsysOrderNumber",
                table: "Payments",
                column: "RedsysOrderNumber",
                unique: true,
                filter: "\"RedsysOrderNumber\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_CheckoutSession",
                table: "Payments",
                sql: "(\"CheckoutUrl\" IS NULL AND \"CheckoutExpiresAt\" IS NULL)\r\nOR (\"Provider\" = 'Stripe' AND \"StripeCheckoutSessionId\" IS NOT NULL AND \"CheckoutUrl\" IS NOT NULL AND \"CheckoutExpiresAt\" IS NOT NULL AND \"RedsysOrderNumber\" IS NULL)\r\nOR (\"Provider\" = 'Redsys' AND \"ExternalOperationId\" IS NOT NULL AND \"RedsysOrderNumber\" IS NOT NULL AND \"CheckoutUrl\" IS NOT NULL AND \"CheckoutExpiresAt\" IS NOT NULL AND \"StripeCheckoutSessionId\" IS NULL AND \"StripePaymentIntentId\" IS NULL)\r\nOR (\"Provider\" = 'PayPal' AND \"ExternalOperationId\" IS NOT NULL AND \"RedsysOrderNumber\" IS NULL AND \"CheckoutUrl\" IS NOT NULL AND \"CheckoutExpiresAt\" IS NOT NULL AND \"StripeCheckoutSessionId\" IS NULL AND \"StripePaymentIntentId\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_Provider",
                table: "Payments",
                sql: "\"Provider\" IN ('Stripe', 'Redsys', 'PayPal')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_ProviderReferences",
                table: "Payments",
                sql: "(\"StripeCheckoutSessionId\" IS NULL OR \"Provider\" = 'Stripe')\r\nAND (\"StripePaymentIntentId\" IS NULL OR \"Provider\" = 'Stripe')\r\nAND (\"RedsysOrderNumber\" IS NULL OR (\"Provider\" = 'Redsys' AND \"RedsysOrderNumber\" ~ '^[0-9]{4,12}$'))");

            migrationBuilder.Sql(
                """
                CREATE SEQUENCE redsys_operation_numbers
                    AS bigint
                    START WITH 100000000001
                    INCREMENT BY 1
                    MINVALUE 100000000001
                    MAXVALUE 999999999999
                    NO CYCLE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS redsys_operation_numbers;");

            migrationBuilder.DropIndex(
                name: "IX_Payments_Provider_ExternalOperationId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_RedsysOrderNumber",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_CheckoutSession",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_Provider",
                table: "Payments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payments_ProviderReferences",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalCaptureId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "ExternalOperationId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Provider",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "RedsysOrderNumber",
                table: "Payments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payments_CheckoutSession",
                table: "Payments",
                sql: "(\"CheckoutUrl\" IS NULL AND \"CheckoutExpiresAt\" IS NULL)\r\nOR (\"StripeCheckoutSessionId\" IS NOT NULL AND \"CheckoutUrl\" IS NOT NULL AND \"CheckoutExpiresAt\" IS NOT NULL)");
        }
    }
}
