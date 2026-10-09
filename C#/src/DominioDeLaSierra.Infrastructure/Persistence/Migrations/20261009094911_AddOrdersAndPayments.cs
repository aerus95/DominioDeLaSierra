using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdersAndPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AddressLine = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PostalCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    City = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Province = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    DeliveryNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProductSubtotalCents = table.Column<long>(type: "bigint", nullable: false),
                    ProductTaxableBaseCents = table.Column<long>(type: "bigint", nullable: false),
                    ProductVatCents = table.Column<long>(type: "bigint", nullable: false),
                    ShippingCents = table.Column<long>(type: "bigint", nullable: false),
                    ShippingVatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    ShippingTaxableBaseCents = table.Column<long>(type: "bigint", nullable: true),
                    ShippingVatCents = table.Column<long>(type: "bigint", nullable: true),
                    TotalCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReservationExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.CheckConstraint("CK_Orders_Amounts", "\"ProductSubtotalCents\" >= 0\r\nAND \"ProductTaxableBaseCents\" >= 0\r\nAND \"ProductVatCents\" >= 0\r\nAND \"ShippingCents\" >= 0\r\nAND \"TotalCents\" >= 0\r\nAND \"ProductTaxableBaseCents\" + \"ProductVatCents\" = \"ProductSubtotalCents\"\r\nAND \"TotalCents\" = \"ProductSubtotalCents\" + \"ShippingCents\"");
                    table.CheckConstraint("CK_Orders_Country", "\"CountryCode\" = 'ES'");
                    table.CheckConstraint("CK_Orders_Currency", "\"Currency\" = 'EUR'");
                    table.CheckConstraint("CK_Orders_Reservation", "\"ReservationExpiresAt\" > \"CreatedAt\"");
                    table.CheckConstraint("CK_Orders_ShippingVat", "(\"ShippingVatRate\" IS NULL AND \"ShippingTaxableBaseCents\" IS NULL AND \"ShippingVatCents\" IS NULL)\r\nOR (\"ShippingVatRate\" IS NOT NULL AND \"ShippingTaxableBaseCents\" IS NOT NULL AND \"ShippingVatCents\" IS NOT NULL\r\n    AND \"ShippingTaxableBaseCents\" >= 0 AND \"ShippingVatCents\" >= 0\r\n    AND \"ShippingTaxableBaseCents\" + \"ShippingVatCents\" = \"ShippingCents\")");
                    table.CheckConstraint("CK_Orders_StatusTimes", "(\"Status\" = 'PendingPayment' AND \"PaidAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"ExpiredAt\" IS NULL)\r\nOR (\"Status\" = 'Paid' AND \"PaidAt\" IS NOT NULL AND \"CancelledAt\" IS NULL AND \"ExpiredAt\" IS NULL)\r\nOR (\"Status\" = 'Cancelled' AND \"CancelledAt\" IS NOT NULL AND \"PaidAt\" IS NULL AND \"ExpiredAt\" IS NULL)\r\nOR (\"Status\" = 'Expired' AND \"ExpiredAt\" IS NOT NULL AND \"PaidAt\" IS NULL AND \"CancelledAt\" IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "OrderItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    UnitPriceCents = table.Column<long>(type: "bigint", nullable: false),
                    VatRate = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    LineTotalCents = table.Column<long>(type: "bigint", nullable: false),
                    TaxableBaseCents = table.Column<long>(type: "bigint", nullable: false),
                    VatCents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItems", x => x.Id);
                    table.CheckConstraint("CK_OrderItems_Amounts", "\"Quantity\" > 0\r\nAND \"Quantity\" <= 1000000\r\nAND \"UnitPriceCents\" >= 0\r\nAND \"LineTotalCents\" >= 0\r\nAND \"TaxableBaseCents\" >= 0\r\nAND \"VatCents\" >= 0\r\nAND \"LineTotalCents\" = \"UnitPriceCents\" * \"Quantity\"\r\nAND \"TaxableBaseCents\" + \"VatCents\" = \"LineTotalCents\"\r\nAND \"VatRate\" >= 0\r\nAND \"VatRate\" <= 999.99");
                    table.ForeignKey(
                        name: "FK_OrderItems_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    StripeCheckoutSessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RefundedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Payments_Amount", "\"AmountCents\" >= 0");
                    table.CheckConstraint("CK_Payments_Currency", "\"Currency\" = 'EUR'");
                    table.CheckConstraint("CK_Payments_StatusTimes", "(\"Status\" = 'Pending' AND \"SucceededAt\" IS NULL AND \"FailedAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"RefundedAt\" IS NULL)\r\nOR (\"Status\" = 'Succeeded' AND \"SucceededAt\" IS NOT NULL AND \"FailedAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"RefundedAt\" IS NULL)\r\nOR (\"Status\" = 'Failed' AND \"FailedAt\" IS NOT NULL AND \"SucceededAt\" IS NULL AND \"CancelledAt\" IS NULL AND \"RefundedAt\" IS NULL)\r\nOR (\"Status\" = 'Cancelled' AND \"CancelledAt\" IS NOT NULL AND \"SucceededAt\" IS NULL AND \"FailedAt\" IS NULL AND \"RefundedAt\" IS NULL)\r\nOR (\"Status\" = 'Refunded' AND \"SucceededAt\" IS NOT NULL AND \"RefundedAt\" IS NOT NULL AND \"FailedAt\" IS NULL AND \"CancelledAt\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Payments_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderItemComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComponentProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    QuantityPerPack = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderItemComponents", x => x.Id);
                    table.CheckConstraint("CK_OrderItemComponents_QuantityPerPack", "\"QuantityPerPack\" > 0 AND \"QuantityPerPack\" <= 1000000");
                    table.ForeignKey(
                        name: "FK_OrderItemComponents_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderItemComponents_Products_ComponentProductId",
                        column: x => x.ComponentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalEventId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    EventType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentEvents_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemComponents_ComponentProductId",
                table: "OrderItemComponents",
                column: "ComponentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItemComponents_OrderItemId_ComponentProductId",
                table: "OrderItemComponents",
                columns: new[] { "OrderItemId", "ComponentProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_OrderId_ProductId",
                table: "OrderItems",
                columns: new[] { "OrderId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_ProductId",
                table: "OrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Email",
                table: "Orders",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_IdempotencyKey",
                table: "Orders",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Number",
                table: "Orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ReservationExpiresAt",
                table: "Orders",
                column: "ReservationExpiresAt",
                filter: "\"Status\" = 'PendingPayment'");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_ExternalEventId",
                table: "PaymentEvents",
                column: "ExternalEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentEvents_PaymentId",
                table: "PaymentEvents",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OnePending",
                table: "Payments",
                column: "OrderId",
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                table: "Payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StripeCheckoutSessionId",
                table: "Payments",
                column: "StripeCheckoutSessionId",
                unique: true,
                filter: "\"StripeCheckoutSessionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_StripePaymentIntentId",
                table: "Payments",
                column: "StripePaymentIntentId",
                unique: true,
                filter: "\"StripePaymentIntentId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderItemComponents");

            migrationBuilder.DropTable(
                name: "PaymentEvents");

            migrationBuilder.DropTable(
                name: "OrderItems");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Orders");
        }
    }
}
