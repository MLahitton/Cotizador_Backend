# Claim inicial y renovacion de lease v2

Actualizacion: la seccion 9 registra el primer subbloque de modelo/persistencia;
la seccion 10 incorpora claim operativo explicito y la seccion 11 renovacion
explicita de lease vigente, sin activacion.
Las restricciones de tarea documental siguientes conservan su contexto historico;
no describen esos subbloques posteriores. Heartbeat e integracion siguen pendientes.

Estado: **CONTRATO P1-P7 APROBADO CON PRECISIONES**, 2026-10-26. Contrato para el
siguiente bloque, no implementacion ni nueva auditoria de D.2c.6a.
La aprobacion comprende las decisiones descritas y la migracion aditiva de P3
para un bloque posterior. Esta tarea autoriza UNICAMENTE actualizar el contrato:
no implementar codigo, tests, migraciones, claim, renovacion ni worker; no activacion.

## 1. Base contrastada y frontera

| Hecho existente | Fuente relativa al repo | Consecuencia |
|---|---|---|
| `AssignExecution` solo admite Queued; fija Running, StartedAtUtc, owner, vencimiento y generacion 1 | `Domain/PreQuotes/RequirementProcessingJob.cs` | Claim no crea intento. No incrementar a 2 ni reasignar Running |
| Queued tiene generacion 0, sin lease/inicio/intento; Running exige generacion 1, fechas y owner no vacio; terminal borra owner/vencimiento | `Infrastructure/Persistence/Configurations/RequirementProcessingJobConfiguration.cs`, CHECK lifecycle/attempt | El job terminal no conserva suficiente identidad tecnica para reconocer un claim propio |
| Owner maximo 200; fechas UTC; vencimiento posterior a asignacion; no metodo de renovacion | Dominio/configuracion anteriores | Hace falta operacion focal nueva; setters privados no prueban autoridad |
| Un job activo por Requirement, request unico, intento vinculado unico | Indices de esa configuracion | DB contribuye a exclusion; no sustituye locks/autorizacion |
| `Create` y constructor del start store conservan v1; factory verificada y `StartVerifiedAsync` seleccionan v2 | Dominio, `AssignedJobStartStore.cs`, `StartAssignedJobService.cs` | Nuevas operaciones solo v2, sin convertir filas v1 |
| Inicio asignado crea intento y cambia Requirement/actividad, solo tras asignacion Running vigente | `AssignedJobStartStore.cs` | Claim no debe llamar a este servicio ni duplicar sus efectos |
| Preparacion, consumo y publicacion revalidan owner de negocio, lease, intento y cancelacion | `ExecutionDocumentPreparationStore.cs`, partial `.Consumption.cs`, `JobPublicationStore.cs` | Renovar no concede permiso permanente para enviar/publicar |
| Confirmacion exige Running, solicitud persistida y lease vigente, incluido guard final DB | `JobTerminationStore.cs` | Denegar renovacion tras cancelacion deja una ventana finita para confirmar |
| D.2c.6a solo continua con Started, no retoma AlreadyStarted ni incertidumbre | `CoordinateAssignedExecutionService.cs`; contrato de bytes seccion 18 | Recibo de lease no autoriza recovery ni reenvio |

Decisiones ya aprobadas: publicacion v2/RequiredPublicationVersion y coordinador
explicito, con los limites de las secciones 17-18 de
`document-byte-identity-contract.md`. Generacion 1 y ausencia de reasignacion son
restricciones reales. Duracion, identidad tecnica, recibos de lease y politica
de renovacion descritos abajo son decisiones P1-P7 aprobadas expresamente en esta
actualizacion, no decisiones heredadas ni funcionalidades implementadas.

Incluye solo (A) claim inicial de JobId exacto Queued v2 y (B) renovacion explicita
de Running vigente. Excluye crear jobs/intentos, leer/enviar archivos, invocar
coordinador, worker, endpoints, heartbeat en segundo plano, recovery, retry,
reapertura y reasignacion de vencidos. No cambia timeouts/retries del proveedor.

## 2. Identidad y seleccion aprobadas

Actor de negocio: `actorUserId` explicito no vacio desde un contexto autenticado
confiable; comprobar usuario persistido activo y owner actual del Project. Admin
ajeno recibe NotFound seguro. Nunca derivar actor de RequestedBy ni tomar
CreatedByUserId como credencial: este ultimo solo sirve para comparar autorizacion.
Project/Client deben estar activos para mutar y las relaciones deben ser exactas.

Owner tecnico: `exec:{instanceId:D}:{claimOperationId:D}` en minusculas ASCII;
dos UUID aleatorios no vacios generados por backend confiable, no por usuario HTTP.
InstanceId nace por encarnacion del ejecutor y cambia al reiniciar; claimOperationId
nace una vez por solicitud de claim de un job. Owner no se reutiliza en otro job,
no es hostname/PID ni secreto, identidad humana o token de autenticacion. Colision
UUID extremadamente improbable no es unicidad matematica: los recibos imponen
unicidad global de OperationId y de owner de claim. Fallar ante colision, no adoptar.
Misma solicitud conserva ambas identidades; reinicio no las recupera para continuar.

Este bloque solo acepta un caller backend con actor validado por la frontera de
autenticacion existente. Un futuro worker necesitará un contexto de delegacion
explicitamente aprobado (quien lo emite, alcance JobId/actor, caducidad, revocacion
y autenticacion del ejecutor), y revalidacion DB en cada operacion. No hay tal
delegacion aprobada hoy: sin ella no puede arrancar desatendido. No se inventa
cuenta de servicio ni bypass Admin. El string owner por si solo no autoriza nada.

Decision P2: **JobId exacto**, mas RequirementId y ExecutionRequestId
esperados; esperar locks con CancellationToken, no buscar otro candidato.
Alternativa de cola: el indice State/CreatedAtUtc/Id permitiria descubrir candidatos
ordenados, pero falta politica de equidad, autorizacion, bloqueo e invalidos.
No se propone implementarla aqui. Un futuro selector no debe tomar Job FOR UPDATE
SKIP LOCKED antes de Submission/User/Project/Requirement: invertiria el protocolo.

## 3. Firmas y resultados del contrato

Firmas internas, sin endpoints ni registro DI en este bloque:

```csharp
Task<LeaseOperationResult> ClaimInitialV2Async(
    ClaimInitialLease request, Guid actorUserId, CancellationToken ct);
Task<LeaseOperationResult> RenewActiveV2Async(
    RenewActiveLease request, Guid actorUserId, CancellationToken ct);
```

`ClaimInitialLease`: JobId, RequirementId, ExecutionRequestId, OperationId
(claimOperationId), InstanceId. Owner se construye en backend con formato anterior.
`RenewActiveLease`: mismos tres IDs, OperationId NUEVO, ClaimOperationId,
LeaseOwner, LeaseGeneration (=1), ExpectedLeaseExpiresAtUtc y
ExpectedProcessingAttemptId nullable. Este ultimo distingue 0/null de 1/ID sin
crear un intento; si inicio concurrente cambio el vinculo, devuelve Conflict.
Todos los UUID obligatorios no vacios. Fechas de entrada UTC a microsegundos
exactos (ticks % 10 = 0), tomadas de lectura DB, sin truncar para hacerlas coincidir.
Duracion no es parametro libre del caller; politica interna v1 fija L=120 segundos.

Resultado: `Claimed`, `Renewed`, `AlreadyApplied`, `Rejected`, `Conflict`,
`PersistenceError` o `Indeterminate`. Son resultados de llamada, no estados nuevos
del job. Rejected incluye InvalidRequest, NotFound/InactiveUser, InactiveContext,
WrongVersion, CancellationRequested, Expired y Terminal; Conflict incluye
IdentityMismatch, AlreadyOwned, StaleLease, AttemptConflict y ReceiptConflict.
Errores simbolicos de esta API interna futura, no codigos de FinalizeFailed.

`Renewed` significa operacion aplicada y recibo confirmado, NO necesariamente
extension efectiva. Para Renew, distinguirla por la comparacion del recibo
`ExpiresAtUtc > ExpectedLeaseExpiresAtUtc`; igualdad significa aplicada sin extension.
El replay conserva esa comparacion original, no la recalcula contra la lease actual.

Exitos incluyen recibo inmutable y observacion separada: JobState, attempt actual,
ObservedAtUtc, CurrentExpiresAtUtc y disponibilidad `Live`, `Expired`, `Terminal`,
`CancellationRequested`, `ContextChanged` o `Superseded`. Recibo prueba operacion,
NO vigencia permanente ni permiso de proveedor. AlreadyApplied puede ser historico
sin lease viva; no se transforma en Claimed/Renewed ni habilita retomar D.2c.6a.
Ante rechazo de autorizacion no exponer recibo ni estado ajeno. No identificar
un commit propio solo por estado Running o vencimiento mayor/igual.

## 4. Preconditions, locks y mutaciones

Transaccion corta en contexto dedicado; descubrimiento AsNoTracking no autoriza.
Orden: Submission SHARE si aplica; User SHARE; Client SHARE; Project SHARE;
PreQuote UPDATE; Requirement UPDATE; Job UPDATE; Attempts UPDATE por Id;
despues recibos de lease. Releer relaciones al obtener locks. Los recibos se leen
tras autorizar, nunca adquiriendo primero su lock. No Files ni evidencia: estas
operaciones no validan bytes y no necesitan tomar sus locks. No I/O de storage.
El job serializa escritores de recibos; no depender de mutex local.

| Operacion | Precondiciones bajo locks | Mutaciones exclusivas |
|---|---|---|
| Claim nuevo | Actor/owner y actividad; ruta Submission/Client/Project/Quote/Requirement exacta; IDs esperados; RequiredPublicationVersion v2; Queued, generacion0, owner/expiry/Started/Finished/cancel/error/recibo publicacion null; AttemptCount0 y vinculo null; Requirement current Pending y CommercialLine presente; ningun intento Pending/Processing del Requirement | Job: Running, StartedAtUtc=T, LeaseOwner calculado, LeaseExpiresAtUtc=T+L, LeaseGeneration=1. Insertar recibo Claim atomico |
| Renovacion nueva | Misma ruta/autoridad; actor igual al del recibo Claim; recibo Claim propio exacto; v2 Running sin cancel/Finished/error/recibo publicacion; owner exacto, generacion1; expiry igual Expected y >T; Started igual recibo Claim; Requirement current; sin otro intento activo incompatible | Solo LeaseExpiresAtUtc=N y recibo Renew atomico. NO modificar Started/owner/generacion |
| Renovacion sin intento | Ademas: AttemptCount0, vinculo y ExpectedProcessingAttemptId null, Requirement Pending, sin intento activo | Ningun intento ni actividad |
| Renovacion con intento | Ademas: AttemptCount1 y vinculo=ExpectedProcessingAttemptId; intento del mismo Requirement, Processing, correlacion del job, Started >= asignacion; Requirement Processing | No cambia intento, evidencia, extraccion ni propuesta |
| Repeticion exacta | Actor activo/owner y relaciones autorizados; recibo y todos los campos de solicitud identicos | Cero DML, devolver recibo original y observacion separada |

No claim de Queued cancelado: Request ya lo convierte a Cancelled. No aceptar un
Running legacy sin recibo Claim propio: renovar exige procedencia nueva, no backfill.
No requisito de abrir documentos en claim; la incorporacion se valida despues
en su servicio existente. Claim no garantiza que D.2c.6a vaya a poder completarse.

Usar PostgreSQL UTC/timestamptz y precision microsegundos. T se obtiene de
`clock_timestamp()` DESPUES de esperar locks, en la sentencia final de mutacion,
no de reloj local, transaction_timestamp ni instante de entrada al metodo.
T >= CreatedAtUtc para claim; T >= StartedAtUtc para renovacion, sino rechazo seguro.
Para renovacion: `N = greatest(expiry_actual, T + interval '120 seconds')`.
No `expiry + L`: una rafaga no acumula duraciones. Cada extension queda acotada
por max(expiry anterior,T+L); no se reduce un vencimiento mayor ante salto de reloj.
Un OperationId repetido no vuelve a calcular N ni escribe; operaciones nuevas a
lo largo del tiempo pueden mantener una ejecucion autorizada viva, no tras cancelar.

Guard final claim: repetir IDs, v2, Queued, generacion0, nulidades y attempt0.
Guard final renew: repetir IDs, v2, Running, owner/generacion, Started, attempt/count,
expiry esperado, cancel null y **expiry > T obtenido en ese guard**. Validar solo
antes de esperar locks no basta. Actividad/ruta/Requirement/intentos permanecen
protegidos por los locks previos. Exigir una fila; cero => sin recibo y rollback.

Diseno SQL para implementacion posterior: CTE materializada de reloj, UPDATE condicionado
RETURNING valores y INSERT del recibo desde esas filas en la MISMA sentencia,
dentro de la transaccion. Excepcion de INSERT revierte UPDATE; cero UPDATE no
inserta. Recibo usa exactamente T/N devueltos, no un reloj posterior. No SaveChanges
posterior que sobrescriba el job detached. Commit inmediato; la vigencia garantizada
es la del guard, no una atomicidad entre DB, commit y futura llamada de red.

## 5. Recibos, repeticion e incertidumbre

**Persistencia adicional aprobada en P3, aun no implementada**: tabla focal de recibos
`JobLeaseOperation`, sin nueva maquina de estados. Columnas conceptuales:
OperationId uuid PK, Kind Claim/Renew, JobId, RequirementId, ExecutionRequestId,
ActorUserId, ClaimOperationId, LeaseOwner varchar(200), LeaseGeneration bigint,
PolicyVersion smallint (=1), ExpectedLeaseExpiresAtUtc nullable,
ExpectedProcessingAttemptId nullable, AssignedAtUtc, AppliedAtUtc y ExpiresAtUtc.
Fechas timestamptz UTC microsegundos; IDs no vacios; generacion1; expiry>asignacion.
Claim: ClaimOperationId=OperationId, ExpectedExpiry/ExpectedAttempt null,
Assigned=Applied y expiry=Applied+L. Renew: ExpectedExpiry presente >Applied,
Assigned del claim, Expires>=ExpectedExpiry y formula exacta anterior.
FK RESTRICT a Job/Requirement/User y al recibo Claim; unique parcial un Claim por
Job y owner Claim unico. Verificar tipo/identidades del claim referenciado en store;
FK simple no prueba esas correspondencias. Retener recibos durante vida del job;
sin purga en este bloque, crecimiento y retencion requieren medicion operacional.

Estimacion de volumen, no medicion: con una operacion nueva aplicada cada 30 s,
son aproximadamente 2 recibos Renew/minuto, 120/hora y 2.880/dia por job
continuamente Running, mas un recibo Claim. Para J jobs sostenidos durante H horas,
aproximadamente J * (1 + 120 * H) recibos. Solo cuentan operaciones aplicadas;
replays del mismo OperationId no agregan filas. IDs distintos aplicados sin extension
si pueden agregarlas, por lo que la cadencia orientativa no es un limite de volumen.
Sin purga, los recibos de jobs terminados siguen acumulados. No estimar bytes de
disco sin medir filas, indices, WAL y overhead reales; capacidad, crecimiento y
retencion operativa definitiva siguen pendientes de validacion, no certificados.

Insert-only en servicios; EF Throw sobre propiedades; CHECK de forma/nulidad/tipos.
No trigger propuesto ni garantia frente a SQL arbitrario. P3 autoriza para el bloque
posterior migracion aditiva de tabla/indices/FK, sin backfill, purga ni cambio al
CHECK generacion1. No se genera ni aplica esa migracion en esta tarea.
El metodo local de dominio para renovar validaria estructura/no reduccion; ni
dominio ni EF prueban reloj vivo, actor o commit. DB guard/store aportan eso.

Alternativa sin tabla: CAS de expiry esperado reduce dobles extensiones, pero
owner desaparece al terminar y otro Renew puede reemplazar el vencimiento. No
demuestra arbitrariamente un replay antiguo propio; exigiria devolver incertidumbre
en esos casos. Un unico ultimo OperationId tambien pierde historia al siguiente
renew. P3 elige tabla por reconocimiento exacto solicitado, aceptando costo
de persistencia. Esta aprobacion no autoriza implementarla en la tarea documental.

Repeticion: consultar recibo bajo locks y autorizar ANTES de reevaluar elegibilidad
para nueva mutacion. Mismo OperationId con campo/actor/Kind distinto => Conflict,
nunca reutilizar. Mismo claim con otro OperationId/owner sobre Running => AlreadyOwned
sin mutacion; devolver Conflict sin revelar propietario ajeno. P4 distingue:
- Mismo OperationId: AlreadyApplied, sin nueva escritura ni recalculo de T/N.
- IDs distintos con igual ExpectedExpiry y cambio efectivo del vencimiento por
  el primero: CAS rechaza al segundo con StaleLease si conserva el vencimiento anterior.
- Si greatest(expiry_actual,T+L) conserva el vencimiento, la operacion puede quedar
  aplicada con su recibo, pero NO hubo extension. El CAS sigue viendo el mismo
  vencimiento: otra solicitud con ID distinto puede aplicarse tambien. No se
  garantiza un solo recibo entre solicitudes distintas en esta rama. Una posterior
  puede extender si su T+L supera expiry; desde ese cambio rige el rechazo CAS anterior.
En todos los casos, el guard exige lease vigente. Ni un ID nuevo ni un replay
reviven una lease vencida, permiten reclamar otra vez o habilitan recovery.

Si recibo exacto existe pero lease expiro o job termino: AlreadyApplied historico,
CurrentExpires puede ser null; no renovar, recrear owner ni seguir procesamiento.
Si otra renovacion avanzo expiry, devolver recibo original y observacion Superseded,
no atribuir el nuevo valor a la operacion vieja. Si actividad/current cambio:
ContextChanged, sin permiso operativo. Reconocimiento requiere usuario activo,
owner vigente y ruta exacta, pero no actividad Client/Project, current o lease viva.

Commit intentado con respuesta perdida: disponer scope fallido; contexto y token
independientes con presupuesto de diez segundos, observacion read-only por el mismo
orden y autorizacion. Comparar recibo completo contra candidato propio T/N/IDs/actor/
owner/inputs conservado antes de commit, ademas de integridad de referencias.
Coincidencia demuestra commit propio aunque sea historico; ausencia, bloqueo,
denegacion o fallo => Indeterminate, nunca rollback confirmado ni mutacion repetida.
Error anterior al intento de commit, con rollback conocido => PersistenceError;
si no puede probarse ese limite, Indeterminate. No compensacion ni retry automatico.

Comprobar ct original tras observacion (confirme o falle), tras commit y antes de
entregar resultado: propagar OCE original, no exito tardio. Un token local nunca
crea RequestJobCancellation. OCE puede dejar claim Running sin intento o una lease
renovada confirmada; no borra recibos. Indeterminate impide acciones dependientes.
Una futura observacion explicita puede aclarar estado, pero no autoriza recovery
ni retomar el coordinador. El check cooperativo no es atomico con cancelacion futura.

## 6. Cancelacion y politica temporal inicial

Decision P5: **no renovar si CancellationRequestedAtUtc no es null**. No cambia
owner/generacion/expiry y devuelve Rejected/CancellationRequested. No llama Confirm
ni Request. La lease restante permite al consumidor autorizado intentar Confirm
por su operacion existente; NO permite enviar/publicar, cuyos guards rechazan
cancelacion. Confirm usa unicamente la lease todavia vigente. Si se vence antes,
Confirm falla y queda Running con cancelacion solicitada pendiente de recuperacion:
no fabricar Cancelled ni reasignar. Resolver este caso es OBLIGATORIO antes de
activacion, mediante un bloque separado; no se implementa recovery aqui.

Renew primero: puede confirmar extension; Request posterior fija solicitud y toda
renovacion posterior se deniega. Request primero: Renew no extiende. Asi no existe
prolongacion indefinida despues de solicitud persistida. Alternativa descartada
para este minimo: gracia limitada solo para cierre; requeriria modo/cota persistidos
y guards adicionales para no ampliar permiso de envio. Sigue pendiente, no activa.

P6 aprueba L=120 s como politica inicial v1 y 30 s como cadencia orientativa futura
para un caller autorizado. No heartbeat ni temporizador implementado. El margen
para contencion/pausas es una hipotesis de diseno, no un valor medido u optimo ni
definitivo para produccion. No derivar rendimiento de tests locales: faltan p95/
p99 de copias, espera locks/DB, publicacion, latencia proveedor y pausas de proceso,
ademas de carga de recibos. El cliente actual usa TimeoutSeconds configurable;
la renovacion no cambia ese timeout. Si una llamada supera L sin renovaciones
explicitas concurrentes futuras, perdera lease y no podra publicar. Esto impide
activar un flujo largo contando solo con este bloque. Cadencia no es garantia de
ejecucion; interrupciones y reloj PostgreSQL siguen siendo limites operacionales.

## 7. Aceptacion futura y bloques pequenos

Pruebas propuestas, NO ejecutadas en esta tarea. PostgreSQL y constraints reales;
conexiones/PIDs diferentes, barreras, pg_blocking_pids y relecturas independientes.
No sleeps como unica prueba de orden. Reloj de DB observado al liberar una espera
tras vencimiento, guard afectando cero y rollback, no solo reloj simulado local.

| Caso | Cierre verificable |
|---|---|
| Dos claims mismo job | Un Claimed y un Conflict; un recibo Claim, generacion1; bloqueo observado |
| Mismo OperationId concurrente/repetido | Un efecto, recibo original exacto, segundo cero DML; payload distinto Conflict |
| Commit perdido Claim/Renew | Commit real + fallo respuesta; observacion propia o Indeterminate; nunca acciones dependientes |
| Replay vencido/terminal/superado | Recibo original reconocido historicamente, sin revivir lease ni recrear owner |
| Renew owner/generacion/actor/request/intento incorrectos | Cero UPDATE/INSERT; Admin ajeno sin metadata |
| Expira mientras espera locks | Guard con reloj DB rechaza; no recibo ni extension |
| Renew contra Request/Confirm/publicacion terminal, ambos ordenes | Locks observados; no renovacion tras solicitud/terminal; version/recibos originales preservados |
| Renew con mismo OperationId | Replay sin DML ni recalculo, mismo recibo y distincion original aplicada/extension |
| IDs distintos, extension efectiva | Primero cambia expiry; segundo con ExpectedExpiry anterior recibe StaleLease y no crea recibo |
| IDs distintos, sin extension efectiva | Preparar condicion valida N=expiry sin desactivar constraints; demostrar recibos distintos aplicados con expiry identico, sin afirmar extension ni unicidad entre solicitudes; formula no suma L |
| Lease vencida, incluido caso sin extension | Guard rechaza nueva operacion aunque greatest conservaria expiry; replay historico no revive lease |
| Cancelacion solicitada y posterior vencimiento | Renew rechazado; Confirm solo antes de vencer; despues no Cancelled ficticio ni reasignacion, pendiente fuera del bloque |
| Caller cancelado durante commit/observacion | OCE original incluso ante confirmacion; commit conservado, cero Request automatico |
| Claim/Renew con y sin intento previo | Cero intentos NUEVOS/evidencia/proveedor/cambios comerciales; intento existente intacto; snapshots de documentos/extraccion/propuestas iguales |
| v1, Running vencido, esquema y no activacion | Rechazo sin writes; v1 intacto; upgrade aditivo conserva filas; intake/replay Pending/cero automatismos |

Bloques posteriores, cada uno con autorizacion y cierre propio:
1. Implementar en tarea posterior contratos/domain y recibos/EF/migracion aditiva conforme a P1-P7, sin consumidor.
2. Claim exacto con autoridad, locks, guard y reconocimiento; tests focales.
3. Renew vigente con CAS, politica de cancelacion y observacion; carreras focales.
4. Auditoria del conjunto sin activacion. Integracion/worker/delegacion quedan aparte.

Decisiones P1-P7 aprobadas con precisiones:
- P1: identidad por encarnacion+claim y actor explicito; NO delegacion de worker,
  cuenta de servicio ni bypass de ownership. Delegacion exige contrato separado.
- P2: JobId exacto, sin selector de cola.
- P3: recibos persistidos y migracion aditiva posterior, sin backfill ni purga;
  crecimiento documentado, sin certificar retencion operativa definitiva.
- P4: CAS/guard/formula y replay historico sin permiso operativo; aplicada no
  equivale a extension; sin extension no hay unicidad de recibo entre IDs distintos.
  El limite se debe probar expresamente en el bloque de implementacion, no ahora.
- P5: no renovar tras CancellationRequested; Confirm solo con lease vigente;
  vencimiento deja recuperacion pendiente, de resolucion obligatoria antes de activar.
- P6: 120 s politica inicial v1; 30 s cadencia orientativa futura, no heartbeat ni
  valores medidos/definitivos de produccion.
- P7: observacion independiente acotada a 10 s, reconocimiento del commit propio
  y OCE original; Indeterminate nunca autoriza acciones dependientes.

## 8. Integridad de este entregable

### Creacion original (historia conservada)

Solo se agrega este archivo. Sin builds/tests/DB, nueva migracion, procesos,
proveedores, activacion ni cambios de produccion/configuracion/documentos anteriores.
No se reabre la auditoria D.2c.6a. Instrucciones AGENTS aplicables: ninguna encontrada.
Baseline: feature/public-experience-intake,
HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Manifiestos SHA256 de tracked y
untracked y estado Git antes/despues en:
`C:/Users/USUARIO/AppData/Local/Temp/sng-lease-contract-2ada9922bcd94763864b2a252077490c`.
El manifiesto inicial del worktree, no HEAD, es la base de preservacion.
Comprobacion final: 909 rutas anteriores byte-identicas, un documento nuevo
(910 totales), cero modificadas/eliminadas; rama/HEAD preservados y diff --check=0.

### Actualizacion de aprobacion P1-P7

2026-10-26: solo se actualiza este contrato con la aprobacion y precisiones del
usuario. No codigo, tests, configuracion, migraciones, claim, renovacion ni worker.
Sin builds/suites, DB habitual, commit/push o activacion. No se reabre D.2c.6a.
Baseline inmediato y manifiestos antes/despues, incluidos untracked:
`C:/Users/USUARIO/AppData/Local/Temp/sng-lease-approval-790c2348873d476f8794ed12828027e0`.

## 9. Primer subbloque: modelo y persistencia de recibos

2026-10-26. Solo JobLeaseOperation, configuracion EF/DbSet, migracion aditiva y
pruebas. No ClaimInitialV2Async/RenewActiveV2Async, interfaces operativas vacias,
stores, guards, locks de negocio, reconciliacion, DI, endpoint ni consumidor nuevo.
Las secciones 1-7 siguen siendo contrato de las futuras operaciones, no evidencia
de que este modelo las ejecute. No lease del job es modificada al crear un recibo.

### Garantias por capa

Dominio: sealed con constructor privado, getters/setters privados y factories
CreateClaim/CreateRenew. Valores de IDs/fechas siempre del caller; sin reloj ni
generacion oculta. Claim fuerza ExpectedLeaseExpiresAtUtc/ExpectedProcessingAttemptId
null por firma. Renew exige expected expiry y admite intento null/no vacio. Valida
UUIDs, owner canonico exacto ASCII minuscula de 78 caracteres, instancia no vacia
y claim concordante, generacion/politica 1, UTC/microsegundos exactos y formulas
temporales aprobadas. AddSeconds rechaza overflow antes de materializar el objeto.
No normaliza identidades, offset ni precision para aceptar datos incompatibles.
PolicyVersion=1 identifica la politica temporal, no RequiredPublicationVersion
del job (v1/v2). No se convierte ni valida autorizacion v2 desde esta entidad.
IsEffectiveExtension es derivada exclusivamente del recibo Renew; Claim devuelve
false. No se persiste ni consulta el job actual para calcularla.

EF: tabla core.job_lease_operations; DbSet JobLeaseOperations. Quince propiedades
persistidas con PropertySaveBehavior.Throw (las claves tambien tienen rechazo EF
propio); la derivada se ignora. Esto no impide DELETE, ExecuteUpdate o SQL valido.

DB: PK global OperationId; dos indices unicos parciales Kind=Claim, por JobId y
LeaseOwner; tres indices auxiliares de FK. Cuatro FK RESTRICT a job, Requirement,
User y JobLeaseOperation autorreferenciado. Claim puede insertarse con la FK activa;
Renew puede referenciarlo posteriormente. No indice unico sobre expected/final expiry.
CHECK de IDs, owner y forma/tiempos; grupo Renew exige expected IS NOT NULL, evitando
aceptacion por UNKNOWN. Campos obligatorios NOT NULL; fechas SQL finitas. PostgreSQL
almacena microsegundos y normaliza timezone: no prueba el offset original ni rechaza
necesariamente precision submicro del SQL libre; esa validacion previa es de dominio.

El owner canonico incluye ClaimOperationId, que en Claim coincide con OperationId:
duplicar owner Claim tambien duplica PK. El test comprueba definicion real del indice
parcial y rechazo de duplicados; no desactiva PK/CHECK para forzar que owner sea el
primer indice denunciado. La carrera de jobs si aisla el indice unico por JobId.

Limites demostrados: FK simple permite que un Renew referencie otro Renew, use
Requirement/actor existentes ajenos al job, request o intento arbitrarios no vacios.
El futuro store debera comprobar tipo Claim, Job/Requirement/request/actor/owner/
generacion y pertenencia exacta del intento; y confirmar recibo+UPDATE de job juntos.
SQL valido puede modificar una fila y borrar una hoja sin dependientes. No triggers,
proteccion frente a SQL hostil ni prueba de autorizacion, reloj vivo o commit por
construir/materializar una entidad. El recibo historico no depende del owner/expiry
actuales del job: se admite estructuralmente sobre job terminal que ya los libero.

### Migracion y cobertura

20261026165918_AddJobLeaseOperations: Up crea solo tabla, cinco indices, tres CHECK,
cuatro FK y PK; Down elimina esa tabla. Designer/snapshot generados con EF. SQL
revisado en migration.sql: sin UPDATE/DML de negocio; unico INSERT de metadata
__EFMigrationsHistory. Sin cambios a migraciones anteriores, backfill, purga,
restricciones de generacion del job ni recibos de publicacion v1/v2.

| Criterio | Pruebas focales |
|---|---|
| A: estructura/inmutabilidad | JobLeaseOperationTests: Claim, Renew extendido/igual, precision, IDs/owner/versiones, overflow; job serializado intacto, setters no publicos |
| B: persistencia | Roundtrip_SelfReference_RenewalsWithoutExtension_AndNoBusinessMutation; dos Renew sin extension con expected/final iguales e IDs distintos; mismos timestamps/valores |
| B: constraints | Checks_NullsAndInvalidShapes_CannotPassThroughUnknown; nulidades obligatorias, grupos Claim/Renew, fechas/versiones/owner; SQLSTATE y nombre exactos |
| B: claves/FK | Keys_PartialIndexes_ForeignKeys_AndRestrictiveDeletes y GlobalPrimaryKeyAcrossKinds_AndDuplicateClaimOwner_AreRejected; sin desactivar restricciones |
| C: EF | EfThrow_EveryPersistedProperty_NoUpdate_IndependentRead, quince propiedades; InvalidOperationException, nombre de propiedad, cero DML y relectura intacta |
| D: concurrencia | ConcurrentClaimInserts_IndependentPids_OneUniqueWinner_NotOperationalClaim; transaccion ganadora retenida, PIDs distintos, pg_blocking_pids observado, loser 23505/ux_lease_operation_claim_job |
| E: upgrade | UpgradeFromImmediatePreviousSchema_PreservesV1V2HistoryAndPublicationReceipts; filas v1/v2 Queued/Succeeded insertadas en esquema anterior, hashes/recibos/estados intactos, tabla nueva vacia, HasPendingModelChanges=false |
| E: no activacion | Dos Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict con nueva assertion cero recibos; Pending, cero jobs/attempts/evidencia/AI automaticos conservados |
| Limites | CrossRowAndSqlImmutabilityLimits_AreNotMisrepresented prueba correspondencias no garantizadas, UPDATE/DELETE SQL permitidos y jobs aun Queued |

Los historicos de upgrade son fixtures estructurales usando factories/lifecycle,
no certificados de una extraccion remota ni un nuevo recorrido de publicacion.
La carrera demuestra unicidad de INSERT de recibos, NO claim atomico ni replay del
servicio. Tests no implementan los servicios pendientes para aparentar cobertura.

### Trazabilidad de ejecucion

Baseline inmediato: 910 rutas no ignoradas, branch feature/public-experience-intake,
HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Instrucciones AGENTS: ninguna encontrada.
Artefactos externos, incluidos manifiestos SHA256 tracked/untracked y SQL:
`C:/Users/USUARIO/AppData/Local/Temp/sng-lease-model-66989ee6746a4e1aaa842273dcdc6fbf`.

Inventario focal: nuevos Domain/PreQuotes/JobLeaseOperation.cs,
Infrastructure/Persistence/Configurations/JobLeaseOperationConfiguration.cs,
migracion y Designer anteriores, Tests/Domain/PreQuotes/JobLeaseOperationTests.cs,
Tests/Infrastructure/Persistence/JobLeaseOperationPostgreSqlTests.cs.
Existentes: ApplicationDbContext (DbSet), snapshot EF, test HTTP (una assertion)
y este contrato. No fuente de los servicios de inicio/coordinacion/publicacion/
terminacion, frontend, foto, descarga, manual, proveedor ni DI modificada.

Claim/CAS, reloj vivo,
autorizacion y observacion del commit propio siguen pendientes de subbloques
separados; ninguna nueva activacion, heartbeat o recuperacion queda certificada.

### Resultado final y limpieza

| Ejecucion | Passed | Failed | Skipped |
|---|---:|---:|---:|
| preflight.trx | 78 | 0 | 0 |
| final.trx | 79 | 0 | 0 |

Final: 2.3983 minutos, 55 dominio, 22 PostgreSQL del modelo y dos HTTP. Sustituye
la corrida solapada de 78; no sumar 157. Entre corridas se agrego solo el caso
explicito de PK global Claim/Renew y owner duplicado; produccion no cambio.
No fallos preliminares de compilacion/tests ni skips. No suite historica completa.

Rebuild completo final: dotnet build Tests/CotizadorBackend.Tests.csproj
-c LeaseModel --no-restore --no-incremental --verbosity minimal: cero errores y
111 warnings, 19.51 s. final-build.log y warning-delta.csv: sin diferencias de
diagnosticos por archivo/mensaje respecto del rebuild D6aAudit al ignorar lineas.
test-build.log previo tambien 0/111; model-build.log inicial compila solo Api y
dependencias, 0/18: su menor alcance NO es correccion de warnings existentes.
Tests con --no-build -c LeaseModel, filtro exacto filter.txt, runners/logs en root
de artefactos. SQL generado sin conectar a DB, con conexion ficticia puerto 1.

Carrera final: ganador PID30060, perdedor PID8528; conexiones/contextos distintos,
pg_blocking_pids observado antes de commit; 23505 y ux_lease_operation_claim_job.
Job permanece Queued, cero intentos. No inferir claim operativo ni reloj vivo.

PostgreSQL17 propio en el artifact root, 127.0.0.1:55537, usuario audit. Solo bases
cotizador_backend_test_*; migraciones aplicadas/un upgrade exclusivo de fixtures.
final-remaining.txt=0 y final-server-status.txt confirma no server running.
Variables child-local retiradas y cluster detenido en finally; cluster parado y
logs retenidos como evidencia. El modelo no crea storage/spools. Al cierre no
se observaron sng-sealed-* ni sng-receipt-unit-*; no limpieza general de temporales.

Manifiestos initial.csv/final.csv: 910/916 rutas, seis nuevas, cuatro existentes
modificadas, cero ausentes, 906 anteriores byte-identicas. delta.csv coincide con
inventario anterior; branch/HEAD preservados. Status: 55 tracked modificados,
134->140 archivos untracked, cero staged. git diff --check=0 con avisos LF/CRLF
previos; bin/obj LeaseModel son artefactos ignorados, no fuentes del manifiesto.
Solo documentacion cambio despues del rebuild final.

Veredicto: modelo/persistencia implementados y verificados en este alcance, sin
claim/renovacion operativos ni activacion. Sin DB/API habitual, proveedor real,
reinicio de API, frontend, commit/push/merge/despliegue. No certifica retencion
operativa, SQL hostil, crash, fsync, autorizacion o atomicidad recibo/mutacion de
lease. P5 vencida y la delegacion del worker siguen pendientes antes de activacion.

## 10. Claim inicial por JobId exacto, sin activacion

2026-10-26. Implementacion explicita del claim conforme a P1-P7, sobre el modelo
auditado de seccion 9. Las cifras 78/79 de esa seccion son historia, no ejecuciones
de este bloque. No discrepancia de autoridad, identidad o lifecycle que requiera
ampliar persistencia. Sin migracion nueva ni modificacion del modelo/EF existentes.

### Entrada y responsabilidades

ClaimInitialLeaseService.ClaimInitialV2Async recibe JobId, RequirementId,
ExecutionRequestId, OperationId, InstanceId y actor explicito. Rechaza UUID vacio
antes de abrir DB; el store repite la validacion. No genera identidades ni fechas
ocultas. Owner se construye exec:instance:operation en formato D minuscula.
IInitialLeaseClaimStore e InitialLeaseClaimStore no se registran en DI ni tienen
consumidor productivo. No interfaz vacia de Renew, selector, endpoint o worker.
Las clases publicas son invocables por backend de confianza, no una frontera HTTP
ni una delegacion a ejecutores desatendidos. InstanceId/owner no son credenciales.

Contextos dedicados a partir de DbContextOptions, sin limpiar/guardar tracking del
caller. Descubrimiento sin tracking no autoriza. Locks: Submission SHARE opcional,
User SHARE, Client SHARE, Project SHARE, PreQuote UPDATE, Requirement UPDATE,
Job UPDATE, Attempts UPDATE ordenados por Id; despues lectura de recibos.
Todas las relaciones se releen bajo locks. Usuario activo y owner actual, sin
bypass Admin; usuario ajeno recibe NotFound sin recibo ni observacion. No se usa
RequestedBy como credencial. No Files/evidencia, storage ni proveedor bajo locks.

Claim nuevo exige actividad Client/Project, v2, Requirement current/Pending y
CommercialLine enum valido; job Queued/generacion0 y nulidades/attempt0 del
contrato, sin intento Pending/Processing. v1 no se convierte. Running se rechaza
AlreadyOwned o Expired; terminal no se reabre. CancellationRequested persistida
no permite claim ni se transforma en confirmacion. Contexto/estado incoherente
se expresa mediante IdentityMismatch; intento incompatible mediante AttemptConflict.
No codigos terminales ni estados persistidos nuevos.

### Sentencia atomica y recibo

Una CTE MATERIALIZED obtiene clock_timestamp() despues de los locks. UPDATE
condicionado repite IDs/version/Queued/generacion0/nulidades/attempt0 y T>=Created.
Modifica solo State=Running, StartedAtUtc=T, owner, generacion1 y expiry=T+120s.
RETURNING alimenta INSERT de Claim en la misma sentencia; sus fechas son exactamente
las devueltas por UPDATE. Cero filas produce cero recibos y rollback; error de
INSERT revierte tambien UPDATE. Recibo se valida con factory del dominio antes
de commit; contexto sin tracking, sin SaveChanges posterior. No cambio de
Requirement, actividad, intentos, documentos, evidencia, extraccion o propuesta.
Vigencia y observacion del nuevo claim corresponden a T del guard, no al instante
fisico de commit/entrega. No se inicia automaticamente D.2c.6a.

Dominio sigue probando forma/formula, DB PK/FK/CHECK, store autoridad, relaciones,
reloj DB y atomicidad. No triggers ni garantia ante escritores SQL arbitrarios.
El modelo permite historicos estructurales; el store no los adopta por estado
Running solamente: exige identidad/forma y concordancia del recibo con el job.

### Reconocimiento, incertidumbre y cancelacion

Buscar OperationId solo tras autorizar ruta. Coincidencia de tipo Claim, actor,
todos los IDs/owner, politica/generacion/formula/nulidades y asignacion del job
devuelve AlreadyApplied sin DML. Entrada diferente devuelve ReceiptConflict sin
adaptar identidad. Intento vinculado, si existe, debe pertenecer al Requirement,
con correlacion/fechas/estado compatibles. Reconocimiento no exige actividad
Client/Project, current o lease vigente; usuario activo/owner y relaciones si.

Recibo original separado de LeaseObservation: estado/attempt actuales, reloj DB,
expiry actual y disponibilidad. Precedencia de observacion: Terminal, solicitud
de cancelacion, ContextChanged, Expired, Superseded por expiry distinto, Live.
Esa etiqueta resume el estado observado, nunca autoriza envio ni recovery.
Terminal puede no conservar owner/expiry; replay no los recrea. Sucesor intacto.

Fallo antes de intentar commit: rollback explicito con token independiente acotado;
si se confirma, PersistenceError (o ReceiptConflict para colision unique); si no
puede establecerse rollback despues de iniciar mutacion, Indeterminate. No retry.
Fallo de commit: scopes originales dispuestos antes de observacion, contexto y
token nuevos de diez segundos, mismo orden de locks/autorizacion, solo lectura.
Se compara recibo COMPLETO contra candidato propio conservado, incluidas fechas.
Reconocimiento exacto devuelve AlreadyApplied, no nuevo Claimed. Ausencia, cambio,
denegacion, bloqueo o fallo devuelven Indeterminate, nunca rollback supuesto.
No compensacion, nueva identidad, reconstruccion ni efecto dependiente.

Token original comprobado tras commit/observacion y antes de entregar resultado.
Propaga OCE original tambien si la observacion falla. Cancelar al caller nunca
solicita cancelacion de negocio ni borra un claim confirmado: puede quedar Running
sin intento. Comprobaciones cooperativas, no atomicidad con cancelacion posterior.
No recurso documental que liberar: scopes DB se disponen; no originales borrados.

### Pruebas y trazabilidad del bloque

Artefactos externos y baseline inmediato (worktree, no HEAD):
`C:/Users/USUARIO/AppData/Local/Temp/sng-claim-8c6281c2b1264389a4bc69c91d1873a9`.
916 rutas tracked/untracked no ignoradas; feature/public-experience-intake,
HEAD 56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Sin AGENTS aplicables encontrados.

Nuevos: Application/PreQuotes/ProcessRequirement/ClaimInitialLeaseService.cs;
Infrastructure/Persistence/Repositories/InitialLeaseClaimStore.cs;
Tests/Infrastructure/Persistence/InitialLeaseClaimPostgreSqlTests.cs y
InitialLeaseClaimCoordinationPostgreSqlTests.cs. Unico existente editado: este
documento. Modelo, migraciones, servicios anteriores, DI y consumidores intactos.

Las pruebas usan servicios reales, PostgreSQL temporal y relecturas independientes.
La carrera de este bloque SI ejecuta claim atomico; no se confunde con el INSERT
estructural de seccion 9. Fallo de respuesta es interceptor tras commit real, no
crash fisico. Expiracion historica se monta desplazando fechas concordantes bajo
constraints; no se afirma haber esperado 120s ni ejecutado Renew. La observacion
Superseded usa cambio SQL controlado de expiry, no un renovador operativo.
El test de candidato distinto altera fechas por SQL controlado despues del commit:
demuestra rechazo de reconocimiento, no proteccion del modelo contra SQL hostil.

Antes de activar siguen pendientes Renew/CAS y pruebas propias, resolucion P5 de
cancelacion con lease vencida, delegacion/autenticacion de worker y mediciones de
latencia/capacidad/retencion. No heartbeat, enqueue, recovery o proveedor real.

### Aceptacion A-F y cierre

| Criterio | Evidencia final ejecutada |
|---|---|
| A: claim atomico | Claim_AtomicDatabaseClock_OnlyJobAndReceipt_ExactReplay, publico/manual; T entre lecturas DB, fechas exactas/120s, generacion1, Pending y cero intentos; un comando CTE y replay cero DML |
| A: datos ajenos | Claim_PreservesExistingDocumentEvidenceExtractionAndProposal; archivos/evidencia/extraccion/propuesta no vacios de control y snapshots intactos, sin proveedor |
| B: rechazo | Rejections_NoEffects_NoReceiptDisclosure, 17 escenarios; seis InvalidIdentifiers sin abrir DB. Ajeno/Admin, actividad, links publicos, IDs, v1, current/estado/cancelacion/attempt y Created futuro; snapshots intactos. Guard futuro ejecuta sentencia con cero filas, no afirma cero comandos |
| C: replay | HistoricalReplay, ocho escenarios; identidad distinta/ajeno/Admin sin recibo, expiracion/terminal/contexto/sucesor/expiry avanzado con recibo original y observacion separada; cero DML |
| D: concurrencia | Races, cinco casos: distintos/mismo ID, colision entre jobs y cancelacion en ambos ordenes. PIDs/Lock/blocker observados, un claim o rechazo; segundo job de colision conserva Queued |
| E: atomicidad/cancelacion | FailureBeforeCommit, dos casos; comando ejecutado y rollback real, con/sin caller cancelado. CallerCancelledAfterSuccessfulCommit conserva Running y OCE original, cero solicitud de cancelacion |
| E: incertidumbre | CommitResponseLoss, cuatro combinaciones caller/observador; contextos y tokens distintos, commit conservado, cero repeticion de DML. ObservationBlocked agota token independiente. Uncertainty, tres casos: ausencia/fechas cambiadas/denegacion no prueban commit propio |
| E: espera | WaitingForExactJob, dos casos; Lock observado, T posterior al reloj DB leido durante espera; cancelacion original impide claim y deja Queued |
| F: no activacion | Dos HTTP Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict; Pending/cero jobs/attempts/evidencia/recibos lease/AI automaticos. Busqueda sin consumidor ni DI |

Los casos de la matriz se agrupan dentro de la seleccion; no son conteos adicionales.
Los controles historicos se insertan por factories/lifecycle como datos de prueba,
no representan una publicacion remota nueva. El store no tiene dependencias de
storage/proveedor y no abre archivos. HTTP usa su proveedor simulado y los helpers
de almacenamiento existentes, sin AI2/Gemini real.

| Corrida | Passed | Failed | Skipped |
|---|---:|---:|---:|
| preflight.trx, build1 | 47 | 1 | 0 |
| final.trx, final-build | 54 | 0 | 0 |

Preflight: 3.7718 minutos. Unico fallo: fixture successor llamaba SupersedeBy sobre
Requirement Pending, porque Fail sin intento no cambia ese estado. Se corrigio
usando StartVerified real y Fail real con ese intento antes de reemplazar; no se
cambio lifecycle ni se relajo la comprobacion del sucesor. No defecto productivo
reproducido por ese fallo. Se agregaron seis casos focales (espera/reloj y token,
candidato ausente/fechas distintas/denegado, grafo ajeno no vacio), y se reforzo
la comprobacion de pertenencia/estado del intento en reconocimiento del store.

Final: 4.2824 minutos; 52 casos de claim y dos HTTP. Los 48 nombres preliminares
se repiten y quedan Passed; seis nuevos. Son 54 casos distintos con evidencia
final, no 102 ni suma con los 79 historicos. cases.csv/name-delta.csv y los dos
TRX conservan resultados y nombres, incluido el fallo preliminar. No suite global.

Dos rebuilds completos aislados: dotnet build Tests/CotizadorBackend.Tests.csproj
-c LeaseClaim --no-restore --no-incremental --verbosity minimal. build1.log:
0 errores/111 warnings, 2:31.40; final-build.log: 0/111, 1:17.06. Sin errores de
compilacion. warning-delta.csv vacio contra rebuild LeaseModelAudit al normalizar
lineas. No se detuvo API/IDE durante el primer build mas lento. Fuentes productivas
y tests no cambiaron despues del rebuild final; solo documentacion de cierre.
Tests mismo proyecto --no-build -c LeaseClaim, filtro exacto filter.txt, loggers
TRX/console normal y results en artifact root. run.ps1 conserva ciclo del servidor.

PIDs finales (ganador/bloqueado): distintos claims 18800/2760; misma solicitud
22852/14068; colision entre jobs 23148/32684; cancelacion primero 29256/36492;
claim primero 21372/1660. Observador 36892 bloqueado por 32432 hasta cancelacion
del token independiente. Espera normal 15724/35608; cancelada 32988/35588. Se
comprueba pg_blocking_pids antes de liberar, no sleeps como evidencia de orden.
Perdida de respuesta y cambio de datos son inyecciones controladas, no crash/red
fisicos. Los diez segundos no prometen latencia total ante todos los fallos.

PostgreSQL17 propio 127.0.0.1:55541, usuario audit, data bajo artifact root.
Solo migraciones existentes en bases cotizador_backend_test_* de fixtures.
Cada corrida retira variable de conexion child-local y detiene servidor en finally.
final-remaining.txt=0, final-server-status.txt=no server running. Cluster parado
y logs retenidos. No sng-sealed-* ni sng-receipt-unit-* observados al cierre;
no barrido/borrado de recursos ajenos. Claim no crea storage ni spool.

Manifiestos initial.csv/final.csv: 916/920 rutas, cuatro nuevas, un existente
modificado (este contrato), cero ausentes y 915 anteriores byte-identicos.
delta.csv contiene el inventario indicado. Rama/HEAD preservados, 55 tracked
modificados, 144 untracked, cero staged. git diff --check=0 con avisos LF/CRLF
previos. Bin/obj LeaseClaim son artefactos ignorados, no fuentes del manifiesto.
Sin cambios a modelo/EF/migraciones, servicios anteriores, intake, manual, foto,
descargas, frontend o DI. Sin DB/API habitual, commit/push/merge/despliegue.

Veredicto: **claim explicito implementado y verificado A-F, sin activacion**.
No certifica SQL hostil, almacenamiento desplegado, fsync, crash fisico ni
exactly-once remoto. Lease de 120s sigue politica inicial, no medida productiva.
Renew operativo y sus carreras/CAS, heartbeat/worker/delegacion y recovery P5
permanecen fuera del bloque y requieren autorizacion/cierre propios.

### Complemento H1: rechazo del intento incompatible durante replay

2026-10-26. Resuelve exclusivamente el faltante de evidencia H1 de la auditoria
del claim inicial. No cambia produccion, lifecycle, autoridad, modelo, migraciones
o configuracion. No implementa Renew ni activa consumidores. La evidencia anterior
47/1/0 y 54/0/0 se conserva: no se suma a esta ejecucion ni se reescribe su historia.

Nuevo theory `Replay_IncompatibleLinkedAttempt_ExactConflict_NoDisclosureOrWrites`
en InitialLeaseClaimReplayAttemptPostgreSqlTests (partial de la clase existente).
Cada caso crea Claim real y StartVerified real; conserva solicitud exacta,
OperationId, InstanceId y actor. Comprueba desde otro contexto que recibo,
asignacion, version, generacion y vinculo exacto siguen siendo los esperados.

| Caso | Condicion ejercitada en replay | Resultado actual |
|---|---|---|
| foreign-requirement | Intento vinculado existe, pero pertenece a otro Requirement; no aparece en la seleccion autorizada, linked null | Passed |
| correlation | Intento del Requirement correcto con correlacion distinta | Passed |
| not-started | Intento Pending, StartedAtUtc null | Passed |
| early-start | Intento Processing con inicio anterior a asignacion, conservando Created <= Started | Passed |
| running-finished | Job Running, intento Finished mediante Fail del dominio | Passed |
| terminal-processing | Job Failed por servicio real, intento luego incompatible Processing | Passed |

Resultado exigido en LOS SEIS: State=Conflict, Failure=AttemptConflict, Receipt y
Observation null, cero DML y snapshots independientes antes/despues identicos.
El interceptor se conecta SOLO al replay, no a la preparacion del fixture; observa
lectura de recibos. Recibo existente exacto y AttemptConflict, junto con las
precondiciones contrastadas, distinguen la rama de reconocimiento del rechazo
de claim nuevo. No se instrumenta ni modifica produccion para demostrarlo.
Snapshots incluyen jobs, recibos, Requirements, attempts, actividad de quotes,
documentos, evidencia, extraccion y propuestas, tambien el otro job/Requirement
de control. No reparacion, reasignacion, compensacion ni dependencias de proveedor.

Los servicios no generan esas incoherencias entre filas. El fixture usa SQL
parametrizado para cambiar pertenencia/correlacion/fechas/estado del intento,
sin tocar el recibo Claim o autoridad; running-finished usa el metodo Fail del
dominio sobre el intento. Terminal-processing primero finaliza job/intento con
el servicio real y luego prepara la incoherencia. Todas las FK/CHECK/indices
permanecen activos; no se modifica el esquema. Esto prueba el guard ante esas
incoherencias representables, NO proteccion frente a cualquier escritor SQL.

La FK simple de ProcessingAttemptId exige existencia, no pertenencia compuesta;
por eso foreign-requirement usa un intento existente y otra entidad real. El
CHECK ck_processing_jobs_attempt impide count0 con vinculo o count1 sin vinculo:
no se desactiva para fabricar esas ramas. El CHECK de lifecycle del intento
admite Pending sin inicio, pero no Processing sin inicio; not-started conserva
Pending. early-start conserva el orden interno Created/Started exigido por CHECK.
Las discordancias job/intento no estan certificadas por esos CHECK individuales.

Control positivo agregado a HistoricalReplay: `started`, con StartVerified real,
job Running e intento valido, AlreadyApplied/Live, mismo recibo, cero DML y snapshot
intacto. Se reejecutan tambien sus ocho controles anteriores, incluidos terminal
y successor con inicio/fallo reales y preservacion del sucesor. No confundir el
control previo active-attempt de claim nuevo con H1: no se usa como prueba de H1.
El control renewed sigue siendo SQL de fixture, no renovacion operativa.

Unica corrida de este complemento: **15 passed / 0 failed / 0 skipped**, 1.1576
minutos: seis negativos nuevos y nueve controles HistoricalReplay (uno nuevo).
No preflight separado ni fallos preliminares de build/tests. No defecto productivo
reproducido; no correccion de produccion. Las 54 pruebas completas y carreras
anteriores NO se repitieron. Fuentes/tests no cambiaron despues del rebuild;
solo se completo esta documentacion.

Rebuild aislado ClaimH1 --no-restore --no-incremental --verbosity minimal:
0 errores/111 warnings, 15.48 s. warning-delta.csv vacio frente al build de la
auditoria LeaseClaimAudit normalizando numeros de linea. Tests --no-build -c
ClaimH1; filtro exacto filter.txt selecciona los dos metodos anteriores.
Artefactos externos: build.log, run.ps1, final-tests.log, results/final.trx,
final-cases.csv, final-evidence.txt, initial/final.csv y delta.csv en:
`C:/Users/USUARIO/AppData/Local/Temp/sng-claim-h1-9c80212f37104e6ab3abfeb7722e28f9`.

PostgreSQL17 propio 127.0.0.1:55545 usuario audit, migraciones existentes solo en
fixtures temporales. final-remaining.txt=0; final-server-status.txt: no server
running. Variable child-local retirada y servidor detenido en finally; cluster
parado/logs retenidos. No storage/spools creados por claim; cero sng-sealed-* y
sng-receipt-unit-* observados, sin barrido de recursos ajenos.

Baseline inmediato: 920 rutas SHA256 tracked/untracked; final 921, un archivo
nuevo, dos existentes modificados y cero ausentes; 918 anteriores byte-identicos.
Inventario: nuevo partial de pruebas anterior; existentes modificados:
InitialLeaseClaimPostgreSqlTests.cs (control started) y este contrato (complemento).
Rama feature/public-experience-intake y HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79 preservados; trabajo anterior conservado.
git diff --check=0, con avisos LF/CRLF previos. Bin/obj ClaimH1 ignorados.

Veredicto exclusivo: **H1 RESUELTO en PostgreSQL real**, sin ampliar el cierre a
Renew, recovery P5, delegacion, worker o activacion. Sin DB/API habitual, reinicio
de servicios, proveedor real, frontend, commit, push, merge ni despliegue.

## 11. Renew explicito de lease vigente, sin activacion

2026-10-26. Implementacion de P1-P7 sin decisiones comerciales nuevas, migracion,
modelo adicional, heartbeat, worker, selector, enqueue, recovery o registro DI.
No invoca claim, inicio, coordinador, proveedor, storage, cierre ni publicacion.
Las referencias a Renew pendiente en secciones anteriores conservan su contexto
historico; no se borran resultados del modelo, claim o complemento H1.

### Entrada y limites de confianza

RenewActiveLeaseService.RenewActiveV2Async recibe RenewActiveLease y actor explicito.
Request: JobId, RequirementId, ExecutionRequestId, OperationId, ClaimOperationId,
LeaseOwner, LeaseGeneration, ExpectedLeaseExpiresAtUtc y ExpectedProcessingAttemptId
nullable. No genera IDs ni sustituye expected por el vencimiento actual. Rechaza
UUID vacio, OperationId igual al Claim, generacion distinta de 1, owner no canonico
o discordante con ClaimOperationId, fecha no UTC o con precision submicrosegundo.
No normaliza ni trunca para aceptar. Servicio y store validan antes de abrir DB.

Renewed y StaleLease se agregan AL FINAL de los enums compartidos de resultado y
failure; valores numericos previos conservados. No son estados persistidos del job
ni codigos de finalizacion. No hay cambios en InitialLeaseClaimStore. El store
nuevo, ActiveLeaseRenewalStore, usa contextos dedicados; no modifica tracking ajeno.

Actor activo y owner actual antes de revelar recibos; no bypass Admin, RequestedBy
no es credencial, owner tecnico no autentica. Ruta se descubre sin tracking y se
revalida bajo locks: Submission SHARE opcional, User/Client/Project SHARE,
PreQuote/Requirement/Job UPDATE, Attempts UPDATE por Id, luego recibos. No Files,
evidencia o E/S documental. No mutex local ni transaccion global DB/red.

ClaimOperationId debe referenciar Claim de forma valida, mismo job/Requirement/
request/actor/owner/generacion y asignacion. Se comprueba la factory original;
las FK simples no se interpretan como prueba de correspondencia. Job siempre v2,
Started igual a Assigned original. Intento actual, si existe, pertenece al mismo
Requirement, correlacion y fechas compatibles, Processing si Running y Finished
si terminal; contador/vinculo exactos. Terminal no necesita owner/expiry liberados.

### Nueva operacion, CAS y reloj

Client/Project activos, Requirement current, Running sin cancelacion/error/terminal/
recibo de publicacion. Sin intento: Pending, count0/link null y expected attempt
null, ningun activo incompatible. Con intento: Requirement Processing, count1,
vinculo exactamente esperado e intento Processing. Otro intento activo se rechaza.
No adopcion silenciosa si inicio concurrente cambio el vinculo esperado null.

Expiry actual debe igualar ExpectedLeaseExpiresAtUtc exactamente: diferencia da
Conflict/StaleLease, no retry con valor actualizado. CTE MATERIALIZED obtiene
clock_timestamp DESPUES de locks. UPDATE repite IDs/v2/Running/owner/generacion/
Started/attempt/count/nulidades y exige T>=Started y expiry>T. Solo cambia expiry
a greatest(expiry,T+120s). INSERT Renew se alimenta de UPDATE RETURNING en la MISMA
sentencia, usando T y N exactos; no SaveChanges posterior. Cero filas revierte sin
recibo: Expired si vencio, IdentityMismatch ante limite temporal incompatible.
Error de INSERT revierte UPDATE; no promesa de vigencia fisica al entregar commit.

Renewed indica aplicacion confirmada, no necesariamente extension. IsEffectiveExtension
del recibo distingue N>Expected de N=Expected. Dos IDs distintos pueden aplicar
sin extension y producir dos recibos si el expected sigue igual. Si uno cambia
expiry, el segundo con expected anterior recibe StaleLease. Mismo OperationId
reconocido nunca recalcula T/N ni escribe. No suma 120 al expiry anterior.
No incrementa generacion, reasigna, crea intento ni revive una lease vencida.

### Replay y resultado incierto

Despues de autorizar y verificar correspondencias, un recibo existente debe ser
Renew y coincidir en actor, todos los IDs/Claim/owner/generacion, expected expiry
y expected attempt; factory valida politica/formula/fechas y Assigned del Claim.
Entrada incompatible da ReceiptConflict sin recibo/observacion. Intento actual
incompatible da AttemptConflict. Se reconoce antes de exigir actividad/current,
Running sin cancelacion o lease vigente para una NUEVA operacion.

Recibo original separado de observacion actual: Terminal, CancellationRequested,
ContextChanged, Expired, Superseded o Live, con la misma precedencia del claim.
Replay de una renovacion anterior sin intento puede reconocer historia legitima
despues del inicio y mostrar el intento ACTUAL; no cambia el expected null del
recibo. Eso NO permite aplicar una renovacion nueva con expected attempt obsoleto.
Expected attempt no null debe corresponder al vinculo actual. Sucesor y decisiones
posteriores quedan intactos. AlreadyApplied no concede permiso de envio/recovery.

Rollback anterior al commit conocido: PersistenceError; unique con rollback:
ReceiptConflict. Resultado no demostrable tras mutacion: Indeterminate. Tras
intento de commit, scopes originales dispuestos antes de observar. Contexto/token
nuevos, diez segundos, misma autoridad/locks, solo lectura. Compara candidato
completo propio (incluidos T, N, expected y fechas originales), no solo expiry
igual/mayor. Ausencia/cambio/denegacion/bloqueo/fallo no prueba rollback ni exito.
Sin retry, compensacion ni nueva OperationId. Token ORIGINAL se comprueba despues
de commit/observacion, tambien fallida; OCE no crea RequestCancellation y no borra
commit confirmado. Comprobacion cooperativa, no atomica con cancelacion posterior.

P5: solicitud persistida impide nuevos Renew. Confirm existente solo usa lease
vigente; vencida deja Running con solicitud pendiente, sin Cancelled ficticio ni
reasignacion. Este bloque prueba ese limite, NO resuelve recovery. Su resolucion
es obligatoria antes de activar, igual que delegacion y autenticacion de worker.
120s politica inicial v1 y 30s orientativos no son mediciones ni timers activos.

### Pruebas y fronteras de simulacion

Tests nuevos en tres partials de InitialLeaseClaimPostgreSqlTests reutilizan
fixtures, claim/inicio/finalizadores reales y PostgreSQL aislado. Ningun proveedor
real. Readbacks/snapshots independientes; interceptores separados de preparacion.

| Criterio | Casos focales |
|---|---|
| A | FormulaAndExactEffects, cuatro combinaciones con/sin intento y con/sin extension; T entre relojes DB, microsegundos/formula, job serializado salvo expiry, negocio intacto y replay sin DML; NonemptyGraphAndClaimPreserved conserva control no vacio de archivos/evidencia/extraccion/propuesta, Claim y job ajeno |
| B | InvalidInput, doce entradas sin conexion; Rejections, 25 escenarios con failure preciso, sin metadata y snapshots iguales; expired ejecuta guard cero filas, no afirma cero comandos; FutureAssignment prueba T>=Started con cero filas |
| C | HistoricalReplay, ocho escenarios: payload, otro Renew real, vencido, terminal, cancelacion, inicio posterior, intento incompatible y contexto inactivo; TerminalAttemptAndSuccessor agrega inicio/fallo reales con intento y sucesor preservado |
| D | Diez Races: same/different/no-extension/collision y ambos ordenes con Request/start/terminal; servicios reales, conexiones/PIDs distintos y pg_blocking_pids antes de liberar ganador |
| E | Waiting normal/expired/cancel con reloj DB observado; PreCommitFailure dos casos; CommitLoss cuatro combinaciones caller/observador; PostCommit cuatro escenarios; ObserverBlocked con presupuesto independiente real |
| P5 | Dos casos: Renew denegado tras solicitud; Confirm real vigente confirma, vencido devuelve LeaseConflict sin mutar ni fabricar terminal |
| F | Regresiones focales claim: AtomicDatabaseClock publico/manual, nueve HistoricalReplay y seis H1; dos HTTP intake/replay Pending/cero jobs/attempts/evidencia/recibos/AI automaticos |

Sin extension: fixture cambia solo expiry actual a reloj DB+10 minutos, manteniendo
constraints activas y Claim original intacto. Es estado estructural valido para
ejercitar greatest cuando conserva un horizonte mayor, NO una renovacion previa
real. Recibos resultantes usan reloj real y demuestran formula/IsEffectiveExtension.
La carrera permite ambos recibos, sin afirmar un unico ganador en esa rama.

Esperando locks: fixture acorta expiry actual a DB+3s (valido, posterior a Started),
verifica lease viva al entrar, observa bloqueo y consulta DB hasta que vence antes
de liberar. No sleep como demostracion de orden ni reloj productivo falso. Otros
escenarios historicos desplazan fechas por SQL con constraints; no se presentan
como reloj fisico esperado ni como operaciones previas ejecutadas. Los controles
no vacios son grafos estructurales, no prueba nueva de publicacion remota.
Respuesta perdida se inyecta DESPUES de commit real; no crash/red fisicos. No
certifica SQL hostil, fsync, exactly-once remoto o duracion total acotada a diez s.

### Inventario y ejecuciones

Baseline inmediato SHA256 tracked/untracked: 921 rutas, rama
feature/public-experience-intake, HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79.
Artefactos externos:
`C:/Users/USUARIO/AppData/Local/Temp/sng-renew-d60d0fcba0024ba7b9a28f27dc50237e`.

Nuevos: Application/PreQuotes/ProcessRequirement/RenewActiveLeaseService.cs;
Infrastructure/Persistence/Repositories/ActiveLeaseRenewalStore.cs;
Tests/Infrastructure/Persistence/ActiveLeaseRenewalPostgreSqlTests.cs,
ActiveLeaseRenewalCoordinationPostgreSqlTests.cs y ActiveLeaseRenewalBoundaryPostgreSqlTests.cs.
Existentes: ClaimInitialLeaseService.cs, solo append de enums; este contrato.
Sin modelo/EF/migracion, DI, endpoints, claim store, coordinador, finalizadores,
intake, manual, foto, descargas, frontend o reglas comerciales modificados.

Preflight: 73 passed / 0 failed / 0 skipped, 5.3233 minutos. Build1 completo
LeaseRenew --no-restore --no-incremental: 0 errores/111 warnings, 2:44.24.
Despues solo se agregaron cinco tests Boundary; produccion intacta. Rebuild final
completo: 0 errores/111 warnings, 1:05.91. Sin fallos preliminares de builds/tests.
La seleccion ampliada y cierre de integridad se registran al terminar debajo;
las corridas superpuestas no se suman ni incluyen toda la suite historica.

### Resultado final, integridad y limpieza

| Corrida | Passed | Failed | Skipped |
|---|---:|---:|---:|
| preflight.trx, build1 | 73 | 0 | 0 |
| final.trx, final-build | 97 | 0 | 0 |

Final: 6.8368 minutos, 78 Renew, 17 regresiones focales de claim y dos HTTP.
Los 73 nombres preliminares reaparecen; se agregan cinco Boundary y las 19
regresiones indicadas, no son 170 pruebas distintas. final-cases.csv,
preflight-cases.csv y name-delta.csv permiten contrastar los nombres. No tests
omitidos ni fallidos en estas corridas. No fuentes productivas/tests editados
despues de final-build; solo documentacion. warning-delta.csv vacio contra el
rebuild ClaimH1 al comparar archivo/mensaje sin numeros de linea: 111 warnings
previos, sin atribuir mejoras a builds incrementales.

A-F de la matriz anterior demostrados en runtime para los casos descritos;
ausencia de consumidor/DI/E/S/proveedor y forma del guard tambien contrastadas
por inspeccion. Dominio aporta forma/formula del recibo; DB aporta PK/FK/CHECK y
atomicidad de sentencia/transaccion; store aporta autoridad/correspondencias,
orden de locks, CAS/reloj vivo, replay y reconciliacion. Ninguna capa se presenta
como autoridad permanente, prueba de despliegue o proteccion ante SQL arbitrario.
No contradiccion de P1-P7 ni defecto productivo reproducido durante estas pruebas.

Carreras finales ganador/bloqueado, todas observadas por pg_blocking_pids:
same 34108/27288; different 24252/31424; no-extension 34928/14876;
collision 32080/8924; request-first 29312/11204; renew-request 35972/15616;
start-first 34044/25692; renew-start 36184/10516; terminal-first 24600/29992;
renew-terminal 33352/29524. Lease vencida durante espera: PID31812 bloqueado por
22212; reloj DB observado 20:20:55.790785Z posterior al expiry20:20:55.690541Z.
Espera normal 37488/12940 y cancelada 12540/24252. Observador 31596 bloqueado
por 27008 hasta cancelar el token independiente. Evidencia en final-evidence.txt
y TRX; los PIDs son distintos dentro de cada carrera, pueden reutilizarse entre casos.

PostgreSQL17 propio 127.0.0.1:55547 usuario audit, data bajo el artifact root.
Solo bases cotizador_backend_test_* y migraciones existentes de fixtures.
preflight/final-remaining.txt registran cero fixtures; estados por corrida
confirman no server running. Variable de conexion child-local retirada y servidor
detenido en finally. Cluster parado/logs retenidos, no servicio activo. Cero
sng-sealed-* o sng-receipt-unit-* observados al cierre. Renew no crea storage;
HTTP usa sus dobles/helpers existentes. Sin barrido ni borrado de recursos ajenos.

Manifiestos initial/final: 921->926 rutas, cinco nuevos, dos existentes modificados,
cero ausentes y 919 anteriores byte-identicos. delta.csv coincide con inventario
anterior. Rama/HEAD preservados, 55 tracked modificados, 145->150 untracked,
cero staged. git diff --check=0 con avisos LF/CRLF previos. Trabajo acumulado
preservado, sin restaurar desde HEAD. Bin/obj LeaseRenew ignorados; scripts/logs/
CSV/TRX externos. Sin AGENTS aplicables encontrados.

Veredicto: **Renew explicito implementado y verificado A-F, sin activacion**.
Solo Windows. Sin DB/API habitual, reinicio de servicios, AI2/Gemini reales,
migracion nueva, frontend, commit/push/merge/despliegue. Integracion, heartbeat,
delegacion/autenticacion de worker, recovery P5 y validacion operativa de latencia,
capacidad/retencion siguen pendientes. Los 120s no son una medicion productiva.

## 12. Verificacion combinada Claim, coordinador y Renew

2026-10-26. Complemento de pruebas, no integracion productiva ni activacion.
El coordinador D.2c.6a y los stores de Claim/Renew permanecen sin cambios. No
heartbeat, temporizador, worker, selector, enqueue, recovery, DI o endpoint nuevo.

### Cobertura previa y diferencia de este bloque

Coordinator_Success_PublicManual_Review_AndNoResend ya prueba publicacion y ausencia
de reenvio, pero su fixture asigna Running directamente. BusinessCancellation y
LostAuthorityBeforePublication tampoco componen Claim/Renew operativos. Los tests
Renew de seccion 11 prueban inicio y finalizadores reales, no el coordinador con
archivos/copias reales. No se presentan esas pruebas como composicion ya cubierta.

Se reutilizan Files/LocalFileStorage, Copies/VerifiedContentCopy, ConsumingClient,
Coordinator y servicios reales del partial ExecutionDocumentPreparationPostgreSqlTests.
SeedFiles agrega assigned=true opcional, conservando defaults; solo los nuevos
casos usan start:false/assigned:false para crear Queued y reclamarlo por servicio.
No se modifica el default de H.Create ni se introduce un servicio de enqueue.
Cuatro exito/review existentes ejercitan el helper con los defaults anteriores;
dos HTTP intake/replay preservan la frontera sin activacion. No repetir 97 Renew.

### Casos combinados y fronteras

CombinedLeaseExecutionPostgreSqlTests agrega ocho casos en tres metodos:

| Aceptacion | Caso y propiedad |
|---|---|
| A/B/E | Combined_ClaimCoordinatorRenew_PublicManual_PublishAndReplay: cuatro combinaciones publico/manual y copia/proveedor; Claim real, unico intento del coordinador, Renew exacto, publicacion v2; replay de Claim/Renew y coordinador mientras ganador pausado y despues de terminal |
| C | Combined_ClaimCoordinator_CancellationOrExpiry_NoPublication(false): Request real durante proveedor, Renew rechazado CancellationRequested, respuesta valida no publica; Confirm real deja Cancelled/Finished-Cancelled/Requirement Pending y actividad terminal, replay sin DML |
| D | Mismo metodo(true): horizonte corto controlado, reloj DB observado vencido, Renew Expired y respuesta valida tardia rechazada en publicacion por LeaseConflict; Running/Processing sin cierre fabricado |
| E | Combined_RenewCommitLoss_DoesNotControlInFlightExecution: dos casos, commit real y respuesta perdida, observacion disponible o fallida; AlreadyApplied/Indeterminate sin repetir mutacion ni cambiar identidad, ejecucion separada ya en curso |
| F | Dos HTTP existentes Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict: Pending, enlaces y cero jobs/attempts/evidencia de ejecucion/recibos lease/llamadas automaticas |

La pausa de copia ocurre despues de sellar el primer documento, aun dentro de
PrepareAsync y antes de preparar el segundo/registrar el conjunto. La pausa del
proveedor ocurre despues de leer un byte del primer lector. Barreras explicitas,
no sleeps como prueba de orden. Otra conexion adquiere Quote/Requirement/Job
FOR UPDATE NOWAIT mientras la ejecucion esta pausada; Renew usa su contexto propio
y termina sin liberar la barrera. No transaccion global que envuelva el recorrido.
Se contrastan snapshots independientes de negocio/documentos/evidencia/resultado,
job completo salvo expiry y Claim original. Un Renew efectivo, mismo owner,
generacion/Started, unico intento, dos originales abiertos una vez y copias dispuestas.

AssignedJobExecution no contiene un vencimiento capturado: preparacion, consumo
y publicacion releen lease actual con sus guards existentes. La continuacion tras
Renew demuestra composicion con esa asignacion, no un permiso permanente. La
publicacion persiste recibo v2, identidades concordantes y propuesta RequiresReview
sin confirmar comercialmente (el elemento minimo compartido carece de categoria);
los recibos de lease historicos no cambian al liberar owner/expiry.

Vencimiento: solo se altera lease_expires_at_utc actual a reloj DB+1s mientras el
proveedor esta pausado. Claim/Started/attempt/evidencia quedan intactos, constraints
activas; no se finge un Renew previo. Se comprueba vivo inicialmente y se consulta
clock_timestamp hasta superar ese horizonte. La politica productiva sigue 120s.
La respuesta tardia valida no crea extraccion/propuesta, Failed, Cancelled ni otro
intento. Se conserva Running/Processing pendiente, sin recovery.

Incertidumbre: interceptor pierde respuesta despues de commit real; otro hook
puede impedir la observacion independiente. Antes de liberar proveedor se observa
Running, un Claim/un Renew persistido y una llamada local aun pausada, sin cambios
de negocio por Renew. La liberacion del harness es incondicional en finally, NO
una decision del supervisor basada en Indeterminate. El coordinador no recibe
ese resultado y puede publicar usando la lease DB vigente. Esto describe un
estado observable posible; NO aprueba continuar, cancelar o reintentar ante
Indeterminate en una integracion futura. Esa politica sigue pendiente.

Dominio conserva forma/transiciones; DB constraints y atomicidad por operacion;
stores/coordinador autoridad, identidades y guards. No atomicidad DB/proveedor,
consumo remoto, exactly-once, crash fisico ni storage desplegado certificados.
Proveedor simulado consume los lectores reales; nunca AI2/Gemini reales.

### Trazabilidad del complemento

Baseline inmediato: 926 rutas tracked/untracked no ignoradas, rama
feature/public-experience-intake, HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79.
55 tracked modificados,150 untracked,0 staged. Manifiestos SHA256 y artefactos:
`C:/Users/USUARIO/AppData/Local/Temp/sng-combined-5d67cc1ab75240d2a169d6d76542e1e9`.
Se conserva todo trabajo previo; no se usa HEAD para restaurar fuentes.

Inventario final: nuevo Tests/Infrastructure/Persistence/CombinedLeaseExecutionPostgreSqlTests.cs;
existentes ExecutionDocumentPreparationPostgreSqlTests.cs (opcion de fixture) y
este contrato (complemento). No cambios productivos, configuracion o migraciones.

### Resultados, integridad y pendientes

| Corrida | Passed | Failed | Skipped | Interpretacion |
|---|---:|---:|---:|---|
| final.trx (primera corrida) | 8 | 6 | 0 | Expectativa incorrecta Completed en helper nuevo |
| closure.trx | 14 | 0 | 0 | Mismos 14 nombres, expectativa exacta RequiresReview y concordancia reforzada |

La primera corrida dura 2.2895 minutos. Sus seis fallos corresponden a los cuatro
exitos y dos reconciliaciones, todos en la misma assertion de CombinedPublished:
Expected Completed, Actual RequiresReview. El payload minimo compartido omite
categoria; Ai2RequirementExtractionAdapter agrega UnknownElementType y requiere
revision. PrepareJobPublicationService conserva RequiresReview si respuesta o
propuesta lo requieren. Se corrigio SOLO la expectativa del test a RequiresReview
y se agrego igualdad exacta de outcome del recibo y estado de la propuesta.
No se admiten outcomes arbitrarios ni se cambia mapper, propuesta, lifecycle o
produccion para pasar. No defecto productivo reproducido. Los otros ocho casos
de la primera corrida pasaron. La historia fallida se conserva en TRX/cases.csv.

Cierre 2.0812 minutos: ocho combinados, cuatro regresiones del helper/defaults y
dos HTTP, todos Passed. Son 14 casos distintos, no 28 ni suma con los 97 Renew
auditados. name-delta.csv vacio confirma mismos nombres entre ambas corridas.
No fuentes/tests cambiaron despues del rebuild de cierre; solo documentacion.

Builds completos --no-restore --no-incremental -c CombinedProtocol:
build.log, 0 errores/111 warnings, 2:55.82; closure-build.log, 0/111, 18.07s.
warning-delta.csv vacio contra LeaseRenewAudit por archivo/mensaje sin numeros
de linea. No build fallido ni mejora de warnings atribuida a duracion diferente.
Tests --no-build -c CombinedProtocol, filtro filter.txt y runner run.ps1; loggers
TRX/console normal, resultados externos. No suite historica completa repetida.

Runtime final: NOWAIT/Renew PIDs publico-copia35096/6424, manual-copia35540/28296,
publico-proveedor3288/34420, manual-proveedor24884/34200. La barrera sigue cerrada
cuando Renew termina; contexto propio, sin lock de negocio retenido por E/S.
No es una nueva carrera de claims ni se atribuye pg_blocking_pids a estos casos
sin bloqueo esperado. Vencimiento: reloj DB21:04:15.889411Z supera horizonte
21:04:15.824190Z antes de Renew y de liberar proveedor. runtime-evidence.txt
registra fechas/PIDs y estados; snapshot y contadores se comprueban en assertions.

A-F verificados en los escenarios descritos. El resultado normal del fixture es
Published/Succeeded con intento Finished/RequiresReview y Requirement Processed;
la propuesta RequiresReview no esta confirmada comercialmente. Cancelacion deja
Cancelled/Finished-Cancelled/Pending mediante Confirm real, actividad terminal
exacta y replay sin DML. Vencida deja Rejected/Publication/LeaseConflict y
Running/Processing sin publicacion ni cierre. Reconciliacion produce AlreadyApplied
o Indeterminate del Renew mientras el coordinador permanece pausado, con commit
real conservado; despues del release incondicional puede completar Published.
Esto no resuelve una decision de supervision futura ni concede permiso desde
Indeterminate. Solo una invocacion local simulada del proveedor por ejecucion.

PostgreSQL17 propio127.0.0.1:55551, usuario audit, data bajo artifact root.
Migraciones existentes solo en fixtures temporales. Ambas corridas retiran variable
child-local y paran servidor en finally; closure-remaining.txt=0 y
closure-server-status.txt=no server running. Cluster parado/logs retenidos.
storage-check.csv:24 raices distintas registradas por ambos TRX, todas ausentes;
originales comprobados intactos antes del teardown. Cero sng-sealed-*,
sng-execution-docs-* o sng-receipt-unit-* observados al cierre. Sin barrido general.

Manifiestos initial/final926/927: un nuevo, dos existentes modificados, cero
ausentes y924 anteriores byte-identicos. delta.csv coincide con inventario.
Rama/HEAD preservados;55 tracked modificados,151 untracked,0 staged.
git diff --check=0 con avisos LF/CRLF anteriores. Produccion, modelo, migraciones,
DI, intake, manual, foto, descargas y todo trabajo anterior ajeno byte-identicos.
Bin/obj CombinedProtocol ignorados; informes/scripts/logs/manifiestos externos.
El script auxiliar de cierre se ajusto al lector XmlDocument de PowerShell tras
un error de variable local optimizada; no fue fallo de pruebas ni de produccion.

Veredicto: **verificacion combinada implementada y APTO A-F en el alcance probado,
sin integracion productiva ni activacion**. Pendientes: politica del ejecutor ante
Renew Indeterminate con trabajo en curso, recovery P5 y otros Running pendientes,
delegacion/autenticacion, integracion/heartbeat/cola autorizados y mediciones de
latencia/capacidad/retencion. No se implementa ninguna de esas decisiones.
Solo Windows, DB/archivos reales y proveedor simulado; sin DB/API habitual,
reinicios, proveedor real, frontend, commit/push/merge/despliegue o activacion.
