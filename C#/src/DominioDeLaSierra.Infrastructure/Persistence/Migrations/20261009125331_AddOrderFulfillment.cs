using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Carrier",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FulfillmentStatus",
                table: "Orders",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unfulfilled");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FulfillmentUpdatedAt",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FulfillmentUpdatedByUserId",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingNumber",
                table: "Orders",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FulfillmentTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Carrier = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FulfillmentTransitions", x => x.Id);
                    table.CheckConstraint("CK_FulfillmentTransitions_Step", "(\"FromStatus\" = 'Unfulfilled' AND \"ToStatus\" = 'Preparing')\r\nOR (\"FromStatus\" = 'Preparing' AND \"ToStatus\" = 'Prepared')\r\nOR (\"FromStatus\" = 'Prepared' AND \"ToStatus\" = 'Shipped')");
                    table.ForeignKey(
                        name: "FK_FulfillmentTransitions_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Orders_Fulfillment",
                table: "Orders",
                sql: "\"FulfillmentStatus\" IN ('Unfulfilled', 'Preparing', 'Prepared', 'Shipped')\r\nAND (\r\n    (\"Status\" = 'Paid')\r\n    OR (\"FulfillmentStatus\" = 'Unfulfilled'\r\n        AND \"Carrier\" IS NULL\r\n        AND \"TrackingNumber\" IS NULL\r\n        AND \"FulfillmentUpdatedAt\" IS NULL\r\n        AND \"FulfillmentUpdatedByUserId\" IS NULL)\r\n)\r\nAND (\r\n    (\"FulfillmentStatus\" = 'Unfulfilled'\r\n        AND \"FulfillmentUpdatedAt\" IS NULL\r\n        AND \"FulfillmentUpdatedByUserId\" IS NULL)\r\n    OR (\"FulfillmentStatus\" <> 'Unfulfilled'\r\n        AND \"FulfillmentUpdatedAt\" IS NOT NULL\r\n        AND \"FulfillmentUpdatedByUserId\" IS NOT NULL)\r\n)");

            migrationBuilder.CreateIndex(
                name: "IX_FulfillmentTransitions_OrderId_OccurredAt",
                table: "FulfillmentTransitions",
                columns: new[] { "OrderId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FulfillmentTransitions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Orders_Fulfillment",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Carrier",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentUpdatedAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FulfillmentUpdatedByUserId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "TrackingNumber",
                table: "Orders");
        }
    }
}
