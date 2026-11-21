using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Materias.Migrations
{
    /// <inheritdoc />
    public partial class PrefijoUtaUnico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Carreras_PrefijoUta",
                table: "Carreras",
                column: "PrefijoUta",
                unique: true,
                filter: "[PrefijoUta] <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Carreras_PrefijoUta",
                table: "Carreras");
        }
    }
}
