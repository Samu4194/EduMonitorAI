using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduMonitor.Seguimiento.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Analisis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    DocenteId = table.Column<int>(type: "int", nullable: false),
                    Materia = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Paralelo = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Periodo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    NotaMinima = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TerminadoEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalEstudiantes = table.Column<int>(type: "int", nullable: false),
                    ARecomendar = table.Column<int>(type: "int", nullable: false),
                    Generadas = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Analisis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Eventos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecomendacionId = table.Column<int>(type: "int", nullable: false),
                    Evento = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    UsuarioId = table.Column<int>(type: "int", nullable: true),
                    Detalle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Eventos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EstudianteAnalisis",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnalisisId = table.Column<int>(type: "int", nullable: false),
                    EstudianteId = table.Column<int>(type: "int", nullable: false),
                    Apellidos = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Nombres = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Correo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Resultado = table.Column<decimal>(type: "decimal(4,2)", precision: 4, scale: 2, nullable: true),
                    Estado = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Pendientes = table.Column<int>(type: "int", nullable: false),
                    Nivel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ContextoJson = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EstudianteAnalisis", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EstudianteAnalisis_Analisis_AnalisisId",
                        column: x => x.AnalisisId,
                        principalTable: "Analisis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Hallazgos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnalisisId = table.Column<int>(type: "int", nullable: false),
                    EstudianteAnalisisId = table.Column<int>(type: "int", nullable: true),
                    Codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Nivel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detalle = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hallazgos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hallazgos_Analisis_AnalisisId",
                        column: x => x.AnalisisId,
                        principalTable: "Analisis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Hallazgos_EstudianteAnalisis_EstudianteAnalisisId",
                        column: x => x.EstudianteAnalisisId,
                        principalTable: "EstudianteAnalisis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Recomendaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AnalisisId = table.Column<int>(type: "int", nullable: false),
                    EstudianteAnalisisId = table.Column<int>(type: "int", nullable: false),
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Origen = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Modelo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Resumen = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    CausasJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccionesDocenteJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AccionesEstudianteJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MensajeEstudiante = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: false),
                    Editada = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualizadoEn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevisadaPor = table.Column<int>(type: "int", nullable: true),
                    RevisadaEn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MotivoRechazo = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recomendaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recomendaciones_Analisis_AnalisisId",
                        column: x => x.AnalisisId,
                        principalTable: "Analisis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Recomendaciones_EstudianteAnalisis_EstudianteAnalisisId",
                        column: x => x.EstudianteAnalisisId,
                        principalTable: "EstudianteAnalisis",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Comunicaciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecomendacionId = table.Column<int>(type: "int", nullable: false),
                    CursoId = table.Column<int>(type: "int", nullable: false),
                    Canal = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Destinatario = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Asunto = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Cuerpo = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnviadoPor = table.Column<int>(type: "int", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comunicaciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comunicaciones_Recomendaciones_RecomendacionId",
                        column: x => x.RecomendacionId,
                        principalTable: "Recomendaciones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Analisis_CursoId_CreadoEn",
                table: "Analisis",
                columns: new[] { "CursoId", "CreadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_Comunicaciones_CursoId",
                table: "Comunicaciones",
                column: "CursoId");

            migrationBuilder.CreateIndex(
                name: "IX_Comunicaciones_RecomendacionId",
                table: "Comunicaciones",
                column: "RecomendacionId");

            migrationBuilder.CreateIndex(
                name: "IX_EstudianteAnalisis_AnalisisId_EstudianteId",
                table: "EstudianteAnalisis",
                columns: new[] { "AnalisisId", "EstudianteId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Eventos_RecomendacionId",
                table: "Eventos",
                column: "RecomendacionId");

            migrationBuilder.CreateIndex(
                name: "IX_Hallazgos_AnalisisId",
                table: "Hallazgos",
                column: "AnalisisId");

            migrationBuilder.CreateIndex(
                name: "IX_Hallazgos_EstudianteAnalisisId",
                table: "Hallazgos",
                column: "EstudianteAnalisisId");

            migrationBuilder.CreateIndex(
                name: "IX_Recomendaciones_AnalisisId",
                table: "Recomendaciones",
                column: "AnalisisId");

            migrationBuilder.CreateIndex(
                name: "IX_Recomendaciones_CursoId_Estado",
                table: "Recomendaciones",
                columns: new[] { "CursoId", "Estado" });

            migrationBuilder.CreateIndex(
                name: "IX_Recomendaciones_EstudianteAnalisisId",
                table: "Recomendaciones",
                column: "EstudianteAnalisisId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comunicaciones");

            migrationBuilder.DropTable(
                name: "Eventos");

            migrationBuilder.DropTable(
                name: "Hallazgos");

            migrationBuilder.DropTable(
                name: "Recomendaciones");

            migrationBuilder.DropTable(
                name: "EstudianteAnalisis");

            migrationBuilder.DropTable(
                name: "Analisis");
        }
    }
}
