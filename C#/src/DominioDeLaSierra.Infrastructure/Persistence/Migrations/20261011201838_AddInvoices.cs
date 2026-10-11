using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerTaxId",
                table: "Orders",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Series = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    FullNumber = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Fictional = table.Column<bool>(type: "boolean", nullable: false),
                    SellerLegalName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SellerTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SellerAddressLine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SellerPostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SellerCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SellerProvince = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SellerCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    BuyerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    BuyerTaxId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BuyerAddressLine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BuyerPostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    BuyerCity = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BuyerProvince = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    BuyerCountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    ProductSubtotalCents = table.Column<long>(type: "bigint", nullable: false),
                    ProductTaxableBaseCents = table.Column<long>(type: "bigint", nullable: false),
                    ProductVatCents = table.Column<long>(type: "bigint", nullable: false),
                    ShippingCents = table.Column<long>(type: "bigint", nullable: false),
                    ShippingVatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    ShippingTaxableBaseCents = table.Column<long>(type: "bigint", nullable: false),
                    ShippingVatCents = table.Column<long>(type: "bigint", nullable: false),
                    TotalCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoices_Amounts", "\"ProductSubtotalCents\" >= 0\nAND \"ProductTaxableBaseCents\" >= 0\nAND \"ProductVatCents\" >= 0\nAND \"ShippingCents\" >= 0\nAND \"ShippingTaxableBaseCents\" >= 0\nAND \"ShippingVatCents\" >= 0\nAND \"TotalCents\" >= 0\nAND \"ProductTaxableBaseCents\" + \"ProductVatCents\" = \"ProductSubtotalCents\"\nAND \"ShippingTaxableBaseCents\" + \"ShippingVatCents\" = \"ShippingCents\"\nAND \"TotalCents\" = \"ProductSubtotalCents\" + \"ShippingCents\"\nAND \"ShippingVatRate\" >= 0\nAND \"ShippingVatRate\" <= 999.99");
                    table.CheckConstraint("CK_Invoices_Currency", "\"Currency\" = 'EUR'");
                    table.CheckConstraint("CK_Invoices_Number", "\"Number\" >= 1\nAND \"Number\" <= 999999");
                    table.ForeignKey(
                        name: "FK_Invoices_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSeries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    NextNumber = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSeries", x => x.Code);
                    table.CheckConstraint("CK_InvoiceSeries_Number", "\"NextNumber\" >= 1\nAND \"NextNumber\" <= 1000000");
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceCents = table.Column<long>(type: "bigint", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    LineTotalCents = table.Column<long>(type: "bigint", nullable: false),
                    TaxableBaseCents = table.Column<long>(type: "bigint", nullable: false),
                    VatCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.CheckConstraint("CK_InvoiceLines_Amounts", "\"Position\" > 0\nAND \"Quantity\" > 0\nAND \"Quantity\" <= 1000000\nAND \"UnitPriceCents\" >= 0\nAND \"LineTotalCents\" >= 0\nAND \"TaxableBaseCents\" >= 0\nAND \"VatCents\" >= 0\nAND \"LineTotalCents\" = \"UnitPriceCents\" * \"Quantity\"\nAND \"TaxableBaseCents\" + \"VatCents\" = \"LineTotalCents\"\nAND \"VatRate\" >= 0\nAND \"VatRate\" <= 999.99");
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId_Position",
                table: "InvoiceLines",
                columns: new[] { "InvoiceId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_FullNumber",
                table: "Invoices",
                column: "FullNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_OrderId",
                table: "Invoices",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Series_Number",
                table: "Invoices",
                columns: new[] { "Series", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "InvoiceSeries");

            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropColumn(
                name: "CustomerTaxId",
                table: "Orders");
        }
    }
}
