using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OrderNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    LastError = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    ProcessingUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderNotifications", x => x.Id);
                    table.CheckConstraint("CK_OrderNotifications_State", "\"Kind\" IN ('PaidConfirmation')\r\nAND \"Attempts\" >= 0\r\nAND \"Attempts\" <= 8\r\nAND (\r\n    (\"Status\" = 'Pending'\r\n        AND \"Attempts\" = 0\r\n        AND \"NextAttemptAt\" IS NOT NULL\r\n        AND \"SentAt\" IS NULL\r\n        AND \"ProviderMessageId\" IS NULL\r\n        AND \"LastError\" IS NULL\r\n        AND \"ProcessingUntil\" IS NULL)\r\n    OR (\"Status\" = 'Processing'\r\n        AND \"Attempts\" < 8\r\n        AND \"NextAttemptAt\" IS NOT NULL\r\n        AND \"ProcessingUntil\" IS NOT NULL\r\n        AND \"SentAt\" IS NULL\r\n        AND \"ProviderMessageId\" IS NULL)\r\n    OR (\"Status\" = 'Sent'\r\n        AND \"SentAt\" IS NOT NULL\r\n        AND \"ProviderMessageId\" IS NOT NULL\r\n        AND \"ProcessingUntil\" IS NULL\r\n        AND \"LastError\" IS NULL)\r\n    OR (\"Status\" = 'Failed'\r\n        AND \"Attempts\" >= 1\r\n        AND \"Attempts\" < 8\r\n        AND \"NextAttemptAt\" IS NOT NULL\r\n        AND \"LastError\" IS NOT NULL\r\n        AND \"SentAt\" IS NULL\r\n        AND \"ProviderMessageId\" IS NULL\r\n        AND \"ProcessingUntil\" IS NULL)\r\n    OR (\"Status\" = 'Abandoned'\r\n        AND \"Attempts\" = 8\r\n        AND \"NextAttemptAt\" IS NULL\r\n        AND \"LastError\" IS NOT NULL\r\n        AND \"SentAt\" IS NULL\r\n        AND \"ProviderMessageId\" IS NULL\r\n        AND \"ProcessingUntil\" IS NULL)\r\n)");
                    table.ForeignKey(
                        name: "FK_OrderNotifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_Due",
                table: "OrderNotifications",
                column: "NextAttemptAt",
                filter: "\"Status\" IN ('Pending', 'Failed')");

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_OrderId_Kind",
                table: "OrderNotifications",
                columns: new[] { "OrderId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderNotifications_Processing",
                table: "OrderNotifications",
                column: "ProcessingUntil",
                filter: "\"Status\" = 'Processing'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderNotifications");
        }
    }
}
