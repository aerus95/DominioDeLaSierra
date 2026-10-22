using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationHoldIncidents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReservationHoldIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reconciliation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AgeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextReviewAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Resolution = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AlertCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationHoldIncidents", x => x.Id);
                    table.CheckConstraint("CK_ReservationHoldIncidents_Age", "\"AgeSeconds\" >= 0 AND \"AlertCount\" >= 1 AND \"NextReviewAt\" >= \"OpenedAt\" AND \"UpdatedAt\" >= \"OpenedAt\"");
                    table.CheckConstraint("CK_ReservationHoldIncidents_Provider", "\"Provider\" IS NULL OR \"Provider\" IN ('Stripe', 'Redsys', 'PayPal')");
                    table.CheckConstraint("CK_ReservationHoldIncidents_Resolution", "(\"ResolvedAt\" IS NULL AND \"Resolution\" IS NULL)\r\nOR (\"ResolvedAt\" IS NOT NULL AND \"ResolvedAt\" >= \"OpenedAt\" AND \"Resolution\" IN ('Verified', 'Administrative'))");
                    table.CheckConstraint("CK_ReservationHoldIncidents_Severity", "(\"Severity\" = 'Held' AND \"Reconciliation\" = 'Holding')\r\nOR (\"Severity\" = 'Critical' AND \"Reconciliation\" = 'PaidUnsettled')");
                    table.ForeignKey(
                        name: "FK_ReservationHoldIncidents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationHoldIncidents_OpenOrder",
                table: "ReservationHoldIncidents",
                column: "OrderId",
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationHoldIncidents");
        }
    }
}
