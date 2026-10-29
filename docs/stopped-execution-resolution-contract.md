# Tratamiento explicito de ejecuciones detenidas

Estado: **R1-R4 APROBADAS; A CERRADO; B IMPLEMENTADO SIN ACTIVACION**, 2026-10-27.
La entrega original de A implemento lectura para cierre, drenaje local conocido
y Confirm controlado con lease viva. H1 y A estan cerrados dentro del alcance
auditado. La seccion13 registra exclusivamente el recibo R4 y su migracion
aditiva; la seccion14 agrega el cierre operativo B y su evidencia focal.
Integracion, delegacion y activacion permanecen pendientes.
No cambia Confirm legacy ni reabre la auditoria del supervisor. Referencias: secciones12-13 de
[supervision](assigned-execution-supervision-contract.md), P1-P7 y secciones10-12
de [lease](assigned-job-lease-contract.md). Los hechos siguientes corresponden al
codigo contrastado; la seccion9 conserva aceptacion del contrato completo y la
seccion14 distingue su ejecucion focal e inspeccion. La seccion12 conserva A.

## 1. Frontera y decisiones conservadas

Ya aprobado, NO sujeto a nueva aprobacion: Stop irreversible ante Renew incierto;
sin reanudacion/reenvio automaticos; preservar commits/resultados confirmados;
recuperacion explicita por owner vigente autorizado, sin bypass Admin ajeno;
expiry no prueba que el proveedor termino; incertidumbre no fabrica Failed/Cancelled.
Experience normal sera automatico: la intervencion de este contrato atiende
excepciones, no una aprobacion comercial manual por proyecto. No define delegacion.

Minimo aprobado: A, Confirm controlado con lease viva, separado del control local
ya detenido; B, cierre de cancelacion solicitada con lease vencida por operacion
distinta. Ambas por identidades exactas, sin proveedor, archivos ni creacion de jobs.
No fallback automatico A->B, ni Request implicito por Stop/OCE/Indeterminate. Un
rechazo no autoriza refrescar inputs y reintentar. Observar, cerrar y autorizar una
nueva ejecucion son acciones diferentes. No worker, selector, enqueue ni frontend.

## 2. Hechos contrastados

Rutas relativas al repositorio; simbolos son la referencia, no numeros historicos.

| Fuente | Hecho que limita el diseno |
|---|---|
| `Domain/PreQuotes/RequirementProcessingJob.cs`, RequestCancellation/ConfirmCancellation/Finish | Request sobre Queued termina Cancelled sin inicio; Running solo fija solicitud. Confirm de dominio requiere Running+solicitud, no comprueba lease. Finish conserva Started/generacion/intento y borra owner/expiry |
| `Domain/PreQuotes/Requirement.cs`, RequirementProcessingAttempt.Cancel | El intento del job es **RequirementProcessingAttempt**, no DocumentProcessingAttempt. Solo Processing con Started puede pasar a Finished/Cancelled; ErrorCode=null, CompletedAt=fecha de cierre. Pending/Finished no se cancelan de nuevo |
| Misma fuente, MarkProcessingCancelled/CanReplace/SupersedeAfterConfirmedCancellationBy | Confirm con intento devuelve Requirement Processing->Pending y actualiza fecha, no lo desactiva. Replacement Pending requiere prueba coordinada de cancelacion, no CanReplace ordinario |
| `Application/PreQuotes/ProcessRequirement/JobTerminationService.cs` | Resultados/errores/recibo existentes; Confirm recibe AssignedJobExecution con intento nullable. Recibo no tiene OperationId, actor de cierre, Claim ni expiry anterior |
| `Infrastructure/Persistence/Repositories/JobTerminationStore.cs`, RunAsync | Request/Confirm exigen usuario activo y owner actual, pero NO Project/Client activos. Fail SI exige ambos activos. No exige actor del Claim ni lee Claim. Confirm exige lease viva y guard final `lease_expires_at_utc > clock_timestamp()` |
| Misma fuente, ramas sin intento/terminal | Confirm sin intento no cambia Requirement ni actividad y no exige current/status del Requirement en esa rama; con intento exige current/Processing y ningun otro intento activo. Replay Cancelled no exige current/lease viva, pero si autoridad/relaciones/intento compatibles |
| Misma fuente, ExecuteAsync | Observa commit incierto con contexto/token nuevos10s, reconoce terminal compatible; NO compara candidato propio con OperationId/fecha generada. No check final de caller como Renew/v2; caller nuevo debe aportarlo sin cambiar legacy |
| `Infrastructure/Persistence/Configurations/RequirementProcessingJobConfiguration.cs` | Cancelled exige solicitud, terminal sin lease, generacion1 si hubo inicio, intento0/null o1/ID. Recibo de publicacion solo en Succeeded. No CHECK de procedencia viva/vencida del cierre |
| `SuperviseAssignedExecutionService.cs` y `AssignedExecutionControl.cs` en Application/PreQuotes/ProcessRequirement | Supervisor espera coordinador+ciclo, luego observa; Stop no admite Termination nueva. No entrega prueba durable de drenaje ni candidato de commit incierto. OCE puede coexistir con Succeeded |
| `Infrastructure/Persistence/Repositories/AssignedExecutionObservationStore.cs`, Matches | Observador de ejecucion exige Claim.ActorUserId=actor. Owner nuevo no puede adoptar Claim antiguo. Snapshot no certifica grafo publicado ni commit propio |
| `Infrastructure/Persistence/Repositories/JobPublicationStore.cs` | Publicacion exige Running, intento Processing, current, actor/contexto activos, evidencia y lease viva; guard final sin solicitud. Cancelled/Failed rechazan. Replay Succeeded exige snapshot/recibo/evidencia, no los fabrica un observador |
| `Application/Common/Abstractions/PreQuotes/IRequirementDocumentWrite.cs`, Evaluate | Running/Queued o cualquier intento Pending/Processing bloquean replacement aunque lease venza. Pending solo es reemplazable con exactamente un job Cancelled y cero intentos, o exactamente su intento Finished/Cancelled |
| `Infrastructure/Persistence/Repositories/RequirementRepository.DocumentWrite.cs` | Replacement revalida owner/usuario activo Y Project/Client activos, current y conjuntos completos bajo locks. Inactividad permitida para cancelacion NO se hereda aqui |
| `Application/PreQuotes/ManageRequirementDocuments/ManageRequirementDocumentsService.cs`, ReplaceRequirementAsync | Crea version/archivos nuevos por otra transaccion; usa SupersedeAfterConfirmedCancellationBy solo para Pending autorizado por policy. No reescribe historia anterior |

## 3. Matriz real y politica aprobada

Aplicar primero autoridad/ruta (tabla siguiente), luego esta matriz. `A` y `B`
identifican las vias del contrato; solo A pertenece al bloque actual. Rechazos de
mutacion sin recibo ni snapshot ajeno. Una lectura autorizada es siempre separada.
N = sin intento, count0/null; P = un intento exacto Processing, count1/ID,
correlacion concordante, Created<=Started y Started>=asignacion. No otro intento activo.

| Estado comprobado | Operacion y efecto | Rechazo / reconocimiento |
|---|---|---|
| Queued sin intento, sin solicitud | Request existente, solo por voluntad explicita: Cancelled inmediato, generacion0, sin inventar Claim/Started/intento | No A/B; replay Request reconoce Cancelled coherente sin DML |
| Running N, sin solicitud, viva o vencida | Solo Request explicito puede fijar solicitud; ningun cierre por Stop | A/B: CancellationRequired; Request repetido reconoce solicitud, no terminal |
| Running P, sin solicitud, viva o vencida | Igual; Request no modifica intento/Requirement/actividad | A/B: CancellationRequired |
| Running N, solicitud y viva | A tras drenaje local conocido; Cancelled, sin cambios a Requirement/actividad | B: LeaseConflict. A final guard puede vencer y rechazar con rollback |
| Running P, solicitud y viva, current/Processing | A tras drenaje; Cancelled + intento Finished/Cancelled + Requirement Pending + actividad una vez | B: LeaseConflict; A no renueva para conseguir tiempo |
| Running N, solicitud y vencida, current/Pending | B explicito; Cancelled, count0/null, Requirement/actividad intactos | A: LeaseConflict. No inicio/intento sintetico |
| Running P, solicitud y vencida, current/Processing | B explicito; mismos efectos comerciales de Confirm con intento | A: LeaseConflict. No afirmar remoto detenido |
| Running con intento vinculado Pending o Finished; otro intento activo; identidad/correlacion/fechas incompatibles | No A/B, no reparacion | AttemptConflict. Request actual tambien rechaza vinculado no Processing; Request puede registrar solicitud sin reparar otro intento activo |
| Running con sucesor/no current, o status incompatible | Recomendacion conservadora para NUEVAS A/B: no mutar | StateConflict. Confirm existente sin intento es menos restrictivo: no se atribuye esa comprobacion al store actual |
| Cancelled coherente, con o sin intento, incluso con sucesor | A puede reconocer por Confirm existente; B solo reconoce SU recibo exacto si existe | Sin DML. B sin recibo propio: TerminalConflict y lectura separada del terminal, nunca atribuirse el cierre de otro |
| Failed previo, con/sin intento, o Succeeded/publicacion previa | Conservar terminal/grafo/fechas y decisiones; solo lectura, no cierre | A/B: TerminalConflict. Failed sin intento deja Pending y NO habilita replacement ordinario |
| Running con recibo de publicacion, grafo publicado de ese intento o forma inconsistente | No cerrar ni borrar para hacer coherente | InconsistentEvidence; forma Running+recibo normalmente impedida por CHECK. Datos incoherentes requieren diagnostico, no recovery general |

La tabla no inventa combinaciones legales: CHECK del job no prueba por si solo
estado/correlacion de la fila de intento. Pending vinculado puede ser representable
por SQL pero no por Start atomico normal; el guard lo rechaza, no lo promueve.
Terminal compatible con intento exige Finished, outcome/CompletedAt/ErrorCode
concordantes. Para N no se crean intentos; B exige conjunto vacio, no solo link null.
Sucesor afecta elegibilidad de NUEVO cierre, no replay historico ya confirmado.

| Actor / contexto | Request/Confirm actuales | A/B aprobados y lectura para cierre | Replacement actual |
|---|---|---|---|
| Actor original, usuario activo, aun owner | Autorizado si resto cumple | Autorizado | Requiere ademas contexto activo/current/policy |
| Actor original ya no owner, o Admin ajeno | NotFound seguro | Rechazar sin metadata | Rechazar |
| Nuevo owner actual activo; Claim de actor anterior | Request/Confirm no exigen actor Claim | Autorizar SOLO cierre/lectura acotados; Claim anterior sigue ajeno para ejecutar/Renew | Autoridad de owner actual, resto de policy vigente |
| Owner actual inactivo | InactiveUser | Rechazar; no cuenta de servicio sustitutiva | Rechazar |
| Project o Client inactivo, usuario/owner validos | Request/Confirm permitidos; Fail rechazado | Recomendar misma excepcion de cancelacion (decision R2), no ejecutar ni publicar | Rechazar: deben estar activos |
| Ruta/links/Claim ausentes o incompatibles | Rechazo segun checks propios | ContextConflict/IdentityConflict/InconsistentEvidence segun causa; no adoptar ni backfill | Revalidacion propia |

## 4. Firmas y datos minimos del contrato

APIs internas sin endpoints/DI. Ningun nuevo estado persistido ni codigo terminal.
Reutilizar JobTerminationState/Failure, no inventar Failed por ausencia de permiso.

```csharp
Task<ClosureObservationResult> ObserveForClosureAsync(
    ExecutionClosureIdentity identity, Guid actorUserId, CancellationToken actionCt);
Task<JobTerminationResult> ConfirmStoppedCancellationAsync(
    LiveCancellation request, LocalDrainCompletion local,
    Guid actorUserId, CancellationToken actionCt);
Task<ExpiredClosureResult> CloseExpiredCancellationAsync(
    ExpiredCancellation request, Guid actorUserId, CancellationToken actionCt);
```

- Identity: JobId, RequirementId, ExecutionRequestId, ClaimOperationId no vacios.
- LiveCancellation: Identity, LeaseOwner, LeaseGeneration=1, AssignedAtUtc,
  ExpectedProcessingAttemptId nullable, ExpectedCancellationRequestedAtUtc. El
  snapshot vivo previo orienta, Confirm final decide; no nuevo CAS de expiry en A.
- ExpiredCancellation: lo anterior + OperationId nuevo estable y ExpectedExpiry.
  UUIDs no vacios, owner canonico del Claim, fechas UTC microsegundos exactos, sin
  truncar para coincidir. Actor no se obtiene del request ni de RequestedBy.
- ClosureObservationResult: Failure existente + snapshot nullable de valores;
  Failure=None solo lectura coherente, PersistenceError sin snapshot si no disponible.
  Snapshot separa actor lector, actor historico Claim, terminal, solicitud, asignacion,
  intento, expiry, reloj DB y contexto. Sin `CanExecute`, grants o referencias EF.
- ExpiredClosureResult: JobTerminationResult + recibo propio nullable de seccion7.
  CancellationConfirmed/AlreadyRecognized solo con ese recibo; Conflict con failure
  preciso; Indeterminate/InconsistentEvidence si no puede demostrarse commit.
  Error precommit con rollback conocido: Conflict/PersistenceError, no falso terminal.

ObserveForClosure es una lectura focal NUEVA, no ampliacion de ObserveAssignedExecution.
Autoriza usuario activo y owner ACTUAL antes de divulgar; valida Claim tipo/selfref/
forma/IDs/generacion/asignacion contra job, pero NO Claim.ActorUserId==actor actual.
Ese actor historico permanece inalterado. No permite Start/Renew/consumo/publicacion,
ni devuelve un contexto delegable a esos servicios. Confirm ya tiene esta separacion
de autoridad de cierre; B debe implementarla expresamente, sin pasar otro Guid al
observador existente para eludir su rechazo. Si cambio owner durante la operacion,
locks de Project serializan la mutacion; el nuevo estado se revalida en cada llamada.

Lectura terminal no exige current/actividad comercial; preserva sucesor. Puede
informar Succeeded/recibo existente, NO anunciar Published ni reconocer un candidato
desconocido. Ante incoherencia, snapshot no operativo y failure, sin reparacion.

## 5. A: Confirm controlado con lease viva

1. Solo una solicitud durable de cancelacion permite este camino. La accion humana
   puede usar Request existente por separado y conservar su resultado; nunca hacerlo
   por la mera cancelacion del token o para ocultar Renew Indeterminate.
2. Para invocacion local conocida: Stop ya irreversible y esperar tarea supervisor,
   coordinador, ciclo Renew/reconciliacion y drenaje. Capturar tambien OCE/resultado
   fallido sin afirmar rollback. `LocalDrainCompletion` seria evidencia interna
   efimera creada por el propietario que realmente espero esas tareas de ESA
   invocacion, no booleano HTTP, ni nuevo registro durable, ni prueba de fin remoto.
3. Si no termina lectura/callback, no forzar disposal ni dar certificado de drenaje.
   Si otra instancia desaparecio o no se posee su tarea, NO inventar LocalDrainCompletion:
   A no procede mientras viva. Puede solicitarse cancelacion y esperar vencimiento
   DB para B explicito. B no depende de demostrar muerte del proceso ni fin remoto.
4. Nueva accion autenticada/token propio; NO usar StopToken ni token de ejecucion
   cancelado. No reabrir control ni admitir Termination despues de Stop. La llamada
   separada reutiliza JobTerminationService.ConfirmJobCancellationAsync exactamente
   una vez, fuera de todo scope DB de lectura, con AssignedJobExecution exacto.
5. Lectura para cierre verifica identidad/solicitud/intento, v2/Claim y condiciones
   nuevas conservadoras current/Pending o Processing. Confirm revalida autoridad,
   solicitud, intento y lease bajo locks. No confiar en la lectura como permiso
   permanente. Sin intento, la comprobacion conservadora adicional es de esta via,
   no cambio silencioso de Confirm legacy. Con solicitud, Start normal ya no puede
   crear un intento despues; una identidad cambiada se rechaza, no se adopta.
6. Si vence durante espera/SaveChanges, guard vivo afecta cero y revierte todo:
   LeaseConflict, sin Cancelled ficticio. No extender lease ni convertir esa llamada
   en B. Nueva decision explicita puede solicitar B con estado DB actualizado.
7. Terminal concurrente: Cancelled coherente se reconoce sin DML; Succeeded/Failed
   se conserva y devuelve TerminalConflict, lectura separada informa historia. No
   cierre adicional ni degradar un resultado ya conocido. Indeterminate no habilita
   replacement ni otra mutacion para averiguar. Confirm no es una API de consulta.

El reconocimiento interno actual de Confirm acredita terminal compatible, NO
OperationId propio ni autor de ese commit. A debe conservar esa distincion, no
exponer `OwnCommitConfirmed`. No cambiar su reconciliacion legacy en este bloque.
Evaluar terminal/replay antes de los requisitos de mutacion nueva; la lectura
historica no necesita un comprobante local de drenaje. A no debe mantener locks
externos mientras llama al store con contexto propio. Sus checks adicionales de
v2/Claim/current sin intento son preflight: la seguridad de cierre sigue siendo
la revalidacion de Confirm y el protocolo que impide replacement mientras Running.
No atribuir a Confirm una comparacion nueva de Claim/expecteds que no recibe.
Tras retorno/observacion comprobar actionCt original: OCE original incluso ante
commit confirmado. El supervisor permanece intacto; no se agrega Confirm automatico.

Presupuesto aprobado R3: un TOTAL de10s cooperativos para lectura/preparacion y
mutacion de la accion, mas hasta10s de observacion independiente si se intento
commit. Reutiliza orden de magnitud ya existente sin consumir indefinidamente una
lease120s; no son valores medidos ni garantia de20s wall-clock. Drenaje local es
PRECONDICION anterior y no cabe ficticiamente dentro de este presupuesto. En A,
Confirm conserva su observador interno10s; no agregar otro observador encadenado.
Timeout/caller no implican rollback; actionCt original cancelado prevalece. Agotado
solo presupuesto interno y sin resultado demostrable: Indeterminate, no codigo
terminal nuevo. Esperar siempre tareas/cleanup; no abandonar Task en background.

## 6. B: cierre excepcional vencido

Decision R1 aprobada para B futuro: reutilizar **Cancelled** para cancelacion de la
ejecucion backend abandonada solicitada explicitamente. Es coherente con dominio,
CHECK y policy documental, pero NO prueba cancelacion remota, ausencia de consumo
ni aprobacion de un nuevo envio. B permanece sin implementar en esta tarea;
no sustituirlo por Failed ni inventar otro estado/codigo ahora.

Precondiciones bajo locks: owner actual activo, ruta exacta incluida Submission,
v2, Claim propio de la asignacion (no del actor de cierre), Running/generacion1,
Started=Claim.Assigned, owner exacto, expiry=ExpectedExpiry no null, solicitud no
null igual a ExpectedCancellationRequestedAt. No Finished/error/recibo publicacion.
Requirement current/Pending con N y cero intentos, o current/Processing con P y
ningun otro intento activo. Rechazar extraccion/propuesta del intento objetivo:
no sobrescribir un resultado; conservar grafos de otros intentos/Requirements.
Evidencia de ejecucion existente es admisible, no exige spool ni prueba de no envio.
B no necesita LocalDrainCompletion: tambien cubre otra instancia desaparecida o
una lectura local que aun drena tras Stop. Si existe tarea local, su propietario
debe seguir observandola y disponer recursos solo al terminar; cerrar DB no permite
abandonar esa tarea, disponer handles ajenos ni afirmar cleanup remoto/local realizado.

### Locks y transaccion corta

Contexto dedicado, descubrimiento AsNoTracking sin autoridad. Orden de escritura:
Submission SHARE si aplica; User SHARE; Client SHARE; Project SHARE; PreQuote UPDATE;
Requirement UPDATE; Job UPDATE; Attempts por Id UPDATE; Claim y recibo de cierre.
Releer ruta bajo locks, autorizar ANTES de exponer recibo. Leer evidencia/grafo solo
para correspondencia/rechazo sin modificarlos; no storage ni proveedor. No locks
de Files necesarios: referencias quedan intactas y los escritores documentales
serializan por Requirement/Job. Lectura dedicada usa SHARE en el mismo orden.

1. Autorizar y buscar recibo por OperationId antes de elegibilidad para mutacion.
   Replay exacto se resuelve segun seccion7; no reevaluar lease/current para historia.
2. Sin recibo: verificar TODAS las precondiciones. Leer T0=`clock_timestamp()` tras
   esperar locks, exigir expiry<=T0 y fechas lifecycle<=T0. No reloj local ni mtime.
3. Aplicar transiciones locales existentes: job.ConfirmCancellation(T0); si P,
   attempt.Cancel(T0), requirement.MarkProcessingCancelled(T0), quote.RegisterActivity(T0).
   Esto no llama al store Confirm ni altera su guard vivo. El metodo de dominio no
   certifica lease: la operacion B lo hace mediante su guard SQL separado.
4. Guardar solo intento/Requirement/actividad si P, job detached. Ultima sentencia
   mutante: UPDATE job condicionado + INSERT recibo desde RETURNING en una sentencia.
   Si no actualiza exactamente1 fila y crea1 recibo, rollback INTEGRO, incluido
   SaveChanges previo. No SaveChanges despues ni tracking que sobrescriba el job.
5. Commit inmediato; preservar candidato completo antes de intentarlo. No transaccion
   sobre drenaje/red ni garantia de que el proceso anterior desaparecio.

Guard conceptual (parametrizado; no migracion ni SQL para ejecutar en esta tarea):

```sql
WITH clock AS MATERIALIZED (SELECT clock_timestamp() AS g), changed AS (
  UPDATE core.requirement_processing_jobs j
  SET state='Cancelled', finished_at_utc=@t0,
      lease_owner=NULL, lease_expires_at_utc=NULL
  FROM clock
  WHERE j.id=@job AND j.requirement_id=@requirement
    AND j.execution_request_id=@request AND j.correlation_id=@correlation
    AND j.required_publication_version='sng-job-publication-v2'
    AND j.state='Running' AND j.started_at_utc=@assigned
    AND j.lease_owner=@owner AND j.lease_generation=1
    AND j.lease_expires_at_utc=@expected_expiry
    AND j.lease_expires_at_utc <= clock.g AND clock.g >= @t0
    AND j.processing_attempt_id IS NOT DISTINCT FROM @attempt
    AND j.attempt_count=@count
    AND j.cancellation_requested_at_utc IS NOT NULL
    AND j.cancellation_requested_at_utc=@requested_at
    AND j.finished_at_utc IS NULL AND j.last_error_code IS NULL
    AND j.publication_contract_version IS NULL AND j.publication_content_hash IS NULL
  RETURNING j.id, j.finished_at_utc, clock.g
)
INSERT INTO core.expired_execution_cancellations (/* candidato y guard_at_utc */)
SELECT /* valores exactos del candidato y changed.g */ FROM changed
RETURNING *;
```

La comprobacion expiry<=T0 anterior asegura FinishedAt>=expiry; clock.g>=T0 evita
certificar tiempo regresado en el guard. Rechazar fechas futuras, no redondear ni
avanzar el reloj artificialmente. Actor/ruta/Claim/Requirement/intentos/grafo se
validan previamente y permanecen protegidos por locks. Cero filas: LeaseConflict
si vencimiento/expectativa no cumple; incoherencia restante: IdentityConflict,
sin efectos. SQL no certifica autoridad por si solo ni defensa frente DBA arbitrario.

## 7. Recibo, replay e incertidumbre de B

Decision R4 aprobada para un bloque posterior: tabla focal `ExpiredExecutionCancellation`, no
maquina de estados, no reutilizar JobLeaseOperation como si cerrar fuera Renew.
Sin recibo adicional, terminal borra owner/expiry y no conserva quien solicito
este cierre ni OperationId: no permite replay historico exacto de esta operacion.

Modelo minimo aprobado para B futuro: OperationId UUID PK global; JobId UNIQUE; RequirementId,
ExecutionRequestId, ClaimOperationId, ClosedByActorUserId, CorrelationId, LeaseOwner,
LeaseGeneration=1, AssignedAtUtc, ExpectedExpiry, ExpectedProcessingAttemptId nullable,
CancellationRequestedAtUtc, FinishedAtUtc=T0, GuardAtUtc=G, PolicyVersion=1.
Todas identidades/fechas del request se conservan; actor de cierre es el autenticado,
no Claim.Actor ni RequestedBy. No inventar actor de la solicitud de cancelacion:
el job actual conserva fecha, no su autor. FK RESTRICT a Job/Requirement/User/Claim
e intento nullable; store valida correspondencias y tipo Claim. CHECK UUID/owner/
UTC representable/fechas finitas/generacion/policy/ExpectedExpiry<=Finished<=Guard,
Assigned<ExpectedExpiry y solicitud<=Finished. Timestamptz microsegundos. EF Throw,
insert-only en servicio; sin garantia ante SQL directo, sin triggers/backfill/purga.
Un recibo por cierre B, no por tick. Retencion/capacidad operativa aun por definir.

Repeticion explicita del MISMO OperationId+payload+actor, tras autorizar owner
vigente: devolver AlreadyRecognized con mismo recibo y cero DML. Reconstruir forma,
comparar job Cancelled, fechas/IDs/count/intent Finished/Cancelled/ErrorCode y ausencia
de recibo publicacion. No exigir current ni actividad Client/Project; no comparar
estado comercial posterior del Requirement ni tocar sucesor/actividad. Caso N:
sin intento vinculado; no imponer que no exista un sucesor ajeno al cierre.
Mismo ID con datos/actor distintos: IdentityConflict, sin metadata ajena.
Otro ID tras terminal, incluido cierre ganado por A: TerminalConflict, cero nuevo
recibo. Lectura para cierre puede mostrar terminal coherente, no commit propio.
Nuevo owner puede observar historia autorizada; no puede presentarse como autor
de una operacion antigua. Actor anterior ya sin ownership no puede reconocerla.

Commit intentado con respuesta perdida: disponer scope, contexto/token nuevos10s,
lectura read-only por mismo orden y autoridad actual. Comparar recibo COMPLETO con
candidato propio retenido, incluidos T0/G/IDs/actor/expecteds, mas terminal/intento.
Solo igualdad exacta confirma ese commit; otro cierre compatible NO lo confirma.
Ausencia, bloqueo, cambio de owner, fallo o datos incompatibles => Indeterminate,
nunca rollback confirmado. No retry ni segunda mutacion. Antes de commit solo
Conflict/PersistenceError si rollback se establece; limite incierto => Indeterminate.
Tras commit/observacion, incluso fallida, comprobar actionCt original y propagar
su OCE si cancelado. OCE puede dejar Cancelled/recibo confirmados; no compensar.
Si el candidato se pierde al reiniciar, lectura posterior acredita registro durable,
no reconstruye la certeza local de la llamada perdida ni reanuda ejecucion.

## 8. Efectos, carreras y replacement

| Caso confirmado | Job | Intento / Requirement / PreQuote | Inmutables |
|---|---|---|---|
| A/B N | Cancelled; solicitud original, Finished, owner/expiry null; generacion1, count0/null | Cero intentos nuevos. Requirement current/Pending y UpdatedAt intactos; ninguna actividad nueva | Started, identidades, Claim/Renew y documentos |
| A/B P | Igual, count1/ID conservados | Exacto Processing->Finished/Cancelled, CompletedAt=T0, ErrorCode=null; Requirement Processing->Pending con UpdatedAt=T0, IsActive/links intactos; RegisterActivity(T0) una vez | RequestedBy, correlacion, inicio, archivos/evidencia/grafo ajeno/decisiones historicas |
| Terminal previo o replay | Sin cambios | Sin actividad, cierres, reparaciones ni mutaciones del sucesor | Todos los recibos/fechas/identidades originales |

RequiredPublicationVersion v2 permanece; cancelar no genera PublicationContractVersion/
ContentHash. No borrar evidencia, intentos, extraccion o propuestas. Un grafo del
intento objetivo ya publicado no se convierte a cancelacion. Nunca compensacion remota.

Carreras decididas en DB:
- Publicacion gana antes: si commit ya existe, A/B no lo degradan. B requiere
  solicitud persistida: tras Request ganado, una publicacion NUEVA ya no es elegible.
  La carrera normal con publicacion ganadora se inicia ANTES de Request: Publish
  confirma, Request/Close posteriores ven terminal. No fabricar una publicacion
  valida con cancelacion persistida para probar un orden imposible.
- Cierre gana: job Cancelled, intento Finished y lease borrada; Publish rechaza
  TerminalConflict/state/guard aun si conservo respuesta/spool local. Start/Renew/
  registro/consumo nuevos tambien revalidan. Bytes ya enviados pueden seguir siendo
  procesados remotamente: el cierre solo impide publicacion backend posterior.
- Renew gana ANTES de Request: puede extender; Request fija solicitud y bloquea
  siguientes Renew. B con expiry viejo rechaza, no adopta extension; otra accion
  explicita necesita identidad actual y esperar su vencimiento. Request/B ganan:
  Renew rechaza solicitud/terminal. Con solicitud ya persistida NO hay un Renew
  exitoso legal que compita por ampliar; probar rechazo en ambos ordenes de locks.
- A pierde vigencia: final guard hace rollback; B puede cerrar despues con solicitud
  propia explicita. A que ya confirmo y retuvo respuesta: B ve terminal, no otro cierre.
- Dos B mismo ID: uno aplica, otro reconoce mismo recibo sin DML. IDs distintos:
  uno aplica, otro TerminalConflict; UNIQUE JobId no sustituye locks/autorizacion.
  Colision OperationId entre jobs: rollback total del perdedor, IdentityConflict.
- Replacement comparte orden Quote/Requirement/Jobs/Attempts: si gana antes del
  cierre, ve Running y rechaza; si cierre confirma primero, reevalua policy. Replay
  de cierre posterior a successor reconoce historia sin cambiar el successor.

Replacement NO se vuelve automaticamente permitido por expiry ni por Stop. Requiere
usuario/owner y Project/Client activos, Requirement current, cero jobs/intentos activos.
Para Pending tras cancelacion: exactamente1 job Cancelled; si link null, cero intentos;
si link presente, exactamente1 intento coincidente Finished/Cancelled. Multiples
terminales ambiguos no satisfacen esa excepcion. Processed/Failed current conservan
su regla ordinaria sin activos. Fail sin intento/Pending NO basta. No se amplia
CanEditDocuments/CanCancel: historia de jobs/intentos impide editar originales.
El replacement existente crea otra version, preserva anterior y revalida; el cierre
no crea version/job/intento ni enqueue. Nueva ejecucion exige accion separada,
identidades nuevas y decision explicita sobre riesgo de duplicacion remota.

## 9. Pruebas de aceptacion propuestas

No ejecutadas aqui. PostgreSQL temporal real, constraints activas, servicios reales;
proveedor simulado solo para preparar ejecucion en vuelo y medir cero llamadas NUEVAS
por cierre. Snapshots independientes no vacios de jobs/attempts/Requirement/actividad,
documentos/evidencia/Claim/Renew/grafos/recibos antes/despues; medir DML separado del
fixture. No sleeps como prueba unica de orden ni SQL que desactive constraints.

| Caso | Evidencia exigida |
|---|---|
| A N/P con solicitud viva, ambos contextos activos/inactivos | Confirm real, efectos exactos de seccion8; replay cero DML, version conservada y recibo publicacion null |
| Owner cambiado tras Claim | Actor viejo/Admin ajeno rechazados; owner nuevo activo puede leer para cierre y cerrar A/B; observador/Renew originales siguen rechazando adopcion. Claim/RequestedBy intactos |
| Sin solicitud, caller cancelado, proceso desaparecido | Sin Request implicito; A sin drenaje conocido no invoca Confirm. B vivo rechazado, no atribuir muerte a expiry |
| A expira esperando locks o antes del ultimo guard | Conexion independiente retiene filas, reloj DB acreditado; rollback intento/Requirement/actividad, no Cancelled. No fallback B |
| B vivo/vencido/igualdad y reloj regresado | Guard DB <= para B, > para A; ExpectedExpiry exacto. Prueba estado DB antes de liberar barrera; cero recibo si guard pierde. T0/G temporales coherentes |
| Incompatibilidades | Pending/Finished vinculado, otro activo, Req ajeno, correlacion/fechas, Claim Renew/ajeno, generation/expiry/solicitud obsoletos, grafo del intento y no current: error preciso sin DML/metadata. Documentar casos impedidos por CHECK |
| Publicacion ganadora | Publish real retiene locks, Request espera con PID distinto/pg_blocking_pids; tras commit Request y cierre rechazan terminal, grafo/recibo intactos. No inventar solicitud previa compatible con Publish |
| Cancelacion/cierre ganador | Request real primero, cierre B al vencer; Publish tardio real con snapshot asociado rechaza, cero grafo nuevo/reenvio. Barreras y PIDs distintos; estado terminal preservado |
| Renew/cierre | Renew antes de Request puede extender; B expected viejo no cierra. Request primero deniega Renew; B y Renew ambos ordenes de locks con solicitud no producen extension. Mostrar pg_blocking_pids antes de liberar |
| Dos cierres y colision | Mismo ID reconoce; distintos IDs un efecto/TerminalConflict; colision global con otro job revierte modificaciones propias y conserva ajenas |
| Error post-SaveChanges/precommit | Rollback completo real, intento/Req/actividad/job/recibo. Distinguir fallo que permite rollback conocido de conexion/reader que impide demostrarlo |
| Commit perdido y OCE | Commit real, respuesta perdida, observador independiente disponible/bloqueado/denegado/candidato distinto: reconocimiento exacto o Indeterminate; OCE original tras commit, ningun retry/compensacion |
| Historia/sucesor | Replay mismo recibo despues de replacement conserva historia y sucesor sin DML; lectura de otro owner no adopta actor de cierre ni concede ejecutar |
| Replacement y recursos | Running vencido bloquea; cierre compatible confirmado habilita SOLO policy completa/contexto activo; replay no duplica actividad. Originales/recibos intactos, sin borrar recursos de instancia ajena |
| Compatibilidad | Confirm legacy sigue rechazando vencidos; v1 y finalizadores previos intactos; dos HTTP intake/replay Pending/cero automatismos en futuro bloque de implementacion |

## 10. Cambios minimos y decisiones aprobadas

No cambio al lifecycle, estados, codigos de fallo, generacion1, duracion120s, Renew,
Confirm vivo, supervisor, observador de ejecucion ni policy de replacement. Metodos
de dominio existentes ya permiten las transiciones locales propuestas; autorizacion
y tiempo pertenecen al store. Alcance aprobado: lectura focal para
cierre/owner vigente; caller controlado A; store B separado; recibo R4 y migracion
aditiva aprobados para despues de A. No backfill, trigger, purga o modificacion de recibos viejos.

| Decision aprobada | Politica | Consecuencia / limite |
|---|---|---|
| R1 semantica del cierre vencido | Cancelled por abandono explicitamente solicitado, sin garantizar remoto cancelado | Impide publicacion posterior backend; B no se implementa en esta tarea |
| R2 elegibilidad minima | Solicitud persistida obligatoria; v2 con Claim real; N current/Pending o P current/Processing; permitir contexto comercial inactivo como cancelacion existente | Bloquea incoherencias/no current en mutacion nueva; no inventa reparacion. Lectura/replay historicos sin current. Restriccion de N en A es adicional a Confirm actual |
| R3 presupuesto/control A | Drenaje local conocido previo; token de accion nuevo;10s TOTALES lectura/preparacion/mutacion + observador independiente existente10s | Sin garantia20s wall-clock ni plazo maximo de drenaje; no reiniciar presupuesto por etapa, no segundo observador |
| R4 evidencia propia B | Recibo focal inmutable y migracion aditiva aprobados para bloque posterior, sin backfill/purga | No implementados aqui. A acredita terminal compatible, NO OperationId propio ni autoria del commit |

No se vuelve a pedir aprobacion de Stop, ownership vigente, ausencia de bypass,
recuperacion explicita ni flujo normal automatico. No aprobacion global D1-D9.
Separar permiso de cerrar del de ejecutar es requisito de este contrato, no una
delegacion generica ni mecanismo de activacion.

Division minima con autorizacion/cierre propios:
1. R1-R4 aprobadas; implementar lectura para cierre y caller A, probar autoridad,
   drenaje/tokens/guard vivo/compatibilidad, sin tocar supervisor ni B.
2. Modelo/recibo B y migracion aditiva R4, forma/inmutabilidad/upgrade, sin consumidor.
3. Store B con guard vencido, efectos atomicos, replay/reconciliacion y carreras;
   pruebas de replacement separado. No integrar ejecucion automatica.
4. Auditoria de A+B y procedimiento operativo para pendientes. Activacion, permisos
   automaticos Experience, hosting y nuevos envios quedan en bloques separados.

## 11. Integridad de esta entrega documental

Historia de la creacion documental original, conservada; no describe el delta del
bloque A posterior de la seccion12.

Solo se agrega este documento. No builds/tests, DB, procesos nuevos, proveedor,
migracion ni correccion del supervisor. No repite su auditoria. AGENTS aplicables:
ninguno encontrado en backend/ancestros en la revision de este worktree.
Baseline inmediato:939 rutas tracked/untracked no ignoradas, rama
feature/public-experience-intake, HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79.
Manifiestos SHA256 y Git antes/despues en
`C:/Users/USUARIO/AppData/Local/Temp/sng-stopped-contract-20261027`.
Base de preservacion: worktree real, no HEAD. Sin commit/push/merge/despliegue.
Comprobacion final:939 archivos anteriores byte-identicos, un documento nuevo
(940 totales), cero modificaciones/eliminaciones anteriores; rama/HEAD preservados.
git diff --check=0; trabajo previo y untracked conservados. No tests ejecutados
ni evidencia runtime nueva: las matrices describen contratos y aceptacion futura.

## 12. Bloque A: lectura para cierre y Confirm controlado

2026-10-27. R1-R4 aprobadas con las precisiones del usuario, NO aprobacion global
D1-D9. Implementacion focal sin activacion. B, su recibo/EF/migracion y recovery
vencido siguen sin implementar. Cancelled describe el cierre backend solicitado,
no certifica que el proveedor remoto dejo de trabajar. No permiso para otro envio.

### Componentes y frontera de confianza

Nuevos en `Application/PreQuotes/ProcessRequirement`:
- `ObserveForClosureService.cs`: identidad exacta, resultado y snapshot de valores.
  El snapshot contiene `Execution` (actor lector) y `ClaimActorUserId` historico,
  current, conteo de intentos, correlacion y error. No referencias EF mutables,
  permiso operativo ni reconocimiento de candidato incierto.
- `LocalExecutionInvocation.cs`: adaptador de uso unico que construye y espera un
  supervisor REAL. Su proxy de Claim delega al store y conserva solo un `Claimed`
  exacto de esa invocacion; `AlreadyApplied` nunca genera comprobante. No acepta
  Task arbitraria, booleano ni DTO de drenaje. `DrainCompletion` permanece null
  hasta finalizar el await del supervisor, tambien si este propaga OCE tras join.
  El comprobante liga Job/Requirement/request/Claim, owner tecnico, generacion y
  fecha de asignacion. No exige igualdad entre actor historico y actor del cierre.
  Constructor internal y propiedades de solo lectura: frontera de codigo backend
  confiable, NO defensa frente reflection o componentes hostiles en el proceso.
- `ConfirmStoppedCancellationService.cs`: accion explicita, recibo compatible de
  Confirm legacy, sin OperationId propio ni `OwnCommitConfirmed`. Una llamada como
  maximo a Confirm, cero Request/Renew/retry/fallback. No usa el control detenido.

Nuevo `Infrastructure/Persistence/Repositories/ExecutionClosureObservationStore.cs`:
contexto/transaccion propios; SHARE en orden Submission/User/Client/Project/Quote/
Requirement/Job/Attempts ordenados/Claim; usuario activo y owner ACTUAL antes del
snapshot. No compara actor Claim con actor lector, pero si tipo/autorreferencia,
forma de factory, IDs, asignacion, generacion y correspondencias de intento/terminal.
Running con estado de Requirement incompatible devuelve StateConflict sin snapshot.
Terminal e historia no requieren current ni actividad de Client/Project. Reloj DB
posterior a locks. Solo SELECT, sin SaveChanges, storage ni proveedor.

El supervisor existente espera coordinador y ciclo, incluido consumo/drenaje y
reconciliacion, antes de completar su Task. El adaptador no reemplaza ese algoritmo
ni certifica fin remoto. Una lectura/callback que no termina mantiene Task pendiente
y comprobante ausente; no fuerza disposal. Solo cubre ESA invocacion local que posee,
no otras instancias desaparecidas. No es registro global ni prueba durable.

### Preflight frente a transaccion

La lectura y la validacion de A agregan v2/Claim, asignacion/solicitud/intento
esperados, drenaje local, current y estado Pending/Processing. Sin intento exige
conjunto de intentos vacio. Project/Client inactivos no bloquean cancelacion.
Historia Cancelled se evalua antes de drenaje/current/lease; Failed/Succeeded se
conservan y devuelven TerminalConflict. El store lector ya libero todos sus locks
cuando A invoca Confirm. No transaccion externa alrededor de dos contextos.

Confirm existente revalida usuario activo, owner, ruta, IDs, generacion/intento,
solicitud, estado y lease bajo sus propios locks; con intento valida current y
Processing. Su ultimo UPDATE exige `lease_expires_at_utc > clock_timestamp()`;
cero filas revierte tambien intento/Requirement/actividad guardados previamente.
NO recibe Claim, AssignedAt ni expected cancellation date, ni agrega current para
su rama legacy sin intento. No se atribuyen esos guards a ese store.

La separacion es segura dentro del protocolo vigente: Claim/asignacion/identidades
no se reasignan ni reescriben; Request conserva su primera fecha y bloquea Start;
Running bloquea replacement/edicion documental. Cambios de owner se revalidan en
Confirm; cierre concurrente se reconoce o rechaza sin sobrescribir terminal.
El test cambia owner entre lectura y Confirm y obtiene NotFound, sin DML del cierre.
SQL arbitrario que altere Claim/current/expecteds fuera del protocolo no queda
protegido por el preflight; no se promete serializar ese escritor ni se amplia legacy.

### Presupuesto y resultado

Un CTS enlazado al actionCt, con CancelAfter10s, abarca lectura/preparacion/Confirm.
No reinicio por etapa; el test compara el MISMO token en ambas conexiones. Drenaje
previo queda fuera. Confirm conserva su observacion independiente interna10s; A
no agrega un observador. Timeout sin resultado demostrable devuelve Indeterminate;
si el commit confirmado retorna tras vencer el presupuesto, se conserva su resultado.
El finally comprueba actionCt original incluso ante excepcion/reconciliacion:
caller cancelado recibe OCE original, no exito tardio ni rollback supuesto.
Todas las tareas se esperan; sin deadline duro20s ni drenaje forzado/background.

### Matriz de evidencia focal

Prefijo de casos nuevos: `ClosureA_`. R=runtime PostgreSQL real; S=inspeccion.
Las pruebas de B de seccion9 NO se ejecutan ni se consideran cubiertas aqui.

| Propiedad | Escenario y evidencia |
|---|---|
| Lectura coherente, historia, lease/contexto separados [R/S] | `Observation_History_NoDml_AllLocksReleased`: seis estados/contextos; snapshots independientes, reloj entre lecturas DB, precision; cero DML/SaveChangesAsync; NOWAIT sobre todas las tablas del protocolo despues del retorno |
| Autoridad y relaciones [R/S] | `Observation_ExactRejection_NoDisclosure`: IDs, Claim ausente/tipo Renew, intento ajeno/correlacion/Pending/temporal/Finished, Admin/inactivo y estado Requirement; error exacto, snapshot null, cero DML y datos propios/ajenos intactos |
| Cambio REAL de owner [R] | `RealOwnerTransfer_ClosureOnly_NotExecutionAdoption`: FK Project cambiada en DB; viejo actor sin metadata; nuevo actor observa y cierra, observador original/Renew rechazan ReceiptConflict; Claim y evidencia intactos |
| Coherencia concurrente [R] | `Observation_CoherentWithRequest_IndependentPids`: lector SHARE y Request UPDATE, PIDs distintos y pg_blocking_pids antes de liberar; snapshots antes/despues de solicitud |
| Drenaje autentico [R/S] | `Drain_RealOutstandingRead_NoProofOrDisposalUntilJoined`: reader ignora token, Task no completa/proof null/handles vivos; Confirm no se invoca; despues de release, join/OCE original, dos copias dispuestas y comprobante utilizable. `Drain_ForeignInvocationAndRecognizedClaim_DoNotAuthorizeClose`: prueba ajena rechazada, replay Claim sin comprobante ni recursos |
| Cierre N/P y contexto inactivo [R] | `Confirm_ExactEffects_Replay_NoDrainForHistory`: seis combinaciones; Cancelled exacto, v2, recibo publicacion null, lease liberada, intento/Requirement/actividad segun N/P; documentos/evidencia/Claim intactos; replay sin drain y cero DML |
| Preflight conservador [R] | `Confirm_PreflightRejections_NoConfirm`: sin solicitud/drain, no current, estado incompatible, expected date/attempt y vencida; cero conexiones de Confirm, cero DML, snapshot intacto; Confirm legacy vencido sigue LeaseConflict |
| Revalidacion y rollback [R] | `Revalidation_AfterRead_NoExternalLocks`: nuevo owner, cierre concurrente real, expiry modificado dentro de transaccion tras SaveChanges para perder guard; rollback integro, incluyendo intento/Requirement/actividad |
| Esperas y presupuesto [R] | `Waits_PostgreSqlBudgetCallerAndExpiry`: bloqueo real y PIDs distintos; actionCt original, timeout interno10s distinto del caller, expiry acreditado por reloj DB ANTES de liberar Confirm; snapshot intacto |
| Commit y OCE [R] | `CommitLoss_OriginalCaller_ConfirmedStatePreserved`: commit real con respuesta perdida, observador disponible/indisponible, caller antes/despues de commit/durante reconciliacion; AlreadyRecognized o Indeterminate/OCE original, DB Cancelled conservada, replay sin DML |
| Un presupuesto, resultado confirmado [R] | `OneBudgetAcrossReadAndConfirm_LateConfirmedCommitNotErased`: mismo token lectura/mutacion; se retiene retorno despues del commit real hasta cancelacion interna; retorna CancellationConfirmed, no borra commit |
| Terminal publicado/sucesor [R] | `PublishedAndCancelledSuccessor_HistoryPreserved` publica v2 realmente y rechaza cierre; `CancelledSuccessor_ReplayWithoutCurrentOrDrain` conserva sucesor y recibo sin DML |

Los checks de cero DML interceptan comandos Reader/NonQuery async con prefijos
mutantes conocidos; no son un detector universal de SQL. La inspeccion del store
confirma solo SELECT y ausencia de SaveChanges sincrono/async. No garantia por
mero conteo de pruebas. NOWAIT demuestra liberacion de locks, no contencion.

Fixtures: PostgreSQL/constraints/archivos reales, proveedor/catalogos simulados.
No API de transferencia de owner disponible: SQL parametrizado cambia la FK real,
no un actor ficticio en la respuesta. SQL temporal acorta expiry o crea incoherencia
representable sin desactivar FK/CHECK/indices; no es una operacion productiva previa.
Prueba de comprobante ajeno conserva un comprobante genuino de otra preparacion
antes de Reset del fixture aislado; no falsifica constructor ni Task. Los servicios
reales realizan Claim/Start/Request/Confirm/publicacion; el interceptor postcommit
simula respuesta perdida, NO crash/corte fisico de red.

### Inventario y verificacion

Cuatro fuentes nuevas indicadas arriba y cuatro partials nuevos en
`Tests/Infrastructure/Persistence`: `ClosureObservationPostgreSqlTests.cs`,
`ControlledCancellationPostgreSqlTests.cs`,
`ControlledCancellationBoundaryPostgreSqlTests.cs`,
`LocalDrainCompletionPostgreSqlTests.cs`. Unico archivo previo modificado: este
contrato. Sin cambios a supervisor, coordinador, observador de ejecucion, Claim,
Renew, finalizador legacy, modelo/EF/migraciones, replacement, DI o frontend.

Artefactos y manifiestos tracked/untracked inicial/final en
`C:/Users/USUARIO/AppData/Local/Temp/sng-closure-a-20261027`.
Baseline inmediato940 rutas, branch feature/public-experience-intake,
HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Worktree real, no restauracion desde HEAD.
Resultados separados, sin sumar ejecuciones solapadas:

| Corrida actual de este bloque | Passed | Failed | Skipped |
|---|---:|---:|---:|
| preflight.trx | 33 | 0 | 0 |
| final.trx | 71 | 0 | 0 |
| closure.trx | 72 | 0 | 0 |

Cierre7.7317min:50 casos nuevos +22 regresiones focales (ocho finalizacion v1/v2,
cinco Supervisor_NoResume, cuatro publico/manual copia/proveedor, uno drenaje ante
Renew incierto, dos caller durante Renew/reconciliacion, dos HTTP intake/replay).
Los33/71 nombres anteriores reaparecen; unico agregado entre final y closure:
`Observation_ExactRejection_NoDisclosure(requirement-state, StateConflict)`.
Los resultados de auditorias anteriores son historia, no se suman ni se presentan
como ejecutados aqui. HTTP mantiene Pending y cero automatismos/recibos lease.

No fallos preliminares de build/tests. Build1 completo0errores/112warnings35.81s:
una advertencia nueva EF1002 por SQL de tablas fijas del test NOWAIT; reemplazado
por sentencias constantes, sin cambiar expectativas ni produccion. Build2 completo
0/11119.13s. Revision posterior agrego rechazo StateConflict sin snapshot en el
NUEVO observador para Requirement incompatible y su caso; ajuste de inspeccion,
no defecto del store legacy ni fallo de tests ocultado. Rebuild final ClosureA
--no-restore --no-incremental:0errores/111warnings13.16s. warning-delta.csv vacio
frente ExplicitSupervisorAudit normalizando lineas y deduplicando archivo/mensaje.
No fuentes/tests cambiados despues de este rebuild. Solo este documento.

Evidencia de cierre: lector29068/Request21452; presupuesto bloqueado34996 por31628;
caller35644 por13132; expiry9752 por29572, PIDs distintos y pg_blocking_pids.
Antes de liberar Confirm, DB17:00:30.858700Z > expiry17:00:30.831365Z. Runtime exacto
en TRX/runtime-evidence.txt; no sleeps usados como unica prueba de orden/vencimiento.

Manifiestos940->948: ocho nuevos, este documento modificado, cero ausentes y939
anteriores byte-identicos. Rama/HEAD preservados; trabajo previo y untracked
conservados. git diff --check=0. Solo bin/obj ClosureA ignorados dentro del repo;
scripts/reportes/TRX/CSV fuera. Inventario exacto en delta.csv, ejecuciones/nombres
en executions.csv/cases.csv y deltas de nombres; no sustitucion de casos anteriores.

PostgreSQL17 temporal propio127.0.0.1:55565, usuarioaudit, data bajo artifact root.
closure-remaining.txt=0; closure-server-status.txt=no server running. Servidor
detenido y variable child-local retirada en finally por corrida. Cluster parado
y logs retenidos.100 raices distintas registradas en las tres corridas ausentes;
residuals.csv vacio. Originales verificados antes de teardown. Sin barrido/borrado
de recursos ajenos. Migraciones EXISTENTES solo sobre bases de fixture, ninguna nueva.

Sin DB/API habitual, reinicio, proveedores reales, delegacion, worker, enqueue,
heartbeat, frontend, commit/push/merge/despliegue o activacion. No auditoria nueva
del supervisor. Pendientes B/P5 vencido, su evidencia propia/migracion, autorizacion
Experience, hosting y mediciones operativas; no aprobacion comercial manual por
proyecto. Solo Windows; no crash/fsync/exactly-once ni fin remoto certificados.

Lectura para cierre y confirmacion controlada con lease vigente implementadas;
cierre por vencimiento y activacion pendientes.

### Complemento H1: autoridad del Claim en Start v2

2026-10-27. Correccion focal autorizada tras la auditoria del bloque A; no B ni
cambio de la politica de cierre por owner vigente. La auditoria anterior conserva
su NO APTO historico: su H1 de inspeccion ahora tiene reproduccion PostgreSQL real.

`StartClaim_H1_NewOwnerFromClosureSnapshot_RejectedWithoutWrites` crea Claim real
de A, Running sin intento ni solicitud, lease viva y documentos validos. Cambia
la FK de owner a B activo con constraints vigentes; la lectura de cierre de B
es real y suministra las identidades para StartVerifiedAsync directo. ANTES del
fix: Started/None, dos comandos DML y un intento persistido, contra la expectativa
Rejected. No rechazo anticipado por Request, expiracion o fixture invalida.
`reproduction.trx`: 0 passed / 1 failed / 0 skipped. Es defecto reproducido,
no solo ausencia de un test; no se atribuye explotacion HTTP ni proveedor remoto.

Unica produccion modificada: `AssignedJobStartStore`. Despues de autorizar owner
actual y tomar los locks de Job/Attempts, lee Claim por JobId y tipo Claim con
SHARE. Antes de reconocer o iniciar v2 exige actor historico igual al actor actual,
self-reference, IDs de job/Requirement/request, owner tecnico, generacion y Assigned
exactos; reconstruye la forma mediante CreateClaim (UUIDs, owner canonico, politica,
precision y formula). La fila y la ruta permanecen protegidas durante el inicio.
Falta/incompatibilidad: Rejected/IdentityConflict, recibo null, sin DML.

El actor anterior sin ownership recibe NotFound. No se usa RequestedBy como
credencial ni basta cualquier recibo del actor. El store configurado v2 aplica
el mismo guard a StartAsync/RecognizeAsync y a la observacion de commit incierto;
StartVerifiedAsync tambien pasa por el. Store default v1 rechaza el job v2 por
StateConflict antes de reconocer/iniciar; no hay entrada alternativa que omita
la validacion. Reconocimiento legitimo Running/terminal conserva recibo y cero DML.
No exige que expiry actual sea el expiry ORIGINAL del Claim: Renew puede extender.

Guard final de lease/version/identidades, atomicidad, default v1 y reconciliacion
anteriores intactos. Retirar EN MEMORIA solo el nuevo condicional/metodo reproduce
el hash original completo del store; no restauracion de fuentes. No se modifican
supervisor, Claim/Renew, publicacion, observadores, Confirm, replacement ni DI.
La observacion para cierre sigue admitiendo al owner vigente distinto del Claim;
eso NO autoriza Start. Request/Confirm por ese owner siguen disponibles bajo sus
condiciones previas, sin transferir ni regenerar el Claim.

Fixtures operativas de `JobTerminationTestSupport.Create`: solo running+v2 pasa
ahora por Claim real antes de Start; v1 y queued conservan su comportamiento y
el default verifiedPublication=false no cambia. Las pruebas negativas nuevas
crean primero un Claim real y despues preparan incoherencias representables por
SQL, manteniendo FK/CHECK/indices. No se presenta ese SQL como operacion productiva
ni proteccion contra cualquier escritor SQL. Otra PreQuote contiene los datos
ajenos para respetar unicidad de Requirement activo. Un Renew del mismo actor/job
con referencia a un Claim de otro job no sustituye al Claim propio.

| Criterio | Prueba focal / evidencia |
|---|---|
| A/C, nuevo owner y actor original revocado | `StartClaim_H1_NewOwnerFromClosureSnapshot_RejectedWithoutWrites`: rechazo exacto, cero DML, snapshot independiente intacto; entradas alternativas sin bypass |
| B/E, inicio valido e historia | `StartClaim_CompatibleStartAndReplay_TransferCannotRecognize`: cuatro combinaciones Running/terminal y owner conservado/transferido; intento real del actor, replay legitimo o IdentityConflict sin recibo; cero DML del replay |
| D, Claim ausente/ajeno/incompatible | `StartClaim_InvalidReceipt_ExactRejection_NoWrites`: missing, actor, request, assignment, owner, requirement, job; siete rechazos precisos y datos ajenos intactos |
| F, cierre no restringido al actor Claim | `StartClaim_NewOwnerCanRequestAndCloseWithoutAdopting`: B rechazado en Start, luego Request y Confirm controlado reales con proof previo; Claim/documentos intactos. Regresion `ClosureA_RealOwnerTransfer_ClosureOnly_NotExecutionAdoption` conserva rechazo de observador de ejecucion/Renew |
| G, orden de ownership | `StartClaim_OwnerTransferRace_PostgreSqlOrder`: ambos ordenes, conexiones/PIDs distintos, barreras y pg_blocking_pids; Start primero confirma como A, transferencia primero hace NotFound para A; no revocacion retroactiva |

No pruebas por conteo: las formulas/shape tambien dependen de dominio/CHECK y de
inspeccion del guard. Generacion/politica invalidas y ciertas formas incompatibles
no son fixtures SQL legales; no se desactivan constraints para inyectarlas.
No proveedor/storage invocados por Start; servicios reales de DB y proveedor
simulado solo en regresiones de ejecucion. Stop/lease/cancelacion no se reinterpretan.

Historia del complemento: primer build fallo CS1061 en la prueba nueva por usar
LeaseOwner en vez de AssignedLeaseOwner del snapshot; corregido antes de ejecutar
la reproduccion, sin produccion editada. Primer build correcto0errores/111warnings.
Primera seleccion tras fix `focal.trx`: 11 passed / 4 failed / 0 skipped. Dos fallos
eran fixtures con dos Requirements activos en una PreQuote (23505); dos eran
rollback del harness sobre transaccion ya confirmada. Corregidos a otra PreQuote
y seguimiento explicito del commit del harness, sin relajar expectativas ni
cambiar produccion para esos fallos. Logs/TRX originales conservados.

Artefactos del complemento, scripts, manifiestos SHA256 inicial/final (untracked
incluidos), resultados y diagnosticos en:
`C:/Users/USUARIO/AppData/Local/Temp/sng-start-h1-20261027`.
Baseline inmediato948 rutas, branch feature/public-experience-intake,
HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79. No restauracion contra HEAD.

La corrida ampliada `final.trx` registro 148 passed / 2 failed / 0 skipped:
15 casos H1, 28 de Start legacy, 35 Coordinator y 72 de la seleccion anterior
del bloque A (50 A, ocho terminacion v1/v2, doce Supervisor focales y dos HTTP).
Los15 nuevos pasaron; no suites completas Claim/Renew. Sus dos fallos fueron
`Coordinator_LostAuthorityBeforePublication_NoCompensation(lease)` y
`Coordinator_FailureFinalizerRejected_DoesNotAnnounceFailed`: las fixtures previas
usaban reloj DB menos1s como expiry. Con Claim recien confirmado esa fecha quedo
antes de Started; server.log prueba rechazo por ck_processing_jobs_lifecycle.
La excepcion del callback se tradujo a fallo de proveedor y el resultado fue
Failed, no el Rejected esperado. No se relaja esa expectativa.

Esas dos fixtures ahora usan Started+1microsegundo y comprueban reloj DB posterior
antes de actualizar expiry, con constraints activas. El build de la corrida
ampliada era anterior a este ajuste; se recompila y repiten exclusivamente ambos
metodos (tres casos, incluida authority). Produccion no cambio despues del primer
fix. No presentar la repeticion como una corrida unica de150 aprobados.

`closure.trx`: 3 passed / 0 failed / 0 skipped,0.7672min. Ambos fallos anteriores
reaparecen aprobados con expectativas originales. Union por ultima evidencia:
150 nombres Passed, cero Failed/Skipped; NO una corrida unica ni suma de169 casos.
Reproduccion y fallos preliminares permanecen en executions.csv/failures.csv/TRX.
Rebuild final aislado StartClaimH1 --no-restore --no-incremental:0errores,
111warnings,2:33.82. warning-delta.csv vacio contra ClosureAAudit normalizando
lineas/deduplicando archivo y mensaje. No cambios de fuentes/tests despues de
este rebuild; solo documentacion. Produccion sin cambios desde el primer fix.

Carreras aprobadas Start/transferencia: ownership primero34508/32884;
Start primero30808/37392. PIDs diferentes y pg_blocking_pids observado en cada
caso. No se cuenta como aprobada la corrida focal que fallo luego en cleanup.
H1 final: Rejected/IdentityConflict, DML=0 e intento null; mismo test que antes
mostro Started/None y DML=2. Snapshots de relectura independientes intactos.

Inventario: un nuevo partial; cinco existentes modificados (store Start, helper
JobTerminationTestSupport, dos partials Coordinator con fixtures temporales y
este contrato). Manifiestos948->949, cero ausentes;943anteriores byte-identicos.
Rama/HEAD preservados; ningun cambio a supervisor/Claim/Renew/Confirm/publicacion,
observadores, modelo/EF/migraciones, replacement, DI, transporte o frontend.
Scripts/logs/reportes externos; bin/obj StartClaimH1 ignorados dentro del repo.

PostgreSQL17 propio127.0.0.1:55569, usuarioaudit, cluster bajo artifact root.
closure-remaining.txt=0; closure-server-status.txt=no server running. Variable
child-local retirada y servidor parado en finally por corrida.112raices storage
registradas en los TRX ausentes; residuals.csv vacio. Cluster parado/logs retenidos,
sin limpieza general ni recursos ajenos. Sin DB/API habitual, reinicios, proveedor
real, migracion nueva, commit/push/merge/despliegue, B ni activacion.

H1 resuelto: Start v2 impide adopcion de una asignacion de otro actor;
la autorizacion del owner vigente para cierre permanece intacta.

## 13. Modelo y persistencia de ExpiredExecutionCancellation

2026-10-27. Implementacion exclusiva de la entidad, mapeo y migracion aditiva R4,
sin consumidor ni cierre operativo. Las referencias a entregas anteriores en
secciones1-12 conservan su historia; no se reabren H1, A o supervisor. No se
implementa CloseExpiredCancellationAsync, UPDATE a Cancelled, replay operativo,
reconciliacion, delegacion, worker, enqueue, DI, endpoints o activacion.

### Forma y garantias por capa

`ExpiredExecutionCancellation` es sealed, constructor privado, setters privados
y factory `Create` con las16 propiedades explicitas. No Guid.NewGuid, reloj,
normalizacion, referencias mutables a entidades ni efectos sobre job/intento/
Requirement/actividad. `ExpectedExpiry` del contrato corresponde a la propiedad
`ExpectedLeaseExpiresAtUtc` y columna `expected_lease_expires_at_utc`, siguiendo
JobLeaseOperation/Renew. Los otros15 nombres conservan el contrato.

La factory exige UUIDs obligatorios no vacios, intento null o no vacio; owner
canonico exacto `exec:{instance:D}:{claim:D}` en minusculas y ligado a ClaimOperationId;
generacion/politica1; las cinco fechas UTC con ticks divisibles por10. Exige solo
Assigned<ExpectedExpiry, ExpectedExpiry<=Finished<=Guard y Requested<=Finished.
Las igualdades permitidas y una solicitud anterior a Assigned se aceptan: validar
la solicitud real del job pertenece al store. No impone desigualdad entre UUIDs
de espacios distintos. No recibe ni compara actor historico Claim, no inventa
actor de la solicitud y no acredita que el actor de cierre tenga autoridad.

| Capa | Garantia implementada | Limite deliberado |
|---|---|---|
| Dominio | Forma, identidades explicitas, owner, politica/generacion, UTC/microsegundos y orden temporal aprobado | No reloj vivo, autoridad, solicitud persistida, estado job ni commit |
| EF | DbSet/config focal; Throw despues de guardar en las16 propiedades; ninguna generacion de OperationId | No protege DELETE, SQL directo, ExecuteUpdate ni escritores arbitrarios |
| PostgreSQL | PK OperationId en ESTA tabla, UNIQUE JobId, cinco FK RESTRICT, tres CHECK y NOT NULL salvo intento; cinco fechas finitas timestamptz | FK simples solo prueban existencia, no tipo Claim ni correspondencias entre filas |
| Futuro store | NO implementado en este bloque | Owner vigente, v2/Claim real, intento/solicitud/expiry exactos, locks/guard reloj DB, cambios atomicos job+recibo, replay y candidato propio incierto |

PK no introduce un registro global entre clases de operaciones: puede coincidir
OperationId con un OperationId de JobLeaseOperation. UNIQUE JobId limita recibos
de esta tabla, no significa que insertar uno haya cerrado ese job. El test de
limites persiste referencias existentes ajenas y un Renew como referencia Claim;
confirma que esas correspondencias NO se atribuyen a FK simples. SQL directo
puede actualizar/borrar la hoja, sin triggers. No se amplian las reglas aprobadas.

SQL NOT NULL protege las15 propiedades obligatorias; intento nullable tiene CHECK
`IS NULL OR <> empty uuid`. Comparaciones owner/fechas no pueden aceptar UNKNOWN
por nulos en campos obligatorios. PostgreSQL usa precision microsegundos por defecto;
roundtrip conserva los valores. Rechazo del offset original y submicrosegundos es
de dominio: timestamptz normaliza offsets/redondea precision SQL, no se le atribuye
la capacidad de reconstruir y rechazar el input original.

### Migracion y compatibilidad

`20261027195507_AddExpiredExecutionCancellations` y Designer/snapshot generados:
solo `core.expired_execution_cancellations`, PK, cinco indices (incluido UNIQUE
JobId), cinco FK RESTRICT a job/Requirement/usuario/JobLeaseOperation/intento y
tres CHECK. Down elimina solo la tabla nueva. SQL regenerado inspeccionado:
CREATE TABLE/INDEX y metadata __EFMigrationsHistory; sin UPDATE/DELETE/INSERT de
negocio, backfill, purga, triggers o cambios a migraciones anteriores.

Upgrade real desde `20261026165918_AddJobLeaseOperations`: cuatro jobs v1/v2
Queued/Succeeded, intentos, documentos, evidencia, extracciones/propuestas y ocho
recibos Claim/Renew previos permanecen iguales en relectura independiente; recibos
de publicacion v1/v2 preservados, tabla nueva vacia y HasPendingModelChanges=false.
Historicos de fixture estructurales, no cierre o publicacion remota operativos.
No se sintetizan recibos por consultar/materializar jobs. No consumidor productivo.

### Matriz de evidencia actual

R=pruebas actuales con PostgreSQL real cuando corresponde; S=inspeccion.

| Escenario | Evidencia y frontera |
|---|---|
| Dominio,41 casos [R/S] | Valores exactos con/sin intento, igualdad temporal, IDs de espacios distintos iguales, solicitud anterior a Assigned permitida, cinco fechas offset/submicrosegundos rechazadas, IDs/owner/generacion/politica/orden invalidos con ArgumentException precisa; ninguna ThrowsAny |
| Roundtrip,2 casos [R] | Todas propiedades/microsegundos, actor cierre distinto del Claim, job objetivo Queued/Running intacto, grafo ajeno Succeeded no vacio conservado |
| Igualdades DB [R] | Expected=Finished=Guard y Request=Finished permitidos; Request anterior a Assigned permitido; ningun efecto comercial |
| EF,16 casos [R] | Cada propiedad persistida cambia por separado: InvalidOperationException con nombre de propiedad, cero DML interceptado y recibos/grafo identicos desde contextos nuevos |
| CHECK/NULL/infinitos [R] | Nulos obligatorios23502, identidades/owner/forma23514, ambas infinidades en cada fecha rechazadas; recibo/grafo intactos. No se exige orden entre CHECK competidores |
| PK/UNIQUE [R] | OperationId repetido entre jobs23505/PK; otro OperationId mismo job23505/ux_expired_cancellation_job; ID tambien existente en tabla lease permitido |
| Cinco FK y deletes [R] | Referencias ausentes23503/nombre exacto, confdeltype=r en las cinco; deletes rechazados por cada FK. Se separan dependencias de fixtures, nunca se desactivan constraints |
| Limites entre filas [R/S] | Referencia Renew y entidades ajenas admitidas por modelo; SQL directo update/delete de recibo permitido. No se presenta como cierre autorizado |
| INSERT concurrente [R] | Contextos/conexiones distintos, ganadorPID2984/bloqueado26308, barrera transaccional y pg_blocking_pids observado; un recibo y23505/ux_expired_cancellation_job. Job Queued, cero intentos, grafo intacto |
| Upgrade [R/S] | Esquema inmediatamente anterior, datos/recibos preservados, tabla nueva vacia, modelo sin cambios pendientes; SQL y migracion revisados |
| HTTP,2 casos [R] | DIRECT_CONTACT/ANDRES_CANDELA intake/replay/conflicto, Pending, cero jobs/attempts/evidencia ejecucion/recibos lease/recibos cierre/llamadas automaticas; proveedor y storage del host simulados |

El contador EF observa Reader/NonQuery async con prefijos INSERT/UPDATE/DELETE,
adecuados para los SaveChanges probados; no detector universal de SQL arbitrario.
La carrera demuestra unicidad de recibos, NO cierre operativo ni replay. Los tests
de modelo no abren archivos: fixtures son metadatos/grafos persistidos, no spools.
Snapshots comerciales incluyen filas no vacias; insertar el recibo no dispone
recursos, cancela proveedor, cambia actividad o confirma un terminal.

### Ejecuciones, fallos e integridad

| Corrida actual | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Primera, archivo final.trx | 61 | 7 | 0 |
| Cierre, closure.trx | 68 | 0 | 0 |

Son los mismos68 nombres:41dominio+25PostgreSQL+2HTTP, no136casos diferentes.
Ninguna suite historica ajena repetida. Los siete fallos preliminares eran la
misma preparacion: dos grafos usaban StorageKey identica,23505 del indice existente
ux_requirement_files_storage_key antes de probar el recibo. Solo se agrego index
a esa clave del fixture; produccion y expectativas intactas. Se conserva copia
del test previo, TRX, nombres/mensajes/stacks y name-delta vacio. No fallos de build.

Tres rebuilds completos aislados ExpiredReceiptModel:0errores/111warnings cada uno;
primero1:46.43, previo a primera corrida14.51s, cierre1:01.45. warning-delta vacio
contra StartClaimH1Audit normalizando coordenadas/deduplicando archivo/mensaje.
Primera corrida2.4662min; cierre2.5317min. Produccion sin cambios tras build1;
despues del rebuild de cierre solo documentacion. No evidencia historica se cuenta
como ejecutada en este bloque.

Inventario seis nuevos:
- Domain/PreQuotes/ExpiredExecutionCancellation.cs
- Infrastructure/Persistence/Configurations/ExpiredExecutionCancellationConfiguration.cs
- Infrastructure/Persistence/Migrations/20261027195507_AddExpiredExecutionCancellations.cs
- Infrastructure/Persistence/Migrations/20261027195507_AddExpiredExecutionCancellations.Designer.cs
- Tests/Domain/PreQuotes/ExpiredExecutionCancellationTests.cs
- Tests/Infrastructure/Persistence/ExpiredExecutionCancellationPostgreSqlTests.cs

Cuatro existentes modificados: ApplicationDbContext (solo DbSet), snapshot (solo
nueva entidad/relaciones), PublicExperienceHttpPostgreSqlTests (solo assertion de
tabla vacia) y este contrato. Retirar esas adiciones en memoria reproduce hashes
iniciales exactos de los tres archivos compartidos, preservando BOM originales.
Start/Confirm/supervisor/bloque A/Claim/Renew/publicacion/modelos anteriores/EF
anterior/migraciones previas/DI/replacement/transporte permanecen intactos.

Artefactos, SQL, scripts, TRX/CSV y manifiestos SHA256 inicial/final incluidos
untracked en `C:/Users/USUARIO/AppData/Local/Temp/sng-expired-receipt-model-20261027`.
Baseline949 rutas, rama feature/public-experience-intake,
HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Base de preservacion: worktree real,
no HEAD. Sin restauracion, commit/push/merge/despliegue. No AGENTS aplicables
en backend/ancestros consultados. Bin/obj ExpiredReceiptModel ignorados.

Cierre de integridad:949->955 rutas, seis nuevas/cuatro modificadas/cero ausentes,
945anteriores byte-identicas. Rama/HEAD preservados;55tracked modificados,
173->179untracked,0staged. git diff --check=0; trabajo previo preservado.

PostgreSQL17 temporal propio127.0.0.1:55573,audit, data bajo artifact root. En ambas
corridas remaining=0 y servidor detenido en finally; variable child-local retirada.
Cluster parado/logs retenidos; residuals vacio para sng-sealed/sng-execution-docs/
sng-receipt-unit. Modelo sin storage; HTTP usa cleanup de helpers existentes.
No limpieza general ni recursos ajenos, DB/API habitual, proveedores reales o
reinicios. Solo Windows; no crash/fsync/consumo remoto certificados. Sin politica
de purga/retencion definitiva: un recibo por job, capacidad operacional pendiente.

**Modelo y persistencia de ExpiredExecutionCancellation implementados;
cierre operativo por vencimiento y activacion pendientes.**

## 14. Implementacion B: cierre explicito por vencimiento

2026-10-27. Complemento a la historia anterior, no otra aprobacion de R1-R4 ni
reapertura de H1/A/supervisor/modelo. La matriz previa a las ediciones esta en
`C:/Users/USUARIO/AppData/Local/Temp/sng-expired-closure-20261027/decisions.txt`.
No se encontro contradiccion material que exigiera una politica nueva.

### Superficie y semantica

`CloseExpiredCancellationService.CloseExpiredCancellationAsync` recibe
`ExpiredCancellation`, actor explicito y token de accion. `ExpiredClosureResult`
separa `JobTerminationResult` del recibo propio `ExpiredExecutionCancellation`.
Identity, OperationId, owner/generacion, Assigned, expected attempt/expiry y fecha
esperada de Request son explicitos; CorrelationId procede del job bajo locks y
se conserva en guard/recibo. La validacion de entrada reutiliza la factory solo
para forma: sus fechas provisionales no se devuelven ni persisten como cierre.

`ExpiredCancellationStore` usa contexto propio, presupuesto TOTAL cooperativo10s
y observacion independiente10s solo tras commit intentado. No endpoint ni DI.
Autoriza usuario activo/owner ACTUAL antes de leer/divulgar recibos. No bypass
Admin, actor inferido ni igualdad obligatoria con Claim.ActorUserId. Permite
Client/Project inactivos para cancelar, no para ejecutar ni hacer replacement.

Nueva mutacion exige v2 Running, Claim de tipo/self-reference/forma completos,
asignacion/identidades exactas, solicitud persistida exacta, expiry esperado y
N current/Pending sin intentos o P current/Processing compatible. Rechaza grafo
del intento objetivo; evidencia de ejecucion es admisible y grafos ajenos intactos.
No Request implicito desde Stop/token/timeout, ni LocalDrainCompletion requerido.

Orden global: Submission si aplica, User, Client, Project SHARE; Quote,
Requirement, Job UPDATE; Attempts por Id UPDATE; Claim y recibo SHARE. Relee ruta
bajo locks. T0=clock_timestamp despues de esperar; expiry<=T0 y lifecycle no futuro.
Transiciones de dominio existentes; solo attempt/Requirement/quote se adjuntan
cuando P. Job detached. Ultima sentencia mutante materializa G, UPDATE guardado
del job e INSERT recibo desde RETURNING. Finished=T0, Guard=G, G>=T0. Cero filas
o colision revierte tambien el SaveChanges previo. No SaveChanges posterior.
Una lectura posterior del reloj no reconstruye G: cero filas se clasifica por
identidades/expiry todavia protegidas, o perdida del guard temporal.

Replay exacto autoriza primero, reconstruye recibo y compara terminal/intento,
sin exigir current/lease/contexto comercial activo ni modificar sucesor. Otro ID
tras terminal no crea recibo. Reconocer un recibo de otro actor no esta permitido.
Tras commit incierto se dispone scope original y compara candidato PROPIO completo
(16 propiedades, incluidas T0/G) en lectura nueva. Ausencia/bloqueo/denegacion/
incompatibilidad => Indeterminate sin recibos. Rollback no establecido no se anuncia
como PersistenceError. Finally comprueba caller original aun tras observacion
fallida o commit confirmado. No retry, compensacion, segundo ID ni tarea abandonada.

Cuatro conceptos distintos: Cancelled impide publicacion backend posterior;
CancellationConfirmed/AlreadyRecognized con recibo acreditan esta operacion;
Indeterminate/OCE pueden coexistir con commit real; ninguno prueba fin del proveedor
ni concede permiso para nuevo envio. Una tarea local aun drenando sigue siendo
responsabilidad de su propietario: B no dispone sus handles ni certifica cleanup.

### Matriz de evidencia

R identifica escenarios ejecutables de esta seleccion; S indica inspeccion.
El resultado de cada corrida se consigna al cierre, no se deduce del numero de casos.
Prefijo comun de los metodos nuevos: `ExpiredB_`.

| Propiedad | Escenario R / complemento S |
|---|---|
| Efectos N/P, contexto activo/inactivo, evidencia permitida | ExactEffects_RealClaimRequest_Replay, seis combinaciones; Claim/Start/Request reales; Finished/Completed/Updated/actividad concordantes; N deja negocio intacto; replay DML0 |
| Actor actual distinto de Claim, no adopcion de autoria | OwnerTransfer: FK de Project cambiada con constraints; viejo owner NotFound, nuevo cierra, Claim intacto; actor posterior no reconoce recibo anterior; observacion para cierre separada |
| Rechazos precisos sin metadata | Rejections,25 casos; RelationalIncompatibility,3; InvalidInput,7 sin conexion. Correlacion/fechas/estado/Claim/expecteds/grafo/autoridad; ambos recibos null y snapshots independientes |
| Historicos y otro OperationId | TerminalAndReplay: payload, otro ID, Fail real, Confirm real; replay propio despues de replacement; terminal publico real en PublicationAndClosure_LegalOrders |
| Grafo ajeno no vacio | ForeignGraphNonempty conserva extraccion/propuesta estructurales, job ajeno y documentos/recibos. El rechazo de intento ajeno esta en RelationalIncompatibility, no se infiere del nombre del primer metodo |
| Atomicidad post-SaveChanges | PostSave(error/guard/caller), guard temporal inyectado y cambio de identidad: rollback entero, sin job tracked ni recibo residual; cierre de conexion sin rollback reconocido => Indeterminate |
| Commit propio y cancelacion | CommitLoss,8 variantes: commit real y respuesta perdida, observador disponible/ausente/candidato G distinto/denegado/fallido, OCE en commit/observacion disponible o fallida; una mutacion, dos scopes |
| Presupuesto y locks | BlockedContexts budget/caller/observer, PIDs distintos y pg_blocking_pids; OneActionBudget mismo token durante accion y resultado confirmado conservado tras timeout interno |
| Reloj tras espera | WaitsForLock retiene Quote, acredita lease viva, espera reloj DB>horizonte antes de liberar; T0 posterior y G>=T0. No reloj local como autoridad |
| Dos B y colision | RacingClosures_LocksAndAtomicCollision same/different/collision: PIDs/conexiones distintos, barrera precommit y bloqueo observado; aplica/reconoce, aplica/TerminalConflict, colision global IdentityConflict y rollback integro del perdedor |
| Replacement ambos ordenes | ReplacementBothOrders_RealOperation_HistoricalReplay: BeginDocumentWrite real serializa por Quote con B; servicio completo rechaza Running en su preflight, despues del cierre crea version nueva; replay B no altera sucesor ni actividad |
| Publicacion/operaciones tardias | PublicationAndClosure_LegalOrders: Publish real gana ANTES de Request, que espera locks; o Request+B ganan con proveedor pausado, Renew y Publish tardios rechazan terminal. Un proveedor simulado, originales y copias bajo propietario existente |
| Renew legal | RenewWithRequest_BothLockOrders: con Request, nunca extension. RenewBeforeRequest real extiende primero; B expected anterior recibe LeaseConflict sin adoptar fecha |
| A separado | ConfirmAliveWins_OrLosesGuard usa caller A y drenaje genuino: commit A retenido gana; guard A perdedor revierte, posterior accion B explicita cierra. No fallback automatico |
| Replacement no ampliado | ReplacementDoesNotInheritClosureException mantiene rechazo por Client/Project inactivo y multiples terminales ambiguos |
| Compatibilidad | Ocho Finalize_Atomic v1/v2, dos Renew_P5, transferencia/cierre A y dos HTTP intake/replay Pending sin automatismos, incluidos recibos de cierre |

Los nombres completos y parametros en cases.csv/TRX son la referencia exacta.
Los snapshots usan contextos independientes y comprenden job, Requirements,
attempts, actividad, documentos/evidencia/extraccion/propuestas/Claim/Renew, recibos
de cierre, Project y usuario/actividad de usuario. No se afirma snapshot universal
de toda columna de toda tabla. CombinedCommands detecta prefijos mutantes async
conocidos, no SQL arbitrario; inspeccion confirma ausencia de escrituras en replay.
No storage/proveedor en servicio/store por dependencias y codigo, no por un mock
de cierre que los sustituya. Cinco partials reutilizan helpers anteriores intactos.

### Fronteras de las fixtures

Constraints siempre activas. SQL de preparacion acorta expiry a Started+1us o
DB+2s con comprobacion de reloj; no simula un Renew previo ni cambia politica120s.
SQL relacional crea incompatibilidades representables, no reglas de lifecycle
nuevas. Intento ajeno se vincula solo despues de liberar su link unico anterior;
otro activo usa N con un Pending no vinculado, no dos activos imposibles.
Graficos ajenos son controles estructurales, no publicacion remota.

FinalTimeGuardLoses modifica mediante interceptor SOLO la comparacion final para
forzar guard perdedor; prueba rollback/clasificacion, no regresion fisica del reloj.
Hooks postcommit pierden respuesta DESPUES del commit real, no crash ni corte de red.
RenewBeforeRequest prueba CAS anterior a evaluar tiempo; no afirma que el horizonte
viejo haya vencido en esa secuencia. Las pruebas de vencimiento son independientes.
Carreras de replacement distinguen el scope real de escritura del servicio completo:
este ultimo rechaza Running antes de abrir ese scope; no se atribuye un bloqueo
PostgreSQL a su preflight. A con respuesta retenida es orden postcommit, no espera
de locks. PIDs/pg_blocking_pids se afirman solo donde se observaron antes de release.

No plazo duro20s: rollback/cleanup/callbacks pueden demorar y se esperan. No defensa
ante escritor SQL arbitrario, fsync/crash/Unix/consumo remoto/exactly-once. El cierre
no detiene por si mismo una tarea/proveedor anterior, ni automatiza otra ejecucion.

### Inventario y preservacion

Nuevos, exclusivamente:
- Application/PreQuotes/ProcessRequirement/CloseExpiredCancellationService.cs
- Infrastructure/Persistence/Repositories/ExpiredCancellationStore.cs
- Tests/Infrastructure/Persistence/ExpiredCancellationPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ExpiredCancellationBoundaryPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ExpiredCancellationRacePostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ExpiredCancellationCompatibilityPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ExpiredCancellationAdditionalPostgreSqlTests.cs

Un existente modificado: este contrato, complemento sin borrar historia previa.
Sin cambios a supervisor, A, Start, Confirm, Claim/Renew, publicacion, replacement,
modelo/EF/migraciones/DI ni helpers anteriores. Artefactos externos en
`C:/Users/USUARIO/AppData/Local/Temp/sng-expired-closure-20261027`.
Baseline955 rutas tracked/untracked no ignoradas, rama feature/public-experience-intake,
HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79; preservacion contra worktree real,
no restauracion desde HEAD. Solo bin/obj ExpiredClosure ignorados dentro del repo.
Sin DB/API habituales, reinicios, proveedores reales, frontend, commit/push/merge/
despliegue, nuevas migraciones, worker, enqueue, heartbeat, delegacion o activacion.

### Historia de verificacion B

Dos rebuilds preliminares fallaron sin ejecutar pruebas: preflight,5errores y
16warnings (tres CS1503 por interpolacion condicional sin FormattableString explicito,
dos CS1061 por nombre de propiedad de grafo); second,1error y111warnings (CS0234
por namespace Application resuelto dentro de Tests). Corregidos en fuentes nuevas,
sin cambiar contratos ni fuentes previas. Logs completos conservados; no se cuentan
tests sobre un binario que no compilo. Tiempos1:18.86 y1:17.22 respectivamente.

Rebuild third:0errores/111warnings,36.67s. third.trx:76passed/2failed/0skipped,
78casos,6.7413min. Fallos de preparacion conservados por nombre y stack:
Rejections(other-active),23505 ux_requirement_processing_attempts_active_requirement_id;
RelationalIncompatibility(foreign-attempt),23505 ux_requirement_processing_attempts_correlation_id.
Ambos ocurrieron antes del cierre auditado. Fixtures corregidas como se explica
arriba, sin desactivar constraints ni relajar AttemptConflict/DML0/snapshots.
Copias third-main-tests.cs/third-additional-tests.cs/third-race-tests.cs conservan
fuentes previas a esos ajustes. No se afirma que la primera corrida haya pasado.

Antes del rebuild final se reforzaron los dos ordenes A con caller A/drenaje reales,
el orden documental con BeginDocumentWrite real, y se agregaron tres casos sobre
rollback desconocido/guard de identidad/guard temporal. La clasificacion productiva
del guard0 se ajusto por inspeccion para no inferir G desde otro reloj posterior;
no cambio la condicion de cierre. No se atribuye toda diferencia entre corridas
exclusivamente a las dos fixtures. El cierre repite los78 nombres, agrega tres
casos nuevos y13regresiones focales; no ejecuta suites completas historicas.

### Cierre de ejecucion e integridad B

| Corrida de este bloque | Passed | Failed | Skipped |
|---|---:|---:|---:|
| third.trx | 76 | 2 | 0 |
| final.trx | 94 | 0 | 0 |

Final10.0350min, exit0:81casos nuevos y13regresiones (8Finalize v1/v2,2Renew_P5,
1transferencia/cierre A,2HTTP). Los78 nombres anteriores reaparecen;16adiciones
son3casos nuevos y13regresiones existentes, no16tests nuevos. No sumar corridas
como172casos distintos. cases.csv/executions.csv/failures.csv/latest-cases.csv y
name-delta.csv derivan de TRX. Todos los escenarios R de la matriz pasaron en final;
S complementa locks/guards/forma completa y ausencia de consumidores, no se infiere
una garantia del numero de pruebas. Los dos fallos de third no se borran.

Rebuild final aislado ExpiredClosure --no-restore --no-incremental --verbosity
minimal:0errores/111warnings,1:09.97. warning-delta.csv vacio contra audit-build.log
de ExpiredReceiptAudit, normalizando coordenadas y deduplicando archivo/mensaje.
No mejora de warnings inferida de duracion. Tests --no-build, filtros y comandos
en run.ps1/final-filter.txt. Solo documentacion modificada despues de este rebuild.

Evidencia final de bloqueos (ganador/bloqueado, PIDs distintos): B mismo ID
17652/22876; distintos2904/27992; colision31004/29260; replacement cierre primero
32364/31592 y scope documental primero13652/32772; Renew primero29200/30728 y
B primero25456/29196; Publish antes de Request24916/14068. pg_blocking_pids observado
antes de liberar. Esperas budget34952/29908, caller36924/7296 y observador33980/27384.
Vencimiento: DB21:08:54.491438Z > expiry21:08:54.438542Z antes de liberar25404/19760;
T0 del cierre posterior. runtime-evidence.txt separa cada salida por corrida/nombre.

Manifiestos inicial/final SHA256 incluidos untracked:955->962 rutas,7nuevas,
1modificada (contrato),0ausentes,954anteriores byte-identicas. delta.csv enumera
exactamente el inventario anterior. Rama/HEAD preservados;55tracked modificados,
179->186untracked,0staged. git diff --check=0; no restauracion desde HEAD. Produccion
previa, helpers, modelo/EF/migraciones/DI y servicios excluidos byte-identicos.
consumers.txt limita referencias productivas nuevas al servicio/interfaz y store.

PostgreSQL17 propio127.0.0.1:55577,usuarioaudit,data bajo artifact root. third y final
remaining=0; ambos server-status=pg_ctl:no server running. Runner retira variable
child-local y detiene servidor en finally; sin tareas propias pendientes. Cluster
parado/logs retenidos como evidencia, no servicio activo.146raices distintas de
storage registradas entre ambas corridas comprobadas ausentes; storage-check.csv
todosExists=false. residuals.csv vacio para sng-sealed/sng-execution-docs/
sng-receipt-unit. Originales comprobados antes del teardown; sin limpieza general
ni borrado de recursos ajenos. Migraciones existentes solo sobre fixtures propias.

**Cierre explicito por vencimiento implementado y verificado;
integracion, delegacion y activacion pendientes.**
No certifica detencion remota, nuevo permiso de ejecucion, hosting/presupuestos
medidos, retencion operativa ni integracion automatica. No cambio a los bloques
ya cerrados, ni activacion productiva por este entregable.
