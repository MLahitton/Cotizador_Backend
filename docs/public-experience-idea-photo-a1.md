# FOTO A1: recepcion y persistencia de fotografia opcional

Fecha: 2026-10-23. Estado: A1 implementado y verificado en el alcance descrito.
Solo backend. No GET, URL, thumbnail, UI, Contexto, edicion o eliminacion de foto.
No publicacion v2, extraccion, jobs, worker, enqueue ni retry automatico.

## Preflight y alcance real

Baseline del worktree, no de HEAD: feature/public-experience-intake,
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 877 archivos no ignorados,
48 tracked modificados, 101 untracked y cero staged.
Artefactos: `C:/Users/USUARIO/AppData/Local/Temp/sng-photo-a1`.
initial.csv registra SHA256 por ruta; initial-status/head/branch conservan Git.
Se leyeron controlador, intake, repositorio, dominio, EF, storage y fixtures.
No instrucciones AGENTS adicionales encontradas en el backend.

Se confirmaron el rechazo anterior de partes distintas de files, limite anterior
de 101 MiB, captura documental y SaveVerifiedAsync, unicidad de SubmissionId y
preparacion basada exclusivamente en Submission.Files. No existia entidad de foto.

Experience remoto contrastado mediante lectura del commit aprobado
afc4bddb259967d3503fa8f41daf36b4b0b14621 en Steel-And-Glass/SnG-Experience-Frontend:
idea-photo-policy.ts admite jpg/jpeg/png, 20 MiB y MIME vacio; submit-experience.ts
agrega ideaPhoto fuera de payload.files. No se modifico ese repositorio.
El backend tambien admite application/octet-stream segun el contrato aprobado,
aunque la validacion UX remota es mas estrecha. No se amplian MIME documentales.

La API local se identifico en ejecucion como PID 16372, bin/Debug/net10.0/Api.exe.
No fue detenida, reiniciada ni usada por los tests. Build separado en PhotoA1.
No se consulto ni migro la DB habitual. No se llamo a AI2/Gemini real.
Una observacion posterior encontro PID 35924, creado a las 10:41:22 locales,
bajo dotnet run. Ese cambio no procede de un comando de esta implementacion;
no se afirma continuidad de PID ni se atribuye una causa externa no comprobada.

## Recepcion e identidad

Multipart: un payload, files documentales y cero o una ideaPhoto. Partes de archivo
desconocidas, fotos duplicadas y campos adicionales se rechazan. La foto nunca
sustituye los documentos obligatorios y no aparece en Submission.Files.

Limites documentales intactos: cinco archivos, 20 MiB individual, 100 MiB total;
payload 256 KiB. RequestSizeLimit y MultipartBodyLengthLimit del endpoint pasan a
121 MiB = 126877696 bytes; no se aumenta un limite global. Una cabecera Content-Length
superior se rechaza antes de leer el body. BadHttpRequestException 413 conserva
ProblemDetails. Multipart estructuralmente invalido mantiene el error 400 existente.
Un filtro de recurso exclusivo de esta accion retira FormValueProviderFactory,
FormFileValueProviderFactory y JQueryFormValueProviderFactory: la accion ya lee el
multipart explicitamente y debe poder mapear errores antes de model binding.
Sin ese filtro, MVC interceptaba el exceso y devolvia 400 antes del guard de 413.
Un cuerpo incompleto que produce BadHttpRequestException 400 mantiene error seguro.

La foto requiere nombre seguro con las mismas reglas documentales, extension
jpg/jpeg/png sin distinguir mayusculas, longitud positiva <=20971520 bytes,
contenido JPEG/PNG y coherencia de extension/MIME. MIME vacio u octet-stream
se resuelve desde el contenido; image/jpeg o image/png debe coincidir. Se persiste
solo el MIME canonico detectado. No se deriva una ruta del nombre recibido.

Errores especificos, sin rutas ni mensajes nativos:
- PUBLIC_EXPERIENCE_PHOTO_INVALID: 400, vacio, corrupto, truncado, nombre inseguro,
  longitud declarada discrepante, animacion o limites de decodificacion.
- PUBLIC_EXPERIENCE_PHOTO_TOO_LARGE: 413, foto mayor de 20 MiB.
- PUBLIC_EXPERIENCE_PHOTO_UNSUPPORTED: 415, extension/MIME no admitido o incoherente.
- PUBLIC_EXPERIENCE_REQUEST_TOO_LARGE: 413, request superior a 121 MiB.

El algoritmo PayloadFingerprint no cambia: payload canonico mas digests binarios
documentales ordenados. Ningun campo de foto entra en ese hash.
Replay compara ese fingerprint, presencia/ausencia y descriptor v1 de foto:
SHA256 ASCII MAYUSCULA y longitud observada. Nombre/MIME no son identidad.
Ambos caminos de reconocimiento (lectura inicial y perdedor del indice unico)
usan esa comparacion. Una foto invalida se rechaza antes de buscar replay.
Mismos bytes y nombre valido diferente conservan ID, key, nombre, MIME y fechas
originales. Agregar/quitar/cambiar foto da el conflicto 409 existente.
Historico sin foto repetido sin foto conserva su comportamiento; no hay reparacion.

## Decodificador y licencia

Dependencia: Magick.NET-Q8-AnyCPU 15.0.0, con target net10.0 y nativos empaquetados.
NuGet identifica publicacion del 2026-10-10; Notice.txt identifica ImageMagick
7.1.2-33. Magick.NET usa Apache-2.0; ImageMagick permite uso comercial con avisos.
El paquete tambien contiene componentes LGPL: no se presenta como Apache puro.
El usuario confirmo expresamente uso SOLO como servicio alojado propio. No se
aprueba aqui entrega de binarios/contenedores a terceros. Esa distribucion futura
requiere revisar las obligaciones de todos los componentes, no solo del wrapper.
No se instala Ghostscript, FFmpeg ni un servicio externo. No hay licencia comercial
de pago asumida para este uso. Notice.txt completo se copia a
ThirdPartyNotices/Magick.NET.txt en output/publish; comprobado en Api/bin/PhotoA1.

Fuentes oficiales consultadas:
- https://www.nuget.org/packages/Magick.NET-Q8-AnyCPU/15.0.0
- https://github.com/dlemstra/Magick.NET/blob/main/License.txt
- https://github.com/dlemstra/Magick.NET/blob/main/docs/ReadingImages.md
- https://github.com/dlemstra/Magick.NET/blob/main/docs/Initialization.md
- https://imagemagick.org/security-policy/
- Notice.txt y XML de API del paquete restaurado 15.0.0.

Se evaluo SkiaSharp 4.153.1 fuera del repositorio y se descarto como validador
unico: GetPixels devolvio Success para PNG con cierre truncado. La prueba temporal
contrasta los mismos bytes con Magick.NET, que los rechaza. JPEG truncado puede
emitir advertencia y devolver imagen: por eso CUALQUIER advertencia rechaza la foto.

## Limites del decodificador

- Dimension maxima: 8192 por eje. Maximo: 16000000 pixeles, comprobado antes
  de decodificar y nuevamente despues. Se prueban 8192x1 y 4000x4000 validos.
- Captura comprimida privada: hasta 20 MiB por foto. SemaphoreSlim estatico de
  dos plazas por proceso, adquirido antes de reservar el buffer y retenido hasta
  DisposeAsync del propietario. No es un limite distribuido entre replicas.
- Cache nativa de pixeles: Memory=256 MiB; umbral Area=128 MiB; Disk=0 para no
  desbordar la cache a archivos. El limite geometrico NO se delega a Area.
  MaxProfileSize=20 MiB, Thread=1, ListLength=2 (el valor 1 rechaza incluso una
  imagen estatica en esta version). Estas opciones afectan esta biblioteca a nivel
  de proceso; no existian otros consumidores de Magick.NET en el repositorio.
- Dos buffers propios suman como maximo 40 MiB. La cache nativa, copias del codec,
  perfiles y overhead son adicionales. No se afirma un techo duro del RSS completo.
  El buffering multipart de ASP.NET tampoco pertenece a ese presupuesto.
- JPEG/PNG se seleccionan mediante firma y se pasan como codec explicito. La firma
  NO valida la imagen. Ping inspecciona conjunto/dimensiones; Read decodifica todos
  los pixeles de la imagen estatica, sin reducir escala. No se usa Ping como prueba
  de contenido. Errores y advertencias nativos se traducen a error seguro.
- Solo imagen estatica: conjunto distinto de uno y marcador PNG acTL se rechazan.
  El test APNG construye dos frames con chunks y CRC validos a partir del PNG literal;
  ese generador es exclusivamente fixture, no un decoder productivo.
- Cancelacion durante espera y copia usa el token; el progreso del decode consulta
  ese token y se vuelve a comprobar despues de Ping/Read. No existe timeout duro
  ni interrupcion garantizada de una llamada nativa. No se abandona una tarea de
  lectura con Task.WhenAny mientras se disponen sus recursos. Los 60 s del harness
  de diagnostico NO son un timeout productivo.

Validar contenido significa completar el decode de pixeles bajo esas reglas y
rechazar advertencias/errores. No es antivirus, prueba de autenticidad, auditoria
universal de metadatos ni garantia contra vulnerabilidades del codec. Solo Windows
se ejecuta; no se certifican Linux, hosting ni limites de proxies desplegados.

## Captura, almacenamiento y recursos

El upload se abre exactamente una vez y se dispone. Una lectura acotada exige la
longitud declarada y EOF; no consulta Seek/Length del origen. El byte[] privado no
se exporta. Hash, inspeccion nativa y almacenamiento usan esa misma captura estable.
Lectores de almacenamiento son MemoryStream no escribibles, sin buffer publico.
Se permiten vistas controladas nuevas de esta captura; no se cambia ni se relaja
el lector unico de SealedContentCopy documental.

SaveVerifiedAsync recibe longitud observada y limite; hash/longitud del recibo deben
coincidir antes de crear la fila. La fecha UTC se toma despues del recibo valido.
Se guardan los bytes originales, nunca la imagen decodificada o reencodificada.
OriginalFileName/MIME son metadatos; la key usa IDs generados por backend.

Lectores se drenan/disponen antes del propietario; no se ofrece lectura/disposal
concurrente ni proteccion frente a consumidores hostiles. Dispose es idempotente,
libera buffer propio y plaza; no borra almacenamiento. Fotos invalidas no escriben
negocio ni destinos. Fallos posteriores retienen todos los destinos completados o
inciertos, incluidos perdedores concurrentes. No compensacion ni limpieza general.

## Persistencia y limites por capa

PublicExperienceIdeaPhoto separado, FK a Submission; Project no cambia. Ausencia
es ausencia de fila. Campos: Id, SubmissionId, OriginalFileName, ContentType,
StorageKey, StoredSha256, StoredLengthBytes, StoredEvidenceAtUtc,
StoredEvidenceVersion=1 e IdentityVersion=1. Longitud observada sirve tambien al
descriptor; no se duplica una longitud declarada que ya se exigio igual al capturar.
Las dos versiones describen contratos distintos aunque inicialmente valgan uno.

Dominio valida forma y coherencia, con constructor privado y setters privados.
AttachIdeaPhoto impide sustitucion y cruce de Submission. No hay API de edicion.
EF Throw protege todos los campos persistidos. PostgreSQL exige NOT NULL, CHECK
de IDs/versiones/digest/longitud/MIME, FK RESTRICT y unicos SubmissionId/StorageKey.
No trigger: SQL directo autorizado puede cambiar una fila valida. No se atribuye
inmutabilidad universal a EF ni atomicidad filesystem/DB a SaveVerifiedAsync.

TryInsertAsync guarda Submission, documentos y foto en el mismo SaveChanges
transaccional y conserva la autoridad del indice unico de SubmissionId. FindAsync
carga la foto para comparar replay. Se desacoplan del tracking todas las filas
propias en finally. Copia/decode/storage siguen fuera de transacciones de negocio.
No se agrega observador generico: ante excepcion de commit se propaga el fallo y
se conservan destinos. Un retry HTTP explicito posterior compara la fila ganadora.
Comprobaciones del token original evitan exito tardio tras cancelacion, incluso
despues de commit real; no afirman rollback ni atomicidad DB/respuesta HTTP.

Migracion 20261023151342_AddPublicExperienceIdeaPhoto con Designer/snapshot.
SQL revisado: una tabla, dos indices unicos y registro de EFMigrationsHistory.
Sin ALTER de tablas anteriores, triggers, DML de negocio o backfill. Down elimina
solo la tabla nueva. Aplicada exclusivamente en bases temporales de fixtures.

## Evidencia de pruebas

Vectores literales PNG/JPEG proceden del probe externo; SHA256 PNG:
08A65B9D47394FAFE293CD5C94E91C3E8E0C78CE3BE6C1C8D014E59C0C3DF621.
PNG de 32x32, no texto renombrado. Pruebas nuevas cubren identidad, metadata,
captura, limites, APNG, truncamiento, cancelacion, disposal, recibo discrepante,
fallos antes/despues de escritura, rollback, commit real con respuesta perdida,
cancelacion posterior, constraints y diez mutaciones EF con cero UPDATE y relectura.
Tres carreras de foto usan conexiones/PIDs distintos y WaitBlocked consulta
pg_stat_activity/pg_blocking_pids antes de liberar el ganador. No sleeps como prueba.

HTTP usa Kestrel real loopback, no la API de la demo ni proveedor real. Se comprueban
foto valida, error integral, exceso individual y request Content-Length >121 MiB;
este ultimo rechaza antes de enviar body, no simula 121 MiB transferidos por un proxy.
Sin foto: creacion, consulta de workspace por el servicio real, Requirement Pending,
replay con enlaces estables e historico sin foto despues de migrar. Cero jobs,
attempts, evidencia de ejecucion y AI2 automaticos. Consulta workspace se prueba
con repositorios reales, no como nuevo GET HTTP de imagen ni navegador frontend.
El host HTTP usa su doble de storage existente con recibos obtenidos de filesystem
real; las pruebas de incorporacion/fallos por separado usan LocalFileStorage real
y releen los bytes por StorageKey desde otra instancia de DbContext.

Corridas preliminares, no sumables:
- build1: dos errores por namespace de DI; corregidos.
- capture.trx: 15 passed / 7 failed / 0 skipped; limite ListLength incorrecto.
- preflight.trx: 43/0/0 despues de corregirlo.
- second.trx: 58/0/0 con constraints, EF, migracion y commit faults.
- final-build.log: 0 errores/112 warnings; CS8604 nuevo del doble corregido despues.
- final-tests.log: corrida interrumpida al fallar fixture de 4000x4000; PowerShell
  ErrorActionPreference=Stop sobre stderr impidio drenaje normal. Se detuvieron SOLO
  testhost PhotoA1 29132 y su hijo 27816. El finally paro PostgreSQL temporal.
- decoder-isolation.trx: 24/1/0; reproduce cache insuficiente para el limite valido.
  Se separo el umbral nativo Area del guard explicito de pixeles y se corrigio el
  harness para registrar stderr sin abandonar procesos. No se relajo el limite.
- verification.trx: 187/0/0, 5.7465 minutos, rebuild 0 errores/111 warnings.
- closure-build.log: un error de interpolacion en el test HTTP nuevo; corregido.
- closure2.trx: 188/1/0, 7.0517 minutos. Fallo real: HTTP >121 MiB recibia 400 por
  lectura anticipada de MVC. Se corrigio el filtro de recurso, sin rebajar assertion.
- http-final.trx: 47/0/0, 4.2775 minutos. Repite la clase HTTP completa y sus unitarios,
  corrige el caso anterior y agrega regresiones manuales justificadas por el filtro.

- http-boundaries-preliminary.trx: 24/1/0. El harness cerraba el socket tras
  declarar 121 MiB sin enviar cuerpo y esperaba 400; Kestrel cerro sin respuesta.
  No demostraba el comportamiento de un request completo. Se sustituyo por un
  multipart completo de exactamente 121 MiB con parte desconocida: devuelve 400,
  no 413, sin negocio ni storage. El nombre historico del caso conserva
  TruncatedBodyRemains400, pero la prueba final ya no corta el socket.
- http-boundaries.trx final: 25/0/0, 1.3072 minutos. Incluye el caso anterior,
  rechazo >121 MiB, foto valida/invalida, historico, dos intake/replay sin foto
  y los doce unitarios HTTP. last-build.log: rebuild PhotoA1, 0 errores/111 warnings.

Reconciliacion por nombres individuales en final-cases.csv: **213 casos distintos
con ultimo resultado Passed, cero Failed y cero Skipped**. No fue una unica corrida
de 213: 165 quedan respaldados por closure2.trx, 23 por http-final.trx y 25 por
http-boundaries.trx. all-final-executions.csv conserva los solapamientos y el fallo
original de closure2; no se borra ni se cuenta como pasado en esa corrida.
No fuentes productivas o tests cambiaron despues de last-build; solo este informe.
Los filtros/comandos, SQL, builds y TRX quedan bajo el artifact root. No toda la suite.

## Limpieza e integridad final

PostgreSQL 17 propio, loopback 55475, usuario audit, data bajo artifact root.
Solo migraciones de fixtures cotizador_backend_test_*. La corrida interrumpida
dejo una base propia; cleanup.ps1 comprobo SHOW data_directory contra el directorio
del cluster, valido su nombre y la elimino en ese cluster. remaining-databases.txt
registra cero fixtures; server-status.txt confirma no server running. Cluster parado
y logs retenidos, no servicio activo. Variables de conexion de los hijos retiradas.
No DB habitual ni proveedores reales. No scanner ni borrado de huerfanos productivos.

storage-check.csv identifica 49 rutas distintas en los TRX finales: todas ausentes.
La foto usa memoria privada acotada, no temporales propios en disco; los tests
comprueban disposal y liberacion de slots. La limpieza de multipart corresponde al
hosting y no certifica ausencia universal de residuos tras crash.

initial.csv/final.csv: 877/889 rutas, diez existentes modificadas, doce nuevas,
cero ausentes y 867 anteriores byte-identicas. El inventario siguiente explica todo
el delta. Rama/HEAD preservados; trabajo anterior no revertido. Bin/obj PhotoA1
son artefactos ignorados. git diff --check pasa con avisos LF/CRLF. La API observada
al cierre sigue siendo PID 35924; no fue detenida ni reiniciada por este trabajo.

Veredicto: **APTO para FOTO A1 en el alcance probado**. La demo sin foto conserva
creacion/consulta, Pending y replay estable en los fixtures HTTP y repositorios
reales. Esto no afirma despliegue ni smoke contra la DB habitual: la API activa
no ha sido actualizada por esta implementacion. La habilitacion local requiere
el orden siguiente: migracion, API compatible y smoke sin foto. No se cierra
lectura autorizada, Contexto ni integracion visual completa. Solo Windows probado;
sin garantia de timeout duro nativo, limites del proxy desplegado o crash fisico.

## Inventario focal

Existentes modificados respecto de initial.csv:
- Api/Controllers/PublicExperienceSubmissionsController.cs
- Application/PublicExperience/PublicExperienceIntakeModels.cs
- Application/PublicExperience/PublicExperienceIntakeService.cs
- Application/PublicExperience/PublicExperiencePayloadValidation.cs
- Domain/PublicExperience/PublicExperienceSubmission.cs
- Infrastructure/DependencyInjection.cs
- Infrastructure/Infrastructure.csproj
- Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs
- Infrastructure/Persistence/Repositories/PublicExperienceSubmissionRepository.cs
- Tests/Api/Integration/PublicExperienceHttpPostgreSqlTests.cs

Nuevos:
- Application/PublicExperience/IdeaPhotoCapture.cs
- Domain/PublicExperience/PublicExperienceIdeaPhoto.cs
- Infrastructure/Storage/IdeaPhotoCapture.cs
- Infrastructure/Persistence/Configurations/PublicExperienceIdeaPhotoConfiguration.cs
- Infrastructure/Persistence/Migrations/20261023151342_AddPublicExperienceIdeaPhoto.cs
- Infrastructure/Persistence/Migrations/20261023151342_AddPublicExperienceIdeaPhoto.Designer.cs
- Tests/Infrastructure/Persistence/IdeaPhotoCaptureTests.cs
- Tests/Infrastructure/Persistence/IdeaPhotoPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/IdeaPhotoPersistencePostgreSqlTests.cs
- Tests/Api/Integration/IdeaPhotoHttpPostgreSqlTests.cs
- Tests/Domain/PreQuotes/IdeaPhotoModelTests.cs
- docs/public-experience-idea-photo-a1.md

## Actualizacion local posterior, NO ejecutada

El usuario reporto posteriormente migracion A1 aplicada y un envio local exitoso.
Es evidencia reportada por el usuario, no una comprobacion de FOTO A2. El envio
antiguo con 500 sigue siendo un pendiente operativo separado; A2 no lo consulta
ni lo repara y no vuelve a aplicar migraciones ni reinicia la API habitual.

1. Identificar instancia/build, conexion efectiva y backup. Inspeccionar TODO el
   historial __EFMigrationsHistory y la existencia de la tabla antes de ejecutar.
   Si A1 ya esta aplicada, no reaplicarla. Si hay migraciones posteriores, NO
   apuntar al target A1: podria hacer downgrade. Si historial y esquema divergen,
   detener y diagnosticar sin borrar historial ni ejecutar SQL a ciegas. Comparar
   historial con migraciones del MISMO build compatible, sin IDs desconocidos.
   SOLO si las pendientes constituyen avance validado hasta A1, revisar SQL forward
   y aplicar con conexion explicita a la DB local verificada:
   `dotnet ef database update 20261023151342_AddPublicExperienceIdeaPhoto --project Infrastructure --startup-project Api --configuration PhotoA1 --no-build`.
   No inferir version/compatibilidad de la API activa porque una tarea no la reinicio.
2. Solo despues de verificar esquema compatible, sustituir voluntariamente la instancia identificada
   por la API compatible: `dotnet run --project Api --configuration PhotoA1 --no-build --launch-profile http`.
   El perfil local existente usa http://localhost:5042. No iniciar una segunda
   instancia en ese puerto mientras la anterior siga activa.
3. Antes del smoke nuevo, conservar y comprobar el SubmissionId del envio con 500,
   su payload/bytes/presencia de foto originales, estado, enlaces y archivos. Un 500
   no prueba rollback ni ausencia de persistencia. No borrar ni recrear su intencion
   con otro ID. Tras comprobar el estado, reenvio explicito con el MISMO ID/entrada:
   replay 200 si confirmado; 201 posible si se demostro ausencia. Un 409 requiere
   comparar entrada original, no sobrescribir ni cambiar ID para evitar el conflicto.
   Estados inconsistentes requieren diagnostico separado, no reparacion automatica.
4. Solo despues de resolver el envio anterior, smoke SIN ideaPhoto con SubmissionId nuevo,
   payload valido y documento obligatorio:
   esperar 201/RECEIVED, abrir el proyecto en Cotizador, comprobar Pending; repetir
   exactamente el envio y esperar 200 con los mismos links; abrir un proyecto historico
   sin foto y comprobar que no se condiciona su consulta a lectura de imagenes.
5. Solo entonces probar recepcion con foto. A1 no incluye mostrarla: lectura autorizada
   y Contexto son bloques posteriores. No activar extraccion como parte de este smoke.

No commit, push, merge, despliegue, reinicio de API ni migracion habitual ejecutados.
