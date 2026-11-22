# Guion de presentación

1. En Docker Desktop, muestra el grupo **edumonitorai**: `usuarios`, `materias`, `estudiantes`, `gateway` y `frontend`. Si usas SQL Server dentro de Docker, también verás `sqlserver`; `database-init` termina con código 0 después de preparar las bases y que esté detenido es normal.
2. Abre **http://127.0.0.1:5080**. Entra como administrador usando `.local/ACCESO.txt`.
3. En **Carreras y mallas** da de baja una carrera y reactívala (se oculta para lo nuevo y conserva el historial), muestra el período (en preparación o en curso), crea una carrera nueva (el código lo asigna el sistema) y carga su malla con un CSV; explica que con el período en curso la malla queda bloqueada y que una malla nueva no afecta a los estudiantes ya registrados. Abre la malla de una carrera: materias por nivel, con horas y créditos. Explica que se cargó desde las mallas oficiales de la facultad.
4. En **Docentes** registra uno con su ficha (cédula, nombres, apellidos, correo institucional, teléfono y título) y marca sus carreras. Explica que su cuenta tiene la contraseña protegida con hash y que los datos personales se guardan aparte de las credenciales.
5. En **Cursos** elige una carrera y un nivel. Un curso es una materia en un paralelo. Asigna el docente: solo aparecen los de esa carrera. Si intentas asignar uno de otra carrera por la API, responde 409.
6. En **Estudiantes** pulsa **Cargar CSV**, descarga la plantilla, elige carrera, nivel, paralelo y materia, y súbela con `cedula, apellidos, nombres, correo`. Explica que cada carga es de una materia y que el sistema respeta los 15 créditos por período, una materia en un solo paralelo y una sola carrera; muestra el rechazo al cargar los mismos estudiantes en una materia de otro nivel cuando ya tienen 15 créditos. Filtra por materia y retira a uno de una materia con la × de su etiqueta.
7. Cierra sesión y entra como docente. Solo ve Resumen, Carreras (las suyas) y **Mis cursos**. En un curso abre **Ver estudiantes**: aparece la lista de su paralelo, sin cédulas. Las APIs también rechazan operaciones administrativas y cursos ajenos.
8. Vuelve a la cuenta de administrador y ejecuta `docker compose stop estudiantes` desde esta carpeta (o `-f compose.express.yaml` si usas SQL Server de Windows).
9. En Estudiantes pulsa **Actualizar**. Aparece un mensaje de indisponibilidad y **Reintentar**. Cambia a Docentes, Carreras y Cursos para mostrar que siguen disponibles.
10. Ejecuta `docker compose start estudiantes`, espera el estado `healthy` y pulsa **Reintentar**. Los estudiantes cargados siguen guardados; no reiniciaste los otros servicios.

Docentes y estudiantes se crean desde la interfaz con datos ficticios; no se siembran automáticamente. Lo único que se carga solo en una base nueva es el catálogo académico (5 carreras y 237 materias, de las mallas públicas de la facultad), un período inicial y los paralelos A y B de cada nivel.

La prueba automática se ejecuta con `powershell -ExecutionPolicy Bypass -File scripts/Test.ps1` y guarda el resultado en `TestResults/microservices.trx`. Incluye el escenario de fallo y recuperación. No ejecutes esa prueba mientras otra persona presenta, porque detiene temporalmente servicios.

Distinción para explicar al ingeniero: una parada manual con Compose requiere un inicio manual. Si el proceso termina sin una parada de Compose, `restart: unless-stopped` lo reinicia. Un health check fallido por sí solo no provoca un reinicio. El detalle y una demostración de terminación controlada están en el README.

Las contraseñas están únicamente en los archivos privados `.local/ACCESO.txt` y `.local/DEMO.txt`; no los incluyas en informes ni capturas compartidas. El estudiante no tiene cuenta de acceso.

## Gestión académica (Sprint 2)

1. Entra como administrador y verifica que existe un docente con carreras y un curso asignado (Cursos); si no hay estudiantes en el paralelo, cárgalos desde Estudiantes (CSV).
2. Cierra sesión y entra como ese docente. En el menú aparecen **Unidades y temas**, **Actividades y evaluaciones**, **Calificaciones** y **Rendimiento académico**.
3. En **Unidades y temas** elige la materia y el paralelo; crea una unidad, un tema con contenidos mínimos y reordénalos.
4. En **Actividades y evaluaciones** crea una tarea, una APE y un examen eligiendo los temas que evalúan. Intenta guardar con campos vacíos: se muestran los mensajes.
5. En **Calificaciones** escribe notas sobre 10. Deja casillas vacías: son pendientes, no cero. Prueba una nota de 11: se rechaza. Abre **Ponderaciones** y cambia los porcentajes (deben sumar 100).
6. En **Rendimiento académico** muestra el promedio del paralelo, los estudiantes en riesgo (resultado menor a 7), el rendimiento por tema y el detalle de un estudiante. Filtra por unidad y tema.
7. Para la parte de fallos: `docker compose -f compose.express.yaml stop evaluaciones`. Unidades, actividades, notas y rendimiento avisan que el servicio no está disponible, mientras Cursos, Carreras y Estudiantes siguen funcionando. `start evaluaciones` lo recupera sin perder datos.
