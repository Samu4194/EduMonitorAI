using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Estudiantes.Migrations
{
    /// <inheritdoc />
    public partial class CreditosInscripcion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Creditos",
                table: "Inscripciones",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Creditos",
                table: "Inscripciones");
        }
    }
}
