using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DominioDeLaSierra.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalReference",
                table: "ReservationHoldIncidents",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProviderReconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Attention = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderReconciliations", x => x.Id);
                    table.CheckConstraint("CK_ProviderReconciliations_Attempt", "\"AttemptCount\" >= 1 AND \"NextAttemptAt\" >= \"LastAttemptAt\"");
                    table.CheckConstraint("CK_ProviderReconciliations_Evidence", "\"Evidence\" IN ('Unknown', 'StillPayable', 'Paid', 'ClosedUnpaid', 'Unavailable')");
                    table.CheckConstraint("CK_ProviderReconciliations_Provider", "\"Provider\" IN ('Redsys', 'PayPal')");
                    table.CheckConstraint("CK_ProviderReconciliations_Reference", "\"ExternalReference\" ~ '^[A-Za-z0-9]{4,36}$'");
                    table.ForeignKey(
                        name: "FK_ProviderReconciliations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProviderReconciliations_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReservationHoldIncidents_Reference",
                table: "ReservationHoldIncidents",
                sql: "\"ExternalReference\" IS NULL OR \"ExternalReference\" ~ '^[A-Za-z0-9]{4,36}$'");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReconciliations_NextAttempt",
                table: "ProviderReconciliations",
                column: "NextAttemptAt");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReconciliations_OrderId",
                table: "ProviderReconciliations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderReconciliations_Payment",
                table: "ProviderReconciliations",
                column: "PaymentId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProviderReconciliations");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReservationHoldIncidents_Reference",
                table: "ReservationHoldIncidents");

            migrationBuilder.DropColumn(
                name: "ExternalReference",
                table: "ReservationHoldIncidents");
        }
    }
}
