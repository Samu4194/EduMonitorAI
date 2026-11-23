using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Evaluaciones.Migrations
{
    /// <inheritdoc />
    public partial class VinculosMoodle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VinculoMoodle",
                columns: table => new
                {
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    MoodleId = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VinculoMoodle", x => x.CursoId);
                });

            migrationBuilder.CreateTable(
                name: "CorrespondenciaMoodle",
                columns: table => new
                {
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    Clave = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    LocalId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CorrespondenciaMoodle", x => new { x.CursoId, x.Clave });
                    table.ForeignKey(
                        name: "FK_CorrespondenciaMoodle_VinculoMoodle_CursoId",
                        column: x => x.CursoId,
                        principalTable: "VinculoMoodle",
                        principalColumn: "CursoId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VinculoMoodle_Codigo",
                table: "VinculoMoodle",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VinculoMoodle_MoodleId",
                table: "VinculoMoodle",
                column: "MoodleId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CorrespondenciaMoodle");

            migrationBuilder.DropTable(
                name: "VinculoMoodle");
        }
    }
}
