using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStockReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmationStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockReservations", x => x.Id);
                    table.CheckConstraint("CK_StockReservations_StatusTimes", "(\"Status\" = 'Reserved' AND \"ConfirmationStartedAt\" IS NULL AND \"ConfirmedAt\" IS NULL AND \"ReleasedAt\" IS NULL)\r\nOR (\"Status\" = 'Confirming' AND \"ConfirmationStartedAt\" IS NOT NULL AND \"ConfirmedAt\" IS NULL AND \"ReleasedAt\" IS NULL)\r\nOR (\"Status\" = 'Confirmed' AND \"ConfirmationStartedAt\" IS NOT NULL AND \"ConfirmedAt\" IS NOT NULL AND \"ReleasedAt\" IS NULL)\r\nOR (\"Status\" = 'Released' AND \"ReleasedAt\" IS NOT NULL AND \"ConfirmedAt\" IS NULL AND \"ConfirmationStartedAt\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_StockReservations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_OrderId",
                table: "StockReservations",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockReservations_Status",
                table: "StockReservations",
                column: "Status",
                filter: "\"Status\" = 'Reserved'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockReservations");
        }
    }
}
