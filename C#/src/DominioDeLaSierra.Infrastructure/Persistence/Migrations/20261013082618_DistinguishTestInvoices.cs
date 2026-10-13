using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DistinguishTestInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Amounts",
                table: "Invoices");

            migrationBuilder.AlterColumn<decimal>(
                name: "ShippingVatRate",
                table: "Invoices",
                type: "numeric(5,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)");

            migrationBuilder.AlterColumn<long>(
                name: "ShippingVatCents",
                table: "Invoices",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "ShippingTaxableBaseCents",
                table: "Invoices",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "BuyerTaxId",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<string>(
                name: "FiscalForm",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE "Invoices"
                SET "FiscalForm" = CASE WHEN "Fictional" THEN 'NotApplicable' ELSE 'Complete' END
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Amounts",
                table: "Invoices",
                sql: "\"ProductSubtotalCents\" >= 0\r\nAND \"ProductTaxableBaseCents\" >= 0\r\nAND \"ProductVatCents\" >= 0\r\nAND \"ShippingCents\" >= 0\r\nAND \"TotalCents\" >= 0\r\nAND \"ProductTaxableBaseCents\" + \"ProductVatCents\" = \"ProductSubtotalCents\"\r\nAND \"TotalCents\" = \"ProductSubtotalCents\" + \"ShippingCents\"\r\nAND (\r\n    (\r\n        \"Fictional\" = TRUE\r\n        AND \"ShippingVatRate\" IS NULL\r\n        AND \"ShippingTaxableBaseCents\" IS NULL\r\n        AND \"ShippingVatCents\" IS NULL\r\n    )\r\n    OR (\r\n        \"ShippingVatRate\" IS NOT NULL\r\n        AND \"ShippingTaxableBaseCents\" IS NOT NULL\r\n        AND \"ShippingVatCents\" IS NOT NULL\r\n        AND \"ShippingTaxableBaseCents\" >= 0\r\n        AND \"ShippingVatCents\" >= 0\r\n        AND \"ShippingTaxableBaseCents\" + \"ShippingVatCents\" = \"ShippingCents\"\r\n        AND \"ShippingVatRate\" >= 0\r\n        AND \"ShippingVatRate\" <= 999.99\r\n    )\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_DocumentKind",
                table: "Invoices",
                sql: "(\r\n    \"Fictional\" = TRUE\r\n    AND \"FiscalForm\" = 'NotApplicable'\r\n    AND \"SellerTaxId\" = 'FICTICIO-TEST'\r\n)\r\nOR (\r\n    \"Fictional\" = FALSE\r\n    AND \"FiscalForm\" IN ('Complete', 'Simplified')\r\n    AND \"BuyerTaxId\" IS NOT NULL\r\n    AND \"ShippingVatRate\" IS NOT NULL\r\n)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Amounts",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_DocumentKind",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "FiscalForm",
                table: "Invoices");

            migrationBuilder.AlterColumn<decimal>(
                name: "ShippingVatRate",
                table: "Invoices",
                type: "numeric(5,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ShippingVatCents",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "ShippingTaxableBaseCents",
                table: "Invoices",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerTaxId",
                table: "Invoices",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Amounts",
                table: "Invoices",
                sql: "\"ProductSubtotalCents\" >= 0\nAND \"ProductTaxableBaseCents\" >= 0\nAND \"ProductVatCents\" >= 0\nAND \"ShippingCents\" >= 0\nAND \"ShippingTaxableBaseCents\" >= 0\nAND \"ShippingVatCents\" >= 0\nAND \"TotalCents\" >= 0\nAND \"ProductTaxableBaseCents\" + \"ProductVatCents\" = \"ProductSubtotalCents\"\nAND \"ShippingTaxableBaseCents\" + \"ShippingVatCents\" = \"ShippingCents\"\nAND \"TotalCents\" = \"ProductSubtotalCents\" + \"ShippingCents\"\nAND \"ShippingVatRate\" >= 0\nAND \"ShippingVatRate\" <= 999.99");
        }
    }
}
