# Contratos REST de los Sprints 1 y 2

El navegador utiliza el origen `http://127.0.0.1:5080`. Nginx envía `/api` a YARP. El Gateway solo enruta; los cuatro servicios validan JWT y permisos.

En rutas protegidas: `Authorization: Bearer <token>`. Ningún endpoint de listado o detalle devuelve hashes de contraseñas.

El administrador ve todas las carreras. Un docente solo ve las carreras a las que pertenece y los cursos que tiene asignados; el JWT lleva un claim `carrera` por cada una.

| Método | Ruta | Permiso | Servicio |
|---|---|---|---|
| POST | `/api/auth/login` | Público, limitado por intentos | Usuarios |
| GET | `/api/auth/me` | Autenticado | Usuarios |
| GET | `/api/auth/session` | Autenticado; verifica vigencia | Usuarios |
| POST | `/api/auth/logout` | Autenticado | Usuarios |
| GET / POST | `/api/docentes` | Administrador. `GET ?carrera=SOF` filtra por carrera | Usuarios |
| GET / PUT | `/api/docentes/{id}` | Administrador | Usuarios |
| GET | `/api/carreras?estado=` | Administrador: todas (`estado`: `activas` por defecto, `bajas` o `todas`); docente: sus carreras activas | Materias |
| GET | `/api/carreras/{codigo}/materias?nivel=&malla=` | Igual que arriba (malla curricular). `malla`: la vigente por defecto, un número de versión o `todas` | Materias |
| POST | `/api/carreras` | Administrador. Crea una carrera: `nombre, modalidad, niveles`. El código y el prefijo UTA los asigna el sistema (únicos, no se reutilizan ni se cambian) | Materias |
| PUT | `/api/carreras/{codigo}` | Administrador. Edita nombre, modalidad y niveles (solo aumentan); el código y el prefijo UTA se ignoran | Materias |
| GET | `/api/carreras/codigo-sugerido?nombre=` | Administrador. El código y el prefijo UTA que se asignarían a ese nombre | Materias |
| PUT | `/api/carreras/{codigo}/baja` y `/reactivar` | Administrador. Baja lógica de la carrera (no se elimina) | Materias |
| POST | `/api/carreras/{codigo}/malla` | Administrador (archivo CSV). Carga o actualiza la malla; solo con el período activo en preparación | Materias |
| GET / POST | `/api/periodos` | Autenticado / Administrador | Materias |
| PUT | `/api/periodos/{id}/activar` | Administrador | Materias |
| PUT | `/api/periodos/{id}/iniciar` y `/finalizar` | Administrador. El período pasa de Preparación a En curso (bloquea la carga de mallas) y de En curso a Finalizado | Materias |
| GET | `/api/paralelos?carrera=&nivel=&periodoId=` | Autenticado (período activo por defecto) | Materias |
| POST | `/api/paralelos` | Administrador. `malla` opcional: la versión con la que se dicta (la vigente por defecto) | Materias |
| GET | `/api/cursos?carrera=&nivel=&paralelo=&periodoId=` | Administrador: todos; docente: los suyos | Materias |
| GET | `/api/cursos/{id}` | Administrador o docente asignado | Materias |
| PUT | `/api/cursos/{id}/docente` | Administrador | Materias |
| GET / POST | `/api/estudiantes` | Administrador. `GET ?carrera=&nivel=&paralelo=&materia=&estado=` (`materia` es el código, p. ej. `SOF-102`; `estado`: `activos` por defecto, `bajas` o `todos`). Nivel, paralelo y materia filtran por las inscripciones del estudiante y la respuesta solo trae las que cumplen | Estudiantes |
| GET / PUT | `/api/estudiantes/{id}` | Administrador. `PUT` edita solo los datos personales (`cedula, apellidos, nombres, correo`) | Estudiantes |
| POST | `/api/estudiantes/precarga` | Administrador (archivo CSV). Responde `creados`, `actualizados`, `total` (estudiantes), `reactivados` e `inscripciones` (materias agregadas o reincorporadas) | Estudiantes |
| POST | `/api/estudiantes/{id}/baja` y `/api/estudiantes/{id}/reactivar` | Administrador. Baja lógica, idempotente | Estudiantes |
| POST | `/api/estudiantes/inscripciones/{id}/baja` y `/api/estudiantes/inscripciones/{id}/reactivar` | Administrador. Retira o reincorpora al estudiante en **una** materia; sus demás materias y sus notas no cambian | Estudiantes |
| GET | `/api/estudiantes/curso/{id}` | Administrador o docente asignado | Estudiantes |

No hay rutas de matrícula ni de borrado: los estudiantes llegan por precarga y pertenecen a un paralelo. Para retirar a uno se le da de baja (`Activo = false`): deja de aparecer en la lista del docente, en la grilla de calificaciones y en el rendimiento, pero sus datos y notas se conservan, y se reactiva con `/reactivar` o al volver a venir en un CSV. Su cédula sigue ocupada. Los estudiantes no tienen cuentas de acceso.

## Gestión académica (Sprint 2, servicio Evaluaciones)

Todo cuelga de un curso. Consultar lo puede hacer el administrador o el docente del curso; escribir, solo el docente del curso (el administrador recibe 403). Un curso que no es del docente responde 404, igual que en Materias.

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/unidades?cursoId=` | Unidades del curso con sus temas y contenidos mínimos |
| POST | `/api/unidades` | `{cursoId, nombre, descripcion?}` |
| PUT / DELETE | `/api/unidades/{id}` | Editar / eliminar (409 si algún tema está en uso por una actividad) |
| POST | `/api/unidades/{id}/mover` | `{direccion: -1 o 1}` sube o baja la unidad |
| POST | `/api/unidades/{id}/temas` | `{nombre, descripcion?, contenidos?: string[]}` |
| PUT / DELETE | `/api/temas/{id}` | Editar / eliminar tema (409 si está en uso) |
| POST | `/api/temas/{id}/mover` y `/api/temas/{id}/contenidos` | Reordenar tema / agregar contenido mínimo |
| PUT / DELETE | `/api/contenidos/{id}` | Editar / eliminar contenido mínimo |
| GET | `/api/actividades?cursoId=&tipo=&temaId=` | Tipo: `Tarea`, `APE` o `Examen` |
| GET / PUT / DELETE | `/api/actividades/{id}` | Consultar, editar y eliminar (borra sus notas) |
| POST | `/api/actividades` | `{cursoId, nombre, tipo, descripcion?, fechaEntrega?, temaIds[]}`; al menos un tema, todos del mismo curso |
| GET | `/api/calificaciones/curso/{cursoId}` | Estudiantes del paralelo, notas por actividad, resultado ponderado y estado |
| PUT | `/api/calificaciones/actividad/{id}` | `{notas:[{estudianteId, nota}]}`. Todo o nada; nota de 0 a 10; `null` deja la nota pendiente |
| GET / PUT | `/api/calificaciones/curso/{cursoId}/ponderaciones` | `{tareas, ape, examen, notaMinima}`; los tres porcentajes suman 100 |
| GET | `/api/rendimiento/curso/{cursoId}?unidadId=&temaId=` | Resumen, resultados por estudiante y rendimiento por tema |
| GET | `/api/rendimiento/curso/{cursoId}/estudiante/{id}` | Notas y promedios por tema de un estudiante |

Reglas de cálculo: escala de 0 a 10 y nota mínima 7 (configurable por curso). Cada tipo de actividad se promedia; el resultado es la media ponderada de los tipos con notas. Una actividad sin nota es *pendiente*: no se toma como cero y el resultado se marca `definitivo: false` hasta que no quede nada pendiente. Estado: `Óptimo` (resultado mayor o igual a la nota mínima), `En riesgo` (menor) o `Sin calificaciones`. Sin ponderación guardada se usa 30 % tareas, 30 % APE y 40 % exámenes.

## Cédulas

La cédula de docentes y estudiantes (API y CSV) debe ser una cédula ecuatoriana válida: exactamente 10 dígitos numéricos, provincia 01 a 24 o 30, tercer dígito de 0 a 5 y dígito verificador correcto (módulo 10). En el CSV no se corrige nada salvo el cero inicial que Excel elimina (9 dígitos pasan a 10); una cédula con letras, espacios o guiones es un error de esa fila. La validación solo comprueba el formato y el dígito verificador: no confirma que la cédula pertenezca a una persona real.

## Modelo académico

- **Carrera** → **Materia** (de la malla curricular: nivel, créditos y horas).
- **Período** activo y **Paralelo** = carrera + nivel + letra dentro de un período.
- **Curso** = materia dictada en un paralelo. Al crear un paralelo se crea un curso por cada materia de ese nivel, sin docente. La asignación del docente se hace en el curso.
- El catálogo (5 carreras y 237 materias) se carga desde `services/materias/Data/Seed` al arrancar con la base vacía. También se crea un período inicial y los paralelos A y B de cada nivel.

## Cuerpos de solicitud

Login:

```json
{"email":"correo de la cuenta","password":"contraseña de la cuenta"}
```

Devuelve `token`, `expiresAt` y `user: {id,name,email,role,carreras}`.

Docente (la ficha personal se guarda aparte de las credenciales):

```json
{"cedula":"0000000001","nombres":"Ana María","apellidos":"Pérez Torres","email":"docente@example.test","telefono":"0999999999","tituloAcademico":"Ingeniero en Sistemas","carreras":["SOF","TI"],"password":"una contraseña de al menos 10 caracteres"}
```

La cédula tiene 10 dígitos y es única. `telefono` y `tituloAcademico` son opcionales. Debe indicar al menos una carrera (códigos `SOF`, `TI`, `TEL`, `IND`, `AUT`). En una edición, `password:null` conserva la contraseña. Si cambian la contraseña o las carreras, las sesiones anteriores del docente quedan revocadas. El correo es único entre todos los usuarios y se normaliza a minúsculas.

Paralelo: `{"carrera":"SOF","nivel":4,"letra":"C"}`. Se crea en el período activo. Duplicado: 409.

Asignación de docente: `{"teacherId":7}`; `{"teacherId":null}` deja el curso sin docente. El docente debe existir en Usuarios **y pertenecer a la carrera de la materia** (409 si no).

Estudiante (`POST`, equivale a una fila del CSV; `materia` es opcional):

```json
{"cedula":"2459123452","apellidos":"Mora Cruz","nombres":"Luis","correo":"luis@example.test","carrera":"SOF","nivel":4,"paralelo":"B","materia":"SOF-402"}
```

El paralelo y la materia deben existir en el período activo (Estudiantes lo valida con Materias). Sin `materia`, el estudiante queda inscrito en todas las materias de ese nivel y paralelo.

Precarga: `multipart/form-data` con el campo `archivo` (CSV, hasta 2 MB y 5000 filas) y el **destino obligatorio** en campos del formulario: `carrera` (código), `nivel`, `paralelo` y `materia` (código). Cada carga es de una materia de un paralelo; sin los cuatro campos responde 400. Columnas: `cedula, apellidos, nombres, correo`. Si el archivo trae además columnas `carrera`, `nivel`, `paralelo` o `materia`, deben coincidir con el destino o la fila se rechaza. Para inscribir a los mismos estudiantes en otra materia, paralelo o nivel se hace otra carga con ese destino. Acepta coma o punto y coma, UTF-8 o ANSI. La carga solo **agrega**: nunca quita inscripciones anteriores (para eso está el retiro por materia). Reglas frente a lo ya registrado: un estudiante pertenece a una sola carrera, una materia se cursa en un solo paralelo (para pasarla a otro se retira primero del anterior) y suma hasta **15 créditos** por período (sumar menos es válido, incluso una sola materia). Si una fila incumple una regla no se guarda nada: 400 por errores del archivo y 409 por conflicto con lo ya registrado, indicando la fila, la cédula y el motivo, por ejemplo «ya tiene 15 créditos y sumaría 18». Las cédulas que ya existen se actualizan; un estudiante o una materia dados de baja que vuelven a venir se reactivan. Respuesta: `{"creados":2,"actualizados":1,"total":3,"reactivados":0,"inscripciones":3}`.

La lista de un curso (`/api/estudiantes/curso/{id}`) devuelve apellidos, nombres y correo, sin cédula, solo de los estudiantes activos inscritos en esa materia y paralelo. Estudiantes reenvía el JWT a Materias, que responde 404 si el curso no es del docente.

## Carreras y mallas

Una carrera nueva se crea sin malla: `{"nombre":"Mecánica","modalidad":"Presencial","niveles":9}`. El código (por ejemplo `MEC`) se deriva del nombre y se hace único; el prefijo UTA es el siguiente número de 4 dígitos libre (0810 después de 0809). Las horas y los créditos totales salen de la malla.

Malla: `multipart/form-data` con el campo `archivo` (CSV de hasta 2 MB y 600 filas) con la malla completa. Columnas: `nivel, nombre, unidad` y, según la materia, `tipo, codigo_uta, horas_contacto_docente, horas_practico_experimental, horas_autonomas, horas_totales, creditos`. Las materias regulares llevan las tres horas; prácticas, servicio comunitario y titulación llevan `horas_totales`. Si no hay `creditos` se calculan con 48 horas por crédito. Los `codigo_uta` (8 dígitos, opcionales) deben empezar con el `prefijoUta` de la carrera. Debe haber materias en todos los niveles. Todo o nada: 400 con el error de cada fila.

Versiones: si la malla vigente todavía no se usa en ningún paralelo se reemplaza; si ya se usa se crea la versión siguiente. Un paralelo se dicta con una sola malla, y un estudiante continúa con la malla en la que empezó (los estudiantes nuevos entran por paralelos de la malla vigente). Responde `{"version":2,"nuevaVersion":true,"materias":43,"horasTotales":6480,"creditosTotales":135}`. Con el período activo en curso responde 409: la malla solo se carga o actualiza con el período en preparación.

Baja de una carrera: se oculta de las listas y deja de admitir paralelos, mallas, ediciones, docentes y estudiantes nuevos (409 «dada de baja»); su historial, sus mallas, su código y su prefijo UTA se conservan, y se puede reactivar. No se da de baja con paralelos en un período que no ha finalizado (409: finaliza el período antes). Los docentes dejan de ver la carrera y sus cursos, y no se les asigna en ella; pueden pertenecer a otras carreras. En la ficha del docente la carrera de baja deja de mostrarse (`/api/docentes` conserva el código; el frontend lo oculta y al editar se descarta).

## Errores

400: campos inválidos o incompletos. 401: credenciales o token no válidos. 403: operación administrativa prohibida. 404: entidad inexistente o curso/carrera no autorizados. 409: cédula, correo, paralelo o docente de otra carrera. 429: límite de login. 503: dependencia o servicio no disponible. 500: fallo inesperado, con identificador de registro técnico.

Las validaciones de ASP.NET Core devuelven `errors`; los errores controlados devuelven `message` y, cuando corresponde, `traceId`. React soporta ambos formatos.

## Health checks internos

Cada API publica `/health/live` (proceso vivo) y `/health/ready` (conexión a su base). El Gateway tiene ambas rutas para su propio proceso; no falla su health check porque una API esté detenida. Nginx publica `/health` para la interfaz.

Ejemplo sin publicar puertos adicionales:

```powershell
docker compose exec -T materias curl -fsS http://localhost:8080/health/ready
```

Las APIs se comunican por HTTP autenticado dentro de la red de Docker. Este despliegue es local; un despliegue fuera del equipo necesita HTTPS y gestión de secretos adecuada.
