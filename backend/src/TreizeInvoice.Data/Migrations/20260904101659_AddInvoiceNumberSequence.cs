using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TreizeInvoice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InvoiceNumberSequences",
                columns: table => new
                {
                    Year = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LastNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceNumberSequences", x => x.Year);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CancelsInvoiceId",
                table: "Invoices",
                column: "CancelsInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_Invoices_CancelsInvoiceId",
                table: "Invoices",
                column: "CancelsInvoiceId",
                principalTable: "Invoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_Invoices_CancelsInvoiceId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "InvoiceNumberSequences");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_CancelsInvoiceId",
                table: "Invoices");
        }
    }
}
