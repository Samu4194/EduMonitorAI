# Verificación de microservicios — 27 de octubre de 2026

## Resultado automatizado

La solución ASP.NET Core se compiló con **0 errores y 0 advertencias**. TypeScript y el build de producción de React se completaron correctamente. Las cinco imágenes de aplicación (tres APIs, Gateway y frontend) se construyeron y ejecutaron con Docker Compose.

La suite final `TestResults/microservices.trx` contiene **7 pruebas superadas, 0 fallidas y 0 omitidas**, ejecutadas contra el despliegue real y SQL Server:

| Prueba | Evidencia comprobada |
|---|---|
| HU01_LoginValidationRolesAndLogout | Login, credenciales incorrectas, campos vacíos, JWT manipulado, acceso anónimo, permisos y revocación de sesión entre servicios |
| HU02_TeacherCreateReadEditDuplicateAndAccess | Cuenta docente, duplicados, campos inválidos, lectura/edición, persistencia SQL y rechazo de operaciones administrativas al docente |
| HU03_PeriodRegistrationAssignmentAndIsolation | Período obligatorio, registro sin asignación, código único, edición, validación de docente, asignación/reasignación y rechazo de materia ajena |
| HU04_StudentsEnrollmentAndTeacherRoster | Nombres/apellidos/código, edición, duplicados, matrícula válida, matrícula duplicada, materia inexistente, listado docente y consulta SQL |
| DatabaseIsolation_ServiceLoginCannotReadAnotherDatabase | El login SQL de Materias lee su propia base y recibe error 916 al intentar leer Usuarios |
| Failure_StudentsStopsInIsolationAndRecoversPersistentData | Detención real de Estudiantes; frontend, login, Docentes y Materias siguen respondiendo; Estudiantes devuelve 503; recuperación y conservación de estudiante y matrícula |
| Failure_MateriasUnavailableRejectsEnrollmentWithoutSaving | Materias detenido: el listado general de estudiantes funciona, la matrícula responde 503 y no se guarda; al recuperar Materias, la matrícula funciona |

Los registros temporales de esas pruebas se eliminaron mediante el identificador exclusivo de cada ejecución. Las pruebas no limpiaron datos del usuario ni los registros DEMO.

## Verificación manual y navegador

Se comprobó en el navegador integrado el acceso con el administrador conservado, el registro de docente, el registro de materia sin asignación, la asignación posterior, el registro de estudiante, la matrícula y la consulta de la lista con la cuenta docente. Los formularios y listados utilizaron las APIs; no se simularon respuestas en React.

Se crearon y conservaron datos ficticios de demostración: docente de demostración, materia `CV-001`, estudiante `EST-001` y su matrícula. Las credenciales de la cuenta docente están solo en `.local/DEMO.txt`.

También se verificó la política `unless-stopped` terminando el proceso de Estudiantes desde dentro con SIGTERM: Docker lo reinició y volvió a `healthy`. El contador de reinicios aumentó a 1 en esa comprobación. Es una demostración de terminación de proceso, no un corte eléctrico. La parada manual con `docker compose stop` se recuperó con `start`, no automáticamente.

## Datos y migraciones

- Tres bases: `EduMonitor_Usuarios`, `EduMonitor_Materias` y `EduMonitor_Estudiantes`.
- Migración inicial EF Core aplicada por cada servicio y comprobada al arrancar de nuevo.
- Logins SQL separados, sin claves foráneas entre bases.
- Base antigua `EduMonitorAI` conservada. Se comprobó antes de adaptar que solo tenía el administrador, sin registros académicos.
- Respaldo previo en `.local/edumonitor-pre-microservices.bak` y copia de fuentes previa en `.local/pre-microservices`.
- La API monolítica y sus pruebas se conservaron en `backend`, fuera de la solución activa.

## Límites de esta verificación

No hay pruebas pendientes de las cuatro historias o del escenario obligatorio de caída de Estudiantes en este equipo. No se afirma certificación de seguridad, pruebas de carga, funcionamiento en todos los navegadores ni validación en otra computadora. SQL Server sigue siendo una instancia compartida: su caída afecta a los servicios que necesiten sus bases. Usuarios es dependencia de validación de sesión de las APIs protegidas. Estos límites están documentados en el README.

Las historias de los Sprints 2 y 3 no están implementadas. El sistema está preparado como base local para continuar ese trabajo.

La prueba de interfaz con Estudiantes detenido mostró el error y el botón Reintentar; se consultaron Docentes y Materias mientras seguía detenido. La captura está en docs/fallo-estudiantes.png (antes del ajuste textual de '0 registros' a 'Datos no disponibles').



## Actualización de datos de presentación

Se sustituyeron los valores de demostración por Computación Visual (CV-001), Andrés Sebastián Paredes Molina y Valeria Alejandra Torres Cárdenas (EST-001). Se verificó directamente en las tres bases SQL Server la asignación y matrícula. El resultado fue 2 usuarios, 1 materia, 1 estudiante y 1 matrícula. Los identificadores se conservaron.

Se compararon antes y después los correos, roles, hashes y versiones de sesión de todas las cuentas: sin cambios. Se comprobó el login del docente con su contraseña vigente, la lectura de su materia y matrícula y el rechazo 403 al acceso administrativo. No se modificaron React, la arquitectura ni los procesos de inicialización. La admisión de correo institucional se comprobó en la validación EmailAddress del DTO y en el campo HTML de tipo email, sin restricción de dominio y sin registrar una dirección institucional ficticia.
