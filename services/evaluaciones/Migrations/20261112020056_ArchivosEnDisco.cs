using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Evaluaciones.Migrations
{
    /// <inheritdoc />
    public partial class ArchivosEnDisco : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Contenido",
                table: "Archivos");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "Contenido",
                table: "Archivos",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);
        }
    }
}
