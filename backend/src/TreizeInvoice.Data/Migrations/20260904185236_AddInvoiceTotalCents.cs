using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreizeInvoice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceTotalCents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TotalCents",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Les factures déjà en base doivent être triables immédiatement, sans
            // attendre un enregistrement qui n'arrivera jamais pour celles qui sont
            // figées. Quantity et UnitPrice sont stockés en TEXT : CAST avant calcul.
            migrationBuilder.Sql("""
                UPDATE Invoices
                SET TotalCents = COALESCE((
                    SELECT SUM(CAST(ROUND(CAST(it.Quantity AS REAL) * CAST(it.UnitPrice AS REAL) * 100) AS INTEGER))
                    FROM InvoiceItems it
                    WHERE it.InvoiceId = Invoices.Id
                ), 0);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceDate",
                table: "Invoices",
                column: "InvoiceDate");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_Status",
                table: "Invoices",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TotalCents",
                table: "Invoices",
                column: "TotalCents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_InvoiceDate",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_Status",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_TotalCents",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "TotalCents",
                table: "Invoices");
        }
    }
}
