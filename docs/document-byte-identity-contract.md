# 2E.2D.2c: contrato de identidad de bytes para extraccion

Estado original: DISENO PROPUESTO, 2026-10-22. Las secciones 1-9 conservan ese diseno.
Actualizacion: D.2c.1 (seccion 10) incorpora primitivas; D.2c.2 (seccion 11) incorpora
evidencia persistida y cuatro recorridos privados. D.2c.3a (seccion 12) incorpora captura
unica y evidencia del intake publico. D.2c.3b (seccion 13) incorpora preparacion publica
verificada y E/S fuera de locks. D.2c.4a (seccion 14) agrega exclusivamente el contrato
y modelo persistente de evidencia por ejecucion. D.2c.4b (seccion 15) agrega preparacion y
registro explicitos, sin registro DI ni consumidores productivos. D.2c.5a (seccion 16) incorpora
consumo local de una oportunidad, autorizacion viva y resultado asociado, sin activacion ni publicacion.
Actualizacion D.2c.5b (seccion 17): publicacion v2 explicita y version exigida por job,
aprobadas separadamente, sin consumidores productivos ni activacion.
Actualizacion D.2c.6a (seccion 18): coordinador explicito de una ejecucion v2 ya
asignada, sin worker, registro DI, endpoints ni consumidores productivos.
Publicacion v2 y RequiredPublicationVersion estan aprobadas solo en el alcance de la
seccion 17. Las restantes propuestas historicas NO quedan aprobadas por documentarse
aqui, incluidos triggers, OBSERVED_NOW, backfill, recuperacion y activacion.
Alcance: decisiones implementables para una futura secuencia acotada; no habilita jobs,
worker, enqueue, retry, recovery ni procesamiento automatico. D.2a y D.2b conservan su
evidencia historica en document-file-safety.md y document-transaction-coordination.md.
No se repite su auditoria. CreatePreQuoteDocumentService y limpieza de huerfanos excluidos.

## 1. Hechos contrastados en el codigo actual

Referencias relativas al repositorio; lineas corresponden a la inspeccion anterior a D.2c.1.

| Metodo / fuente | Bytes recibidos o abiertos | Evidencia actual | Cambio minimo propuesto |
|---|---|---|---|
| IFileStorage.SaveAsync/OpenReadAsync, Application/Common/Abstractions/Storage/IFileStorage.cs:5 | Stream desde posicion actual / stream por clave | Save devuelve Task, no recibo de contenido | Agregar SaveVerifiedAsync con recibo; conservar SaveAsync para consumidores excluidos |
| LocalFileStorage.SaveAsync, Infrastructure/Storage/LocalFileStorage.cs:35 | Copia al temporal CreateNew, Flush, Move sin overwrite | Publicacion de archivo sin sobrescritura por esta API; no digest | Hash y contador sobre los mismos bloques escritos; recibo solo tras Move exitoso |
| LocalFileStorage.OpenReadAsync, misma fuente:133 | FileStream Read, FileShare.Read | No prueba historica de bytes ni disponibilidad entre replicas | Reutilizar para una sola apertura del origen por preparacion |
| CreateRequirementService.ExecuteAsync, Application/PreQuotes/CreateRequirement/CreateRequirementService.cs:219 | normalized.Content | SizeBytes declarado; RequirementFile sin digest | Guardar recibo de incorporacion junto con cada nueva referencia |
| ManageRequirementDocumentsService Add/ReplaceDocument/ReplaceRequirement, Application/PreQuotes/ManageRequirementDocuments/ManageRequirementDocumentsService.cs:141,304,497 | Stream normalizado, almacenamiento antes del lock documental | Referencias y limites revalidados al commit; D.2a retiene destinos inciertos | SaveVerifiedAsync y campos de evidencia en la misma transaccion que la referencia |
| PublicExperienceIntakeService.ExecuteAsync/HashFileAsync, Application/PublicExperience/PublicExperienceIntakeService.cs:18,101 | upload.OpenRead para hash y otra apertura para guardar | SHA256 + longitud comprobada en primera lectura; no prueba de igualdad con segunda | Una copia privada por upload; fingerprint y almacenamiento desde ella; conservar algoritmo de replay |
| PreparePublicExperienceRequirementService.PrepareAsync, Application/PublicExperience/PreparePublicExperienceRequirementService.cs:62 | Una apertura del intake; copia MemoryStream y hash de los mismos bloques; Save desde copia rebobinada | Compara SizeBytes y ContentHash de intake; RequirementFile pierde el hash | Persistir recibo nuevo del destino; preparar E/S fuera del lock |
| PublicExperienceRequirementPreparationRepository.PrepareLockedAsync, Infrastructure/Persistence/Repositories/PublicExperienceRequirementPreparationRepository.cs:17-34 | Callback anterior incluye toda la copia bajo lock Submission | Savepoint y links atomicos, replay de links existentes | Separar lectura/copia y commit con revalidacion; no mantener este callback largo |
| ProcessRequirementCoreService, Application/PreQuotes/ProcessRequirement/ProcessRequirementCoreService.cs:316-349,695 | Abre cada RequirementFile una vez; request usa esos streams y SizeBytes declarado | Sin hash de extraccion; finally dispone streams (433) | Adaptacion legacy posterior explicita; no atribuir garantia durable al core actual |
| CotizadorAi2DocumentProcessingClient.ProcessAsync/CreateRequest, Infrastructure/DocumentProcessing/CotizadorAi2DocumentProcessingClient.cs:38,352 | StreamContent(NonDisposingStream(file.Content)) | Un SendAsync; no apertura de rutas, no rewind propio | Pasar streams sellados posicion cero y longitud observada, sin cambiar protocolo remoto |
| Infrastructure/DependencyInjection.cs:138-151 | HttpClient AI2 con timeout externo al HttpClient | No politica retry registrada en esa cadena inspeccionada | Mantener un envio explicito; pruebas contra retries/lecturas adicionales |
| AssignedJobStartStore.RunAsync, Infrastructure/Persistence/Repositories/AssignedJobStartStore.cs:122-148 | Solo metadatos de archivos | Attempt/job/Requirement atomicos y lease final; no bytes | Conservar inicio; registrar evidencia despues en transaccion corta independiente |
| PrepareJobPublicationService.PrepareAsync, Application/PreQuotes/ProcessRequirement/PrepareJobPublicationService.cs:24-46 | Respuesta y manifest del caller | Valida attempt/documentId y orden; no abre archivos | Preparacion v2 solo desde resultado asociado a una copia/evidencia registrada |
| PublicationFile, Application/PreQuotes/ProcessRequirement/PublicationInput.cs:13 | FileId, nombre, MIME, SizeBytes, StorageKey, CreatedAtUtc | Todos son identidad/metadatos; ninguno prueba contenido | Mantener v1; manifest v2 con evidencia de contenido por archivo |
| JobPublicationStore.RunAsync, Infrastructure/Persistence/Repositories/JobPublicationStore.cs:94-106,123-163 | Compara manifest DB; guarda version/hash del contrato | Hash canonico del resultado preparado, NO hash de archivos | Version v2, correspondencia con evidencia persistida y guard final obligatorio |

RequirementFile (Domain/PreQuotes/Requirement.cs:321) no tiene hash. El recibo del job
guarda PublicationContractVersion/PublicationContentHash: SHA256 de la representacion
canonica de PreparedJobPublication, no de jsonb releido ni de los bytes enviados.

Intake: PayloadFingerprint = SHA256(canonical payload || digests binarios en orden de
uploads). PublicExperiencePayloadValidation.Canonicalize conserva su contrato actual;
nombre, MIME, tamanos, orden y respuestas pertenecen al payload canonico. ContentHash se
persiste por SubmissionFile y la identidad publica SubmissionId decide replay/conflicto.
Se preservan exactamente canonicalizacion, orden, concatenacion, mayusculas y 200/201/409.
No se agregan campos de evidencia al payload canonico ni se reescriben fingerprints viejos.

Desde almacenamiento hasta envio actual hay UNA apertura por archivo en el core, no dos;
el gap no es una reapertura del cliente sino ausencia de comparacion y copia estable.
Hash y guardado en intake SI son dos lecturas distintas. En preparacion publica son los
mismos bloques de una copia, pero Save no devuelve evidencia persistible del destino.
No se inspecciono infraestructura remota ni se afirma ausencia de reenvios externos.

## 2. Decision recomendada y limites de prueba

Conservar LocalFileStorage; introducir dos componentes focales:

1. SaveVerifiedAsync(key, stream, expectedLength, ct) -> StoredContentReceipt.
   En LocalFileStorage, un bucle lee, limita, hashea y escribe los mismos bloques en el
   temporal privado. Rechaza longitud distinta/no positiva y limites antes de promover.
   SHA256/longitud pertenecen a los bytes escritos, no a una segunda lectura del upload.
   Flush y Move sin overwrite preceden al recibo. No afirmar fsync/disponibilidad durable.
   SaveAsync existente conserva firma/semantica para rutas fuera de alcance.
2. PrepareExecutionDocuments -> VerifiedExecutionDocuments (IAsyncDisposable): abre cada
   origen una vez y copia a un spool privado calculando SHA256/longitud. Verifica TODO el
   conjunto antes de cualquier proveedor. Ningun origen se reabre para enviar.

Spool preferido: archivos temporales privados por preparacion con CreateNew, handles
retenidos, sin compartir escritura; acceso de lectura al consumidor mediante wrapper
no escribible, no exportar rutas ni handles de escritura. Copiar, flush, rebobinar el
mismo handle, sellar y exponer solo lectores. No cerrar/reabrir por nombre entre hash y
envio. El propietario no vuelve a escribir tras sellar; el objeto es de uso unico y no
admite envios concurrentes. La proteccion real requiere directorio privado/permisos del
proceso; FileShare por si solo no es una garantia portatil contra un actor privilegiado.
Pruebas OS/filesystem y configuracion de permisos son requisitos, no hechos desplegados.

Preferido sobre MemoryStream para el conjunto entero: consumo de RAM acotado por buffer,
no proporcional al total de documentos. Se reutiliza un pequeno helper privado de copia,
no un framework de almacenamiento. No se necesita nuevo proveedor/cloud/cola ni CAS.
Hash sobre origen y luego envio del origen se descarta por TOCTOU. Hash solo durante envio
se descarta porque descubre diferencias DESPUES de iniciar proveedor. Reabrir el spool
por ruta se descarta por sustitucion entre verificacion y envio.

La garantia es: bajo el modelo de permisos/proceso declarado, los cuerpos documentales
ofrecidos por el cliente son los bytes de la copia verificada. No es un recibo firmado
del proveedor ni prueba de que el servicio remoto consumio todos los bytes ante un fallo
de red. Multipart/fronteras/cabeceras no forman parte del digest documental. Un digest no
prueba autoria, autenticidad historica, calidad de extraccion o disponibilidad futura.

## 3. Fuentes de verdad y esquema propuesto (sin migracion generada)

SHA256: algoritmo fijo `SHA256`, representacion hexadecimal ASCII MAYUSCULA de 64 caracteres.
Longitudes bigint positivo, UTC timestamptz. UUIDs no vacios. No hashes de nombres/rutas.

### A. Evidencia de incorporacion

Agregar a RequirementFile: StoredSha256 varchar(64) nullable, StoredLengthBytes bigint
nullable, StoredEvidenceAtUtc timestamptz nullable, StoredEvidenceVersion smallint nullable.
Los cuatro son todos null (historico desconocido) o todos presentes (version 1). SizeBytes
existente sigue siendo longitud declarada; una incorporacion nueva exige igualdad con
StoredLengthBytes. No reinterpretar SizeBytes historico como contador observado.

PublicExperienceSubmissionFile conserva ContentHash y SizeBytes actuales; agregar
StoredLengthBytes, StoredEvidenceAtUtc, StoredEvidenceVersion con grupo nullable. Para
nuevos uploads ContentHash debe coincidir con el recibo de SaveVerifiedAsync y con la copia
usada en fingerprint. Version null mantiene significado anterior de ContentHash: evidencia
de lectura del upload, NO certeza de la segunda lectura guardada. No promover historicos.

Nacimiento: recibo de almacenamiento tras escritura verificada. Persistencia: junto a la
fila de archivo en la transaccion existente. Escritores: servicios autorizados de create,
add, replace-file, replacement, intake y preparacion publica; nunca el cliente publica un
digest confiable. Inmutables desde insercion. Sustituir archivo crea otra identidad, no
edita el hash. Domain factories y CHECK de formato/nullabilidad/longitud; trigger focal
impide UPDATE de los campos de evidencia (incluido null -> valor). No bloquear el DELETE
documental permitido antes de jobs. D.2b protege las referencias usadas por ejecuciones.
El recibo prueba lo escrito por esta ruta en ese momento; no que siga intacto para siempre.

### B. Evidencia por ejecucion

Tabla propuesta RequirementExecutionDocuments, header:

| Campo | Tipo / regla |
|---|---|
| Id | uuid PK, generado una vez al preparar conjunto |
| RequirementId, JobId, ExecutionRequestId, ProcessingAttemptId | uuid NOT NULL |
| LeaseGeneration | bigint NOT NULL positivo; generacion verificada |
| PrimaryDocumentId | uuid NOT NULL, primer archivo del orden canonico |
| ManifestVersion | smallint NOT NULL = 1 (independiente de publication v2) |
| ManifestHash | varchar(64) NOT NULL, SHA256 canonico del conjunto |
| VerifiedAtUtc, RegisteredAtUtc | timestamptz NOT NULL; registro usa reloj DB |
| ActorUserId | uuid NOT NULL FK usuario real; no RequestedBy inferido |
| FileCount | int NOT NULL positivo |

Detalle RequirementExecutionDocument:
EvidenceId uuid NOT NULL FK header; FileId uuid NOT NULL; RequirementId uuid NOT NULL;
Ordinal int NOT NULL >=0; ObservedSha256 varchar(64) NOT NULL; ObservedLengthBytes bigint
NOT NULL >0; ExpectedSha256 varchar(64) nullable; ExpectedLengthBytes bigint NOT NULL;
Basis varchar(32) NOT NULL (`INCORPORATION_V1` o `OBSERVED_NOW`);
OriginalFileName varchar(255), ContentType varchar(100), StorageKey varchar(500),
FileCreatedAtUtc timestamptz, todos NOT NULL, copiados del snapshot autorizado.
ExpectedLengthBytes conserva SizeBytes declarado; igualdad observada obligatoria.
INCORPORATION_V1 exige ExpectedSha256 y coincidencia; OBSERVED_NOW no inventa expected hash.

Unicidades: header JobId y ProcessingAttemptId unicos (un conjunto por ejecucion/attempt);
detalle PK(EvidenceId,FileId), UNIQUE(EvidenceId,Ordinal). FK compuestas respaldadas por
alternate keys: detalle(FileId,RequirementId)->RequirementFile(Id,RequirementId),
detalle(EvidenceId,RequirementId)->header(Id,RequirementId),
header(JobId,RequirementId,ExecutionRequestId,ProcessingAttemptId)->job con esa misma
clave candidata; attempt ligado al mismo Requirement mediante FK compuesta adicional.
Todas RESTRICT, sin cascadas que destruyan evidencia. PrimaryDocumentId debe pertenecer
al detalle: FK compuesta diferible al commit (Id,PrimaryDocumentId)->detalle(EvidenceId,FileId).
No agregar FK circular desde job al header: se localiza por JobId unico.

Domain/coordinador verifican ordinales contiguos 0..N-1, principal ordinal 0, conjunto
completo sin duplicados, metadatos exactos, contenido y hash. CHECK/FK/unique cubren forma
y pertenencia; constraints SQL ordinarios no prueban hash ni totalidad entre tablas.
Triggers focales impiden UPDATE/DELETE de header/detalles; no certifican un DBA privilegiado.
No filas parciales: header y detalles se insertan y validan en una sola transaccion.
El coordinador es unico escritor de estas tablas. No hay endpoint para que un usuario
registre hashes arbitrarios. DTOs construidos por copia/verificacion son internos.

Canonico del manifest: JSON UTF-8 sin BOM/espacios, propiedades en orden fijo:
version, requirementId, jobId, executionRequestId, processingAttemptId, leaseGeneration,
primaryDocumentId, files. files en ordinal; cada objeto: ordinal, fileId, originalFileName,
contentType, storageKey, fileCreatedAtUtc, expectedLengthBytes, expectedSha256, basis,
observedLengthBytes, observedSha256. UUID D minuscula, fechas UTC formato literal
`yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'` con precision microsegundos de DB, numeros enteros
invariantes, null explicito, escaping mediante Utf8JsonWriter y JavaScriptEncoder.Default,
Indented=false. No normalizar textos por semejanza ni truncar fechas despues de hasharlas.
Id del header y fechas operacionales no entran al hash; se incluyen luego en recibo v2.
Vector literal independiente obligatorio antes de consolidar esta representacion.

### C. Enlace con resultado

Publication v2 incluye EvidenceId, ManifestVersion, ManifestHash y manifest detallado,
ademas de las identidades Requirement/job/request/attempt/generation. Fuente de verdad:
header/detalles DB para lo verificado, recibo version/hash en job para lo publicado.
No se reconstruye evidencia desde respuesta AI2 ni desde jsonb de extraccion.
El resultado interno de envio conserva la referencia al objeto verificado/registrado;
PrepareV2 recibe ese resultado y no una lista libre de archivos del caller. Verifica
response attempt/documentId como hoy y persiste la cadena exacta hasta el recibo final.
Este enlace es de confianza del backend; no una atestacion criptografica del proveedor.

## 4. Secuencia exacta, transacciones e incertidumbre

1. Inicio durable existente: autorizar actor activo/owner, links y actividad; ejecutar
   StartAssignedJob. Started/AlreadyStarted no equivale a permiso permanente de enviar.
   Indeterminate impide toda copia/envio dependiente hasta resolucion explicita.
2. Lectura autorizada corta del intento vigente y referencias; snapshot completo en orden
   CreatedAtUtc, luego Id como hoy. Capturar evidencia esperada de incorporacion. Leer bajo
   locks compatibles si se requiere snapshot compuesto; liberar antes de abrir storage.
3. Copiar TODOS los origenes al spool privado, limites por archivo/total y cancelacion.
   Verificar hash/longitud esperados. Historicos sin evidencia: por defecto rechazo antes
   de proveedor (politica de seccion 6). No publicar filas parciales mientras se copia.
4. Registrar evidencia en transaccion corta NUEVA. Reutilizar orden de locks del protocolo:
   Submission si aplica, User/Client/Project SHARE, PreQuote/Requirement UPDATE, Job,
   Attempts en orden estable, Files SHARE y evidencia. Contexto fresco sin tracking viejo.
   Revalidar links, owner/actividad, current/Processing, Running, intento exacto Processing,
   sin otro activo, request/generation/leaseOwner, cancelacion ausente, conjunto y principal
   exactos, evidencia esperada igual. Reloj DB para lease, no reloj de maquina.
5. Insertar header+detalles; ultimo guard SQL sobre job verifica nuevamente lease vigente
   con clock_timestamp(), identidades y no cancellation; cero filas => rollback total.
   No transaccion abierta durante copia ni envio. No se modifica estado comercial.
6. Commit conocido: Registered con recibo. Misma identidad/contenido ya registrado devuelve
   AlreadyRegistered sin escrituras; distinto conjunto/hash/id incompatible => conflicto.
   Eso no autoriza por si solo un segundo envio. El objeto local tiene uso unico.
7. Commit incierto: cerrar scope y observacion read-only acotada independiente (patron
   existente de 10 s) de header+detalles completos y hash. Si no demuestra igualdad exacta,
   devolver Indeterminate; cero llamadas proveedor, cero compensacion ni retry. Incluso
   reconocimiento de registro no prueba si otro caller ya envio. Solo la misma invocacion
   que conoce que aun no envio y conserva el spool puede continuar tras reconocimiento
   positivo y nueva validacion viva. Recuperacion tras crash no esta autorizada.
8. Antes de enviar comprobar token, recibo y estado vivo con transaccion corta; liberar.
   Enviar los streams sellados de ESA copia, longitud observada, orden y principal exactos.
   Solo una invocacion del cliente por objeto; no fallback a streams origen ni retry.
   Cancelacion puede ganar despues de esta comprobacion: no existe atomicidad DB/red.
   Se cancela cooperativamente lo posible y siempre se impide publicacion obsoleta.
9. Respuesta canonica y resultado interno asociado -> PrepareV2 fuera de locks. PublishV2
   relee evidencia inmutable y todas las identidades; compara manifest y recibo. Aplica
   guards actuales current/cancelacion/actividad/attempt/lease y guard final SQL, ampliado
   a version exigida y evidencia exacta. Grafo+terminal+recibo v2 en el mismo commit.
10. Finalizar/disponer spool en finally despues de que envio termine o se drene. Fallo,
    cancelacion o Indeterminate tambien liberan handles y eliminan SOLO copias privadas
    creadas por esta instancia, nunca claves de IFileStorage. Fallo de limpieza se registra
    con identificador seguro, no borra originales ni cambia resultado confirmado.

Si el proceso cae, la copia local puede desaparecer o quedar residual. No es recovery,
no permite retomar automaticamente ni demuestra que proveedor no fue llamado. Un intento
nuevo futuro requiere nueva autorizacion/identidad/copia; no se implementa aqui. Directorios
residuales tras crash quedan como riesgo operativo separado, sin escaneo/borrado general.

## 5. Incorporacion publica y compatibilidad de publicacion

Intake futuro: preparar spool por upload en su primera y unica OpenRead; calcular digests
y longitud, fingerprint con algoritmo EXISTENTE; consultar replay. Replay igual/conflicto
dispone spool sin escribir storage. Para nuevo submission, SaveVerifiedAsync desde cada
copia rebobinada y exigir recibo igual; persistir los marcadores nuevos y fingerprint viejo
en forma, sin cambiar su significado. Colision concurrente conserva ganador y destinos
potencialmente referenciados conforme D.2a. Test con OpenRead que retorna bytes diferentes
en segunda apertura debe demostrar que nunca existe esa segunda apertura.

Preparacion publica: primer read autorizado del snapshot de Submission/files/links. Si ya
preparado, devolver links existentes sin copia. Si no, generar identidades candidatas y
copiar/verificar/guardar destinos fuera de locks DB. Commit final bloquea Submission y
revalida identidad, todos los metadatos/hashes, links y actividad. Si otro gano, devolver
sus links sin reemplazarlos; conservar destinos perdedores conforme D.2a. Reservar serial,
insertar quote/Requirement/archivos con recibos y completar links solo bajo transaccion
corta. Errores se registran con relectura protegida: nunca marcar Failed sobre preparacion
ya completada por otro. No modificar Answers, fingerprints ni asociaciones originales.
Esto requiere un subbloque propio; el callback PrepareLockedAsync actual NO satisface
el objetivo de E/S fuera de locks y no debe declararse que ya lo satisface.

Publicacion: nueva cadena `sng-job-publication-v2`; el envelope v2 usa las reglas actuales
de PublicationCanonicalJson para el resultado, con un nodo evidence tipado que contiene
los campos de C y el manifest de B. Su ContentHash es SHA256 del envelope canonico entero;
ManifestHash se verifica aparte contra la representacion B, no se confunden ambos hashes.
Los campos nuevos no son opcionales en v2. v1 conserva canonicalizador, validacion,
hashes y reconocimiento terminal EXACTOS. Dispatch por version, no cambiar Version v1
silenciosamente. Nunca recalcular recibos historicos desde jsonb o grafo comercial mutable.
Un Succeeded v1 se reconoce como v1 sin exigir spool/lease/current ni evidencia nueva.

Agregar RequiredPublicationVersion varchar(100) NOT NULL al job, default v1 para filas
anteriores/compatibilidad; valor inmutable desde creacion y CHECK v1/v2. No backfill a v2.
Futuro flujo durable nuevo DEBE crear jobs v2 explicitamente; su inicio/registro/envio
rechazan v1. Publish store rechaza primer publish con version distinta de la requerida,
incluido v1 para job v2 aunque caller omita evidencia. CHECK terminal asocia version del
recibo a RequiredPublicationVersion; header obligatorio para v2 se comprueba en coordinador
y guard SQL EXISTS. No convertir jobs existentes ni dar default v1 en la futura factory
de flujo durable. Mantener APIs legacy/v1 identificadas, sin certificarlas como v2.

Reconocimiento terminal v2 compara recibo original y evidencia inmutable, no estado vivo
ni preferencias comerciales editadas posteriormente. First publish v2 exige evidencia;
v1 terminal sin evidencia sigue reconocido. Nuevos campos no entran en huella v1.

## 6. Historicos y decisiones pendientes

Estados de evidencia, sin equivalencias silenciosas:
- INCORPORATION_V1: recibo nuevo persistido al guardar; extraccion compara otra vez.
- OBSERVED_NOW: historico copiado/hashado ahora; solo prueba estos bytes actuales.
- UNVERIFIED: sin preparacion completa; nunca autoriza envio/publicacion v2.

Recomendacion inicial concreta: bloquear durable v2 para RequirementFile sin evidencia
de incorporacion y pedir nueva version documental por el lifecycle autorizado. No hacer
backfill ni usar el ContentHash antiguo de intake como si certificase el almacenamiento.
Consecuencia: historicos sin permiso de replacement quedan bloqueados hasta una decision
explicita, no se amplian permisos de D.2b para resolverlo de forma implicita.

Alternativa pendiente de producto: permitir OBSERVED_NOW por accion explicita owner,
auditable, antes de habilitar ese camino; nunca por ausencia automatica. El esquema reserva
Basis, pero el coordinador inicial lo rechaza. Aprobar esa alternativa exige contrato de
consentimiento/actor/fecha y tests adicionales en un subbloque separado; no un flag oculto.
Un archivo cambiado durante copia con digest esperado produce mismatch salvo que la
secuencia observada sea exactamente la esperada. Sin digest esperado no puede probarse
un snapshot historico coherente: longitud/mtime no resuelven el problema. OBSERVED_NOW,
si se aprobara, garantiza solo la secuencia copiada/enviada y debe declararlo asi.

Pendientes operacionales: ruta spool privada, permisos y cuotas de disco/concurrencia;
acceso storage entre replicas, persistencia del volumen, backups/retencion y respuesta a
residuales tras crash. Requieren evidencia del despliegue. No inferidos de pruebas locales.
No introducir un storage distribuido por anticipacion ni certificar disponibilidad.
La validez normativa/comercial del contenido o extraccion sigue fuera de este contrato.

## 7. Matriz de fallos y efectos exigidos

Codigos simbolicos siguientes son propuestos, no endpoints existentes. Errores no llevan
rutas, bytes ni mensajes del proveedor. Fail/Cancel durable reutilizan D.2C.1 solo si el
caller sigue autorizado; fallo de su commit conserva incertidumbre, no se compensa.

| Escenario | Antes de proveedor | Publicacion / efectos |
|---|---|---|
| Origen ausente/no legible | DOCUMENT_SOURCE_UNAVAILABLE, cero llamadas | Sin evidencia parcial ni resultado; posible fallo protegido, originales intactos |
| Hash o longitud distinta, exceso de limite | DOCUMENT_CONTENT_MISMATCH, cero llamadas | No registrar conjunto, no publicar; limpiar solo spool |
| Cambio durante copia | Con evidencia, comparar secuencia completa y rechazar mismatch | Sin evidencia no afirmar deteccion; politica inicial bloquea historicos |
| Origen sustituido despues de sellar copia | No reabrir; envio de copia puede continuar si guards validos | Evidencia describe copia, no promete identidad presente del origen; cambio de referencia DB rechaza publish |
| Cancelacion token durante preparacion | Interrumpir copia, cero llamadas | Sin evidencia parcial; cancelar protegido si corresponde, sin requeue |
| Cancelacion durable mientras copia | Registro/pre-send detecta solicitud: cero llamadas si gana antes del guard | Si gana despues del envio, llamada puede existir; publicacion denegada |
| Replacement mientras copia | Registro detecta no current/conjunto distinto: cero llamadas | Publicacion tardia denegada; D.2b normalmente impide replacement con job activo |
| Error previo a commit de evidencia | Rollback, cero llamadas | PersistenceError, no compensacion ni retry automatico |
| Commit de evidencia incierto | Observacion exacta o Indeterminate, este ultimo cero llamadas | Preservar evidencia potencial y no reenviar desde otra invocacion |
| Lease vencida durante copia/registro | Guard final rechaza, cero llamadas | Resultado tardio no publica; no renovar lease ni implementar recovery aqui |
| Evidencia de otro job/attempt/Requirement/conjunto | Rechazar registro/request antes de envio | FK/guard/version rechazan publish, cero modificaciones parciales |
| Fallo de red despues de iniciar envio | No afirmar cero consumo remoto | No publicar como exitoso ni reintentar automaticamente |
| Commit de publicacion incierto | No repetir proveedor | Reconocimiento exacto v2 o Indeterminate, mismo patron que v1 |

## 8. Division minima implementable y aceptacion

Cada subbloque termina compilando y ejecutando sus pruebas focales; no un prompt monolitico.
Pruebas descritas aqui son FUTURAS, no ejecutadas en este trabajo.

1. **Primitivas de bytes sin consumidores activados.** SaveVerifiedAsync y copia privada,
   recibos inmutables, limites/disposal. Filesystem real: byte a byte/hash/longitud, streams
   no seekables, lecturas parciales, EOF/cancel/excepcion, colision sin overwrite, limpieza
   solo recursos propios, origen reemplazado tras sellado. Sin nuevas tablas todavia.
2. **Evidencia de incorporacion y uploads privados.** Migracion aditiva de campos nullable,
   invariantes y adaptacion create/add/replace-file/replacement. PostgreSQL real: guard de
   evidencia, referencia+recibo atomicos, D.2a rollback/commit incierto conserva archivos,
   D.2b carreras y limites. Historicos permanecen null. No proveedor ni jobs nuevos.
3. **Intake/preparacion publica.** Marcadores nuevos, spool unico, E/S fuera de Submission
   lock, revalidacion de links y ganador concurrente. Vectores replay viejo/nuevo identicos;
   upload cuya segunda apertura cambiaria bytes; destino verificado. PostgreSQL real,
   barreras de concurrencia, fallos por fases, cero jobs/attempts/AI2 en intake y replay.
4. **Evidencia por intento y registro guardado.** Tablas/keys/immutability y version requerida,
   servicio interno explicito sin invocacion automatica. PostgreSQL real: pertenencias
   cruzadas rechazadas, conjunto completo, replays exactos sin writes, commit response-loss,
   lease/cancel/replacement en ambos ordenes usando barreras/PIDs/locks observados. Sin envio.
5. **Contrato y publicacion v2.** Canonico con vector literal independiente, preparacion
   desde resultado asociado, guard EXISTS y no downgrade. Publicacion atomica/rollback,
   evidencia ajena rechazada, cambios comerciales preservados al reconocer. Ejecutar
   vectores/recibos v1 sin cambios y reconocimiento historico despues de replacement.
6. **Adaptador explicito de envio verificado, sin activacion.** Harness con inicio/registro
   reales y HttpMessageHandler de prueba que consume multipart completo, compara bytes
   exactos y digest con copia/DB. Cuenta aperturas de origen (una por archivo), impide
   reapertura con wrapper que falla, sustituye origen por barrera tras sellar; proveedor
   recibe copia. Todo fallo de verificacion => cero invocaciones. Cancellation post-check
   puede producir envio pero jamas publicacion obsoleta. No retry ni automatic worker.

Los tests de contenido usan filesystem real; los de constraints/transacciones usan
PostgreSQL aislado real. Las pausas dependen de barreras y condiciones observables, no de
sleeps como prueba. Ni handler local ni DB prueban consumo remoto real; queda explicito.
El core sincrono actual no obtiene garantia durable automaticamente: su adaptacion para
usar copia verificada puede ser otro cambio focal posterior con contrato/autorizacion
propios, sin simular job ni permitir saltarse RequiredPublicationVersion del flujo v2.

## 9. Integridad de este entregable

Solo se agrega este documento. No se editaron fuentes, tests, migraciones, configuracion
ni documentos historicos. No builds/tests/DB/proveedores ejecutados para este diseno.
Rama feature/public-experience-intake; HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79.
Baseline: 36 tracked modificados, 62 untracked, cero staged, 838 archivos manifestados.
Manifiestos de comparacion en
`C:/Users/USUARIO/AppData/Local/Temp/sng-d2c-design-c5b43afb77e04f4f8079b951f6e2f6e7`.
La inspeccion respalda hechos de codigo, no certifica la propuesta ni el despliegue.

## 10. D.2c.1: primitivas implementadas sin activacion

Implementacion focal, 2026-10-22. No nuevos consumidores productivos, workflow DI,
EF/schema/migracion, jobs/attempts, publicacion, permisos, lifecycle ni cambios a uploads.
Las propuestas de triggers/FK, RequiredPublicationVersion, v2, OBSERVED_NOW, bloqueo de
historicos y backfill NO estan aprobadas ni implementadas por este subbloque.

### API y propiedad

`IFileStorage.SaveVerifiedAsync(string storageKey, Stream content, long expectedLength,
long maximumLength, CancellationToken cancellationToken) -> Task<StoredContentReceipt>`.

LocalFileStorage implementa la operacion separadamente en LocalFileStorage.Verified.cs.
SaveAsync conserva su cuerpo, validaciones y semantica previa, incluidos archivos vacios.
El constructor conserva llamadas existentes mediante un ILogger opcional; DI existente
puede aportar el logger normal. Sin logger explicito se usa NullLogger, por lo que el
consumidor que necesite recoger diagnosticos debe aportar/configurar logging. No se agrega
registro DI ni invocacion de las nuevas operaciones.

StoredContentReceipt es sealed, con getters Algorithm="SHA256", Sha256 y Length (long),
sin constructor publico ni setters. Su constructor interno solo se utiliza despues de
promocion; Application concede visibilidad interna a Infrastructure para materializarlo.
La barrera protege callers ordinarios, no codigo hostil dentro del ensamblado de confianza
ni reflection. No es evidencia historica persistida ni afirma durabilidad ante power loss.

`SealedContentCopy.PrepareAsync(Stream source, long expectedLength, string expectedSha256,
long maximumLength, ILogger logger, CancellationToken cancellationToken)` devuelve una
copia interna sealed que implementa IAsyncDisposable. No requiere ni acepta identidades
de jobs/referencias. No abre IFileStorage; el origen lo aporta el caller. Constructor
privado; digest esperado obligatorio, SHA256 hexadecimal ASCII MAYUSCULA de 64 caracteres.
No permite fallback sin evidencia. La seleccion autorizada y apertura de origen son futuras.

Ambas primitivas usan VerifiedContentIO: buffer fijo 81920 bytes; no consultan Length,
Position o Seek del origen y no lo disponen. Leen desde posicion actual, soportan lecturas
parciales/no seekables. Cada bloque escrito alimenta el hash, sin una segunda lectura.
EOF es ReadAsync=0; una lectura parcial positiva no termina el bucle. Al alcanzar longitud
esperada se lee como maximo un byte mas para demostrar EOF o rechazar exceso. Una fuente
ilimitada no se drena. Hash cubre solo contenido, sin clave/nombre/MIME/cabeceras.

Entradas no legibles o digest mal formado: ArgumentException; longitudes no positivas o
expectedLength>maximumLength: ArgumentOutOfRangeException antes de escribir. Diferencia
real de longitud/digest: ContentVerificationException. Cancelacion conserva
OperationCanceledException. SaveVerifiedAsync traduce IO/acceso/no soportado a las
excepciones de storage existentes y colision a FileStorageConflictException. La copia
interna conserva la excepcion de origen/preparacion; limpieza no la sustituye.

### Recursos y seguridad

Escritura: temporal CreateNew en directorio del destino, propiedad solo despues de abrir
con exito, misma clave normalizada que SaveAsync. Verifica longitud/EOF, flush, cierra y
promueve Move(overwrite:false); recibo solo despues de Move. Colision antes o durante copia
preserva destino. No cancellation check posterior a promocion ni borrado compensatorio
del destino. Excepcion no demuestra ausencia de publicacion; no retry automatico.

Copia: Directory.CreateTempSubdirectory("sng-sealed-") por instancia bajo temp del proceso;
no se expone ruta en la API. Antes de copiar contenido, Windows protege ACL del directorio
sin herencia previa y concede FullControl heredable al SID actual; Unix aplica modo 0700
al directorio y solicita 0600 al archivo. Si la configuracion de permisos falla, no se
entrega copia. Archivo CreateNew ReadWrite/FileShare.None, mismo handle conservado tras
flush y rewind. Estos permisos no certifican volumen desplegado, ACL del padre, aislamiento
frente a procesos del mismo usuario, administrador/root, reparse/symlink malicioso ni
proteccion universal por FileShare. No se configura hosting ni cuota/concurrencia global.

OpenRead entrega UNA vista, inicialmente Position=0, sin FileStream/handle/ruta publicos.
CanWrite/CanSeek=false; escritura, SetLength y Seek/Position-set fallan. No se reabre por
nombre al leer. Disponer la vista invalida ese lector pero no cierra el handle propietario;
no se puede pedir un segundo lector aunque se haya dispuesto el primero. Disponer owner
libera handle y limpia copia/directorio, invalida lectura y nuevas aperturas. DisposeAsync
repetido es idempotente. No se garantiza uso concurrente de lecturas o lectura/disposicion;
caller debe drenar operaciones antes de disponer. Esto no es un contador de envios ni una
exclusion distribuida. Los metadatos inmutables siguen siendo consultables tras disponer.

Limpieza elimina solo archivo propio y directorio propio NO recursivamente; no originales,
destinos promovidos ni otros temporales. Fallos esperables de limpieza emiten Warning por
ILogger con tipo de recurso y tipo de error, sin contenido/rutas. Las pruebas observan el
logger conectado y un directorio con archivo ajeno: este se conserva, el fallo se registra
y la excepcion principal permanece. Logging requiere un sink configurado; NullLogger no
es evidencia de entrega de logs. No scanner/TTL/retencion/recovery; crash puede dejar residuos.

### Cobertura y compatibilidad

VerifiedContentStorageTests usa filesystem real y streams controlados, no sleeps:
- Vector literal independiente `abc` -> SHA256
  `BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD`.
- Igualdad de bytes/longitud, parcial/no seekable, posicion actual y propiedad del origen.
- Vacio, corto/largo, limites invalidos y fuente ilimitada detenida tras el primer exceso.
- Excepcion/cancelacion en segunda lectura, colision inicial y colision inyectada durante
  copia; solo temporales propios limpiados, archivos ajenos intactos.
- Digest invalido/diferente, errores sin objeto exitoso; lector sin escritura/truncado;
  sustitucion del origen tras sellar no cambia copia, disposal repetido/lector invalidado.
- Windows: apertura por nombre rechazada mientras el lector consume el handle exclusivo;
  ACL protegida para SID actual. Rama Unix prueba lectura tras unlink y modos, pero no
  debe considerarse ejecutada por una corrida en Windows.
- Constructores no publicos/setters ausentes; SaveAsync vacio/posicion/colision conservados.

Los cuatro implementadores concretos de prueba de IFileStorage ahora tienen metodo nuevo
que lanza NotSupportedException si se invoca: IntakeStorage, TestFiles, BatchStorage y
ObservedStorage. No se cambia su SaveAsync ni se simula evidencia verificada en esos flujos.
Los dobles NSubstitute compilan con la interfaz. Busqueda de referencias confirma que las
unicas llamadas a SaveVerifiedAsync/SealedContentCopy son pruebas; no nuevas llamadas en
servicios, DI, controller, core o cliente AI2. Regresiones D.2a son pertinentes al mantener
los mismos consumidores/LocalFileStorage; intake HTTP completo/replay valida no activacion.

### Registro de ejecucion

Baseline real: feature/public-experience-intake / 56ace9c3845d2e3327a1ca811c27f8e1562e5a79,
36 tracked modificados, 63 untracked, cero staged. Incluye el documento de diseno previo.
Artefactos de este subbloque:
`C:/Users/USUARIO/AppData/Local/Temp/sng-d2c1-239c5006d22c4f5b8f98a89b0624dc7b`.

Primera compilacion completa fallo con 80 diagnosticos de error por falta de `using Xunit`
en el nuevo archivo de tests; no se ejecutaron tests de ese build. Se corrigio el import y,
por revision, el contador de lecturas del stream de prueba, sin relajar assertions.
build2.log: build incremental, 0 errores/93 advertencias. primitives.trx: 32 passed,
0 failed, 0 skipped. final-build.log: rebuild completo con --no-incremental, 0 errores,
111 advertencias; ningun diagnostico nombra las primitivas nuevas o sus tests. Diferencias
incremental/rebuild no representan fixes de warnings; CS8604 anterior permanece fuera de
alcance. Las 32 pruebas preliminares se solapan con la seleccion final, no deben sumarse.

Seleccion final (mismo proyecto Tests/CotizadorBackend.Tests.csproj, --no-build):

```text
FullyQualifiedName~VerifiedContentStorageTests|FullyQualifiedName~DocumentPersistenceFileSafetyPostgreSqlTests|FullyQualifiedName~ReplaceDocumentFileSafetyPostgreSqlTests|FullyQualifiedName~PublicExperienceIntakeTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

Resultado final: **163 passed / 0 failed / 0 skipped**, 9.4515 minutos. final.trx registra
32 casos de primitivas, 85 DocumentPersistenceFileSafety, 11 ReplaceDocumentFileSafety,
33 PublicExperienceIntake y 2 HTTP completos/replay. Los 32 preliminares estan incluidos,
no son 195 pruebas distintas. No hubo fallos de tests ejecutados. No fuente productiva o
de tests cambio despues del rebuild final; solo se finalizo esta documentacion.

Aceptacion A-E:
| Criterio | Evidencia ejecutada |
|---|---|
| A: recibo tras promocion, mismos bytes | Vector literal y archivo real; colision inicial/durante copia no devuelve recibo |
| B: conservar recursos ajenos / D.2a | Tests nuevos de fuente/cancel/colision/cleanup y 96 regresiones de filesystem/persistencia existentes |
| C: lector de copia sellada | Vista no escribible, handle exclusivo mantenido, sustitucion de origen, disposal e invalidez posterior |
| D: digest/longitud/limites | 32 casos nuevos, buffer acotado, origen no seekable/parcial, exceso finito e ilimitado |
| E: compatibilidad/no activacion | SaveAsync sin cambios de cuerpo, busqueda de llamadas, 33 intake y 2 HTTP con Pending, cero jobs/attempts/AI2 |

Solo Windows fue ejecutado. Las ramas Unix de permisos/unlink estan escritas pero no son
evidencia runtime Unix. No se certifican fallo fisico de disco, perdida de energia, crash,
procesos privilegiados, consumo remoto ni exactly-once. Las primitivas no autentican al
caller ni congelan referencias DB. No existe coordinador de conjunto documental o envio.

Comandos de compilacion: `dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore
--verbosity minimal`, con `--no-incremental` en primer build y rebuild final. Tests finales
con filtro anterior, `--logger "trx;LogFileName=final.trx" --logger "console;verbosity=normal"
--results-directory <artifact-root>/results`. Evidencia: build.log, build2.log,
final-build.log, primitives.log/results/primitives.trx, final-tests.log/results/final.trx.

Se creo PostgreSQL 17 aislado en <artifact-root>/data, usuario audit temporal y puerto
loopback 55451. Solo fixtures cotizador_backend_test_*; migraciones existentes aplicadas
solo dentro de esas bases, sin generar migracion nueva. Variable de conexion child-local
retirada en finally; SELECT final conto cero bases de fixture; pg_ctl confirmo no server
running despues de shutdown. initdb.log y server.log quedan como evidencia; se retienen
archivos del cluster parado, no un servicio activo. Las 96 raices storage registradas por
TRX se comprobaron ausentes; cero directorios sng-content-tests-* o sng-sealed-* restantes.
No DB habitual ni proveedores AI2/Gemini reales utilizados.

Manifiesto inicial 839 rutas tracked/untracked no ignoradas; final 845. Siete existentes
cambiaron, seis nuevos, cero ausentes, 832 anteriores byte-identicos. Inventario completo
debajo. Fuera de abstracciones/storage, todas las fuentes productivas (incluidos uploads,
core, DI, publicacion, jobs y migraciones) permanecen identicas al baseline. Bin/obj
generados por build no forman parte del manifest de fuentes. initial.csv/final.csv quedan
en el artifact root. Rama/HEAD preservados; final 37 tracked modificados / 69 untracked /
0 staged. git diff --check pasa con avisos LF/CRLF anteriores. Sin commit/push/merge/deploy.

Veredicto: **D.2c.1 implementado y verificado en el alcance autorizado, sin activacion**.
Siguiente subbloque requiere autorizacion propia para persistencia/consumidores; este
resultado no aprueba historicos, OBSERVED_NOW, backfill, triggers/FK, v2 ni politica de disco.

Inventario focal respecto del manifest inicial (no respecto de HEAD):

- Application/Common/Abstractions/Storage/IFileStorage.cs: nueva operacion explicita.
- Infrastructure/Storage/LocalFileStorage.cs: partial y logger opcional; SaveAsync intacto.
- Tests/Application/PublicExperience/PublicExperienceIntakeTests.cs: metodo no activado en doble.
- Tests/Infrastructure/Persistence/DocumentCoordinationPostgreSqlTests.cs: mismo ajuste de doble.
- Tests/Infrastructure/Persistence/DocumentPersistenceFileSafetyPostgreSqlTests.cs: mismo ajuste.
- Tests/Infrastructure/Persistence/ReplaceDocumentFileSafetyPostgreSqlTests.cs: mismo ajuste.
- docs/document-byte-identity-contract.md: estado de propuestas y evidencia de este subbloque.

Archivos nuevos:

- Application/Common/Abstractions/Storage/StoredContentReceipt.cs: recibo y error de verificacion.
- Application/Common/Abstractions/Storage/AssemblyInfo.cs: acceso interno para Infrastructure.
- Infrastructure/Storage/LocalFileStorage.Verified.cs: escritura sin cambiar SaveAsync.
- Infrastructure/Storage/VerifiedContentIO.cs: copia acotada y limpieza focal con diagnostico.
- Infrastructure/Storage/SealedContentCopy.cs: copia privada y lector sobre handle retenido.
- Tests/Infrastructure/Persistence/VerifiedContentStorageTests.cs: 32 casos nuevos.

## 11. D.2c.2: evidencia de incorporacion y uploads privados

Alcance 2026-10-22: create privado, add-document, replace-document y replacement de
Requirement. La politica depende del recorrido autenticado, no del origen publico o
manual del proyecto. No cambia autorizacion, capabilities, lifecycle, locks, endpoints,
DTO, procesamiento ni publicacion. Intake y preparacion publica siguen sin adaptar.

### Contrato implementado

RequirementFile agrega StoredSha256 (varchar(64)), StoredLengthBytes (bigint),
StoredEvidenceAtUtc (timestamptz) y StoredEvidenceVersion (smallint), todos nullable.
Todos null significa evidencia desconocida bajo este contrato, tambien para archivos
NUEVOS de preparacion publica. No significa necesariamente antiguo. SizeBytes sigue
siendo longitud declarada; version 1 exige coincidencia con la observada.

CreateWithStoredEvidence requiere valores completos: digest ASCII MAYUSCULA de 64
caracteres, longitud positiva igual a SizeBytes, version 1 y fecha UTC. No setters
publicos ni metodo para completar/borrar evidencia. Create conserva la via sin evidencia.
Domain no referencia Application/Infrastructure: valida estructura, no el origen
criptografico de los valores. Application obtiene hash/longitud exclusivamente del recibo
de SaveVerifiedAsync real; no del request ni de otra lectura.

Los cuatro consumidores usan el limite existente de 20 MiB por archivo; los limites de
lote y su revalidacion bajo locks siguen intactos. Capturan TimeProvider.GetUtcNow despues
de recibir el recibo y antes de la insercion. Esa fecha registra recepcion en Application,
no instante exacto de Move, commit ni persistencia fisica. La evidencia puede preceder
CreatedAtUtc cuando la referencia se crea despues bajo lock; no se falsifica esa fecha.

Add/replace/replacement mantienen storage fuera del scope documental; bajo el scope
existente revalidan y guardan evidencia, referencia y efectos previos atomicos. Create
conserva su SaveChanges y coordinacion previos. ReplaceDocument sigue creando otra
identidad y retirando la referencia anterior; nunca actualiza su hash/key. Replacement
conserva A y solo incorpora archivos verificados en B, Pending y sin ejecuciones.

ContentVerificationException con token no cancelado devuelve InvalidRequest existente:
CreateRequirementFailure o ManageRequirementDocumentsFailure, ambos HTTP 400 ya mapeado.
La validacion normal conserva FileTooLarge/TooManyFiles/EmptyFile. Otros mappings y
propagacion de cancelacion no cambian. Un lote incompleto no llega a SaveChanges; archivos
completados se retienen. No borrado compensatorio, retry ni idempotencia nueva.

### Garantias por capa y migracion

EF configura SetAfterSaveBehavior(PropertySaveBehavior.Throw) en los cuatro campos:
incluye null->valor y valor->null, sin ignorar cambios. No interceptor generico.
PostgreSQL comprueba grupo completo, version, formato y longitud mediante
ck_requirement_files_stored_evidence, con IS NOT NULL explicito para cada campo.
El CHECK no protege contra UPDATE SQL valido: una prueba demuestra ese limite.

Migracion: 20261022202916_AddRequirementFileStoredEvidence, con Designer y snapshot.
Up agrega solamente cuatro columnas nullable y ese CHECK; Down elimina solo esos objetos.
No tablas, FK, indices, triggers, backfill ni actualizaciones de filas previas. SQL generado
en migration.sql del artifact root; solo INSERT de metadata __EFMigrationsHistory ademas
de los ALTER TABLE. Verificacion CLI has-pending-model-changes: sin cambios pendientes.
Los fixtures aplican migraciones exclusivamente sobre sus bases temporales.

### Pruebas y fronteras

RequirementFileStoredEvidenceTests prueba factories, invariantes y setters no publicos.
RequirementFileStoredEvidencePostgreSqlTests prueba 12 mutaciones EF (cuatro campos por
cambio/borrado/poblacion), excepcion InvalidOperationException, cero comandos UPDATE y
relectura independiente. Prueba los 14 grupos parciales y valores completos invalidos
con CheckViolation y nombre exacto del CHECK. La migracion baja al esquema anterior,
inserta una fila por SQL anterior, migra, compara sus valores y cuatro null, conserva
bytes preexistentes y verifica insercion compatible posterior y modelo sin diferencias.

Los ocho casos PrivateIncorporation usan PostgreSQL y LocalFileStorage reales; contrastan
abc con BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD, bytes y recibo
persistido. Los lotes create/replacement con segunda longitud incorrecta conservan el
primer destino sin publicar referencias. Replacement incluye originales con y sin
evidencia. El marcador AfterWrite prueba fecha posterior al recibo (precision DB).

Los tres dobles de filesystem privados delegan SaveVerifiedAsync a LocalFileStorage.
BatchStorage conserva inyecciones write_conflict/write_failure/batch_partial y BrokenStream
falla ahora tambien en ReadAsync; FailedKeys/hook.Injected verifican que se dispararon.
SaveAsync de esos dobles mantiene su via publica. IntakeStorage sigue rechazando la
operacion verificada no activada. Los mocks unitarios/HTTP obtienen recibos de un helper
con filesystem real, sin constructores internos, reflection ni hashes fabricados.

Regresiones D.2a/D.2b comparan evidencia de filas confirmadas, bytes retenidos, rollback,
respuesta perdida tras commit, permisos, historial, limites y carreras con locks/PIDs.
Los snapshots documentales incluyen los cuatro campos. Public HTTP intake/replay exige
los cuatro null; otro test hace add autenticado al proyecto publico, conserva Answers,
fingerprint/links y documento original, verifica nuevo recibo y cero jobs/attempts/AI2.
No se prueba envio remoto, estabilidad futura de bytes, storage desplegado o crash fisico.

### Evidencia de ejecucion

Artifact root: C:/Users/USUARIO/AppData/Local/Temp/sng-d2c2-dea5f7ac982348cdb8411a857465a142.
Baseline: feature/public-experience-intake, HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79;
37 tracked modificados, 69 untracked, cero staged, 845 rutas en initial.csv.
Se preservan cambios previos; HEAD no se utiliza como baseline de este bloque.

Primera compilacion de tests: 15 errores por import static Token faltante y nombre Quote
en el nuevo partial de pruebas; corregidos sin relajar assertions. Build2 incremental:
0 errores, 95 warnings; dos CS8604 nuevos del callback de mocks se corrigieron con la
anotacion no-null correspondiente a Arg.Any<Stream>. Build3 completo --no-incremental:
0 errores, 111 warnings. Comparacion de diagnosticos con rebuild del cierre D.2c.1,
descontando numeros de linea: sin diagnosticos nuevos. No se afirma una mejora global.

Preflight: 60 passed / 0 failed / 0 skipped, antes del refuerzo de lotes/HTTP/migracion.
Regresion ampliada: 287 passed / 0 failed / 0 skipped, 8.6363 minutos, fuentes build3.
Revision posterior ajusto SOLO la prueba de migracion para que el archivo fisico ocupe
exactamente la ruta de su StorageKey. Rebuild final completo: 0 errores, 111 warnings,
sin diagnosticos nuevos respecto del cierre D.2c.1 (se ignoran numeros de linea).
Prueba de migracion final: 1 passed / 0 failed / 0 skipped; reemplaza ese caso anterior,
no agrega otro caso distinto. Los otros 286 casos no cambiaron y no se repitieron.
No hubo fallos de tests ejecutados. Los errores preliminares fueron de compilacion.
No fuentes productivas ni de tests cambiaron despues del rebuild final; solo documentacion.
Las ejecuciones superpuestas NO se suman. Esto no certifica toda la suite del repositorio.

Pendiente D.2c.3: adaptar intake/preparacion publica, sin asumir que ContentHash previo
prueba segunda escritura. Politica de historicos, evidencia por intento, SealedContentCopy
para procesamiento, publicacion v2 y activacion requieren autorizacion separada. Sin
cambios a CreatePreQuoteDocumentService ni limpieza de huerfanos.

### Inventario D.2c.2 respecto de initial.csv

Catorce archivos existentes modificados:

- Application/PreQuotes/CreateRequirement/CreateRequirementService.cs: recibo y error de longitud.
- Application/PreQuotes/ManageRequirementDocuments/ManageRequirementDocumentsService.cs: tres recorridos privados.
- Domain/PreQuotes/Requirement.cs: cuatro propiedades y factory explicita, sin cambiar lifecycle.
- Infrastructure/Persistence/Configurations/RequirementFileConfiguration.cs: columnas, CHECK y Throw EF.
- Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs: modelo generado.
- Tests/Api/Integration/PublicExperienceHttpPostgreSqlTests.cs: null publico y mutacion privada sobre origen publico.
- Tests/Api/Integration/RequirementUploadProblemDetailsTests.cs: recibos reales y HTTP 400 por mismatch.
- Tests/Application/PreQuotes/CreateRequirementServiceTests.cs: doble con escritura real.
- Tests/Application/PreQuotes/ManageRequirementDocumentsServiceTests.cs: doble con escritura real.
- Tests/Infrastructure/Persistence/DocumentCoordinationFaultPostgreSqlTests.cs: evidencia tras commit incierto.
- Tests/Infrastructure/Persistence/DocumentCoordinationPostgreSqlTests.cs: storage verificado y snapshot ampliado.
- Tests/Infrastructure/Persistence/DocumentPersistenceFileSafetyPostgreSqlTests.cs: inyecciones y evidencia persistida.
- Tests/Infrastructure/Persistence/ReplaceDocumentFileSafetyPostgreSqlTests.cs: colision y evidencia persistida.
- docs/document-byte-identity-contract.md: estado actualizado y esta seccion; historia anterior conservada.

Seis archivos nuevos:

- Infrastructure/Persistence/Migrations/20261022202916_AddRequirementFileStoredEvidence.cs.
- Infrastructure/Persistence/Migrations/20261022202916_AddRequirementFileStoredEvidence.Designer.cs.
- Tests/Domain/PreQuotes/RequirementFileStoredEvidenceTests.cs.
- Tests/Infrastructure/Persistence/RequirementFileStoredEvidencePostgreSqlTests.cs.
- Tests/Infrastructure/Persistence/DocumentIncorporationPostgreSqlTests.cs.
- Tests/Infrastructure/Persistence/StoredReceiptTestWriter.cs.

Build: dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental --verbosity minimal.
Tests: dotnet test Tests/CotizadorBackend.Tests.csproj --no-build --filter <seleccion>
--logger "trx;LogFileName=regressions.trx" --logger "console;verbosity=normal"
--results-directory <artifact-root>/results. Seleccion final, una sola linea:

```text
FullyQualifiedName~RequirementFileStoredEvidence|FullyQualifiedName~DocumentCoordination|FullyQualifiedName~RequirementDocumentTransition|FullyQualifiedName~DocumentPersistenceFileSafety|FullyQualifiedName~ReplaceDocumentFileSafety|FullyQualifiedName~CreateRequirementServiceTests|FullyQualifiedName~ManageRequirementDocumentsServiceTests|FullyQualifiedName~RequirementUploadProblemDetailsTests|FullyQualifiedName~GetCurrentRequirementServiceTests|FullyQualifiedName~GetRequirementDetailsServiceTests|FullyQualifiedName~PublicExperienceIntakeTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.PrivateUploadOnPublicProject
```

Preflight selecciono RequirementFileStoredEvidence, PrivateIncorporation,
CreateRequirementServiceTests y ManageRequirementDocumentsServiceTests. Logs: build.log,
build2.log, build3.log, migration-generation.log, migration.sql, preflight.log,
results/preflight.trx, regressions.log, results/regressions.trx. No se ejecuta toda la suite.

El rebuild final esta en final-build.log. La repeticion focal usa el mismo comando de
test con filtro FullyQualifiedName~RequirementFileStoredEvidencePostgreSqlTests.MigrationPreservesPreviousRows_AndModelHasNoPendingChanges;
logs final-migration.log y results/final-migration.trx.

### Aceptacion final y limpieza

| Criterio | Evidencia ejecutada |
|---|---|
| A: cuatro recorridos con recibo real | Ocho PrivateIncorporation; vector literal, bytes/DB, fecha posterior a escritura; add privado sobre proyecto publico |
| B: referencia/evidencia juntas | Regresiones D.2a de rollback/commit real con respuesta perdida, lote con segundo archivo fallido o mismatch y relecturas independientes |
| C: validacion e inmutabilidad ordinaria | 13 domain, 14 PostgreSQL: 12 mutaciones EF sin UPDATE; 14 grupos parciales y valores invalidos; roundtrip; CHECK no equivale a trigger |
| D: compatibilidad | Migracion con fila previa y bytes de su StorageKey, cuatro null y nueva insercion sin evidencia; dos intake/replay y un add privado sobre fuente publica |
| E: conservar D.2a/D.2b | 96 regresiones de seguridad de archivos; clase documental 64 casos con guards, actividad, limites, confirmacion, snapshots y carreras; siete transiciones locales |
| F: no activacion/publicacion | Tres HTTP publicos con Pending/cero jobs/attempts/AI2; fuentes productivas excluidas byte-identicas al baseline |

Los conteos de esta matriz se superponen con 287; no son ejecuciones adicionales.
Las doce carreras documentales registraron PIDs diferentes y PostgreSQL Lock con
pg_blocking_pids antes de liberar al ganador. Ejemplos: legacy primero 10680/28840,
documento primero frente a legacy 10680/23788, limite de tamano 28840/10680; cancelacion
primero 28840/10680 y job primero 28840/23788. Los PIDs pueden reutilizarse entre tests;
la exigencia es conexiones distintas dentro de cada carrera. Evidencia completa en TRX.
Perdida de respuesta es inyeccion tras commit real, no crash fisico ni fallo de red real.

PostgreSQL 17 aislado en <artifact-root>/data, loopback 55453, usuario audit temporal.
Solo bases fixture cotizador_backend_test_*; variable de conexion child-local retirada
en finally. Cada ejecucion paro el servidor; SELECT final conto cero bases de fixture
y pg_ctl status confirmo no server running. Logs initdb.log, preflight-server.log,
regressions-server.log y final-server.log. Se retiene cluster parado y evidencia, no servicio.
Las 155 raices storage registradas en el TRX ampliado estan ausentes (85 D.2a, 59 D.2b,
11 replace-safety). Cero sng-receipt-unit-*, sng-evidence-migration-* y sng-private-public-*
restantes tras la ultima prueba. Sin DB habitual ni AI2/Gemini reales.

Integridad final: rama/HEAD del baseline preservados; 38 tracked modificados, 75 untracked,
cero staged. final.csv contiene 851 rutas: 14 existentes cambiados, seis nuevos, cero
ausentes, 831 rutas anteriores byte-identicas. El inventario anterior explica todo el
delta. Sin cambios a primitivas D.2c.1, consumidores publicos productivos, fingerprints,
core/cliente AI2, jobs/stores, publicacion, lifecycle/capabilities/permisos, DI o frontend.
git diff --check pasa con avisos LF/CRLF. Sin commit, push, merge ni despliegue.

Veredicto: **D.2c.2 implementado y APTO para revision de cierre en el alcance verificado**.
No se valida envio a proveedor ni storage desplegado; no se aprueba politica de historicos,
OBSERVED_NOW, backfill, triggers, evidencia por intento, publicacion v2 ni procesamiento
automatico. D.2c.3 requiere un bloque propio.

## 12. D.2c.3a: captura inicial y evidencia del upload publico

Alcance 2026-10-22: intake anonimo exclusivamente. Preparacion publica conserva sus
lecturas/copias y crea RequirementFiles sin evidencia de incorporacion. No D.2c.3b,
procesamiento, spool de ejecucion, proveedor, jobs, v2, backfill o limpieza de huerfanos.

### Contraste y contrato

Baseline: feature/public-experience-intake, HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79,
38 tracked modificados, 75 untracked, cero staged, 851 rutas. initial.csv es el baseline
inmediato, no HEAD. Se confirmo HashFileAsync/OpenRead y una segunda OpenRead/SaveAsync.
La canonicalizacion y el repositorio no requieren cambios. SaveChanges mantiene la
transaccion del agregado completo y el indice unico de SubmissionId resuelve concurrencia.

Limites PUBLICOS, desde PublicExperiencePayloadValidation: 5 archivos, 20 MiB individual,
100 MiB total y payload 256 KiB. Validaciones tempranas y sus errores 400 permanecen.
La captura con longitud real distinta devuelve el mismo 400 y mensaje anterior.
Cancelacion sigue propagandose. Recibo distinto de captura lanza FileStorageWriteException:
fallo interno existente, no 400 ni conflicto 409. No se agregan codigos HTTP.

IInitialContentCapture.CaptureAsync recibe stream prestado, longitud declarada, limite y
token. InitialContentCapture usa CaptureInitialAsync, entrada explicita sobre el mecanismo
privado de SealedContentCopy. PrepareAsync conserva digest esperado obligatorio y sus
validaciones. Solo el core privado compartido admite ausencia de digest para CAPTURAR;
no se presenta esa captura como verificacion contra evidencia anterior.

Mismo buffer 81920, mismos bloques escritos/hashados, EOF acotado, mismo handle privado
retenido y una sola vista no escribible. No seek/Length/Position del origen, no reopen,
no rutas/handles exportados, no memoria proporcional al conjunto. Permisos Windows/Unix
y limpieza focal son los de D.2c.1; solo Windows se ejecuta en este bloque. No cuotas,
hosting ni certificacion Unix nuevos. DI registra solo la primitiva explicita.

ExecuteAsync valida antes de abrir. Abre cada upload una vez y dispone el stream que
abrio; la primitiva no dispone el origen prestado. Captura todos los uploads en orden
antes de consultar repositorio. Fingerprint conserva canonical payload + digests binarios
en orden, SHA256 MAYUSCULA; no incorpora campos nuevos. Replay/conflicto dispone capturas
sin escribir almacenamiento ni alterar filas. Una carrera puede escribir ambos destinos
antes de que el indice unico elija ganador; el perdedor conserva sus bytes conforme D.2a.

Para insertar, consume el lector unico con SaveVerifiedAsync y limite PUBLICO. Compara
Sha256 y Length contra la captura, toma GetUtcNow despues del recibo e inserta referencia
y evidencia juntas. La fecha expresa recepcion en Application, no Move, commit o fsync.
Captura y almacenamiento ocurren fuera de transacciones DB. Finally dispone capturas en
todos los resultados; solo borra temporales propios. Nunca compensa borrando destinos.

### Modelo y migracion

PublicExperienceSubmissionFile reutiliza ContentHash y agrega StoredLengthBytes,
StoredEvidenceAtUtc y StoredEvidenceVersion nullable con setters privados. Factory
CreateWithStoredEvidence exige SHA256 ASCII MAYUSCULA de 64, longitud positiva igual a
SizeBytes, version 1 y UTC. Domain valida forma; Application prueba procedencia del recibo.
La construccion anterior sin evidencia sigue disponible. No dependencia Domain->Application.

Los tres campos usan PropertySaveBehavior.Throw. ContentHash conserva su configuracion
anterior, sin ampliacion de inmutabilidad. CHECK ck_submission_files_stored_evidence:
todos null o todos presentes con IS NOT NULL explicito, version/longitud/formato validos.
No valida hashes de filas sin evidencia. No trigger ni proteccion contra SQL valido o
bulk updates que omitan tracking. Null no decide elegibilidad futura ni invalida contenido.

20261022211442_AddPublicSubmissionStoredEvidence agrega solo tres columnas nullable y
CHECK; Designer/snapshot y SQL revisados. Down elimina esos mismos objetos. Sin DML de
negocio, backfill, indices o tablas nuevas; INSERT de __EFMigrationsHistory es metadata.

### Pruebas y ejecucion

Artefactos: C:/Users/USUARIO/AppData/Local/Temp/sng-d2c3a-8d876024a73d4deca949a6e28bff69ef.
El vector literal usa abc, digest BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD
y fingerprint 381DEE88EE1D3A1EF415C3DF1468AF7169BB14A033683DDF67551941981C23F7.
Se calculo el fingerprint independientemente con PowerShell/.NET SHA256 sobre JSON
canonico literal ensamblado a mano (incluido whatsapp escapado como \u002B5700) y digest
binario de abc, sin ejecutar el canonicalizador de la aplicacion para obtener el esperado.

PublicSubmissionEvidencePostgreSqlTests cubre apertura unica con segunda fuente distinta,
no seek/parciales, vector y roundtrip, replay/conflicto sin cambios, disposicion, longitud
y cancelacion, recibos realmente escritos pero distintos (hash/longitud), nueve mutaciones
EF con excepcion precisa/cero UPDATE, seis grupos parciales/valores invalidos y migracion
desde esquema previo con hash/fingerprint/bytes intactos y campos null. Incluye insercion
sin evidencia tras upgrade y modelo sin diferencias pendientes.

Dos carreras nuevas retienen al ganador antes del commit, observan INSERT perdedor
bloqueado mediante pg_stat_activity y pg_blocking_pids con PIDs distintos y luego verifican
la evidencia ganadora desde otro contexto. No sleeps como prueba, no enqueuer. Finally
libera barrera y drena tareas. Las pruebas existentes de fallos publicos siguen inyectando
en SaveVerifiedAsync real; FailedKeys/hook.Injected y relecturas verifican los limites.

Errores preliminares: build1 0 errores/111 warnings; build2 incremental 0/109. Una generacion
SQL con --no-build antes de terminar build2 no encontro la migracion en el ensamblado
anterior; se repitio tras compilar y genero migration.sql, sin aplicar nada a DB habitual.
Preflight: 92 passed, 2 failed, 0 skipped. Una expectativa antigua de un solo destino en
carrera se sustituyo por barrera explicita, una fila y ambos destinos retenidos. Otro
fallo fue concurrencia entre inventario global de temporales y intake en otra clase;
la coleccion de filesystem ahora deshabilita paralelismo, sin relajar assertions.
Se reforzo ademas cancelacion durante lectura y rango temporal del recibo. Las ejecuciones
posteriores se solapan: no sumar sus conteos. No errores de compilacion en esos builds.

Primer rebuild completo posterior al preflight: 0 errores/112 warnings, incluyendo un
CS8604 nuevo en callback del test de recibo distinto. Se anoto Arg<string>() como no-null,
coherente con el key de la llamada. Rebuild final (final-build2.log): 0 errores/111 warnings;
comparacion normalizada por archivo/mensaje contra rebuild de D.2c.2 sin diagnosticos nuevos.
No se modificaron fuentes productivas/tests despues de este ultimo rebuild.

Comandos: dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental
--verbosity minimal. Tests con dotnet test Tests/CotizadorBackend.Tests.csproj --no-build
--filter <seleccion> --logger "trx;LogFileName=final.trx" --logger "console;verbosity=normal"
--results-directory <artifact-root>/results. Filtro final exacto en final-filter.txt:

```text
FullyQualifiedName~PublicSubmissionEvidence|FullyQualifiedName~VerifiedContentStorageTests|FullyQualifiedName~PublicExperienceIntakeTests|FullyQualifiedName~PublicExperienceSubmissionPostgreSqlTests|FullyQualifiedName~PublicExperienceSubmissionModelTests|FullyQualifiedName~PublicExperienceSubmissionHttpTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict|FullyQualifiedName~PublicPaths_PreserveOriginalsAndPotentiallyPublishedCopies|FullyQualifiedName~CompetingIntakes_RetainWinnerAndLosingBatch|FullyQualifiedName~PublicExperienceRequirementPostgreSqlTests
```

Seleccion ampliada por dependencias modificadas: helpers de intake/preparacion y storage,
host HTTP y transacciones publicas. No se repite indiscriminadamente la suite privada ni
los protocolos de jobs. Los casos HTTP completos exigen evidencia en SubmissionFile pero
cuatro null en RequirementFile, Pending y cero jobs/attempts/AI2. Preparacion productiva,
repositorio, canonicalizador, respuestas HTTP, core y publicacion permanecen sin cambios.

### Inventario respecto al baseline inmediato

Existentes modificados:
- Application/PublicExperience/PublicExperienceIntakeService.cs: captura, fingerprint, recibo y finally.
- Domain/PublicExperience/PublicExperienceSubmission.cs: factory y tres campos de evidencia.
- Infrastructure/DependencyInjection.cs: registro de la primitiva de captura.
- Infrastructure/Persistence/Configurations/PublicExperienceSubmissionConfiguration.cs: campos/Throw/CHECK.
- Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs: modelo generado.
- Infrastructure/Storage/SealedContentCopy.cs: entrada inicial explicita; core privado reutilizado.
- Tests/Api/Integration/PublicExperienceHttpPostgreSqlTests.cs: DI de captura y evidencia del original publico.
- Tests/Application/PublicExperience/PublicExperienceIntakeTests.cs: constructor y doble con recibo real.
- Tests/Infrastructure/Persistence/PublicDocumentPersistenceFileSafetyPostgreSqlTests.cs: constructores y assertions de evidencia/cleanup/inyecciones.
- Tests/Infrastructure/Persistence/PublicExperienceRequirementPostgreSqlTests.cs: dependencia de captura en helper.
- Tests/Infrastructure/Persistence/PublicExperienceSubmissionPostgreSqlTests.cs: dependencia y barrera real de insercion.
- Tests/Infrastructure/Persistence/VerifiedContentStorageTests.cs: captura inicial y aislamiento de filesystem.
- docs/document-byte-identity-contract.md: esta seccion, sin reescribir evidencia historica.

Nuevos:
- Application/Common/Abstractions/Storage/IInitialContentCapture.cs.
- Infrastructure/Storage/InitialContentCapture.cs.
- Infrastructure/Persistence/Migrations/20261022211442_AddPublicSubmissionStoredEvidence.cs.
- Infrastructure/Persistence/Migrations/20261022211442_AddPublicSubmissionStoredEvidence.Designer.cs.
- Tests/Domain/PreQuotes/PublicSubmissionEvidenceTests.cs.
- Tests/Infrastructure/Persistence/PublicSubmissionEvidencePostgreSqlTests.cs.
- Tests/Infrastructure/Persistence/PublicSubmissionEvidenceRacePostgreSqlTests.cs.

### Resultado final y matriz A-E

| Ejecucion | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Preflight (fuentes build2) | 92 | 2 | 0 |
| Final (fuentes final-build2) | 150 | 0 | 0 |

Final dura 3.9575 minutos, sin promesa de latencia productiva. Sustituye casos solapados
del preflight; no se suman. Distribucion: 33 intake, 12 HTTP unit/integration, 14 preparacion
PostgreSQL, 27 seguridad de archivos publicos, 19 evidencia PostgreSQL, 37 primitivas,
4 dominio, 1 concurrencia del repositorio, 2 HTTP completos y 1 modelo EF. Se corrigieron
los dos fallos preliminares descritos; no queda fallo o skip en la seleccion final.

| Criterio | Evidencia ejecutada |
|---|---|
| A: captura/fingerprint | SingleOpening_LiteralFingerprint_RealStorage_ReplayConflictAndCleanup; fuente parcial no seekable que cambiaria en segunda apertura; abc/hash/fingerprint literales, bytes reales y fuente dispuesta; cinco casos InitialCapture y regresiones de primitivas |
| B: persistencia | Nueve EfRejectsNineMutations_WithoutUpdate; CHECK con seis grupos parciales y valores invalidos; MigrationPreservesOriginalHashFingerprintAndBytes_WithoutBackfill; roundtrip independiente |
| C: replay/concurrencia | Dos UniqueIdRace con PIDs/Lock; snapshot completo sin cambios tras replay, conflicto y dos destinos retenidos con una sola fila; repo concurrente con barrera y tres carreras de file-safety |
| D: conservacion | 24 PublicPaths incluyendo segundo archivo fallido, cancelacion, rollback y commit real con respuesta perdida; inyecciones comprobadas y cero deletes; captures sin residuos; dos recibos discrepantes reales no persisten referencia |
| E: compatibilidad | 33 intake, 12 HTTP y dos multipart completos con preparacion/replay; evidencia original nueva y RequirementFiles null, Pending/cero jobs/attempts/AI2; 14 regresiones de preparacion y modelo EF |

Carreras nuevas finales: mismo contenido winner PID 13272 / loser 33672; contenido distinto
33104 / 29892. Ambos casos observan Lock y blocker identificado antes de soltar commit.
La perdida de respuesta es una inyeccion despues del commit, no caida fisica de red/proceso.

Logs: build1.log, build2.log, final-build.log, final-build2.log; migration-generation.log,
migration.sql; preflight.log/results/preflight.trx; final-tests.log/results/final.trx;
preflight-filter.txt, final-filter.txt, final-list.log. Vector reproducible en
vector-canonical.json (sin salto final para hashear) y vector-fingerprint.txt. Manifiestos
initial.csv/final.csv y checked-storage-roots.txt en el artifact root indicado.

### Limpieza, integridad y limites

PostgreSQL 17 aislado en <artifact-root>/data, loopback 55455 y usuario audit temporal.
Solo bases cotizador_backend_test_* creadas/eliminadas por fixtures. Variable de conexion
child-local retirada en finally; servidor parado en finally en ambas ejecuciones. Consulta
final a pg_database: cero bases de fixtures. pg_ctl status: no server running. Cluster
parado y logs se retienen como evidencia. initdb.log, preflight-server.log, final-server.log,
remaining-databases.txt y server-status.txt registran estas comprobaciones.

Se verificaron ausentes las 33 raices storage registradas por el TRX final y cero directorios
sng-sealed-*, sng-content-tests-*, sng-receipt-unit-* o sng-public-evidence-* restantes.
Solo limpieza de recursos propios, sin barrido/borrado de destinos promovidos ni huerfanos.

Integridad final: 858 rutas, 13 existentes cambiados y 7 nuevos respecto de initial.csv;
cero ausentes, 838 rutas anteriores byte-identicas. El inventario explica todo el delta.
Rama/HEAD preservados; 42 tracked modificados, 82 untracked, cero staged. git diff --check
pasa con avisos LF/CRLF. No cambios ajenos detectados ni revertidos. Bin/obj generados
no son fuentes del manifiesto. Solo documentacion cambio despues del rebuild final.

Veredicto: **D.2c.3a implementado y APTO para revision de cierre dentro de este alcance**.
No DB habitual, AI2/Gemini real, commit/push/merge/deploy ni procesamiento automatico.
No se certifica Unix, aislamiento frente a procesos privilegiados/del mismo usuario,
storage desplegado, cuotas, fsync, crash fisico o estabilidad futura de bytes. No se
atribuye inmutabilidad SQL al CHECK ni a EF fuera de tracking.

D.2c.3b pendiente: preparacion publica con recibo persistido en RequirementFile y E/S
fuera de locks Submission, revalidacion y concurrencia propias. Evidencia por ejecucion,
publicacion v2, politica de historicos/OBSERVED_NOW, activacion y limpieza de huerfanos
siguen requiriendo autorizacion separada. CreatePreQuoteDocumentService no se modifica.

## 13. D.2c.3b: preparacion publica verificada fuera de locks

Implementacion del 2026-10-22. Esta seccion sucede al estado historico de la seccion 12;
no reescribe sus resultados. No activa procesamiento ni modifica intake, fingerprint,
Answers, endpoints, esquema, publicacion o reglas comerciales.

### Contrato y decisiones

El contraste inicial confirmo copia/verificacion dentro del callback PrepareLockedAsync,
SaveAsync sin recibo y nuevos RequirementFiles sin evidencia. D.2a ya conservaba destinos
ante errores; no se restauro compensacion destructiva.

Gap encontrado en la regresion HTTP ampliada: el test historico aceptaba dos IDs presentes
aunque Requirement perteneciese a otra PreQuote. El usuario autorizo expresamente rechazar
esos enlaces inconsistentes con PUBLIC_EXPERIENCE_REQUIREMENT_LINKS_INCONSISTENT (HTTP 500).
Enlaces parciales tampoco se reparan. Enlaces validos historicos, cancelados o reemplazados
siguen reconociendo su identidad original, sin exigir current, recopia o backfill. Los
otros resultados HTTP existentes se conservan; no hay nuevo endpoint ni codigo de error.

El repositorio sustituye el callback por ReadAsync, CommitAsync y FailAsync. Cada fase usa
un ApplicationDbContext y conexion propios, clonando las opciones existentes. No utiliza
ChangeTracker.Clear ni guarda cambios pendientes del caller. El test deja un cambio
legitimo rastreado y comprueba que permanece pendiente y no aparece en la DB independiente.

ReadAsync toma Submission FOR UPDATE y obtiene el snapshot. El orden posterior es User,
Client, Project FOR SHARE; al reconocer enlaces, PreQuote, Requirement y sus Files FOR
SHARE; luego SubmissionFiles FOR SHARE en orden estable de ID. Relee la ruta del owner
despues de proteger las filas. Finalizacion y registro de errores repiten ese protocolo.
La transaccion de lectura termina antes de abrir storage. Snapshot incluye identidades,
contexto, estado, fingerprint, Answers y todos los metadatos/evidencia de fuentes ordenadas
por Sequence. La asignacion sigue usando el owner persistido del Project, no un actor
humano supuesto; una nueva preparacion exige usuario, Client y Project activos.

Fuera de DB, el servicio abre cada origen una vez. IVerifiedContentCopy/VerifiedContentCopy
es un adaptador pequeno a SealedContentCopy.PrepareAsync con digest esperado obligatorio;
no utiliza CaptureInitialAsync. Su registro DI no activa ningun workflow. Conserva buffer,
handle retenido, vista unica, permisos y disposal de D.2c.1. SaveVerifiedAsync consume esa
vista, con el limite publico existente de 20 MiB. Compara recibo contra copia, hash y
longitud esperados. StoredEvidenceAtUtc se toma despues del recibo, no de la fuente.

Fuentes v1 requieren marcadores coherentes. Fuentes con los tres marcadores null conservan
compatibilidad: hash hexadecimal valido, normalizacion a mayusculas exclusivamente en
memoria y comparacion de bytes/longitud. El original y fingerprint nunca se actualizan.
Esto no certifica retrospectivamente su almacenamiento ni elegibilidad durable futura.

Identidades candidatas se crean en memoria antes de copiar. PreQuote.Create admite un ID
reservado opcional no vacio para materializar ese candidato; las llamadas anteriores y
validaciones de serial permanecen. ReserveNextSerialAsync se ejecuta solo en el commit
final con el mecanismo existente. No se inserta un grafo provisional para obtener claves.
CommercialLineBridge sigue Essential. CreatedAtUtc se ajusta a microsegundos antes de usarlo;
archivos sucesivos suman un microsegundo, preservando Sequence en CreatedAtUtc/Id y principal.

CommitAsync relee y compara el snapshot bajo locks antes de insertar PreQuote, Requirement
Pending, archivos con CreateWithStoredEvidence y enlaces/fecha, todo en una transaccion.
Si existe ganador valido, lo reconoce aunque sus claves fisicas sean distintas. Un snapshot
cambiado no publica candidato. FailAsync revalida antes de atribuir el error; un exito
concurrente prevalece, y un error anterior no impide que una preparacion valida complete.
Cancelacion se propaga, sin convertirla en exito ni escribir con otro token por conveniencia.

Una excepcion antes de intentar commit permite rollback del grafo y registro protegido de
error. Una vez intentado commit, los scopes se disponen antes de observacion independiente
read-only con presupuesto real de diez segundos. Comprueba snapshot, enlaces, grafo completo,
orden, metadatos normalizados por la misma factory y evidencia. Candidato propio probado
devuelve exito no replay; otro ganador probado devuelve reconocimiento. Ausencia, bloqueo o
error no prueban rollback: se propaga el fallo original, sin compensacion ni retry automatico.
Los locks de observacion no implican DML. El presupuesto no es una promesa de latencia total.
No se mantiene ninguna transaccion durante hashing, copia, almacenamiento o proveedor.

### Pruebas y matriz de aceptacion

PublicPreparationVerifiedPostgreSqlTests, dividido en dos archivos partial, utiliza PostgreSQL
y LocalFileStorage reales. Las mutaciones SQL de actividad/metadatos/enlaces historicos son
instrumentacion de revalidacion, no nuevos escritores de negocio. Fuentes/copias privadas se
disponen; destinos promovidos, incluso perdedores, se conservan. No barrido de huerfanos.

| Criterio | Evidencia focal |
|---|---|
| A: evidencia/compatibilidad | Incorporation en v1 y null historico: abc y SHA256 literal BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD, bytes/DB, orden, owner/serial/Bridge, original intacto; HistoricalCompleted conserva enlace reemplazado y null; modelo sin cambios pendientes |
| B: fallos/conservacion | Nueve CopyFailures: hash igual longitud, corto/largo, hash invalido, recibos reales discrepantes, segundo fallo y cancelacion; SaveResponseFailure verifica rollback; cinco CommitUncertainty incluyen commit real, metadatos historicos normalizados, observer indisponible y bloqueo hasta vencer token; cero DML posterior |
| C: coordinacion | PausedStorage permite FOR UPDATE NOWAIT de otra conexion; tres CompetingPreparers solapan transacciones finales con PIDs distintos, Lock y pg_blocking_pids; ocho Revalidation prueban actividad y cambio documental antes/despues; snapshots independientes y finally liberan/drenan |
| D: HTTP/seguridad | PartialOrCrossedLinks sin reparacion; multipart/replay con ambos recibos, Pending y cero jobs/attempts/AI2; regresiones de preparacion, conservacion publica, HTTP y primitivas; replay historico cancelado/reemplazado sin procesamiento |

La seleccion HTTP completa incluye tambien procesamiento manual con proveedor simulado:
cero llamadas AI2 se afirma de intake/preparacion/replay, no de esos casos manuales distintos.
El host y helpers de pruebas se adaptaron a la firma nueva. Consumidor productivo del
repositorio: PreparePublicExperienceRequirementService; su consumidor publico conserva
ExecuteAsync. No cambio en el adaptador de procesamiento ni cliente AI2.

### Ejecuciones y correcciones preliminares

Artifact root: C:/Users/USUARIO/AppData/Local/Temp/sng-d2c3b-b665766341014c8ba64e91184ab8a27e.
Baseline inmediato initial.csv: 858 rutas, rama feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 42 tracked modificados, 82 untracked, cero staged.
No se usa HEAD como sustituto del worktree inicial.

| Corrida | Passed | Failed | Skipped | Interpretacion |
|---|---:|---:|---:|---|
| preflight.trx | 35 | 22 | 0 | NewContext solicitaba DbContextOptions como servicio no registrado; corregido a IDbContextOptions conservando opciones/interceptores |
| second.trx | 83 | 2 | 0 | Expectativa antigua de excepcion tras commit propio demostrado y conteo de archivos sin candidato perdedor; adaptadas a reconocimiento y conservacion con barrera |
| final.trx (preliminar pese al nombre) | 156 | 3 | 0 | Clase HTTP ampliada detecto enlaces cruzados aceptados, conteo concurrente antiguo y prueba que esperaba borrado compensatorio |
| verification.trx | 159 | 1 | 0 | 32 casos nuevos pasaron; un mock HTTP intermitente configuraba Returns a traves de un await |

La ultima fila motivo la autorizacion explicita de 500, comprobacion de todos los bytes
retenidos en concurrencia y prueba de persistencia HTTP con dos destinos tras fallo, cero
deletes y tres tras reintento HTTP explicito. No es retry automatico. Se preservaron las
assertions de un solo grafo y errores. Revision adicional corrigio la comparacion de nombre
y MIME historicos normalizados en reconciliacion; nuevo caso after-normalized demuestra
reconocimiento tras commit real. No se rebajaron guards ni se borraron escenarios.

build1.log tuvo diez errores de compilacion en adaptacion de tests; se corrigieron firmas
y namespace. build2.log incremental: 0 errores/93 warnings; build3.log incremental:
0 errores/107 warnings. Esas diferencias de alcance no son fixes de warnings. Builds
incrementales y completos posteriores estan retenidos. final-build.log,
final-build2.log y final-build3.log: 0 errores y 111 warnings en rebuild completo. El ultimo
incluye las fuentes de verification.trx. No se afirma que warnings existentes hayan sido corregidos.
verification.trx dura 6.3786 minutos. Su unico fallo fue
PrivateUploadOnPublicProject_PersistsEvidenceWithoutChangingPublicSource: NSubstitute
perdio el contexto de la llamada al evaluar actor.UserId.Returns((await query).Id).
La consulta ahora termina antes de configurar el mock; assertions funcionales intactas.
Este fallo no se presenta como resultado de una baseline independiente. final-build4.log
recompila todo: 0 errores, 111 warnings, sin diferencias de diagnosticos respecto de
final-build3.log al ignorar numeros de linea. Solo ese test cambio desde verification;
la clase HTTP completa se repite despues de ese rebuild. Las corridas se superponen y
NO se suman. Resultado final se registra debajo tras completarse.

Build: dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental --verbosity minimal.
Tests: mismo proyecto, dotnet test --no-build --filter <seleccion>, --logger
"trx;LogFileName=verification.trx" --logger "console;verbosity=normal" y --results-directory
<artifact-root>/results. Filtro conservado en final-filter.txt:

```text
FullyQualifiedName~PublicPreparationVerifiedPostgreSqlTests|FullyQualifiedName~PreparePublicExperienceRequirementTests|FullyQualifiedName~PublicExperienceRequirementPostgreSqlTests|FullyQualifiedName~PublicPaths_PreserveOriginalsAndPotentiallyPublishedCopies|FullyQualifiedName~PublicExperienceSubmissionHttpTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests|FullyQualifiedName~VerifiedContentStorageTests|FullyQualifiedName~PreQuoteTests|FullyQualifiedName~PublicExperienceSubmissionModelTests
```

La ampliacion responde a firmas/helpers compartidos, DI HTTP y factory de PreQuote. No se
repite la suite privada o de jobs sin dependencia. Logs por corrida y TRX quedan disponibles.

### Inventario focal

Existentes modificados respecto de initial.csv:
- Application/PublicExperience/PreparePublicExperienceRequirementService.cs: tres fases y recibos.
- Application/PublicExperience/PublicExperienceRequirementPreparation.cs: snapshot y contratos internos.
- Domain/PreQuotes/PreQuote.cs: identidad candidata opcional, serial existente.
- Infrastructure/DependencyInjection.cs: adaptador de verificacion explicita.
- Infrastructure/Persistence/Repositories/PublicExperienceRequirementPreparationRepository.cs: contextos dedicados, locks, commit/reconocimiento/fallo.
- Tests/Application/PublicExperience/PreparePublicExperienceRequirementTests.cs: doble por fases.
- Tests/Api/Integration/PublicExperienceSubmissionHttpTests.cs: adaptacion de doble.
- Tests/Api/Integration/PublicExperienceHttpPostgreSqlTests.cs: evidencia, contrato autorizado y conservacion.
- Tests/Infrastructure/Persistence/PublicExperienceRequirementPostgreSqlTests.cs: recibos y carrera con ambos candidatos.
- Tests/Infrastructure/Persistence/PublicDocumentPersistenceFileSafetyPostgreSqlTests.cs: evidencia y reconocimiento tras commit.
- Tests/Domain/PreQuotes/PreQuoteTests.cs: identidad opcional y validaciones anteriores.
- docs/document-byte-identity-contract.md: estado y evidencia de este bloque.

Nuevos:
- Application/Common/Abstractions/Storage/IVerifiedContentCopy.cs.
- Infrastructure/Storage/VerifiedContentCopy.cs.
- Tests/Infrastructure/Persistence/PublicPreparationVerifiedPostgreSqlTests.cs.
- Tests/Infrastructure/Persistence/PublicPreparationCoordinationPostgreSqlTests.cs.

### Limites

Sin migracion nueva, backfill, triggers, evidencia por ejecucion, v2, jobs, worker, enqueue,
retry/recovery automaticos, frontend ni activacion. Las pruebas no certifican estabilidad
futura del origen, atomicidad DB/filesystem, consumo remoto, crash fisico, fsync, cuotas o
despliegue. Solo Windows ejecutado; no se atribuye evidencia runtime Unix. CreatePreQuoteDocumentService
y limpieza de huerfanos siguen excluidos. La politica durable de historicos sigue pendiente.

### Evidencia de coordinacion e integridad

verification.trx registra success/success winner PID 30976 y blocked 31104; success/error
31908/3552; error/success 35796/30024. Todos observan PostgreSQL Lock y blocker concreto
antes de liberar la transaccion ganadora. Durante E/S pausada, PID 4992 adquiere Submission
FOR UPDATE NOWAIT. Reconciliacion: observer 17352 bloqueado por 2968, con cancelacion del
token productivo observada antes de liberar el bloqueo. No son sleeps como prueba ni
simulaciones de caida fisica; las excepciones de commit son inyecciones controladas.

Manifiesto: 862 rutas finales, 12 existentes cambiadas, cuatro nuevas, cero ausentes y
846 anteriores byte-identicas. El inventario explica todo el delta respecto de initial.csv.
Rama/HEAD preservados; 47 tracked modificados, 86 untracked, cero staged. No cambios ajenos
revertidos. Migraciones/configuraciones EF, intake, fingerprints, core/cliente AI2, stores
de jobs, primitivas existentes y servicio legacy excluido permanecen iguales al baseline.
Bin/obj son artefactos de build, no fuentes del manifiesto. git diff --check pasa con
avisos LF/CRLF. No commit, push, merge, despliegue ni DB habitual.

Se comprobaron ausentes las 56 raices de storage registradas en verification.trx (32 nuevas
y 24 de conservacion publica); checked-storage-roots.csv conserva rutas/resultados. No
borrado general de archivos ni limpieza de huerfanos. Cluster PostgreSQL 17 aislado en
<artifact-root>/data, loopback 55457, usuario audit temporal. Las migraciones existentes
se aplican solamente a bases cotizador_backend_test_* creadas/eliminadas por fixtures.
Cada corrida retira su variable de conexion child-local y detiene el servidor en finally.
Logs de servidor por corrida, initdb.log, builds y TRX se retienen como evidencia.

### Resultado final

final-http.trx: **25 passed, 0 failed, 0 skipped**, 1.9368 minutos, clase
PublicExperienceHttpPostgreSqlTests completa con fuentes de final-build4.log. Reemplaza
sus 25 casos de verification.trx, incluido el unico fallo. Los otros 135 casos de esa
seleccion no cambiaron y no se repitieron: 32 preparacion nueva, 24 conservacion publica,
37 primitivas, 14 preparacion existente, 12 HTTP unitarios, cinco unitarios de preparacion,
diez dominio PreQuote y un modelo EF. Por tanto los 160 casos distintos de la seleccion
tienen evidencia aprobada final, sin sumar ejecuciones solapadas ni afirmar una corrida
unica de 160 verdes. No fuente productiva/test cambio despues de final-build4; solo docs.

Comando de repeticion: dotnet test Tests/CotizadorBackend.Tests.csproj --no-build --filter
FullyQualifiedName~PublicExperienceHttpPostgreSqlTests, con los mismos loggers/directorio
y LogFileName=final-http.trx. Logs final-http.log y final-http-server.log; build final
final-build4.log. El rebuild conserva 111 warnings y cero errores; no diagnosticos nuevos
respecto del anterior al normalizar numeros de linea. No queda fallo o skip en los casos
finales afectados; esto no representa toda la suite del repositorio.

Consulta final pg_database: cero bases de fixture (remaining-databases.txt). pg_ctl status:
no server running (server-status.txt). Cluster parado y logs retenidos, no servicio activo.
Sin directorios sng-sealed-*, sng-public-preparation-*, sng-content-tests-* ni
sng-private-public-* restantes. Manifiestos initial.csv/final.csv conservados bajo el
artifact root. Proveedores simulados, ninguna llamada AI2/Gemini real.

Veredicto: **D.2c.3b implementado y APTO para revision de cierre en el alcance verificado**.
La excepcion HTTP aprobada queda explicita; los demas limites de esta seccion permanecen.
Evidencia por ejecucion, publicacion v2 e integracion automatica requieren bloques propios.

### Correccion focal: cancelacion durante reconciliacion

2026-10-22. La auditoria posterior identifico un unico bloqueante por inspeccion: el
caller podia cancelar despues de entrar al catch de WriteAsync y recibir exito cuando
la observacion independiente confirmaba el commit. Los resultados anteriores no cubrian
esa ventana. Este registro conserva la historia y documenta su reproduccion y correccion.

Baseline inmediato: feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 862 rutas, 47 tracked modificados, 86 untracked,
cero staged. Artefactos fuera del repositorio:
C:/Users/USUARIO/AppData/Local/Temp/sng-d3b-cancel-cf65cf77a83a446abb43ee6d367ae9c5.
initial.csv contiene hashes del worktree completo, incluidos untracked, no solo HEAD.

Antes de cambiar produccion se agrego CallerCancellationDuringObservation_PreservesCommitAndReplay
con tres casos. CommitFailure inyecta perdida de respuesta despues de un commit real;
ObservationGate pausa la consulta real a Submission mediante TaskCompletionSource, solo
cuando el callback posterior al commit habilita el observador. Se verifican contextos
EF distintos, token independiente y operacion pendiente antes de cancelar. No sleeps ni
resultado de observacion fabricado. La variante fallida lanza IOException en esa consulta
despues de liberar la barrera; la variante confirmada ejecuta la lectura real. Los flags
de inyeccion/lectura se comprueban. Finally libera la barrera y drena la tarea.

Reproduccion con repositorio productivo byte-identico a initial.csv:
reproduction-build.log: rebuild completo, 0 errores/111 warnings.
reproduction.trx: **1 passed, 2 failed, 0 skipped**, 36.7684 segundos. Fallos DELIBERADOS:
con cancelCaller=true/failObservation=false el resultado fue Success, sin excepcion;
con true/true fue IOException en vez de OperationCanceledException. El control false/false
paso. Antes de la assertion que fallo, ambos casos ya comprobaron commit/bytes/replay
intactos y cero escrituras adicionales. No se revirtio ningun cambio para reproducirlo.

Correccion productiva: una sola instruccion ct.ThrowIfCancellationRequested() inmediatamente
despues de await ObserveAsync y antes tanto del retorno confirmado como de propagar el
fallo no demostrado. Produce OperationCanceledException con el token original, tambien
si el observador devuelve null tras fallar. Al estar dentro del catch, no se recaptura por
ese mismo catch ni cae en FailAsync. El consumidor HTTP existente excluye del catch los
tokens cancelados. No cambia su contrato ni agrega manejo HTTP.

La observacion mantiene su token independiente y presupuesto de diez segundos sin enlazarlo
al caller. Cancelacion del caller no significa rollback: grafo y destinos confirmados se
conservan. Esta es una comprobacion cooperativa final; no ofrece atomicidad entre comprobar
el token, una cancelacion posterior y entregar la respuesta. No retry, reparacion o borrado.

Los tres casos releen desde contextos independientes: un unico quote/Requirement/archivo,
enlaces y fecha de preparacion, error null, hash literal de abc, longitud/version/fecha de
evidencia y bytes reales. Comparan el snapshot completo posterior al commit, cuentan cero
INSERT/UPDATE/DELETE tras el commit y una unica apertura/escritura de storage, cero deletes.
Un token nuevo reconoce replay con los mismos IDs, sin nuevas copias ni cambios de grafo.
No jobs/attempts; el servicio de preparacion y este harness no disponen de cliente AI2 ni
invocan proveedores. El caso no cancelado conserva exito no replay en la primera llamada.

Inventario exclusivo de esta correccion:
- Infrastructure/Persistence/Repositories/PublicExperienceRequirementPreparationRepository.cs: comprobacion final del token original.
- Tests/Infrastructure/Persistence/PublicPreparationCoordinationPostgreSqlTests.cs: tres casos, barrera de observacion y captura del ContextId del commit en el interceptor existente.
- docs/document-byte-identity-contract.md: este registro.

Build final: dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental
--verbosity minimal; final-build.log: 0 errores/111 warnings. Tests: mismo proyecto con
--no-build, --logger "trx;LogFileName=final.trx", --logger "console;verbosity=normal" y
--results-directory <artifact-root>/results. Filtro exacto en final-filter.txt:

```text
FullyQualifiedName~CallerCancellationDuringObservation|FullyQualifiedName~PublicPreparationVerifiedPostgreSqlTests.CommitUncertainty|FullyQualifiedName~PublicPreparationVerifiedPostgreSqlTests.CopyFailures|FullyQualifiedName~PublicPreparationVerifiedPostgreSqlTests.SaveResponseFailure|FullyQualifiedName~PublicExperienceRequirementPostgreSqlTests.CancellationDuringCopy
```

La seleccion incluye observacion indisponible/bloqueada hasta expirar el token productivo,
respuesta perdida sin cancelacion, rollback y cancelacion previa. No repite suites enteras
ni audita otros stores por analogia. Sin cambios a replay historico/500, autorizacion,
lifecycle, limites, evidencia, migraciones, contratos, jobs o publicacion.

Resultado final: **19 passed, 0 failed, 0 skipped**, 2.4460 minutos. Incluye los tres
casos nuevos: los dos con caller cancelado propagan exactamente OperationCanceledException
asociada al token original, y el no cancelado conserva Success. Los tres comprueban replay
posterior con token nuevo, mismos enlaces y cero copias/escrituras adicionales. Los dos
fallos de reproduction.trx son deliberados y anteriores al fix; no se suman a esta corrida.
No fuentes productivas ni tests cambiaron despues del rebuild final; solo este informe.
Los diagnosticos del rebuild final coinciden con los de reproduccion al ignorar lineas.

En final.trx, observacion confirmada cancelada: contexto de commit
466b9766-2a95-4a08-abe4-be0ddfadbba8, observador 0dcbbc54-5697-4b7d-a2cd-7c4caa2734ba;
observacion fallida cancelada: 900ee742-67ee-4d1d-8a39-574f7539c99b y
0d64d036-b32c-4029-9b97-0390888bd611. Son contextos reales distintos, no resultados
simulados. La regresion de bloqueo observo PID 22092 bloqueado por 4224 hasta cancelar
el token independiente de produccion. Las esperas de seguridad del test no sustituyen
ese presupuesto ni constituyen una promesa de tiempo de respuesta.

PostgreSQL 17 temporal propio en <artifact-root>/data, loopback 55461, usuario audit.
Cada corrida retiro la variable de conexion child-local y paro el servidor en finally.
remaining-databases.txt registra cero bases cotizador_backend_test_*; server-status.txt
confirma no server running. Se retienen cluster parado, initdb.log, reproduction-server.log,
final-server.log, logs de build/test y results/reproduction.trx y results/final.trx.
Las 18 raices registradas por los casos focales finales estan ausentes (storage-check.csv);
cero sng-sealed-* y sng-public-preparation-* restantes. Solo limpieza propia de fixtures,
sin barrido de destinos productivos ni huerfanos.

Manifiestos initial.csv/final.csv: 862 rutas, exactamente tres existentes cambiadas,
cero nuevas/ausentes y 859 anteriores byte-identicas. Rama/HEAD y estado de trabajo
preservados: 47 tracked modificados, 86 untracked, cero staged. git diff --check correcto
con avisos LF/CRLF. Ningun trabajo anterior revertido; sin migraciones nuevas, DB habitual,
AI2/Gemini real, commit, push, merge, despliegue ni activacion automatica.

Veredicto focal: **bloqueante de cancelacion reproducido y corregido; APTO para cierre de
este hallazgo de D.2c.3b**. La evidencia nueva cubre esta ventana y sus regresiones, no una
reauditoria de todos los stores. Solo Windows; no crash fisico, fallo de red real ni
garantia atomica frente a cancelacion posterior a la comprobacion cooperativa final.

## 14. D.2c.4a: contrato y modelo de evidencia por ejecucion

Alcance 2026-10-23: modelo estructural, configuracion EF y migracion aditiva. No preparador,
coordinador, registro productivo, DI, autorizacion operativa, lectura de bytes, proveedor,
v2, enqueue ni activacion. La seccion 3.B original es propuesta historica: este bloque NO
implementa sus FK compuestas/circular, triggers, OBSERVED_NOW ni RequiredPublicationVersion.

### Contrato y representacion

RequirementExecutionEvidence es la cabecera y el contrato inmutable del conjunto;
RequirementExecutionDocument es su detalle. No se agrega una segunda jerarquia de DTOs.
La cabecera conserva Id, RequirementId, JobId, ExecutionRequestId, ProcessingAttemptId,
LeaseGeneration, ActorUserId, PrimaryDocumentId, ManifestVersion, ManifestHash,
VerifiedAtUtc, RegisteredAtUtc y FileCount. El detalle conserva EvidenceId, FileId,
Ordinal, OriginalFileName, ContentType, StorageKey, FileCreatedAtUtc, DeclaredLengthBytes,
ExpectedSha256, ExpectedLengthBytes, StoredEvidenceAtUtc, StoredEvidenceVersion,
ObservedSha256 y ObservedLengthBytes. Ninguna distincion conceptual se elimina.

Create copia la coleccion, exige conjunto no vacio, FileId unico, ordinales de entrada
contiguos desde cero, principal en cero, identidad de evidencia uniforme y conteo exacto.
No setters publicos ni operaciones para cambiar el conjunto. La vista de detalles es
readonly y se presenta por ordinal persistido, incluso si EF materializa en otro orden;
no se ordena por nombre, digest o GUID. Se rechaza una entrada desordenada, no se corrige.

Solo version de manifest 1, incorporacion 1 y generacion 1 (la admitida hoy por el job).
Todos los UUID son no vacios; hashes ASCII MAYUSCULA SHA256 de 64 caracteres; las tres
longitudes son positivas e iguales y ambos hashes coinciden. No se representa evidencia
desconocida. ActorUserId se recibe explicitamente: el modelo no deriva ni autoriza actor.
Los valores esperados representan StoredSha256/StoredLengthBytes, pero la factory no puede
demostrar su procedencia ni que alguien haya copiado/verificado bytes. Eso queda en D.2c.4b.

Todas las fechas requieren UTC y precision exacta de microsegundos: ticks % 10 = 0.
Se rechaza precision mas fina, nunca se trunca para fabricar coincidencia. Formato unico:
`yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'`. El futuro preparador usara el snapshot persistido despues
del roundtrip PostgreSQL, no timestamps anteriores a este. No se exige orden entre
VerifiedAtUtc y RegisteredAtUtc ni con relojes de incorporacion: no son un unico reloj.

### Manifest y vector independiente

ManifestHash se calcula internamente; Create no recibe hash de cabecera. JSON UTF-8 sin
BOM ni whitespace de presentacion, propiedades ordinalmente ordenadas y arrays por ordinal.
UUID D minuscula, enteros invariantes, Utf8JsonWriter/JavaScriptEncoder.Default, sin
normalizacion de nombres/MIME/keys. El writer focal vive en Domain; reutilizar el helper
de publicacion de Application invertiria la dependencia. Publicacion v1 no se modifica.

Orden de propiedades de cabecera: actorUserId, executionRequestId, files, jobId,
leaseGeneration, manifestVersion, primaryDocumentId, processingAttemptId, requirementId.
Cada detalle incluye, en este orden: contentType, declaredLengthBytes, expectedLengthBytes,
expectedSha256, fileCreatedAtUtc, fileId, observedLengthBytes, observedSha256, ordinal,
originalFileName, storageKey, storedEvidenceAtUtc, storedEvidenceVersion.
EvidenceId/Id, VerifiedAtUtc y RegisteredAtUtc se excluyen; FileCount es derivado de files,
no una propiedad adicional del JSON. Cambiar identidad del registro o sus fechas no cambia
el hash. Cambiar identidad de ejecucion o datos documentales cubiertos si lo cambia.

Vector literal completo en RequirementExecutionEvidenceTests.LiteralVector_Determinism_AndImmutableInput
y en vector.json del artifact root. Identidades UUID terminadas en 2/3/4/5/6/7 para
Requirement/job/request/attempt/actor/file, generacion/version 1; abc, longitud 3,
a.pdf, application/pdf, documents/a y fechas documentales 2026-10-23T12:00:00.123456Z.
Hash literal del documento: BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD.
SHA256 del manifest literal:
`40EA9D6F43D9F189485C06557375AD5C76C4FC84317E8D881C5666F196889BC2`.
Se calculo por PowerShell/.NET SHA256 sobre JSON manual (sin newline final), sin invocar
la factory ni el canonicalizador. La prueba compara el JSON completo y ese digest literal.

### Garantias de persistencia y limites

Tablas core.requirement_execution_evidence y core.requirement_execution_documents.
UUID -> uuid; long -> bigint; short -> smallint; ordinal/conteo -> integer; fechas ->
timestamptz; hashes varchar(64); nombre/MIME/key varchar(255/100/500). Todos NOT NULL.
EF configura PropertySaveBehavior.Throw para todos los campos persistidos. Los campos
clave tambien estan sujetos al rechazo normal de modificacion de claves de EF.

PK cabecera id; detalle (evidence_id,file_id). Unicos job_id y processing_attempt_id en
cabecera, (evidence_id,ordinal) en detalle. Sin unicidad global de hash. Seis FK simples
RESTRICT: cabecera a Requirement, Job, Attempt, User; detalle a cabecera y RequirementFile.
CHECK focales verifican UUID no vacios, versiones/generacion, formatos, igualdad de
longitudes/hashes, ordinal no negativo y conteo positivo. No triggers ni alternate keys.

La DB NO garantiza correspondencia compuesta Requirement/job/request/attempt/actor/file,
principal perteneciente al conjunto, totalidad/conteo exacto, ordinales contiguos o hash
recalculado. SQL valido puede cambiar filas o borrar detalles: Throw no protege SQL ni
DELETE explicito. El coordinador futuro debe cargar el conjunto completo y revalidarlo
bajo locks, incluyendo procedencia de incorporacion, snapshot, autoridad y lease. Un
objeto materializado por EF no es prueba de esas verificaciones ni de bytes consumidos.

Migracion 20261023091820_AddRequirementExecutionEvidence con Designer/snapshot. Up crea
solo estas dos tablas y seis indices; Down elimina solo detalle y cabecera. migration.sql
revisado: CREATE TABLE/INDEX e INSERT de metadata __EFMigrationsHistory, sin DML de negocio,
ALTER de tablas anteriores, backfill, cambios de estados ni nuevas filas de evidencia.
Los documentos sin incorporacion permanecen sin evidencia; no cambia su lifecycle o
procesamiento legacy ni se define su admision a enqueue.

### Verificacion del bloque

Baseline inmediato: feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 47 tracked modificados, 86 untracked, cero staged.
862 rutas en initial.csv; no se usa HEAD como baseline de contenido.
Artifact root: C:/Users/USUARIO/AppData/Local/Temp/sng-d4a-97b0e97bef194492a6b204fcf90cdbc0.
Generacion inicial de migracion con startup Infrastructure: compilo, pero no pudo resolver
DbContextOptions y no genero migracion. Se repitio con startup Api, --no-build y configuracion
design-only (conexion ficticia loopback puerto 1, sin usar DB habitual); genero correctamente
la migracion y despues migration.sql. Variables de configuracion retiradas en finally.

build1.log incremental: 0 errores/95 warnings. build2.log incremental: 0/110, incluidos dos
EF1002 en SQL interpolado de las nuevas pruebas. Se reemplazaron esas dos llamadas por
ExecuteSqlInterpolatedAsync parametrizado. Los SQL de restricciones usan nombres/valores
controlados exclusivamente por fixtures. No se agrego supresion de warnings.

Preflight con fuentes build2: **55 passed / 4 failed / 0 skipped**, 59 casos, 6.1071 minutos.
Tres fallos fueron assertions que comenzaban en SaveChanges, despues de que EF ya habia
rechazado CurrentValue sobre una clave (Id/EvidenceId/FileId). El Act ahora incluye asignacion
y save; conserva InvalidOperationException exacta, nombre de propiedad, cero UPDATE y
snapshot independiente. El cuarto fallo fue un segundo attempt activo del fixture de
RESTRICT, incompatible con ux_requirement_processing_attempts_active_requirement_id.
Ese attempt aislado ahora se termina con Start/Cancel antes de guardarlo; no cambia indice
ni assertion de FK. No son fallos deliberados ni una reproduccion de baseline historico.

Un rebuild intentado mientras preflight retenia assemblies produjo avisos MSB3061 de
archivos bloqueados; se interrumpio y no es build de verificacion (final-build.log).
Despues de finalizar tests se ejecuto rebuild completo sin concurrencia:
`dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental --verbosity minimal`.
final-build2.log: **0 errores, 111 warnings**. Comparacion por archivo/mensaje, ignorando
lineas, contra final-build.log de sng-d3b-cancel-cf65cf77a83a446abb43ee6d367ae9c5: sin
diagnosticos nuevos. Ninguno nombra los nuevos archivos. No se atribuye mejora de warnings
a la diferencia entre builds incrementales y completos.

La revision agrego un roundtrip multarchivo con carga deliberada en orden inverso y uso
del ordinal persistido al exponer/canonicalizar. La migracion tambien conserva un archivo
historico sin incorporacion, ademas de archivos con evidencia, jobs y Requirements.
Estos refuerzos y correcciones preceden al rebuild final. No fuentes productivas ni tests
cambiaron despues; solo se completo documentacion. No se suman ejecuciones superpuestas.

Comando final: mismo proyecto, `dotnet test --no-build --filter <seleccion>` con
`--logger "trx;LogFileName=final.trx" --logger "console;verbosity=normal"`
y `--results-directory <artifact-root>/results`. Seleccion exacta:

```text
FullyQualifiedName~RequirementExecutionEvidence|FullyQualifiedName~PreparedJobPublicationTests|FullyQualifiedName~JobPublicationReceiptTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

Inventario focal respecto de initial.csv (no HEAD), seis archivos nuevos:
- Domain/PreQuotes/RequirementExecutionEvidence.cs: contrato, entidades y canonicalizacion focal.
- Infrastructure/Persistence/Configurations/RequirementExecutionEvidenceConfiguration.cs: dos configuraciones.
- Infrastructure/Persistence/Migrations/20261023091820_AddRequirementExecutionEvidence.cs.
- Infrastructure/Persistence/Migrations/20261023091820_AddRequirementExecutionEvidence.Designer.cs.
- Tests/Domain/PreQuotes/RequirementExecutionEvidenceTests.cs: vector e invariantes.
- Tests/Infrastructure/Persistence/RequirementExecutionEvidencePostgreSqlTests.cs: modelo y limites reales.

Tres existentes modificados:
- Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs: modelo generado.
- Tests/Api/Integration/PublicExperienceHttpPostgreSqlTests.cs: cero cabeceras/detalles automaticos.
- docs/document-byte-identity-contract.md: estado y registro de este bloque, historia preservada.

Sin cambios a primitivas/storage, servicios/DI, repositorios, estados del job, publicacion
v1, fingerprints, contratos HTTP, permisos, lifecycle, frontend o reglas comerciales.
La configuracion EF se descubre mediante el mecanismo existente del contexto, sin nuevo
registro de coordinador. Las factories solo se invocan desde tests en este bloque.

### Resultado final, integridad y cierre

| Ejecucion | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Preflight, fuentes build2 | 55 | 4 | 0 |
| Final, fuentes final-build2 | 103 | 0 | 0 |

Final: 6.6416 minutos, sin promesa de latencia productiva. Incluye 28 casos de dominio,
32 PostgreSQL nuevos (27 mutaciones EF y cinco escenarios), 33 PreparedJobPublication,
ocho JobPublicationReceipt y dos HTTP completos/replay. Sustituye los 59 casos preliminares
solapados; no son 162 casos distintos. Ningun fallo ni skip final. No se ejecuto toda la suite.

| Aceptacion | Evidencia final ejecutada |
|---|---|
| A: contrato | JSON/digest literales independientes, determinismo, copia de coleccion, rechazo de conjuntos/identidades/incorporacion invalidos; cambios cubiertos alteran hash y tiempos/Id operacionales no |
| B: persistencia | Roundtrip completo y multarchivo cargado inversamente; CHECK/FK con SQLSTATE/nombre exactos; unicos job/attempt/file/ordinal; las seis FK restringen DELETE; 27 cambios EF con InvalidOperationException exacta, cero UPDATE y relectura independiente |
| B: compatibilidad | Downgrade al esquema previo y upgrade conservan jobs, Requirements y archivos con/sin incorporacion; cero filas de evidencia creadas por migracion; HasPendingModelChanges=false |
| C: limites | SQL valido permite repetir ManifestHash en ejecuciones distintas, cambiar correspondencias/conteo/principal/hash y borrar detalles; esos actos no pasan por factory/EF Throw |
| D: no activacion | Dos multipart/replay conservan links y evidencia de incorporacion, Requirement Pending, cero jobs/attempts/cabeceras/detalles/AI2; busqueda estatica sin consumidores productivos; regresiones v1 intactas |

Los registros de fixtures son estructurales. No prueban autorizacion, fencing, lease vivo,
verificacion fisica, proveedor invocado ni resultado publicado. La igualdad de contenido
documental entre dos ejecuciones no implica igualdad de su manifest, que incluye sus IDs;
el test adicional de SQL con hash repetido prueba especificamente ausencia de unicidad global.

PostgreSQL 17 propio en <artifact-root>/data, loopback 55463, usuario audit temporal.
Solo fixtures cotizador_backend_test_*. Variable de conexion child-local retirada y servidor
parado en finally de ambas corridas. remaining-databases.txt registra cero bases de fixtures;
server-status.txt: no server running. La salida 1 de pg_ctl status significa servidor parado,
no fallo de tests (dotnet test final devolvio 0). Se retienen cluster parado y logs.
Comprobacion posterior: cero directorios sng-sealed-* en temp. No barrido ni borrado de
archivos ajenos; este modelo no crea copias ni archivos de ejecucion. Solo Windows ejecutado.

Artefactos: initial.csv/final.csv, status.txt/final-status.txt, build1.log, build2.log,
final-build.log (interrumpido), final-build2.log; migration-generation.log,
migration-generation2.log, migration-script.log/migration.sql; vector.json/vector-digest.txt,
consumer-search.txt; preflight.log/results/preflight.trx, final-tests.log/results/final.trx,
initdb.log, preflight-server.log, final-server.log, remaining-databases.txt/server-status.txt.

Integridad final: 868 rutas, seis nuevas, tres existentes cambiadas, cero ausentes;
859 anteriores byte-identicas respecto a las 862 de initial.csv. Inventario anterior
explica todo el delta. Rama/HEAD preservados; 47 tracked modificados, 92 untracked, cero
staged. git diff --check pasa con avisos LF/CRLF existentes. Todo trabajo previo conservado.
No DB habitual, proveedor real, commit, push, merge, despliegue ni procesamiento automatico.

Veredicto: **D.2c.4a implementado y APTO para revision de cierre en el alcance probado**.
No se valida almacenamiento desplegado, bytes de ejecucion, crash fisico, consumo remoto,
autorizacion o concurrencia de un registro productivo que todavia no existe. D.2c.4b debe
implementar, bajo autorizacion propia, snapshot completo confiable, preparacion/verificacion
de bytes, registro y revalidacion transaccional con locks/lease/cancelacion/reconciliacion.
No se anticipa ese coordinador para probar este modelo ni se aprueban historicos, backfill,
OBSERVED_NOW, triggers, publicacion v2 o envios/retries por la existencia de una evidencia.

## 15. D.2c.4b: preparacion verificada y registro explicito

Alcance 2026-10-23: un recorrido invocable explicitamente desde backend, no conectado a
DI, endpoints, intake, procesamiento ni jobs automaticos. No envio ni proveedor; tampoco
publicacion v2, renovacion, reasignacion, enqueue, retry o recovery. El modelo, migracion,
vector y canonicalizador de D.2c.4a permanecen intactos. No migracion nueva.

### Entrada, autoridad y snapshot

ExecutionDocumentPreparationStore.PrepareAsync recibe AssignedJobExecution, actor explicito
y token. No acepta manifest, metadatos ni digests del caller. Abre contextos dedicados a
partir de DbContextOptions, sin limpiar ni guardar tracking de otro consumidor.

Autoriza usuario activo y owner vigente del Project, sin bypass para Admin ajeno. Ajeno
recibe NotFound sin evidencia/metadatos. Verifica ruta Submission/Client/Project/PreQuote/
Requirement/job, request/generation/attempt exactos, correlacion y fechas de inicio.
Para preparacion nueva exige Client/Project activos, Requirement current/Processing,
job Running, lease owner exacto y lease vivo por clock_timestamp(), sin cancelacion ni
otro intento activo incompatible. El intento exacto debe estar Processing.

Locks en orden: Submission SHARE si aplica; User, Client, Project SHARE; PreQuote,
Requirement, Job UPDATE; Attempts UPDATE ordenados por Id; Files SHARE ordenados por
CreatedAtUtc/Id; despues evidencia. Header/detalles se leen completos bajo los locks de
Requirement/job que serializan este escritor. No proteccion frente a escritores SQL que
ignoren el protocolo. Se releen las relaciones tras proteger filas; no se usa tracking viejo.

Snapshot documental completo, no vacio, con incorporacion v1 completa. Se reutilizan las
constantes publicas de CreateRequirementService: 10 documentos, 20 MiB por archivo y
100 MiB totales. Las tres longitudes y ambos hashes deben coincidir; formatos, UUIDs y
precision documental pasan las validaciones de D.2c.4a. No se infiere hash ni se completa
incorporacion ausente. Este rechazo afecta solo al nuevo recorrido, no al legacy.

### Copias, reloj y registro

Tras disponer la transaccion inicial se abre cada original una vez y se dispone ese
stream. IVerifiedContentCopy usa el adaptador existente a SealedContentCopy.PrepareAsync:
digest esperado obligatorio, copia privada, buffer acotado, mismo handle y vista unica.
No segunda lectura del original ni conjunto entero en RAM. Fallo de cualquier archivo
dispone tambien las copias anteriores; no registra un subconjunto ni borra IFileStorage.

VerifiedAtUtc se lee de PostgreSQL despues de completar todas las copias, mediante un
contexto breve sin transaccion durante E/S. RegisteredAtUtc se obtiene del reloj DB en
la transaccion nueva. Las fechas documentales son las del roundtrip persistido; no se
truncan. El contrato exige microsegundos exactos y UTC, sin orden entre relojes distintos.

La transaccion final repite locks/autorizacion/validaciones, compara ruta, conjunto, orden,
metadatos e incorporacion exactos y materializa header/detalles desde las copias reales.
Un SaveChanges inserta ambos. El ultimo guard es UPDATE condicional del job con
lease_generation = lease_generation: sin cambio funcional, compatible con configuracion
actual. Exige IDs/request/Requirement/attempt, attempt_count=1, Running, owner/generation,
cancellation_requested_at_utc null y lease_expires_at_utc > clock_timestamp(). Debe afectar
una fila; cero provoca rollback del conjunto. No SaveChanges posterior al guard.
No cambia actividad, estados, contador, enlaces de intento, archivos ni grafo comercial.
La garantia de vigencia corresponde al guard, no al instante fisico del commit.

### Resultados y propiedad

- Registered: evidencia confirmada y PreparedExecutionDocuments, propietario de las mismas
  copias verificadas. El caller debe DisposeAsync; no significa permiso de enviar.
- AlreadyRegistered: evidencia exacta existente, sin paquete. No nuevas copias en replay
  inicial; si otro gano durante E/S, se disponen las copias perdedoras.
- Conflict: evidencia previa incompatible o inconsistente; no se repara ni sobrescribe.
- Indeterminate: commit intentado cuyo resultado no se pudo demostrar; sin paquete.
- Rejected: fallo seguro diferenciado (autoridad, actividad, contexto/identidades, estado,
  intento, lease, cancelacion durable, snapshot, incorporacion, contenido o persistencia).
  Un token cancelado propaga OperationCanceledException, no un resultado exitoso.

El paquete tiene constructor interno, evidencia inmutable y OpenRead(fileId) sobre una
vista unica por documento. Conserva ordinal/principal; no publica ruta, FileStream o
handle escribible, no reabre por nombre. No reconstruible desde DB. Disposicion repetida
es idempotente; nuevas aperturas tras disponer fallan. No garantiza lecturas/disposal
concurrentes: el caller debe drenar lecturas. No CanSend, contador de envios o exactly-once.

Antes de transferir el paquete, el coordinador dispone todas las copias en cualquier
salida sin paquete. Fallos de disposal se registran con tipo de error, sin ruta/contenido;
se intenta disponer el resto. Un sink de logging defectuoso tampoco sustituye el resultado.
No scanner, compensacion de destinos ni limpieza de huerfanos. Crash puede dejar residuos.

Reconocimiento autoriza antes de devolver evidencia, reconstruye estructura/hash desde
todos los detalles y compara canonical exacto con el snapshot autorizado, no solo hashes
almacenados. Conserva Id y fechas. Historico puede reconocer sin lease/current ni actividad
Client/Project, como reconocimiento del protocolo existente, pero exige usuario activo,
owner y relaciones exactas; no busca ni copia un sucesor ni habilita otra ejecucion.

Tras commit incierto, el scope fallido ya esta dispuesto. Observacion con contexto nuevo,
token independiente y presupuesto de diez segundos. Compara tambien EvidenceId generado
una vez, VerifiedAtUtc y RegisteredAtUtc de esta invocacion. Ausencia/fallo no prueba
rollback; no retry ni DML de reconciliacion. Solo reconocimiento exacto propio entrega las
copias retenidas. Al resolver observacion se comprueba el token ORIGINAL incluso si fallo:
cancelacion conserva commit y propaga OCE con ese token. Comprobacion cooperativa final,
no atomicidad con una cancelacion posterior y la entrega de respuesta.

### Evidencia e inventario

Baseline inmediato: feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 47 tracked modificados, 92 untracked, cero staged,
868 rutas. Manifiesto initial.csv sobre worktree, no diff contra HEAD.
Artifact root: C:/Users/USUARIO/AppData/Local/Temp/sng-d4b-9921e4cd2ead4de4956e945d3c61da3e.

Cuatro archivos nuevos:
- Application/PreQuotes/ProcessRequirement/ExecutionDocumentPreparation.cs: resultados y paquete propietario.
- Infrastructure/Persistence/Repositories/ExecutionDocumentPreparationStore.cs: recorrido explicito.
- Tests/Infrastructure/Persistence/ExecutionDocumentPreparationPostgreSqlTests.cs: bytes, fallos, fencing y reconciliacion.
- Tests/Infrastructure/Persistence/ExecutionDocumentCoordinationPostgreSqlTests.cs: autoridad, revalidacion, locks y carreras.

Un existente modificado: este documento. No DI, modelo EF, configuraciones, migraciones,
canonicalizador, stores anteriores, storage, servicios productivos, contratos HTTP o frontend.
Las referencias al store nuevo estan solo en sus pruebas; no dependencia de cliente AI2.

Primer rebuild Debug fallo por binarios bloqueados por Api PID 30584: ocho errores de
copia MSB3027/MSB3021, 85 advertencias. No se detuvo ese proceso ni se uso su DB. Se cambio
solo la configuracion de compilacion a D4b, sin editar proyectos. Build1.log completo:
0 errores/112 warnings (incluye campo de test aun sin asignar); build2.log incremental:
0/93. Final-build.log completo: 0 errores/111 warnings. No confundir diferencias de
build incremental con correcciones de warnings existentes. No pruebas sobre build fallido.

Preflight: 29 passed, 0 failed, 0 skipped, 2.4716 minutos. Despues se agrego la carrera
con ambos conjuntos ya copiados y assertions de roundtrip, replay terminal, contexto
independiente y mutacion efectivamente completada. El rebuild final incluye esos cambios
y la proteccion de diagnosticos de limpieza. Las corridas se solapan; no sumar conteos.

Build: dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental
-c D4b --verbosity minimal. Tests: mismo proyecto, --no-build -c D4b, loggers TRX y console
normal; results bajo artifact root. run.ps1 conserva comandos y ciclo del PostgreSQL
temporal. Filtro final:

```text
FullyQualifiedName~ExecutionDocumentPreparationPostgreSqlTests|FullyQualifiedName~RequirementExecutionEvidence|FullyQualifiedName~PreparedJobPublicationTests|FullyQualifiedName~JobPublicationReceiptTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

La seleccion cubre solo D.2c.4b, contrato/modelo D.2c.4a, v1 y dos intake/replay HTTP.
No toda la suite. Proveedores simulados; el store no contiene ni recibe cliente proveedor.

### Resultado final y aceptacion

| Ejecucion | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Preflight, fuentes build2 | 29 | 0 | 0 |
| Final, fuentes final-build | 133 | 0 | 0 |

Final dura 5.1348 minutos; sustituye los casos solapados del preflight, no son 162 casos
distintos. Incluye 30 casos nuevos de coordinador, 28 dominio y 32 PostgreSQL de D.2c.4a,
33 PreparedJobPublication, ocho JobPublicationReceipt y dos intake/replay HTTP. No fallos
ni skips en las pruebas ejecutadas. No fuentes productivas/tests cambiaron despues del
rebuild final, solo documentacion. Sus 111 warnings coinciden por archivo/mensaje con
final-build2.log de D.2c.4a al ignorar numeros de linea; no diagnosticos nuevos.

| Criterio | Evidencia ejecutada |
|---|---|
| A: bytes y propiedad | Public/manual con inicio real, recibos de storage real y snapshot persistido; dos originales abiertos una vez; abc/digest literal, orden/principal, roundtrip completo y timestamps microsegundos; sustitucion del original conserva copia, lector unico, disposal repetido y originales intactos |
| A: fallo parcial | Incorporacion ausente antes de abrir, digest y longitud distintos, segundo origen ausente y cancelacion: cero header/detalles/DML, copias previas dispuestas, cero deletes |
| B: autoridad y revalidacion | Ajeno y Admin ajeno sin metadatos ni aperturas, incluso tras registro; ocho cambios entre copia/commit (actividad User/Client/Project, owner, links, estado, cancelacion, metadatos) con mutacion comprobada y cero DML del store |
| B: E/S fuera de locks | Otra conexion obtiene FOR UPDATE NOWAIT sobre las nueve clases de filas protegidas durante copia; PID 5512 |
| C: atomicidad/fencing | Excepcion tras SaveChanges y expiracion inyectada antes del guard: cero evidencia residual, snapshot funcional intacto; ambos hooks disparados |
| C: concurrencia | Registro/registro y cancelacion en ambos ordenes: PIDs distintos, Lock y pg_blocking_pids antes de liberar ganador; una evidencia o rechazo; ambos conjuntos ya copiados tambien cubiertos, perdedor dispone copias y no recibe paquete |
| D: incertidumbre | Perdida de respuesta tras commit real, observacion en contexto/token nuevos, exito exacto; observer indisponible/bloqueado produce Indeterminate, cero DML de observacion y copias dispuestas; cancelacion original durante observacion confirmada/fallida propaga OCE con token original |
| D: reconocimiento | Replay cero aperturas/escrituras y sin paquete, mismos Id/fechas; conserva reconocimiento despues de terminar el intento; detalle SQL alterado con hash almacenado intacto produce Conflict, sin reparar |
| E: compatibilidad | D.2c.4a y v1 intactos, HasPendingModelChanges=false; dos HTTP completos/replay Pending sin jobs/attempts/evidencia de ejecucion/AI2 automaticos; no consumidor productivo ni registro DI |

Los tests del coordinador parten de un job/intento iniciado realmente para probar este
recorrido; no afirman cero jobs en ese fixture. El store no crea otros ni modifica sus
estados/actividad; snapshots independientes y conteos lo contrastan. La preparacion de
fixtures inserta referencias con recibos reales, no simula nuevas autorizaciones de negocio.

Carreras finales: registro primero 26144/29020; cancelacion primero 4908/15980; registro
frente a cancelacion 23020/30392; ambos conjuntos copiados 36212/26512. Primer PID retiene
lock y segundo espera; pg_blocking_pids identifica al ganador antes de liberar la barrera.
Observacion bloqueada: PID 34584 espera a 11676 hasta cancelar el token independiente
productivo. Barreras explicitas; el polling consulta estado de locks, no demuestra orden
por sleeps. La espera de seguridad del test no reemplaza el presupuesto de diez segundos.

### Limpieza, integridad y limites

PostgreSQL 17 propio en <artifact-root>/data, loopback 55467, usuario audit temporal. Solo
bases cotizador_backend_test_* y migraciones existentes en esas bases. Cada corrida retira
la variable de conexion child-local y detiene el servidor en finally. final-remaining-
databases.txt registra cero fixtures; final-server-status.txt confirma no server running.
La salida 1 de pg_ctl status es servidor detenido; dotnet test final devolvio 0. Se retienen
cluster parado y logs, no servicio. No DB habitual ni proveedor real.

Las 30 raices propias registradas en final.trx estan ausentes (storage-check.csv). Cero
sng-sealed-* o sng-execution-docs-* restantes. Limpieza de fixtures propios, sin scanner
ni borrado de huerfanos. Originales se comprueban antes del teardown; el coordinador nunca
llama DeleteIfExistsAsync. No se detuvo la API ajena que bloqueo el primer rebuild.

initial.csv/final.csv: 868/872 rutas, cuatro nuevas y solo este documento existente
modificado, cero ausentes y 867 anteriores byte-identicas. Rama/HEAD preservados;
47 tracked modificados, 96 untracked, cero staged. El inventario anterior explica todo
el delta. Bin/obj D4b son artefactos ignorados de compilacion, no fuentes. git diff --check
correcto con avisos LF/CRLF anteriores. Sin commit, push, merge, despliegue ni activacion.

Logs: initial-build-note.txt (fallo de copia inicial registrado desde consola), build1.log,
build2.log, final-build.log; preflight-tests.log/results/preflight.trx y final-tests.log/
results/final.trx; initdb.log, preflight-server.log, final-server.log, archivos de bases y
estado por corrida; final-filter.txt, run.ps1, initial.csv/final.csv, storage-check.csv.

Veredicto: **D.2c.4b implementado y APTO para revision de cierre en el alcance probado**.
Solo Windows. No certifica Unix, storage desplegado, permisos ante actores privilegiados,
SQL arbitrario, fsync, crash fisico, consumo remoto ni exactly-once. Perder respuesta se
inyecta despues del commit real, no simula una caida fisica. La comprobacion final del
token no es atomica frente a cancelacion posterior. No se cambia politica legacy/historica,
incorporacion, modelo ni protocolo comercial. Consumo de estas copias, autorizacion viva
antes de enviar y publicacion v2 siguen pendientes de bloques separados.

## 16. D.2c.5a: consumo controlado y resultado asociado

Implementacion focal, 2026-10-23. Sucede al estado historico de la seccion 15, sin reescribir
su evidencia. Sin DI, endpoints, consumidores productivos, publicacion v2, persistencia de
respuesta, terminalizacion de job/intento, enqueue, worker, claim, renovacion, recovery o
retry. No cambia modelo EF, migraciones, prompts, reglas de extraccion o comerciales,
preferencias, pricing, frontend ni procesamiento legacy. La comunicacion del usuario sobre
migraciones aplicadas a la DB habitual no se verifica ni utiliza aqui: todas las pruebas DB
de este bloque usan un cluster temporal propio.

### Inspeccion del cliente y adaptacion

El cliente real es CotizadorAi2DocumentProcessingClient, via IAi2DocumentProcessingClient /
IDocumentProcessingClient.ProcessAsync. DocumentProcessingClientRequest contiene principal,
ProcessingAttemptId, CorrelationId, ProjectId/RequirementId opcionales y Files ordenados.
Cada DocumentProcessingFile contiene ID, nombre, MIME, SizeBytes y stream. El cliente valida
identidades basicas, archivos legibles, longitud positiva y MIME soportado. Construye un
multipart con archivos en ese orden, nombres/MIME recibidos y cabeceras de intento/correlacion.
No abre StorageKey ni rutas. Usa StreamContent sobre NonDisposingStream; el caller conserva
la propiedad de los streams. No rebobina ni bufferiza el conjunto documental en memoria.

El cuerpo de ProcessAsync contiene un SendAsync con ResponseHeadersRead, sin bucle de retry
o hedging. La cadena DI inspeccionada tampoco agrega politicas de retry; esto no demuestra
ausencia de redirects o comportamiento interno del transporte. La respuesta se lee completa
con MaximumResponseBytes existente y se adapta desde JSON. Ai2RequirementExtractionAdapter
asigna las identidades canonicas desde el request: no es una atestacion remota de identidad
ni de consumo completo. El cliente dispone request/response y documentos JSON internos.

SingleRequestAi2Client es una construccion explicita NUEVA, sin DI. Reutiliza ese cliente y
adaptador, pero posee un HttpClient/SocketsHttpHandler sin redirects, cookies, credenciales
automaticas ni handlers de retry/hedging agregados. Constructor de handler de prueba interno
permite interceptar el transporte sin resolver ai2.invalid ni llamar proveedores reales.
Su HttpClient usa Timeout.InfiniteTimeSpan: el timeout vinculado del cliente AI2 conserva
options.TimeoutSeconds (rango existente 1..900 s). MaximumResponseBytes conserva su contrato.
No se ajustan timeouts legacy ni se agrega otro presupuesto de envio.

Se detecto captura de respuesta cruda a disco cuando el entorno es Development. El constructor
del cliente existente agrega captureRawResponse=true opcional; todas las llamadas legacy
conservan ese default y su comportamiento. SingleRequestAi2Client pasa false, tambien en
Development. No se persiste la respuesta desde este recorrido. El propietario del transporte
debe disponerlo despues de sus llamadas; el consumidor solo es propietario del paquete.

### Reserva, autoridad y lectores

ConsumeExecutionDocumentsService recibe PreparedExecutionDocuments real, AssignedJobExecution,
actor explicito y token. No admite EvidenceId, manifest deserializado o streams libres como
sustituto. El propio paquete almacena una reserva mediante Interlocked.CompareExchange.
La reserva ocurre antes de autorizar o abrir lectores, incluso para un token ya cancelado.
No se libera tras rechazo, fallo o cancelacion. Crear otro servicio no reinicia la reserva.
El perdedor devuelve AlreadyConsumed sin tocar lectores, DB, proveedor ni disposal del ganador.

El ganador asume la propiedad y dispone el paquete en todas sus salidas. El caller no puede
disponerlo/leerlo concurrentemente. OpenRead publico se rechaza despues de reservar; solo el
consumidor interno abre las vistas para envio. Un lector previamente reclamado causa rechazo
al preparar el conjunto completo de lectores, antes de invocar al cliente. No se envia un
subconjunto aunque lectores anteriores hayan abierto correctamente.

ExecutionDocumentPreparationStore.Consumption reutiliza ReadLockedAsync y ValidateLiveAsync
de D.2c.4b. Contexto/transaccion nuevos, orden Submission, User/Client/Project, PreQuote,
Requirement, Job, Attempts, Files y evidencia; relectura y autorizacion owner/usuario activo,
sin bypass Admin ajeno. Ademas de estructura/hash recalculado y correspondencias contra DB,
exige evidencia existente exacta al paquete: Id, VerifiedAtUtc, RegisteredAtUtc, ManifestHash
y manifest canonico. Exige actividad Client/Project, current/Processing, Running, request,
attempt/generacion/leaseOwner, intento Processing sin otro activo, ausencia de cancelacion
y lease vivo segun clock_timestamp(). No adopta hashes aportados por caller.

La transaccion es solo lectura con locks, sin SaveChanges, UPDATE de fencing ni cambios de
actividad/estados. Sus scopes terminan antes de abrir lectores o invocar al proveedor.
El consumidor comprueba el token despues de autorizar y justo antes del cliente. Usa orden,
principal, nombres/MIME y longitudes observadas de la evidencia, sin nueva normalizacion.

ExecutionDocumentReadStream entrega una vista no escribible/no seekable y cuenta lecturas
sincronicas y asincronicas en curso. Al cerrar impide nuevas lecturas, espera las iniciadas
y despues dispone la vista subyacente. El consumidor cierra/drena todos los lectores antes
de disponer el paquete. No se usa solo el retorno del transporte como prueba de ausencia de
lecturas pendientes; la prueba de retorno temprano fuerza esa ventana. Esto no espera una
confirmacion del servidor ni prueba que haya consumido todos los bytes. No ofrece lecturas
concurrentes coherentes sobre un mismo archivo ni tiempo acotado si un cliente incumplidor
deja una lectura que nunca termina. No se cierra el handle mientras tal lectura esta activa.

### Resultado y limites

Estados: Rejected antes del cliente, AlreadyConsumed, ValidResponse, InvalidResponse y
ProviderFailure. ClientInvoked distingue si se llego a invocar ProcessAsync, no cuantas
peticiones fisicas o bytes recibio el remoto. Cancelacion original propaga OCE con ese token,
tambien si el cliente devuelve una respuesta tardia o lanza otra excepcion tras cancelarlo.
La comprobacion posterior al cliente es cooperativa, no atomica frente a cancelacion futura.
Un fallo/timeout/cancelacion despues de invocar no afirma que el proveedor no haya ejecutado.

Se conservan las validaciones de PrepareJobPublicationService: outcome Completed/RequiresReview,
StructuredExtraction presente, IDs de documento/intento tanto tipados como en PayloadJson.
No se agregan reglas comerciales o equivalencias. VerifiedExecutionDocumentResult tiene
constructor interno, evidencia exacta inmutable y respuesta serializada privada. ReadResponse
deserializa un grafo independiente por llamada, siguiendo la convencion de PreparedJobPublication;
no retiene streams ni JsonDocument dispuesto. La evidencia incluye IDs de registro/Requirement/
job/request/attempt/generacion/actor, version/hash, principal y manifest completo. El caller
no puede reemplazarla por un manifest libre. No es un recibo firmado del proveedor.

Errores de disposal se registran solo por tipo y se intenta liberar el resto. Un logger que
falla no sustituye el resultado principal. Solo copias propias, nunca DeleteIfExistsAsync,
compensacion, scanner ni limpieza general de originales. La respuesta no se publica ni se
finalizan jobs/intentos. No hay monitor de cancelacion durable durante red: puede ganar
despues del guard y aun existir una respuesta. La futura publicacion debe revalidar autoridad.
DB/red no son atomicos; reserva local por objeto no significa exactly-once distribuido.

### Inventario y verificacion

Baseline inmediato: feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79; 47 tracked modificados, 96 untracked, cero staged,
872 rutas. initial.csv coincide byte a byte con final.csv de la auditoria de cierre D.2c.4b.
Artefactos propios: C:/Users/USUARIO/AppData/Local/Temp/sng-d5a. No se usa HEAD como baseline
del trabajo acumulado ni se revierten cambios anteriores.

Existentes modificados:
- Application/PreQuotes/ProcessRequirement/ExecutionDocumentPreparation.cs: reserva en el paquete.
- Infrastructure/Persistence/Repositories/ExecutionDocumentPreparationStore.cs: partial e interfaz de autorizacion.
- Infrastructure/DocumentProcessing/CotizadorAi2DocumentProcessingClient.cs: captura opcional, default legacy intacto.
- docs/document-byte-identity-contract.md: estado y esta seccion, historia conservada.

Nuevos:
- Application/PreQuotes/ProcessRequirement/ConsumeExecutionDocumentsService.cs.
- Application/PreQuotes/ProcessRequirement/ExecutionDocumentReadStream.cs.
- Infrastructure/Persistence/Repositories/ExecutionDocumentPreparationStore.Consumption.cs.
- Infrastructure/DocumentProcessing/SingleRequestAi2Client.cs.
- Tests/Infrastructure/Persistence/ExecutionDocumentConsumptionPostgreSqlTests.cs.

Los tests nuevos son otra parte de ExecutionDocumentPreparationPostgreSqlTests: reutilizan
fixtures reales de inicio/registro y archivos, no constructores publicos falsos del paquete.
Se incluyen en el filtro de esa clase. Dos documentos con bytes distintos, sustitucion de
origen, orden/principal, longitudes y digest; reserva concurrente con barrera, perdedor sin
disposal y locks NOWAIT de otra conexion mientras el cliente consume. Rechazos de autoridad,
actividad, cancelacion, lease, contexto e identidad/evidencia con cero llamadas/cero DML.
Respuesta ajena tipada/JSON, fallo tras consumir bytes, cancelacion entre lecturas, respuesta
tardia, grafos de respuesta independientes despues del disposal. Inyecciones de disposal y
logger fallidos despues de cerrar recursos comprueban que el resto tambien se libera.

El handler de transporte consume el multipart completo, lo parsea con MultipartReader y
compara documentos, orden, MIME, digest e intento. Mide una invocacion del cliente y una del
handler en cada caso ok/network/redirect/invalid; no mide consumo remoto real ni todos los
comportamientos de sockets/proxies. Redirects deshabilitados se verifican por configuracion
del transporte, no mediante un servidor remoto. En Development se comprueba ausencia de
captura cruda en un directorio temporal propio, restaurando entorno/CWD en finally.

Preflight: rebuild completo -c D5a --no-restore --no-incremental, 0 errores/111 warnings;
30 passed, 0 failed, 0 skipped, 2.0442 minutos. Fue anterior al refuerzo de drenaje y al
test de retorno con lectura pendiente; no se presenta como prueba de esas adiciones.
La ejecucion final y controles de integridad/limpieza se registran al terminar debajo.

### Resultados finales y aceptacion

| Corrida | Passed | Failed | Skipped | Fuentes y alcance |
|---|---:|---:|---:|---|
| preflight.trx | 30 | 0 | 0 | preflight-build, anterior al refuerzo de drenaje |
| final.trx | 112 | 0 | 0 | final-build, 31 nuevos + 30 D.2c.4b + 8 cliente AI2 + 33 PreparedJobPublication + 8 recibo v1 + 2 HTTP |
| transport.trx | 4 | 0 | 0 | transport-build, contador explicito independiente de llamadas al cliente/handler |
| legacy.trx | 178 | 0 | 1 | transport-build, clase legacy real; un caso requiere PostgreSQL y se repite abajo |
| legacy-postgres.trx | 1 | 0 | 0 | transport-build, caso omitido anterior con PostgreSQL temporal |

final.trx dura 5.2532 minutos. El filtro incluia ProcessRequirementCoreServiceTests, pero
ese archivo declara partial ProcessRequirementServiceTests: no selecciono casos por ese
nombre. La lectura del TRX detecto el hueco, antes de declarar compatibilidad, y se ejecuto
la clase real en legacy.trx. Su caso PublicAdapter_PostgreSqlIndependentConnections_
SerializeAdapterAndAuthenticatedCore se omitio por ausencia de variable PostgreSQL y luego
paso en legacy-postgres.trx con el cluster temporal. Este test historico usa EnsureCreated,
no migraciones, exclusivamente en su fixture propio. No se cambio ninguna assertion legacy.

Despues de final.trx solo se reforzaron los cuatro casos de transporte con CountingClient:
cuenta ProcessAsync separadamente del handler y demuestra que el segundo consumo no llama
a ninguno. transport-build.log recompila todo con --no-incremental: 0 errores/111 warnings,
sin diferencias por archivo/mensaje contra el rebuild de la auditoria D.2c.4b al ignorar
numeros de linea. Los otros 108 casos de final.trx no cambiaron ni se repitieron. Los builds
preflight/final tambien fueron completos, 0 errores/111 warnings; no hubo build fallido.
No fuentes productivas o tests cambiaron despues de transport-build, solo documentacion.

transport.trx: 4/0/0 en 30.2944 s; legacy.trx: 178/0/1 en 3.2721 s;
legacy-postgres.trx: 1/0/0 en 4.7507 s. Las corridas superpuestas no se suman: hay 291 casos
distintos con evidencia final aprobada (112 de seleccion ampliada, 179 de la clase legacy),
no una corrida unica de 291 verdes. El skip preliminar queda resuelto, no ocultado. No se
ejecuto toda la suite del repositorio. Las duraciones no prometen latencia productiva.

| Criterio | Evidencia final |
|---|---|
| A: bytes | RealBytes con abc/defg, dos documentos y evidencia/DB reales, origen cambiado y cero reaperturas; cuatro ActualAi2Client consumen multipart completo, comparan SHA256 por ordinal, MIME/nombres/intento; grafo de respuesta independiente |
| B: una oportunidad | Barrera ConcurrentReservation, otro servicio pierde sin disponer ganador; segundo consumo tras exito/fallo/cancelacion/rechazo; PreviouslyClaimedReader rechaza antes de cliente; reserva en el paquete, no en un wrapper |
| C: autoridad | 14 AuthorityRejected con usuario ajeno/Admin, owner, actividad User/Client/Project, cancelacion, lease, estado, request, leaseOwner, links, detalle alterado y fecha de registro incompatible; cero invocaciones/DML y sin paquete reutilizable |
| C: locks | PID 37736 adquiere FOR UPDATE NOWAIT sobre las once clases de filas del protocolo/evidencia durante consumo pausado; no hay transaccion de autorizacion retenida |
| D: respuesta/recursos | Cuatro identidades incompatibles tipadas/JSON; cuatro FailureAndCancellation, incluida lectura pausada y respuesta tardia; CleanupAndLoggerFailures intenta ambos lectores/copias; ClientReturnsWithOutstandingRead observa cierre y espera antes de disponer handles |
| E: compatibilidad | 30 regresiones D.2c.4b con HasPendingModelChanges=false, ocho cliente AI2, 41 publicacion v1, dos HTTP Pending/cero jobs/attempts/evidencia/AI2 automaticos; 179 legacy aprobados al resolver el skip; busqueda sin DI/consumidores productivos |

En transport.trx cada caso ok/network/redirect/invalid registra client invocations=1,
handler invocations=1 y complete multipart documents=2. Ambos contadores permanecen en 1
tras intentar reutilizar el paquete. El handler es local y simulado, no una conexion real
al proveedor. Se prueba la capa de cliente/multipart; no se certifican consumos remotos,
reintentos internos de sockets, proxies o infraestructura ajena.

Las carreras D.2c.4b siguen comprobando PIDs distintos, Lock y pg_blocking_pids antes de
liberar ganador: registro 30036/31544, cancelacion primero 4964/36068, cancelacion despues
10800/29828, ambos conjuntos copiados 31548/3784. Observacion 27752 bloqueada por 17132
hasta agotarse el presupuesto productivo. Lecturas y snapshots independientes comprueban
rollback y efectos; no sleeps como evidencia de orden ni resultados de commit fabricados.

Comandos: dotnet build Tests/CotizadorBackend.Tests.csproj --no-restore --no-incremental
-c D5a --verbosity minimal; tests mismo proyecto con --no-build -c D5a, loggers TRX/console
normal y results bajo artifact root. run.ps1 conserva inicio/parada/variables child-local.
Filtro ampliado exacto (incluye el nombre que no selecciono la clase legacy):

```text
FullyQualifiedName~ExecutionDocumentPreparationPostgreSqlTests|FullyQualifiedName~CotizadorAi2DocumentProcessingClientTests|FullyQualifiedName~ProcessRequirementCoreServiceTests|FullyQualifiedName~PreparedJobPublicationTests|FullyQualifiedName~JobPublicationReceiptTests|FullyQualifiedName~PublicExperienceHttpPostgreSqlTests.Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict
```

Repeticion de transporte: FullyQualifiedName~ExecutionDocumentPreparationPostgreSqlTests.Consume_ActualAi2Client_OneTransportInvocation_ConsumesMultipart.
Legacy: FullyQualifiedName~ProcessRequirementServiceTests. Repeticion PostgreSQL:
FullyQualifiedName~ProcessRequirementServiceTests.PublicAdapter_PostgreSqlIndependentConnections_SerializeAdapterAndAuthenticatedCore.
Preflight selecciono FullyQualifiedName~ExecutionDocumentPreparationPostgreSqlTests.Consume_.

### Integridad, limpieza y pendientes

PostgreSQL 17 propio en <artifact-root>/data, loopback 55471, usuario audit temporal. Solo
bases cotizador_backend_test_*. Cada corrida DB termina retirando variables child-local y
parando el servidor en finally. legacy-postgres-remaining-databases.txt: cero fixtures;
legacy-postgres-server-status.txt: no server running. Se retiene cluster parado, no servicio.
Nunca se detuvo la API ni se consulto/modifico la base habitual. Proveedores solo simulados.

Las 65 raices registradas en final.trx/transport.trx se comprobaron ausentes (storage-check.csv),
sin sng-sealed-* ni sng-execution-docs-* restantes. El preflight tambien dispone fixtures;
no limpieza general ni borrado de destinos ajenos. La disposicion con fallo se inyecta
despues de cerrar el recurso real: prueba preservacion del resultado e intento del resto,
no certifica recuperacion tras fallos fisicos de filesystem. Bin/obj D5a se retienen como
artefactos ignorados. Logs, TRX, CSV por caso y manifiestos quedan en el artifact root.

Manifiestos initial.csv/final.csv: 872/877 rutas, cuatro existentes modificados, cinco nuevos,
cero eliminaciones y 868 anteriores byte-identicos. El inventario explica todo el delta.
Rama/HEAD preservados; 48 tracked modificados, 101 untracked, cero staged. Ninguna migracion,
modelo EF, DI, regla comercial, core legacy o
publicacion v1 editados. git diff --check correcto con avisos LF/CRLF anteriores. Sin commit,
push, merge, despliegue ni activacion. El siguiente bloque requiere autorizacion propia.

Veredicto: **D.2c.5a implementado y verificado en el alcance descrito, sin activacion**.
Solo Windows; no certifica Unix, permisos desplegados, SQL arbitrario, crash fisico, fsync,
disponibilidad entre replicas o exactly-once. Siguen pendientes publicacion v2, validacion
viva al publicar y decisiones de activacion. El resultado asociado no autoriza publicacion,
otro envio ni recuperacion automatica; DB y red siguen sin atomicidad.

## 17. D.2c.5b: publicacion v2 vinculada, sin activacion

Implementacion 2026-10-26. El usuario aprobo expresamente dos decisiones antes de
codificar: conservar el envelope v1 y agregar evidence con el modelo REAL D.2c.4a,
sin ResultId nuevo; y RequiredPublicationVersion inmutable por job, con migracion
aditiva, compatibilidad v1 y creacion explicita v2. Esta aprobacion no comprende
triggers, OBSERVED_NOW, backfill, worker, enqueue, retry ni integracion automatica.
Las propuestas de las secciones 1-9 conservan su caracter historico en lo restante.

### Fuentes de verdad y contrato aplicado

Se inspeccionaron los contratos D.2c.4a/4b/5a y publicacion/transacciones v1. Existian
evidencia persistida, paquete local de uso unico, resultado asociado y publicador
transaccional, pero no un envelope v2 ni politica aprobada de version obligatoria.
Esos dos gaps se resolvieron con las aprobaciones anteriores, no por inferencia.

| Concepto | Representacion y cobertura |
|---|---|
| Ejecucion | RequirementId, JobId, ExecutionRequestId, ProcessingAttemptId, LeaseGeneration y ActorUserId de la evidencia |
| Evidencia | Id, ManifestVersion, ManifestHash, VerifiedAtUtc, RegisteredAtUtc, PrimaryDocumentId, FileCount y Files completos de D.2c.4a |
| Resultado | VerifiedExecutionDocumentResult creado por D.2c.5a; conserva evidencia y respuesta asociadas, sin identidad artificial nueva |
| Publicacion | ContractVersion=sng-job-publication-v2; todos los campos del envelope v1 mas evidence; ContentHash SHA256 del JSON canonico completo |
| Recibo | JobPublicationReceipt existente: IDs de job/request/Requirement/attempt/correlacion/extraccion/propuesta, outcome, FinishedAtUtc, version y hash |
| Transiciones | Las existentes: job Running->Succeeded, attempt Processing->Finished, Requirement procesado y propuesta Draft/RequiresReview; sin confirmacion comercial |

El nodo evidence conserva los nombres PascalCase de las propiedades reales del modelo,
incluidos EvidenceId en cada detalle y StoredEvidenceAtUtc/StoredEvidenceVersion.
No usa el esquema propuesto antiguo de 3.B. Sus fechas se escriben UTC con seis
decimales y Z; sus arrays conservan ordinal. PublicationCanonicalJson aplica el mismo
orden ordinal de propiedades y reglas numericas v1. El manifest D.2c.4a y su hash
no cambian: son distintos del hash del envelope completo, que incluye Id y fechas
operacionales de evidencia. No se reconstruye el recibo desde jsonb ni desde grafo mutable.

PrepareJobPublicationService agrega una sobrecarga que recibe solamente el resultado
asociado, CommercialLine y token. Contexto, manifest y respuesta proceden del resultado.
Reutiliza el mapper/builder anterior; despues comprueba correspondencia de respuesta,
principal, intento, metadatos y evidencia reconstruida. CommercialLine se revalida
contra DB al publicar. PreparedJobPublication conserva constructor privado y FromJson
v1 rechaza evidence; no hay factory publica v2 que acepte respuesta/IDs independientes.
El constructor interno es una frontera del backend de confianza, no contra reflection.
ReadInput de v2 proyecta los campos de negocio al contrato v1 para reutilizar el
materializador; el snapshot, ContractVersion y ContentHash siguen siendo v2.

Vector literal independiente en PreparedJobPublicationV2Tests: JSON completo escrito
como literal y SHA256 calculado con PowerShell/.NET sobre ese literal, sin canonicalizador
productivo: CDB22112FDC00A08FBB5E3A72471513C5893F4FF9643B569DC6DD7A862D79215.
Ese test estructural usa reflection para formar un resultado sintetico; NO demuestra
procedencia D.2c.5a. Los tests PostgreSQL separados recorren preparacion, consumo real
de las copias por proveedor simulado y resultado D.2c.5a antes de preparar/publicar v2.

### Version requerida, persistencia y autorizacion

RequirementProcessingJob.Create conserva v1. CreateForVerifiedPublication crea v2
explicitamente; no se convierte un job existente. AssignedJobStartStore conserva
v1 por defecto y exige seleccionar v2 en su constructor para ese recorrido. Verifica
la version antes de iniciar y en el guard. Preparacion/registro y autorizacion de
envio D.2c.5a exigen ahora v2. Los fixtures D.2c.4b/5a crean jobs v2 explicitamente.
No se agrega consumidor ni registro DI; ninguna ruta productiva comienza a usarlos.
Reconocimiento de inicio v2 exige owner, sin excepcion para Admin ajeno; el contrato
historico v1 del store se conserva.

Dominio rechaza Succeed sin recibo para v2 y recibos cuya version no sea la requerida.
EF Throw impide actualizar RequiredPublicationVersion mediante tracking. Migracion
20261026134938_AddRequiredPublicationVersion agrega una columna varchar(100) NOT NULL
con default v1 y un CHECK: versiones admitidas, recibo compatible y recibo obligatorio
para Succeeded v2. Designer/snapshot incluidos. SQL: ADD COLUMN, ADD CONSTRAINT e
INSERT de metadata EF; sin DML de negocio, triggers ni alteracion de recibos previos.
El default expresa compatibilidad de jobs existentes, no certificacion v2 ni backfill
de evidencia. Solo se aplica en fixtures temporales; nunca en DB habitual.

El CHECK no vuelve inmutable una fila ante SQL arbitrario ni demuestra bytes/autoridad.
Las FK/unique de evidencia siguen siendo las estructurales de D.2c.4a. El coordinador
es quien reconstruye el conjunto completo y hash, compara evidencia y correspondencias,
autoriza y serializa mediante el orden de locks existente. No se agregan FK/triggers
para certificar lo que no pueden probar.

### Transaccion, replay e incertidumbre

JobPublicationStore extiende focalmente el protocolo existente. Autoriza usuario activo
y owner persistido antes incluso del reconocimiento, sin bypass Admin. Una publicacion
nueva exige Project/Client activos, current/Processing, job Running, intento exacto,
lease vivo y ausencia de cancelacion. La evidencia se carga completa bajo los locks
de Requirement/job; se reconstruye y compara el canonico completo con el snapshot.
Luego se comparan referencias e incorporacion actuales. No hay lectura de bytes,
decodificacion, storage ni proveedor dentro de la transaccion.

Grafo, cierre de intento/Requirement, actividad de quote y recibo comparten transaccion.
El ultimo UPDATE exige version requerida y EXISTS de evidencia con identidades/hash,
ademas de los predicates previos y clock_timestamp(); exactamente una fila o rollback.
La comprobacion completa de detalles precede al guard bajo el protocolo de locks.
No SaveChanges posterior, ni tracking que sobrescriba el job detached. La vigencia
corresponde al guard, no a un instante fisico atomico entre DB, commit y red.

Replay v2 compara version/hash persistidos y evidencia completa; conserva sus IDs y
fechas. No exige lease/current/actividad comercial, no compara decisiones comerciales
posteriores ni repara grafo. Si hay sucesor, reconoce la ejecucion antigua sin tocarlo.
Sigue exigiendo usuario activo, owner y relaciones exactas. No requiere spool vivo,
no reconstruye hash desde jsonb, no reabre terminal ni autoriza otro envio. v1 conserva
su canonicalizador, vectores, recibos y reconocimiento sin exigir evidencia nueva.
La version obligatoria impide publicar un snapshot v1 sobre un job v2.

Tras commit incierto se dispone el scope fallido y observa con contexto/token nuevos
y presupuesto existente de diez segundos. v2 solo reconoce su propio commit si tambien
coinciden los IDs de extraccion/propuesta y fecha terminal generados antes del commit,
ademas del recibo y evidencia exactos. Ausencia/fallo produce Indeterminate, no rollback
confirmado. No retry ni compensacion. v2 comprueba el token original despues de observar,
tanto ante confirmacion como fallo, y tras commit exitoso; no transforma cancelacion
en exito tardio. No modifica la semantica historica de cancelacion v1. El check sigue
siendo cooperativo, no atomicidad con cancelacion posterior a la comprobacion.

### Verificacion y trazabilidad

Baseline inmediato de implementacion: 901 rutas no ignoradas, branch
feature/public-experience-intake, HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79;
55 tracked modificados, 124 untracked, cero staged. initial.csv contiene hashes del
worktree real, no se uso HEAD para revertir trabajo previo. Artefactos fuera del repo:
C:/Users/USUARIO/AppData/Local/Temp/sng-d5b-implementation-6f704eb9f8fe4d24bc01226c1c0a68c5.

Preflight independiente previo a aprobar los gaps: 75 passed/1 failed/0 skipped;
fallo de PublicationModelCoverageTests al no clasificar cuatro campos de incorporacion
ya existentes. Se agrego su clasificacion explicita fuera de la huella v1, sin alterar
el manifest ni omitir la assertion. No se atribuye ese fallo a publicacion v2.

build2 fallo por una firma incompleta de ChangeRole en el fixture nuevo; se corrigio
el argumento de fecha. Se sustituyo tambien el bloqueo sincronico del interceptor de
observacion por callback async. Rebuilds completos build4/final: cero errores y 111
advertencias. No se afirma corregir warnings previos por diferencias incrementales.

| Ejecucion | Passed | Failed | Skipped | Interpretacion |
|---|---:|---:|---:|---|
| preflight.trx | 73 | 25 | 0 | Assertion del helper v2 esperaba ArgumentException; FromJson rechaza correctamente con JsonException. Corregida la expectativa exacta, no el parser |
| second.trx | 144 | 0 | 0 | v2 y regresiones de publicacion v1, antes del refuerzo final de reconocimiento y cancelacion |

Las corridas se superponen, no se suman. Los resultados finales se registran debajo;
no constituyen ejecucion de toda la suite.

### Inventario respecto del baseline inmediato

Doce existentes modificados:
- Application/PreQuotes/ProcessRequirement/PreparedJobPublication.cs: snapshot v2 cerrado.
- Application/PreQuotes/ProcessRequirement/PrepareJobPublicationService.cs: sobrecarga desde resultado asociado.
- Domain/PreQuotes/RequirementProcessingJob.cs: version exigida y factory explicita.
- Infrastructure/Persistence/Configurations/RequirementProcessingJobConfiguration.cs: columna, Throw y CHECK.
- Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs: modelo generado.
- Infrastructure/Persistence/Repositories/AssignedJobStartStore.cs: seleccion y guard de version.
- Infrastructure/Persistence/Repositories/ExecutionDocumentPreparationStore.cs: preparacion/envio solo v2.
- Infrastructure/Persistence/Repositories/JobPublicationStore.cs: evidencia, guard y reconocimiento v2.
- Tests/Infrastructure/Persistence/ExecutionDocumentPreparationPostgreSqlTests.cs: fixture explicito v2.
- Tests/Infrastructure/Persistence/JobTerminationTestSupport.cs: opcion v2, default v1 conservado.
- Tests/Infrastructure/Persistence/PublicationModelCoverageTests.cs: clasificacion de incorporacion fuera de v1.
- docs/document-byte-identity-contract.md: aprobaciones y evidencia de este bloque.

Cinco nuevos:
- Application/PreQuotes/ProcessRequirement/PublicationExecutionEvidence.cs.
- Infrastructure/Persistence/Migrations/20261026134938_AddRequiredPublicationVersion.cs.
- Infrastructure/Persistence/Migrations/20261026134938_AddRequiredPublicationVersion.Designer.cs.
- Tests/Application/PreQuotes/PreparedJobPublicationV2Tests.cs.
- Tests/Infrastructure/Persistence/JobPublicationV2PostgreSqlTests.cs.

Sin cambios a DI, endpoints, core sincrono, cliente AI2, prompts, timeouts, reglas
comerciales, Answers, foto, descargas, frontend, primitives/storage ni contratos de
HTTP. La busqueda de referencias muestra la factory v2 solo utilizada por tests;
el overload de preparacion sigue siendo explicito. No hay un nuevo orquestador.

### Matriz de pruebas focales

| Criterio | Casos y propiedades |
|---|---|
| A | LiteralEnvelope, EvidenceIdentityAndTimes, IncompatibleResultAndGraph; JSON/hash independiente, cambio de evidencia, rechazo de respuesta/manifest incompatibles; vectores v1 |
| B | V2_GraphReceiptReplay en cuatro combinaciones con/sin items y outcome Completed/RequiresReview; resultado D.2c.5a real, grafo/recibo y cero confirmacion comercial |
| C | V2_Rejections en catorce escenarios y HistoricalRecognition en cuatro; owner/actividad/identidades/evidencia/referencias/lease/cancelacion; snapshots independientes y cero DML |
| D | V2_Races: publicacion/publicacion y cancelacion en ambos ordenes, barreras, PIDs y pg_blocking_pids observados; ErrorOrExpiryAfterSave: fallo/expiracion antes del ultimo guard, rollback |
| E | CommitResponseLoss: cuatro combinaciones de cancelacion caller/observador indisponible, commit real y token/contexto separados; CommitNotConfirmed: ausencia no se declara rollback confirmado |
| F | Replay tras cambio comercial y successor, contenido distinto rechazado; downgrade bloqueado; EF Throw y CHECK preciso, migracion conserva grafo/recibo v1; regresiones inicio/publicacion/D.2c.4b/5a y dos HTTP Pending/cero activacion |

La expiracion antes del guard se inyecta modificando el vencimiento dentro de la
misma transaccion despues de SaveChanges: demuestra el predicate y rollback, no una
espera fisica hasta vencer. Perdida de respuesta es inyeccion despues de commit real,
no crash ni corte de red. Carreras usan locks observados, no sleeps como prueba.
El limite de diez segundos del observador no es garantia de latencia total.

No se certifican despliegue, SQL arbitrario, permisos Unix, fsync, crash fisico,
storage entre replicas ni exactly-once remoto. Quedan pendientes integracion
autorizada, activacion y decisiones operativas; no se implementan por cerrar v2.

### Resultado final e integridad

| Ejecucion | Passed | Failed | Skipped |
|---|---:|---:|---:|
| final.trx, fuentes final-build.log | 245 | 0 | 0 |
| contract-final.trx, fuentes closure-build.log | 10 | 0 | 0 |

final.trx dura 10.9377 minutos. Distribucion: 95 preparacion/consumo/publicacion v2
(34 de publicacion v2), 28 inicio, 25 publicacion PostgreSQL v1, 12 concurrencia v1,
33 contrato v1, siete contrato v2, seis preparacion v1, ocho recibos, 28 dominio de
evidencia, uno cobertura del modelo y dos HTTP completos/replay. Los 95 usan el
fixture D.2c.4b/5a actualizado explicitamente a v2, sin relajar sus assertions.

Despues se agregaron SOLO tres casos estructurales de incompatibilidad en
PreparedJobPublicationV2Tests; ninguna fuente productiva o prueba anterior de DB
cambio. closure-build.log recompilo todo: cero errores/111 advertencias. Se repitio
la clase de contrato completa: diez passed, 7.3754 segundos. Los siete anteriores
se solapan, por tanto son **248 casos distintos con ultimo resultado Passed**,
cero Failed/Skipped finales, no una unica corrida de 248. executions.csv conserva
las corridas preliminares y sus fallos; final-cases.csv la ultima evidencia por nombre.
Solo documentacion cambio despues del rebuild de cierre.

Build: dotnet build Tests/CotizadorBackend.Tests.csproj -c D5b --no-restore
--no-incremental --verbosity minimal. Tests: dotnet test mismo proyecto -c D5b
--no-build, filtros de final-filter.txt y FullyQualifiedName~PreparedJobPublicationV2Tests
para el cierre; loggers TRX y console normal. run.ps1 conserva el ciclo de PostgreSQL.
migration.sql corresponde a la migracion generada; el test real de upgrade/replay
v1 verifica HasPendingModelChanges=false. No migracion pendiente del modelo.

Carreras v2 finales: publicacion/publicacion winner 3492 y bloqueado 18188;
publicacion primero 28364/16792; cancelacion primero 27964/19984. Cada caso observo
pg_blocking_pids con conexiones distintas antes de liberar la barrera. Los cuatro
casos de incertidumbre verificaron contexto y token independientes; cancelacion
del caller conserva su OperationCanceledException incluso si el observador falla.

PostgreSQL 17 propio en el artifact root, 127.0.0.1:55523, usuario audit; solo bases
cotizador_backend_test_*. final-remaining.txt registra cero fixtures y
final-server-status.txt no server running. Servidor detenido y variables child-local
retiradas en finally. Se retiene el cluster parado y logs como evidencia, no servicio.
Las 93 raices de storage identificadas en final.trx estan ausentes (storage-check.csv);
cero sng-sealed-* o sng-execution-docs-* observados al cierre. No limpieza general.

Manifiestos initial.csv/final.csv: 901/906 rutas; doce existentes modificadas, cinco
nuevas, cero ausentes y 889 anteriores byte-identicas. delta.csv coincide con el
inventario anterior. Rama/HEAD preservados; 55 tracked modificados, 129 untracked,
cero staged. git diff --check=0 con avisos LF/CRLF previos. Bin/obj D5b ignorados
son artefactos de build, no fuentes. Ningun trabajo anterior revertido.

Veredicto: **D.2c.5b implementado y APTO para revision de cierre en el alcance probado,
sin activacion**. No DB habitual, proveedor real, reinicio de API, frontend, commit,
push, merge ni despliegue. La migracion no se instalo en el entorno habitual; antes
de usar ese entorno con el nuevo ensamblado se requiere una actualizacion operativa
autorizada y verificacion de historial/esquema, sin downgrade por rutina. Publicacion
v2 disponible explicitamente no significa extraccion automatica integrada ni activa.

### Complemento de cierre H1/H2 (2026-10-26)

Este complemento resuelve exclusivamente los dos hallazgos de la auditoria
sng-d5b-audit-e732811afcfe4bd5afed7d4f7e558a7f. No reescribe sus resultados ni
convierte sus 207 casos o los 248 historicos en ejecuciones de este complemento.
H1 era un faltante de cobertura terminal v2, no un defecto reproducido. H2 era la
contradiccion del encabezado: ahora delimita la aprobacion de publicacion v2 y
RequiredPublicationVersion a esta seccion, sin aprobar las propuestas restantes.

Solo se modifican este documento y
Tests/Infrastructure/Persistence/JobTerminationPostgreSqlTests.cs. Los dos tests
focales se parametrizan conservando los ocho casos v1 y agregando cinco v2.
JobTerminationTestSupport y su default verifiedPublication=false no cambian.
Los casos v2 seleccionan explicitamente CreateForVerifiedPublication por el helper
existente. Con intento, AssignedJobStartStore realiza el inicio real. Request,
Confirm y FinalizeFailed utilizan JobTerminationService/JobTerminationStore reales.
No cambios productivos, migracion nueva, nuevas carreras ni mejora de reconciliacion.

Nombres de tests en JobTerminationPostgreSqlTests:
- R: Request_OnlyJobChanges_AndReplayPreservesFirstDate.
- F: Finalize_AtomicWithAndWithoutAttempt_AndTerminalReplay.

| Recorrido | Caso v2 (verifiedPublication=true) | Estado y efectos | Resultado v2 / equivalente v1 |
|---|---|---|---|
| A: Queued | R("queued", true) | Request termina Cancelled, sin inicio/intento ni actividad | Passed / Passed |
| B: Running sin intento | F("confirm", false, true) | Request y Confirm; Cancelled, Requirement/actividad intactos | Passed / Passed |
| C: Running con intento | F("confirm", true, true) | Request y Confirm; Cancelled; intento Finished/Cancelled, Requirement Pending | Passed / Passed |
| D: Running sin intento | F("fail", false, true) | AI2_TIMEOUT; Failed, Requirement/actividad intactos | Passed / Passed |
| E: Running con intento | F("fail", true, true) | AI2_TIMEOUT; Failed; intento Finished/Failed, Requirement Failed | Passed / Passed |

Cada lectura posterior usa un contexto independiente del store y AsNoTracking.
Se comprueban version requerida conservada, recibo de publicacion version/hash null,
identidades, correlacion, creacion, inicio, generacion, conteo y enlace al intento.
Queued no inventa inicio/intento; Running sin intento tampoco lo crea. Con intento
se conserva su identidad, actor, correlacion y fechas iniciales; solo ese intento
termina. Se comprueban vigencia/enlaces/creacion del Requirement y sus fechas finales:
con intento UpdatedAtUtc de Requirement y PreQuote coincide con FinishedAtUtc; sin
intento el snapshot completo relacionado permanece intacto. Lease liberada al cierre.

El comando terminal EXACTO se repite con WriteProbe: AlreadyRecognized, recibo igual
(IDs, fechas, estado y codigo), cero INSERT/UPDATE/DELETE y snapshots sin cambios.
Se conserva tambien el replay anterior con leaseOwner liberado y las assertions de
conflicto por otro codigo o cancelacion tras fallo. No actividad ni intentos duplicados.

Un historial ajeno no vacio contiene documento con incorporacion, job/intento
terminados, cabecera/detalle de evidencia, extraccion y propuesta. Se materializa
con factories y constraints reales, sin SQL para fabricar estados ni desactivarlos.
Es un control estructural, NO una nueva prueba de procedencia de bytes/proveedor.
Snapshots independientes conservan ese historial y todos los documentos, evidencias,
extracciones y propuestas antes/despues del cierre y replay. No se crean archivos
fisicos para ese control: la ruta de metadata nunca se abre. El servicio de cierre
no recibe proveedor ni storage; no hay envios, reenvios o compensaciones en el harness.

Artefactos fuera del repo:
C:/Users/USUARIO/AppData/Local/Temp/sng-d5b-h1h2-9b886c2c2e83478d837e7a55c5167e09.
Baseline: 906 rutas tracked/untracked no ignoradas, rama feature/public-experience-intake,
HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79, 55 tracked modificados,
129 untracked, cero staged. No se uso HEAD para reemplazar el trabajo previo.

Build aislado D5bClosure, --no-restore --no-incremental: baseline-build.log y
build2.log completos, ambos 0 errores/111 warnings. warning-delta.csv no encuentra
diferencias por archivo/mensaje normalizando lineas. El build intermedio build.log
fallo por un nombre de metodo incorrecto en el fixture nuevo (MarkProcessing);
se corrigio a StartProcessing, sin tocar produccion ni ejecutar tests de ese build.
Una lectura inicial incompleta de consola sugirio erroneamente assertions ausentes;
rg y el build baseline confirmaron que estaban presentes. No se atribuye un defecto
previo ni se restaura el archivo desde HEAD. El script de integridad tambien se
ajusto para que los avisos LF/CRLF de git no interrumpieran su captura de evidencia.

Una sola corrida de tests, results/terminal.trx: **13 passed / 0 failed / 0 skipped**,
76.47 segundos entre inicio/fin TRX. Cinco v2 nuevos y ocho v1 conservados: los cinco
equivalentes de la tabla mas Request assigned/started/expired. No sumas con corridas
anteriores ni suites amplias. run.ps1 conserva filtro exacto por los dos metodos,
configuracion, loggers y ciclo del servidor; cases.csv conserva nombre/resultado/duracion.
No fuentes de tests cambiaron despues de build2; solo se completo documentacion.

PostgreSQL 17 propio: 127.0.0.1:55531, usuario audit, data bajo artifact root.
Solo migraciones existentes en la base temporal cotizador_backend_test_* del fixture,
con constraints activos. remaining.txt=0; server-status.txt=no server running.
Servidor detenido y variable de conexion child-local retirada en finally. Cluster
parado y logs retenidos. Este complemento no crea almacenamiento ni spools; no habia
sng-sealed-* al comprobar el cierre y no se borro ningun recurso ajeno.

initial.csv/final.csv: 906 rutas, dos modificadas, cero nuevas/ausentes, 904 anteriores
byte-identicas. delta.csv identifica exclusivamente test y documento. Rama, HEAD y
contenido del status iguales (los archivos de captura usan codificaciones distintas).
git diff --check=0, avisos LF/CRLF previos. No cambios a helper compartido, produccion,
modelo, migraciones, configuracion, DI o frontend; bin/obj D5bClosure son ignorados.

Veredicto focal: **H1 y H2 resueltos en el alcance probado**. Ningun defecto productivo
reproducido ni correccion productiva. Sin DB habitual, reinicio de API, AI2/Gemini,
worker, enqueue, retry automatico, integracion/activacion, commit, push, merge o
despliegue. No nueva certificacion de carreras, crash, recovery, hosting, Unix o
consumo remoto. La mejora opcional de reconciliacion sigue fuera de este complemento.

## 18. D.2c.6a: coordinacion explicita de ejecucion asignada

2026-10-26. Numeracion nueva, sin sustituir D.2c.5b ni su complemento H1/H2.
CoordinateAssignedExecutionService compone UNA invocacion sobre un job existente,
Running, con lease asignada, v2 y actor explicito. No crea jobs ni reclama/renueva
leases. No DI, endpoint, worker, scheduler, polling, retry o consumidor productivo.
Los enums de resultado/etapa describen la llamada, no nuevos estados persistidos.

### Contratos y decisiones previas

Se contrastaron StartAssignedJobService/AssignedJobStartStore, preparacion/registro,
ConsumeExecutionDocumentsService, PrepareJobPublicationService, PublishJobResultService,
JobTerminationService/Store, secciones 14-17 y document-transaction-coordination.md.
La matriz previa a editar fuentes se conserva en decisions.txt del artifact root.
Se conserva la exclusion documental/legacy: cualquier historia de job congela las
referencias de esa version; no se adopta un successor ni se amplian capabilities.

| Resultado existente | Accion permitida | Efecto y resultado de coordinacion |
|---|---|---|
| Started | Preparar usando el attempt del recibo | Intento durable, continuar |
| AlreadyStarted | Detener | AlreadyStarted; no retomar ni reenviar |
| Inicio Indeterminate | Detener | Indeterminate; no preparacion ni cierre |
| Rejected por autoridad/identidad/version/lease | Detener | Rejected sin compensacion |
| CancellationRequested de inicio/documentos/consumo/publicacion | ConfirmJobCancellation | Solo confirmacion o reconocimiento valido produce Cancelled |
| Registered con paquete y CommercialLine | Consumir una vez | Evidencia durable; avanzar si respuesta valida |
| AlreadyRegistered sin paquete | Detener | EvidenceAlreadyRegistered; no recrear copias |
| Registro Indeterminate/Conflict | Detener | Indeterminate/Rejected; sin cierre |
| SourceUnavailable | FinalizeJobFailed REQUIREMENT_STORAGE_ERROR | El resultado del finalizador decide el terminal |
| ContentMismatch/MissingIncorporation/InvalidDocuments u otro rechazo | Detener | Rejected; no codigo terminal inventado |
| ValidResponse | Preparar snapshot v2, publicar | Solo resultado asociado D.2c.5a |
| InvalidResponse | FinalizeJobFailed AI_INVALID_RESPONSE | Revalidacion del finalizador |
| ProviderFailure tipado | FinalizeJobFailed con equivalencia existente | Sin retry; no prueba cero consumo remoto |
| Published/AlreadyPublished | Devolver recibo | Published; sin finalizador adicional |
| Publicacion Indeterminate | Detener | Nunca FinalizeJobFailed despues |
| Finalizador Conflict/Indeterminate | Conservar resultado | Rejected/Indeterminate, no terminal anunciado |
| Token original cancelado | Propagar OCE, liberar recursos propios | Sin Request ni compensacion de negocio |

Equivalencias del core conservadas: ServiceUnavailable -> AI2_SERVICE_UNAVAILABLE,
Timeout -> AI2_TIMEOUT, RemoteRejection -> AI2_REQUEST_REJECTED, ServiceError ->
AI2_SERVICE_ERROR, InvalidResponse -> AI_INVALID_RESPONSE. No default que convierta
un valor desconocido en un fallo terminal. La excepcion no clasificada de construccion
de propuesta se detiene sin cierre. Excepciones inesperadas en fronteras mutantes
inicio/documentos/publicacion/finalizacion se tratan conservadoramente como incertidumbre,
no como rollback. El resultado tipado de cada servicio prevalece cuando esta disponible.

Gap deliberadamente no resuelto: no hay codigo aprobado para traducir mismatch,
incorporacion ausente o cualquier excepcion de construccion a Failed. Esas ramas
quedan Rejected, sin nuevas reglas para historicos ni falsificacion de cancelacion.
Pueden dejar Running pendiente de una decision operativa futura. Tambien el token
del caller puede dejar una ejecucion pendiente; este bloque no implementa recovery.

### Composicion y propiedad

Entrada AssignedJobExecution conserva job/request/Requirement/leaseOwner/generation,
mas actor explicito. El coordinador rechaza un ProcessingAttemptId aportado por caller;
solo adopta el attempt retornado por Started. StartVerifiedAsync es una entrada
explicita nueva del servicio/store, selecciona PublicationV2 y reutiliza el mismo
protocolo de inicio y guards. StartAsync/RecognizeAsync y default v1 permanecen.
Dos invocaciones independientes se excluyen mediante ese inicio transaccional,
sin diccionario, mutex distribuido ficticio ni reserva de proceso nueva.

IExecutionDocumentPreparation permite componer el recorrido ya implementado por
ExecutionDocumentPreparationStore desde Application. No es un segundo preparador.
Su resultado agrega CommercialLine nullable procedente del Requirement del snapshot
autorizado inicial, fuera del manifest de evidencia. No es una seleccion del caller,
ni cambia hash/canonicalizacion. Si falta, no se envia. Publish revalida contra DB
como antes, sin inferir autoridad permanente del inicio o del snapshot.

Orden: inicio v2 -> preparacion/registro -> consumo verificado -> preparacion de
publicacion desde VerifiedExecutionDocumentResult -> publicacion transaccional.
No transaccion envolvente. El coordinador no contiene DbContext, storage ni SQL.
Cada servicio conserva sus revalidaciones; no hay finalizacion extra de exito.
Consume recibe el cliente verificado de la composicion explicita, sin alterar AI2,
prompts, timeout o transporte. Los tests usan proveedor simulado que consume bytes.

El coordinador posee el paquete desde que Prepare lo devuelve hasta transferirlo
al consumidor. Si no transfiere (rechazo/token/error), lo dispone en finally.
El consumidor ganador conserva reserva local, drenaje y disposal de D.2c.5a;
el coordinador no dispone recursos del perdedor AlreadyConsumed. La limpieza del
paquete conserva el manejo existente de errores de disposal/logging. Nunca borra
originales. Comprobaciones del token despues de cada await evitan exito tardio,
tambien despues de reconciliacion o commit conocido; siguen siendo cooperativas.

### Inventario y ejecucion

Artifact root:
C:/Users/USUARIO/AppData/Local/Temp/sng-d6a-fbd5094e74e94fb3864508f9d47ff65a.
Baseline: rama feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79, 906 rutas tracked/untracked no ignoradas,
55 tracked modificados, 129 untracked, cero staged. initial.csv registra SHA256.
No instrucciones AGENTS adicionales encontradas. Trabajo previo no revertido.

Nuevos:
- Application/PreQuotes/ProcessRequirement/CoordinateAssignedExecutionService.cs.
- Tests/Infrastructure/Persistence/AssignedExecutionCoordinatorPostgreSqlTests.cs.
- Tests/Infrastructure/Persistence/AssignedExecutionCoordinatorBoundaryTests.cs.

Existentes modificados:
- Application/PreQuotes/ProcessRequirement/StartAssignedJobService.cs: entrada v2.
- Infrastructure/Persistence/Repositories/AssignedJobStartStore.cs: seleccion explicita v2.
- Application/PreQuotes/ProcessRequirement/ExecutionDocumentPreparation.cs: interfaz y CommercialLine de resultado.
- Infrastructure/Persistence/Repositories/ExecutionDocumentPreparationStore.cs: interfaz y procedencia de CommercialLine.
- Tests/Application/PreQuotes/StartAssignedJobServiceTests.cs: doble y validacion de entrada nueva.
- Tests/Infrastructure/Persistence/ExecutionDocumentPreparationPostgreSqlTests.cs: parametros opcionales del fixture, defaults anteriores intactos.
- docs/document-byte-identity-contract.md: esta seccion y referencia inicial.

Build1 fallo con CS0535: el doble NeverCalled no implementaba la entrada nueva.
Se adapto y se verifican tambien sus siete entradas invalidas, sin relajar assertions.
Build2 completo D6a: 0 errores/111 warnings. Preflight: 24 passed/0 failed/0 skipped,
1.7324 minutos. Posteriormente se reforzaron limites y conservacion del successor.
Los resultados finales y controles de integridad se registran al cierre debajo;
no se suman las corridas superpuestas ni se presentan pruebas no ejecutadas como verdes.

### Resultados, limites e integridad de D.2c.6a

| Corrida | Passed | Failed | Skipped | Fuentes |
|---|---:|---:|---:|---|
| preflight.trx | 24 | 0 | 0 | build2, antes de refuerzos |
| final.trx | 89 | 2 | 0 | final-build, 91 casos seleccionados |
| closure.trx | 35 | 0 | 0 | closure-build, coordinador completo |

Los dos fallos de final.trx fueron del fixture ProviderFailure_ExistingSafeCodes:
DocumentProcessingClientResult.Failed rechaza RemoteRejection/ServiceError y lanzaba
ArgumentException; el consumidor existente clasificaba esa excepcion como ServiceUnavailable.
Se corrigio el fixture a RemoteFailure con metadata controlada, manteniendo las dos
expectativas originales. Ningun cambio productivo despues de final-build. closure-build
recompilo todo y closure.trx repitio los 35 casos del coordinador. Los otros 56 casos
de final.trx no cambiaron ni se repitieron. Union final: **91 casos distintos con
ultimo resultado Passed**, cero Failed/Skipped finales; NO una corrida unica de 91
ni suma 24+89+35. executions.csv conserva los fallos y cases.csv la ultima evidencia.

final-build: 0 errores/111 warnings, 57.90 s; closure-build: 0/111, 13.75 s.
Ambos usan --no-incremental --no-restore -c D6a. warning-delta.csv compara el cierre
con build2 del complemento H1/H2 por archivo/mensaje normalizando lineas: cero
diferencias. No se atribuye correccion de warnings a tiempos de compilacion.
final.trx: 5.6932 minutos; closure.trx: 4.5063 minutos. No promesa de latencia.

La seleccion final comprende 35 coordinador, 28 inicio PostgreSQL, siete validacion
del servicio de inicio, dos preparacion publica/manual, cuatro reconciliacion de
registro, uno concurrencia de copias, uno drenaje de lectura pendiente, cuatro
cliente/multipart simulado, uno limpieza/logger fallidos, cuatro publicacion v2 y
replay comercial, uno rechazo de version, dos HTTP intake/replay y uno core manual.
Son regresiones de dependencias tocadas y no activacion; no suites historicas completas.
run.ps1 y final-filter.txt/closure-filter.txt conservan comandos y seleccion precisa.

| Criterio | Evidencia ejecutada |
|---|---|
| A: exito | Success_PublicManual_Review_AndNoResend: cuatro combinaciones, archivos reales/recibos reales, un intento/conjunto/proveedor/publicacion, bytes abc, version/hash/IDs/estados concordantes, sin confirmacion comercial |
| B: repeticion | AlreadyStarted_DoesNotResume; reinvocacion terminal despues de crear successor, snapshots intactos; ExistingRegistrationWithoutPackage conserva evidencia real, no crea copias propias ni envia |
| B: concurrencia | ConcurrentIndependentStarts: dos coordinadores, PIDs distintos y pg_blocking_pids; perdedor AlreadyStarted, cero copias propias, ganador pausado con copias vivas, una llamada proveedor |
| C: autoridad | Rejection_NoStartOrProvider: ajeno, Admin ajeno, usuario inactivo, v1, leaseOwner invalido e intento aportado; cero DML/proveedor/aperturas. LostAuthorityBeforePublication: actividad/lease perdidas, sin compensacion |
| D: negocio | BusinessCancellation: antes de inicio, durante copia y durante proveedor; solicitud realmente persistida, Confirm real, Cancelled y efectos exactos, sin publicar respuesta tardia |
| D: caller | CallerCancellation, CallerCancelledAfterCommit (inicio/publicacion) y TokenAfterPackageTransfer: OCE con token original, no solicitud ni cierre compensatorio, copias liberadas; commit conocido conservado |
| E: fallo | KnownFailure source/timeout y cuatro ProviderFailure: codigos existentes, finalizador real. ContentMismatch: rechazo sin codigo nuevo. FailureFinalizerRejected: lease rechazada, no Failed anunciado |
| E: incertidumbre | CommitUncertain inicio/registro/publicacion/finalizacion: commit real con respuesta perdida y observer indisponible, Indeterminate, cero acciones dependientes/segundo envio, estados leidos independientemente |
| Recursos | NoBusinessLocksDuringCopiesOrProvider: conexion independiente adquiere Quote/Requirement/job FOR UPDATE NOWAIT durante ambas copias y proveedor. Regresiones de drenaje/cleanup; originales retenidos, cero deletes |
| F: no activacion | Dos HTTP completos/replay Pending sin jobs/attempts/evidencia de ejecucion/AI automaticos; core manual existente aprobado; references.txt sin DI ni invocaciones productivas al coordinador |

Perdida de respuesta es inyeccion tras commit real y bloqueo del observador mediante
interceptor; no crash ni fallo fisico de red. Lease vencida se inyecta mediante
actualizacion controlada de fecha, no mediante una espera temporal. El historial
preexistente en ExistingRegistrationWithoutPackage se registra con otro preparador
real desde una frontera de test antes de invocar el preparador del coordinador;
no se fabrica un resultado ni se cuenta como envio. Los proveedores son simulados.

Concurrencia closure: winner PID 35348, bloqueado 35184; Lock/pg_blocking_pids observado
antes de liberar el inicio. Una sola llamada proveedor; el perdedor no dispone las
copias vivas del ganador. NOWAIT durante E/S: PIDs 35004, 36588 y 35336. races.txt
conserva tambien preflight/final, sin presentar PIDs de corridas distintas como una.

PostgreSQL 17 propio en 127.0.0.1:55533, usuario audit y data bajo artifact root.
Solo migraciones existentes sobre fixtures cotizador_backend_test_*. Cada corrida
retira la variable child-local y detiene el servidor en finally. closure-remaining.txt=0,
closure-server-status.txt=no server running. Cluster parado y logs retenidos.
storage-check.csv comprueba ausentes 111 raices distintas registradas en los tres TRX;
cero sng-sealed-* y sng-execution-docs-* observados al cierre. Sin barrido de huerfanos.

Manifiestos initial.csv/final.csv: 906/909 rutas, siete existentes modificadas,
tres nuevas, cero ausentes y 899 anteriores byte-identicas. delta.csv coincide con
el inventario de esta seccion. Rama/HEAD preservados; 55 tracked modificados,
132 untracked y cero staged. H1/H2, foto, descargas, gestion documental, analisis
manual, configuracion, modelos y migraciones ajenos al inventario quedan intactos.
git diff --check=0 con avisos LF/CRLF previos. Solo docs se completo despues del
build de cierre. Bin/obj D6a ignorados; evidencia de ejecucion fuera del repositorio.

Veredicto: **D.2c.6a implementado y verificado en el alcance probado, sin activacion**.
Sin DB habitual, inicio/reinicio/parada de API habitual, AI2/Gemini reales, frontend,
nueva migracion, commit/push/merge/despliegue. No se certifica exactly-once remoto,
recovery tras crash, permisos Unix, storage distribuido ni extraccion automatica
completa. Worker, enqueue, claim/renovacion/reasignacion de lease, recovery y las
ramas sin codigo terminal aprobado requieren alcance separado. No se implemento
la mejora opcional de reconciliacion de la auditoria D.2c.5b.
