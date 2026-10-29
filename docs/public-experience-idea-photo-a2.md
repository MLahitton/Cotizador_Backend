# FOTO A2: lectura autorizada por proyecto

Estado: implementado y APTO para revision focal de cierre, 2026-10-23.

## Preflight de autorizacion

Antes de implementar se contrasto GetProjectWorkspaceService.ExecuteAsync:
lineas 35-55 autentican y consultan el usuario persistido, exigiendo IsActive;
lineas 78-82 permiten Admin o Project.CreatedByUserId igual al actor;
lineas 88-102 exigen existencia de Client. No se exige actividad de Project/Client.
No se reutiliza la politica owner-only de comandos durables ni se altera workspace.
El actor procede de ICurrentUser del servidor; el rol efectivo procede de DB.

Baseline: worktree completo, incluidos untracked. Artefactos fuera del repo:
C:/Users/USUARIO/AppData/Local/Temp/sng-photo-a2-d9d6c8916e044bd1ad9827cb2151c8b2.
initial.csv registra hashes; status.txt, branch.txt, head.txt y api.csv el estado.

El usuario reporto migracion A1 aplicada y envio local exitoso. No es una
comprobacion realizada por A2. El envio antiguo con 500 sigue pendiente operativo;
no se consulta/repara en la DB habitual. Sin migraciones ni reinicio de API.

## Decision de recursos

Se reutiliza IVerifiedContentCopy/SealedContentCopy, sin jobs ni cambios a sus
primitivas. Spool privado con handle retenido, SHA256/longitud/EOF verificados
antes de 200; mismo lector para envio. Dos plazas por proceso, desde antes de
crear la copia hasta completar/abortar respuesta. Memoria de copia por buffer,
no proporcional al conjunto; hasta 40 MiB de contenido privado concurrente en
disco. No limite distribuido, cuota global de volumen ni techo absoluto del RSS.

## Contrato HTTP

`GET /api/v2/projects/{projectId:guid}/idea-photo`, autenticado.

| Estado | Significado / codigo |
|---|---|
| 200 | Bytes originales verificados, image/jpeg o image/png, Content-Length observado, Content-Disposition: inline sin filename |
| 204 | Proyecto autorizado manual sin Submission, o Submission unica vinculada sin foto; body vacio |
| 401 | Autenticacion invalida o identidad no resoluble; AUTH_UNAUTHORIZED |
| 403 | Usuario persistido inactivo; AUTH_USER_INACTIVE; antes de resolver la foto |
| 404 | Proyecto inexistente o usuario activo ajeno; PROJECT_NOT_FOUND, misma estructura/codigo/detalle seguros |
| 500 | Referencia sin archivo, lectura/consulta fallida, metadatos/digest/longitud invalidos o vinculo ambiguo/contradictorio; PROJECT_IDEA_PHOTO_UNAVAILABLE |

Todas las respuestas del endpoint llevan Cache-Control: private, no-store y
X-Content-Type-Options: nosniff. Middleware focal por metadata, anterior a auth,
incluye 401 de JWT y errores; no cambia headers del workspace. No nombre original,
StorageKey, URL, datos personales ni mensajes de SQL/filesystem en ProblemDetails.
Los errores incluyen traceId por la factory existente; logs clasifican tipo de
fallo y ProjectId en el scope HTTP, sin adjuntar la excepcion con rutas o SQL.

Range se ignora: 200 completo. No ETag, Last-Modified, 304, rangos, retries ni
redirect de contenido. No se cambia la politica HTTPS global del host; los tests
usan Kestrel loopback sin redireccion de hosting y no certifican proxies desplegados.

## Consulta y autorizacion

ICurrentUser se obtiene del servidor. IIdentityRepository consulta el rol y
actividad persistidos; no se confia en un actor del navegador ni en un claim Admin
para saltar esa lectura. Se conservan exactamente owner/Admin y existencia del
Client del workspace. Project/Client inactivos siguen siendo legibles por owner
o Admin activo; no se introducen permisos de escritura ni politicas durables.

Solo despues se consulta Submission.ProjectId con AsNoTracking, incluyendo foto,
limitando a dos filas para detectar ambiguedad. Cero filas es ausencia; mas de una
es error, incluso si alguna carece de foto. Una fila exige ClientId igual al Client
del Project y Photo.SubmissionId igual al Id de Submission. No filtro por nombre,
fecha o carpeta, ni seleccion arbitraria de ganador. No reparacion durante GET.
La FK impide una foto huerfana; no se deshabilitan restricciones para probarla.

No se consulta Requirement current ni se condiciona la foto a extraction,
TechnicalProposal o replacement. Se prueba un replacement de un Requirement
Failed por otro Pending conservando el enlace historico de Submission y la foto.
Esa preparacion del fixture no es procesamiento activado por GET.

Autorizacion por peticion, sin promesa de revocacion instantanea durante el envio.
No transacciones ni locks de negocio durante copia/respuesta. Otra conexion obtiene
FOR UPDATE NOWAIT de Project/Client/Submission/foto durante captura pausada.

## Bytes y propiedad

Validacion previa: IDs no vacios, IdentityVersion=1, StoredEvidenceVersion=1,
MIME en allowlist, longitud positiva hasta 20971520, digest SHA256 hexadecimal
ASCII MAYUSCULA de 64 y clave no vacia. IFileStorage resuelve y valida su clave;
el caller solo aporta projectId. No se usa nombre original para construir rutas.

El origen se abre una vez, se copia con buffer acotado y se dispone antes de
entregar contenido. IVerifiedContentCopy exige longitud exacta, EOF y digest,
incluido un byte adicional para rechazar exceso sin drenar una fuente ilimitada.
No se confia en Stream.Length, no se reabre origen ni spool por ruta. Se utiliza
el lector unico de la misma copia sellada sobre handle retenido. No decode Magick
en A2: se contrasta la identidad incorporada en A1, sin nuevos recibos persistidos.

ProjectIdeaPhotoContent posee copia, lector y plaza. El action result drena
CopyToAsync con RequestAborted y dispone en finally mediante await using.
RegisterForDisposeAsync agrega proteccion si la ejecucion del resultado no llega
a consumirlo; disposal idempotente libera la plaza una sola vez. El consumidor
interno debe disponer el resultado si no lo entrega a HTTP. No se admite disposal
concurrente externo mientras se consume. No se abandonan operaciones con WhenAny.

Cancelacion en espera/captura se propaga con token original. Si el transporte falla
despues de iniciar respuesta, se propaga/aborta y se liberan recursos: no se intenta
convertir retrospectivamente el 200 en ProblemDetails ni enviar otra respuesta.
No se afirma que el cliente no recibiera bytes antes del fallo.

Sin DeleteIfExistsAsync, SaveAsync, SaveVerifiedAsync, SaveChanges o DML del GET.
Los originales nunca se borran. Limpieza de spool usa la primitiva existente y
solo sus recursos propios. Fallos de cleanup se diagnostican sin sustituir el
fallo de respuesta; no scanner, huerfanos ni promesa de limpieza tras crash.

## Persistencia y alcance preservado

Ninguna migracion, tabla, columna, indice, trigger ni modificacion de entidades
A1. No cambio a DTO/servicio/controller del workspace, intake/replay/fingerprint,
preparacion documental, primitivas de storage, jobs, AI2 o reglas comerciales.
Se agregan solo servicio/repository focales, endpoint, headers por metadata y DI
necesaria. El controlador no crea jobs, attempts, evidencia o extraccion.

La unica correccion a documentacion A1 es su procedimiento operativo: historial
completo antes de target, no downgrade accidental/reaplicacion, diagnostico ante
divergencia y comprobacion del SubmissionId fallido antes de una intencion nueva.
No se ejecuta ese procedimiento ni se consulta la DB habitual.

## Pruebas y correcciones preliminares

Build aislado PhotoA2; no se reinicia ni se utilizan endpoints de la API habitual.
PostgreSQL 17 propio, 127.0.0.1:55507, usuario audit, data bajo artifact root.
Solo bases cotizador_backend_test_* y migraciones existentes en fixtures.
Autorizacion HTTP usa JWT local firmado/validado, usuarios/repositorios reales en
PostgreSQL y LocalFileStorage real. No solo mocks de owner/Admin. Los dobles se
limitan a observacion, fallos de E/S, salida lenta o datos que CHECK no deja persistir.

build1.log: dos errores de compilacion del harness (namespace de entidad y ctor
FileStorageOptions); corregidos sin cambiar assertions. build2.log incremental:
0 errores/93 warnings. preflight.trx: 28 passed / 3 failed / 0 skipped, 31 casos.
Dos fallos: el hook de sustitucion del origen intentaba escribir cuando FileShare.Read
seguia retenido; se movio a DisposeAsync del origen, despues de cerrar el handle.
Ahora se comprueba que la sustitucion ocurrio y se reciben los bytes anteriores.
El tercero intentaba SupersedeBy sobre Pending; fixture corregido a Failed, estado
reemplazable, sin relajar dominio. No fueron defectos productivos corregidos.

El doble fallido retenia dos copias al lanzar antes de devolverlas. Tras terminar
la corrida se identificaron por rutas exactas, tiempos de sus casos, tamanos y
hashes de fixtures (294 bytes JPEG y 120 PNG); se eliminaron solo esos archivos y
directorios vacios propios. Evidencia: preflight-owned-spool-cleanup.csv. El teardown
del doble ahora dispone tambien sus copias si falla la instrumentacion. No barrido.

Se agregaron cuatro comparaciones workspace HTTP antes/despues, y fallo controlado
de transporte ademas de exito/cancelacion. Rebuild completo build3.log:
**0 errores / 111 warnings**, sin diagnosticos en archivos nuevos. La diferencia
con el incremental no es una correccion de warnings historicos.

Final: **100 passed / 0 failed / 0 skipped**, 4.2991 minutos, results/final.trx.
Incluye 36 casos A2, 13 unitarios workspace, 37 primitivas, diez identidad/migracion
A1 y cuatro HTTP de intake/replay/foto/historico. No sumar los 31 preliminares.
Filtro/comando en final-filter.txt y run.ps1; nombres individuales en final-cases.csv.
No fuente productiva/test cambio despues de build3. build3-sources.csv registra
los hashes de esas fuentes; despues solo se completo este documento.

| Criterio | Evidencia ejecutada |
|---|---|
| A: permisos/no filtracion | Siete casos HTTP owner/Admin/ajeno/inexistente/anonimo/token invalido/inactivo; rechazos con cero aperturas y cero consultas de foto, 404 equivalentes, no-store |
| B: ausencia/correspondencia | Manual, historico, foto trasladada a otro proyecto, ambiguedad y Client cruzado; sin First arbitrario; replacement con foto estable; actividad Project/Client no bloquea lectura |
| C: identidad | JPEG/PNG exactos, headers, Range/If-None-Match ignorados, origen sustituido tras cerrar, una apertura; ausente/alterado/corto/largo/IO/query con 500 seguro y sin imagen parcial |
| D: recursos | Barreras de escritura prueban plaza ocupada durante respuesta lenta; tercero espera sin abrir storage; cancelacion de espera no dispone ganadores; exito, cancelacion y fallo de transporte drenan/disponen; captura cancelada cierra origen, permite dos capturas posteriores y NOWAIT externo |
| E: sin efectos | Snapshots independientes de enlaces/evidencia/referencias/Project/Client, interceptor cero DML; GET nunca llama writes/deletes; workspace HTTP identico antes/despues; intake/replay Pending y cero jobs/attempts/evidencia/AI2 automaticos |

Los seis casos de metadatos invalidos alteran solo el objeto detached retornado
por un doble: DB CHECK impediria esos valores. No se apagan constraints. Las
pruebas de transporte usan streams controlados del resultado HTTP, no simulan una
caida fisica de red. No sleeps como prueba de orden; los timeouts del harness son
guardas contra hangs, no promesas de latencia productiva.

## Inventario e integridad

Existentes modificados:
- Api/Program.cs: middleware focal antes de auth.
- Application/DependencyInjection.cs: servicio de lectura.
- Infrastructure/DependencyInjection.cs: repositorio focal.
- docs/public-experience-idea-photo-a1.md: procedimiento operativo autorizado.

Nuevos:
- Application/Projects/GetProjectIdeaPhoto/ProjectIdeaPhoto.cs.
- Application/Projects/GetProjectIdeaPhoto/GetProjectIdeaPhotoService.cs.
- Infrastructure/Persistence/Repositories/ProjectIdeaPhotoRepository.cs.
- Api/Controllers/ProjectIdeaPhotoController.cs.
- Api/ErrorHandling/ProjectIdeaPhotoHeaders.cs.
- Tests/Api/Integration/ProjectIdeaPhotoReadTests.cs.
- Tests/Api/Integration/ProjectIdeaPhotoResourceTests.cs.
- docs/public-experience-idea-photo-a2.md.

Baseline: feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79, 889 rutas; no se usa HEAD como baseline
del contenido previo sin commit. Manifiestos initial.csv/final.csv: 889/897 rutas,
cuatro existentes modificadas, ocho nuevas, cero ausentes y 885 anteriores
byte-identicas. delta.csv coincide exactamente con el inventario anterior.
Rama/HEAD preservados; estado final: 55 tracked modificados, 121 untracked,
cero staged. Frente a build3-sources.csv solo cambio este informe.
Migraciones/configuraciones/modelo conservados; test de modelo sin
pendientes aprobado. Sin commit/push/merge/despliegue ni cambios de Frontend.

Las 45 raices registradas en TRX final estan ausentes (storage-check.csv); cero
sng-sealed-* restantes al comprobar despues de la corrida. final-remaining.txt
registra cero bases fixture; final-server-status.txt confirma no server running.
Cluster parado y logs retenidos. Variables child-local retiradas en finally.
No limpieza de destinos productivos ni DB habitual; no proveedor real.

## Limites y siguiente bloque

Solo Windows ejecutado. No prueba de hosting/proxy, revocacion atomica DB/red,
permisos ante proceso privilegiado, crash fisico, fsync ni exactly-once distribuido.
El lector interno y su disposal no tienen contrato de concurrencia hostil.
No se afirma que la API habitual ya sirva A2: no se reinicio ni desplego aqui.
El envio antiguo con 500 sigue pendiente independiente; no se repara automaticamente.

Veredicto: **FOTO A2 listo para revision en el alcance probado**. Siguiente bloque:
FOTO A3, exclusivamente visualizacion Frontend. Sin thumbnails, URLs publicas,
edicion/eliminacion, publicacion v2 o extraccion automatica.
