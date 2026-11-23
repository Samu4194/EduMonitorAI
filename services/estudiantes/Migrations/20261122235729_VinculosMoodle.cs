using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Estudiantes.Migrations
{
    /// <inheritdoc />
    public partial class VinculosMoodle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MoodleCodigo",
                table: "Students",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MoodleUsuarioId",
                table: "Students",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Students_MoodleCodigo",
                table: "Students",
                column: "MoodleCodigo",
                unique: true,
                filter: "[MoodleCodigo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Students_MoodleUsuarioId",
                table: "Students",
                column: "MoodleUsuarioId",
                unique: true,
                filter: "[MoodleUsuarioId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Students_MoodleCodigo",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_MoodleUsuarioId",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "MoodleCodigo",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "MoodleUsuarioId",
                table: "Students");
        }
    }
}
