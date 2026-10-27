# Guion de presentación

1. En Docker Desktop, muestra el grupo **edumonitorai**: `usuarios`, `materias`, `estudiantes`, `gateway`, `frontend` y `sqlserver`. `database-init` termina con código 0 después de preparar las bases; que esté detenido es normal.
2. Abre **http://127.0.0.1:5080**. Entra como administrador usando `.local/ACCESO.txt`.
3. Muestra Docentes, registra uno y explica que se crea su cuenta con contraseña protegida mediante hash.
4. Registra una materia con código, nombre y período. Puedes dejarla sin docente y asignarla usando **Editar / asignar**.
5. Registra un estudiante con código, nombres y apellidos. No tiene correo obligatorio ni cuenta de acceso.
6. En la materia abre **Ver estudiantes** y realiza la matrícula.
7. Cierra sesión y entra como docente. Muestra que solo aparecen sus materias y su lista de estudiantes. Las APIs también rechazan operaciones administrativas.
8. Vuelve a la cuenta de administrador y ejecuta `docker compose stop estudiantes` desde esta carpeta.
9. En Estudiantes pulsa **Actualizar**. Aparece un mensaje de indisponibilidad y **Reintentar**. Cambia a Docentes y Materias para mostrar que siguen disponibles. También puedes cerrar sesión y volver a entrar.
10. Ejecuta `docker compose start estudiantes`, espera el estado `healthy` y pulsa **Reintentar**. El registro y su matrícula siguen guardados; no reiniciaste los otros servicios.

Hay un docente, una materia `CV-001` y un estudiante `EST-001` creados desde la interfaz para facilitar la presentación. La cuenta docente está en `.local/DEMO.txt`. Son datos ficticios guardados en SQL Server, no valores incrustados en React.

La prueba automática se ejecuta con `powershell -ExecutionPolicy Bypass -File scripts/Test.ps1` y guarda el resultado en `TestResults/microservices.trx`. Incluye el escenario de fallo y recuperación. No ejecutes esa prueba mientras otra persona presenta, porque detiene temporalmente servicios.

Distinción para explicar al ingeniero: una parada manual con Compose requiere un inicio manual. Si el proceso termina sin una parada de Compose, `restart: unless-stopped` lo reinicia. Un health check fallido por sí solo no provoca un reinicio. El detalle y una demostración de terminación controlada están en el README.



## Datos preparados para las capturas

Se actualizaron los registros existentes mediante las APIs, conservando sus identificadores y relaciones:

- Docente ficticio: Andrés Sebastián Paredes Molina. Cuenta de prueba: `docente.demo@edumonitor.local`, rol Teacher.
- Materia: Computación Visual (`CV-001`), asignada a ese docente.
- Período de ejemplo editable: Octubre 2026 – Febrero 2027.
- Estudiante ficticia: Valeria Alejandra Torres Cárdenas (`EST-001`), matriculada en la materia.

Son datos ficticios para una presentación en contexto académico de la Universidad Técnica de Ambato; no representan personas ni cuentas institucionales reales. El correo docente puede editarse desde Docentes. La validación admite direcciones con dominio `uta.edu.ec`, sin configurar notificaciones ni afirmar que exista una cuenta determinada.

Las contraseñas no cambiaron. Consulta las credenciales únicamente en los archivos privados `.local/ACCESO.txt` y `.local/DEMO.txt`; no los incluyas en informes ni capturas compartidas. El estudiante no tiene cuenta de acceso.

No se añadió ningún sembrado automático de estos registros: reiniciar los contenedores no vuelve a crearlos. Se conservaron los tres microservicios, el Gateway y Docker Compose.
