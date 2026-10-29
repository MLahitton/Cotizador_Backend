# Supervision de ejecucion durable: contrato propuesto

Estado original: **PROPUESTA PENDIENTE DE APROBACION**, 2026-10-26.
Actualizacion 2026-10-27: politica de automatizacion y detencion cooperativa A
aprobada en el alcance preciso de seccion12. Este bloque agrega observacion
autorizada y control local de etapas, NO supervisor/heartbeat/worker/recovery,
DI, endpoints, migracion ni activacion. D1-D9 no se aprueban indiscriminadamente.
Secciones1-11 conservan el diseno y evidencia originales salvo las precisiones
de estado senaladas aqui; hechos del inventario2 corresponden al baseline original.
Seccion12 distingue mecanismos nuevos de propuestas y obligaciones pendientes.
Actualizacion posterior 2026-10-27: seccion13 incorpora supervisor EXPLICITO por
JobId y ciclo local Renew, autorizados separadamente. La ausencia de supervisor
descrita arriba pertenece al cierre de seccion12. Sin worker/enqueue/recovery,
delegacion, DI productiva ni activacion; no aprobacion global de D1-D9.

## 1. Clasificacion y frontera

- **H (hecho):** inspeccion del backend actual; evidencia historica indicada como
  tal, no pruebas nuevas ejecutadas en esta tarea.
- **A (aprobado):** P1-P7 de assigned-job-lease-contract.md y coordinador D.2c.6a,
  dentro de sus alcances explicitos. No contienen una delegacion de worker.
- **P (propuesta):** comportamiento recomendado de primera version, sujeto a las
  decisiones D1-D9 de seccion 8 y pruebas futuras.
- **D (pendiente):** decision de producto/arquitectura/operacion que impide activar.

A: actor explicito activo/owner; identidad tecnica por encarnacion y claim; JobId
exacto; recibos persistidos sin backfill/purga; CAS y greatest(expiry,T+120s);
aplicada no equivale a extension; no renovacion tras solicitud de cancelacion;
lease inicial120s/politica1 y cadencia futura orientativa30s; observacion propia
independiente10s y cancelacion original. No reasignacion ni generacion2.

A: coordinador solo continua con Started, no retoma AlreadyStarted ni registro
sin paquete; no efectos dependientes de incertidumbre; propietario local unico
de las copias; publicacion v2 revalidada y atomica. Sin exactly-once remoto.

P: supervision local de UNA ejecucion de un JobId previamente autorizado, con
un responsable de Renew, parada cooperativa irreversible por invocacion y sin
recuperacion/reintento automaticos. No seleccion de cola ni creacion implicita de
jobs. La autonomia del ciclo no equivale a autoridad comercial permanente.

Los encabezados historicos del contrato de lease (por ejemplo Renew pendiente
en seccion10) describen su fecha, no contradicen secciones11-12. Su seccion12
libera una barrera de test incondicionalmente; NO aprueba una politica ante
Indeterminate. Este documento propone esa politica sin alterar los anteriores.

## 2. Inventario contrastado (H)

Referencias relativas al repositorio, simbolos concretos; lineas orientativas al
baseline de esta tarea. Son hechos de codigo, no certificacion de despliegue.

| Fuente | Hecho y consecuencia |
|---|---|
| Application/PreQuotes/ProcessRequirement/ClaimInitialLeaseService.cs: ClaimInitialV2Async, LeaseOperationResult | Recibe actor/IDs; no genera identidades. Receipt y Observation separados; no prueba de permiso permanente |
| Infrastructure/Persistence/Repositories/InitialLeaseClaimStore.cs: ClaimInitialV2Async/RunAsync | Claim Queued v2, sin intento; owner activo; CTE reloj/UPDATE/INSERT. Commit perdido se observa internamente con contexto/token10s. Puede devolver AlreadyApplied tras su propio commit |
| Application/PreQuotes/ProcessRequirement/RenewActiveLeaseService.cs: RenewActiveLease/Valid | Expected expiry e intento forman entrada exacta; no corrige stale. No API publica de lectura de snapshot para heartbeat |
| Infrastructure/Persistence/Repositories/ActiveLeaseRenewalStore.cs: RunAsync/MatchesClaim/MatchesRenew/Equal | Solo expiry y recibo; valida actor/Claim/intento; CAS, reloj vivo y cancelacion. Replay puede ser historico. ObserveOnly es interno, no interfaz publica |
| Application/PreQuotes/ProcessRequirement/StartAssignedJobService.cs: AssignedJobExecution; AssignedJobStartStore.StartVerifiedAsync | Contexto no contiene expiry capturado. Seleccion v2 explicita; default legacy v1 intacto; Start crea intento y actividad |
| Application/PreQuotes/ProcessRequirement/CoordinateAssignedExecutionService.cs: ExecuteAsync/CloseAsync | Un ct a inicio, documentos, consumo, preparacion, publicacion y cierre; checks antes/despues de awaits. No recibe resultados Renew ni callback de etapas/intento; no gate de pausa. Solo Started continua |
| Infrastructure/Persistence/Repositories/ExecutionDocumentPreparationStore.cs: PrepareAsync/ValidateLiveAsync/SameSource | Copia fuera de locks; revalida lease DB al registrar. SameSource no exige expiry original inmutable. CommercialLine viene del Requirement autorizado |
| Application/PreQuotes/ProcessRequirement/ConsumeExecutionDocumentsService.cs: ConsumeAsync/finally | Reserva unica del paquete, autoriza vivo, comprueba ct antes/despues del cliente; drena lectores y dispone paquete incluso ante OCE. No monitor durable durante red |
| Application/PreQuotes/ProcessRequirement/ExecutionDocumentReadStream.cs: DisposeAsync/CloseAsync | Impide nuevas lecturas y espera activeReads antes de disponer handle. No plazo garantizado si una lectura nunca termina |
| Infrastructure/DocumentProcessing/SingleRequestAi2Client.cs; CotizadorAi2DocumentProcessingClient.ProcessAsync | Transporte explicito sin redirects/retry agregado. Token enlazado con TimeoutSeconds existente; un SendAsync. No confirmacion de consumo remoto ni captura cruda en esta via |
| Infrastructure/Persistence/Repositories/JobPublicationStore.cs: PublishAsync/RunAsync | Lease actual, actor/evidencia/CommercialLine/cancelacion revalidados; ultimo guard DB. Publicacion v2 verifica ct tras commit/observacion: OCE puede ocultar un Succeeded confirmado |
| Infrastructure/Persistence/Repositories/JobTerminationStore.cs: ExecuteAsync/RunAsync | Request Running registra solicitud; Queued puede terminar Cancelled. Confirm/Fail requieren lease viva; intento exacto. Observa commit incierto10s; no mismo check final de ct que Renew/v2. El coordinador SI comprueba su ct tras CloseAsync |
| Application/PreQuotes/ProcessRequirement/JobTerminationService.cs: JobFailureCodes | Lista cerrada; mismatch, historico sin incorporacion y construccion no clasificada no justifican inventar Failed |
| Application/Common/Abstractions/PreQuotes/IRequirementDocumentWrite.cs: RequirementDocumentPolicy.Evaluate | Cualquier Queued/Running o intento activo impide replacement; cualquier historia de job impide editar documentos. Expiry no desactiva el job |
| Domain/PreQuotes/Requirement.cs: CanReplace/SupersedeAfterConfirmedCancellationBy; ManageRequirementDocumentsService.ReplaceRequirementAsync | Replacement ordinario Processed/Failed current; excepcion Pending con cancelacion coherente demostrada por policy bajo locks. Fail sin intento deja Pending: no habilita replacement por si solo |
| Api/Program.cs; Api/Authentication/CurrentUser.cs | JWT/ClaimsPrincipal de HTTP e ICurrentUser scoped; no identidad delegada fuera de HttpContext. No topologia de worker establecida por estos archivos |
| Tests/Infrastructure/Persistence/CombinedLeaseExecutionPostgreSqlTests.cs | Cuatro exitos con Claim/Renew/coordinador reales, cancelacion, expiry y dos commit-loss. Release del harness no es supervisor |

Historia: auditoria combinada 14/0/0, PostgreSQL/archivos reales y proveedor
simulado; seis fallos preliminares por Completed en vez de RequiresReview luego
corregidos. No se repite ninguna suite aqui. No atribuir a estos14 casos una
politica de supervision, agotamiento real de120s o autenticacion de worker.

## 3. Matriz de decisiones propuesta (P)

Cada fila separa resultado de llamada, estado DB y accion local. Una lectura
autorizada puede quedar obsoleta inmediatamente. Ninguna accion aqui extiende
las precondiciones de escritura de los servicios existentes.

| Evento | Conocimiento y concurrencia posible | Accion local propuesta | Escritura durable permitida hoy / limites y observacion posterior |
|---|---|---|---|
| Claim Claimed | Recibo propio confirmado; Running sin intento en guard. Otro actor puede solicitar cancelacion o revocar autoridad | Admitir UNA invocacion del coordinador con actor y asignacion del recibo; iniciar responsabilidad local de lease | StartVerified debe autorizar y devolver Started; Claim no autoriza proveedor por si solo |
| Claim AlreadyApplied | Operacion aplicada; Live tampoco demuestra que nadie haya iniciado. Puede ser reconciliacion de la misma llamada | Primera version conservadora: NO iniciar, ni siquiera tras reconocimiento propio; terminar localmente como reconocimiento | Cero nuevos efectos; observacion read-only distingue terminal/pending. Running sin intento requiere salida separada aprobada; disponibilidad sacrificada deliberadamente |
| Claim Indeterminate, OCE o fallo no clasificado | Puede existir Running/recibo; ningun coordinador lanzado por esta invocacion | No Start ni Renew; conservar identidad para diagnostico, no crear otra | Nada dependiente. Observar recibo exacto sin ejecutar Claim de nuevo; no asumir Queued/rollback |
| Claim Rejected/Conflict o PersistenceError con rollback conocido | Esta invocacion no confirma claim propio; otro caller puede tenerlo | Terminar sin coordinador ni retry automatico | No deducir estado global desde el rechazo. Corregir datos/autoridad o autorizar otra accion requiere decision externa |
| Renew Renewed o AlreadyApplied con observacion vigente compatible | Recibo de operacion EXACTA aplicada; extension solo si Expires>Expected. Trabajo local sigue en vuelo | Continuar solo la MISMA tarea ya admitida y no detenida; planificar siguiente periodo, no reiniciar coordinador | Nueva operacion periodica requiere nuevo snapshot autorizado y guards. AlreadyApplied no rescata una tarea parada ni autoriza inicio |
| Renew reconocido historico: Terminal/Expired/ContextChanged/CancellationRequested/Superseded | Recibo original verdadero, no autoridad actual. Puede competir cierre o nueva mutacion | Detener ciclo; terminal confirmado se observa, demas casos detencion A salvo cancelacion de negocio tratada debajo | Ninguna extension desde replay. Superseded inesperado bajo renovador unico es conflicto de supervision, no permiso de adoptar expiry ajeno |
| StaleLease o AttemptConflict | Operacion rechazada; posible renovador externo o Start concurrente. No prueba vencimiento ni rollback de otra operacion | Primera version: detener cooperativamente, diagnosticar; no cambiar expected/reusar solicitud con payload distinto | Cero retry correctivo. Nueva lectura explica conflicto, no autoriza continuar esta invocacion detenida |
| Renew Indeterminate / excepcion no clasificable / PersistenceError | Commit puede existir en incertidumbre; incluso rollback conocido no garantiza presupuesto suficiente. Coordinador/proveedor/publicacion pueden seguir | A: fijar Stop irreversible, parar tick, cancelar token de ejecucion y esperar tareas | No Request/Fail/Confirm por ese motivo. Observar despues; mismo OperationId no se reenvia por rutina; DB puede ser Succeeded |
| Expired / autoridad revocada / contexto inactivo | Lease o permiso denegados en lectura/guard; un commit previo puede haber ganado | Stop local, no renovaciones adicionales; no asumir propiedad para cerrar | Finalizador existente revalida y puede rechazar. No bypass Admin ni nuevo actor para adoptar claim; recovery separado |
| CancellationRequested persistida | Solicitud real, no terminal. Proveedor puede estar consumiendo | Detener nuevos Renew; impedir nuevas etapas productivas. Cancelar trabajo y drenar; proponer cierre controlado separado solo tras join (seccion4) | Confirm real solo con solicitud, autoridad, intento y lease aun vivos. No Request duplicado ni ampliar lease para cerrar. Si vence: P5 pendiente |
| Caller cancela / host se apaga | Cancelacion local no expresa voluntad comercial; commits anteriores sobreviven | No admitir ejecuciones nuevas; Stop, cancelar espera/tareas y hacer join dentro de apagado supervisado | Cero Request/Fail automaticos. No devolver exito tardio; OCE no implica rollback ni Cancelled. Crash exige observacion posterior |
| Published/AlreadyPublished confirmado | Recibo v2 confirmado, grafo terminal; Renew en vuelo puede terminar despues | Sellar resultado confirmado localmente, detener ciclo y esperar Renew | Nunca degradar Succeeded por error posterior de heartbeat; ninguna compensacion/finalizacion adicional |
| Publicacion Indeterminate u OCE durante commit | Succeeded puede existir aunque caller no reciba recibo | Stop, esperar ciclo/ejecucion; no repetir proveedor ni Publish para averiguar | Observacion read-only del terminal/evidencia. Ausencia no certifica rollback ni autoriza otro job |
| Failed/Cancelled confirmado | Cierre reconocido con identidades exactas, no exito remoto | Parar renovador, drenar, devolver resultado separado del diagnostico local | Sin nuevo cierre. Error tardio del renovador no reabre terminal |
| Cierre Indeterminate o rechazado | Failed/Cancelled puede existir o seguir Running; estado no inferible de excepcion | Stop y observacion; no bucle de finalizadores | No inventar terminal. Recibo/estado exactos y autoridad actuales para reconocer; luego recovery si no terminal |
| Coordinador Rejected/EvidenceAlreadyRegistered/AlreadyStarted | No permiso para continuar; puede existir intento/evidencia/otro caller. Rejected no describe necesariamente estado DB | Stop del renovador PROPIO, sin disponer recursos ajenos ni retomar | No paquetes desde DB ni Fail generico. MissingIncorporation/ContentMismatch/construccion quedan pendientes sin codigo nuevo |

## 4. Incertidumbre, tokens y competencia con terminales

### Alternativa A recomendada, no aprobada

Detencion cooperativa irreversible de esta invocacion. El futuro supervisor
poseeria un stop latch y un CTS de ejecucion; el ct enlazaria cancelacion del
caller, apagado del host y detencion supervisora. Este token llegaria sin sustituir
su significado al coordinador, quien ya lo propaga a cada etapa. AI2 agrega su
timeout propio, sin modificarlo. No llamar Request por cancelar ese token.

CTS de scheduling distinto: finalizar normalmente la ejecucion detiene el reloj
del heartbeat SIN cancelar una publicacion ya completada. Una operacion Renew
admitida tiene identidad fija y token de operacion: al detener se cancela
cooperativamente si sigue pendiente y SIEMPRE se espera su resultado. Su observador
interno10s permanece independiente; no reutilizar el CTS cancelado para reconciliar.
No encadenar observaciones10s indefinidamente. CTS se disponen solo tras joins.

Stop y admision de cada etapa deben tener un punto de orden local comun. Propuesta
minima: coordinador acepta un control interno opcional de ejecucion (default sin
cambios para callers actuales), con TryEnterStage y notificacion de Started/intento
y finalizacion. Stop impide permisos posteriores; no bloquea por una llamada DB
ni mantiene mutex durante red. Permiso adquirido antes de Stop define etapa ya
admitida: puede alcanzar su llamada/commit despues de Stop; recibe token cancelado.
No prometer imposibilidad de efectos fisicos posteriores a Stop. La garantia es
NO admitir una nueva etapa despues de Stop, no atomicidad entre memoria/DB/red.

Actual: checks de ct antes/despues de awaits NO proporcionan ese orden de admision
ni notifican el intento a un renovador. Son suficientes para cancelacion cooperativa
basica, no para afirmar la garantia mas precisa anterior. El control es un cambio
futuro focal del coordinador, no nuevo motor de workflow ni estado persistido.

Si el proveedor ya recibio documentos, cancelar es mejor esfuerzo; puede completar
remotamente. Consumidor drena todas las lecturas antes de liberar handles; el
supervisor no dispone paquete/transporte mientras ExecuteAsync o Renew sigan vivos.
El proveedor no se repite aunque no haya respuesta. Si no termina una lectura,
el drenaje puede exceder el apagado: registrar interrupcion, no cerrar el handle
por fuera ni afirmar cleanup exitoso. Terminacion forzada del proceso puede dejar
residuos y Running; requiere procedimiento operativo, no scanner incluido aqui.

Publicacion compitiendo: si Stop gana la admision, no se inicia esa etapa. Si ya
admitida, cancelacion o guard pueden rechazarla, o commit puede ganar. Succeeded
DB no se degrada. Publish v2 puede lanzar OCE despues de commit; coordinador tambien
puede hacerlo tras recibir el resultado. No convertir ese OCE en Published para el
caller cancelado: separar resultado de llamada de terminal durable observado.
Si supervisor YA recibio un terminal confirmado, ese dato prevalece frente a
diagnosticos tardios del heartbeat. Si no, no inventarlo: observacion read-only.

Inicio y JobTerminationStore no ofrecen exactamente la misma semantica de ct final
que Claim/Renew/publicacion v2. ExecuteAsync/CloseAsync del coordinador aportan el
check posterior. Un futuro cierre separado debe aplicar ese check en su propio
caller sin declarar rollback. No cambiar silenciosamente finalizadores legacy.

Cancelacion comercial detectada por supervisor: proponer Stop y join, luego UNA
Confirm explicita con token de cierre separado y acotado, solo por solicitud DB
comprobada, no por caller/host/Indeterminate. Releer intento actual con autorizacion;
puede ser null si Start no llego a confirmar. Si coordinador ya confirmo, reconocer
el terminal sin escribir; si pudo publicar, observar antes y no intentar cancelarlo.
Confirm conserva guard de lease viva: drenaje puede consumir la ventana, no renovar
despues de Request. Un fracaso o incertidumbre de Confirm no permite repetirlo como
consulta. Este manejo es D4 pendiente; no esta implementado en el supervisor.

### Alternativa B: suspender y observar, no recomendada para v1

Requiere gate PAUSABLE antes de cada etapa, conservar tarea/paquete local sin
reconstruccion, limite de retencion de disco y observador autorizado read-only
de recibo candidato EXACTO, estado/attempt/lease/cancelacion. No basta expiry mayor.
No puede pausar bytes ya enviados ni deshacer publicacion admitida. Debe determinar
que la misma invocacion no inicio otra etapa mientras observaba; solo esa tarea
podria continuar tras nueva autorizacion viva. Nunca crear otro coordinador.
Tras presupuesto agotado, revocacion, expiracion o evidencia incompatible: A sin
reanudar. Falta definir como sincronizar guard, pausa y confirmacion del candidato
sin extender lease durante incertidumbre. Mayor complejidad y retencion de recursos;
no justificada para primera activacion. Preferencia A no equivale a aprobacion.

### Observacion faltante, separada de mutacion

No usar ClaimInitialV2Async/RenewActiveV2Async/PublishAsync/Confirm como una lectura
externa ante incertidumbre: si falta el recibo/terminal, sus vias normales pueden
mutar. Sus observeOnly internos no son API de supervisor. Se propone lectura corta
autorizada dedicada, mismo orden de locks/correspondencias, sin DML ni proveedor.
Separar consulta de snapshot para NUEVO Renew y observacion de un resultado incierto.

Reconocer commit propio exige conservar candidato completo (incluidas fechas DB),
no solo OperationId. Hoy candidatos de escritura viven dentro de los stores; el
supervisor no los recibe en Indeterminate. No fingir acceso. Primera version A no
necesita resolverlos para continuar: una lectura posterior puede informar terminal
durable validado, sin afirmar identidad exacta de un commit propio desconocido.
Si se exige ese reconocimiento externo exacto, aprobar un contrato interno de
contexto de observacion que transporte candidato inmutable; no exponerlo a HTTP ni
reconstruir hash desde grafo mutable. Terminal Succeeded exige recibo v2/evidencia
completos; datos incoherentes quedan sin reconocimiento, no se reparan.

## 5. Heartbeat y concurrencia local (P)

Un propietario local por JobId/claim/encarnacion, un ciclo y maximo UN Renew en vuelo.
Registro local evita duplicados accidentales, NO sustituye exclusion DB ni autentica.
Sin toma de posesion de otro Running, sin recuperar identidad tras reinicio.

1. Generar una vez InstanceId por encarnacion y OperationId del Claim; recibir
   JobId exacto/actor de autoridad delegada aprobada. No descubrir candidatos aqui.
2. Solo Claimed permite iniciar coordinador una vez. AlreadyApplied/Indeterminate
   detienen este primer diseno. No bucle de Claim para conseguir Claimed.
3. Crear responsabilidad local de lease inmediatamente, pero habilitar ticks Renew
   solo al recibir Started e intento exacto del coordinador. Antes de Started no
   renovar desde este supervisor: evita carrera expected null -> intento. Si Start
   consume la lease, rechazar y tratar pendiente, no renovarla retroactivamente.
   Renew sin intento sigue siendo operacion valida separada, no obligacion del ciclo.
4. Primer tick orientativo a30s desde Claim si Started ya disponible; si Started
   llega despues, un tick inmediato con snapshot DB vigente (sin rafaga acumulada).
   Si Start no llego, esperar su resultado; no iniciar varios ticks atrasados.
5. Cada nuevo periodo obtiene snapshot autorizado fresco: job/Claim/actor/attempt/
   expiry y reloj DB. No leer un DbContext compartido con el coordinador. Crear un
   nuevo OperationId SOLO despues de la confirmacion anterior y para ese periodo;
   congelar request completo antes de enviar. CAS final decide, no reloj local.
6. El intento notificado debe coincidir con snapshot. Cambio entre lectura y Renew
   produce AttemptConflict: detener, no sustituir expected ni repetir. StaleLease
   igual. Otros writers inesperados no se adoptan. Serializar localmente no elimina
   concurrencia externa ni permite ocultar conflictos mediante refresh+retry.
7. Renewed o reconciliacion AlreadyApplied de ESA llamada, con observacion compatible
   vigente y sin Stop, permite otro periodo. N=Expected es aplicada sin extension,
   no disparar renovaciones inmediatas para forzarla. IDs distintos sin extension
   pueden crear recibos; no prometer unicidad entre ellos.
8. Al terminar coordinador, cerrar admision de ticks antes del join. Renew que gano
   antes del terminal puede persistir; terminal que gano antes hace rechazar Renew.
   No reasignar ni degradar resultado terminal. Recoger ambas tareas y diagnosticos,
   disponer CTS/scopes/transporte al final; no fire-and-forget ni excepciones perdidas.

El scheduling usa tiempo monotono local para intervalos, nunca para decidir lease
viva. Retraso/lock lento no acumula ticks. 30s no SLA; 120s no timeout AI2. No
introducir retry por timeout/HTTP, cambiar IDs ante duda o usar el mismo ID con
expected nuevo. Politica exacta de presupuestos de operacion/apagado es D8: debe
reservar tiempo para drenaje y observacion, sin prometer plazo total10s del store.

Mediciones previas a activar: p95/p99/max de Start, copias por bytes/documentos,
espera locks/DB/commit, Renew y observacion, red/timeout del proveedor, publicacion,
drenaje y pausas de proceso; margen expiry-T observado por DB; disco/concurrencia,
capacidad recibos/indices/WAL. Cargas sinteticas con proveedor simulado primero;
piloto real solo con autorizacion separada, cuotas y criterio de parada. Telemetria
por IDs seguros/etapa/duracion/outcome, sin bytes, rutas, tokens o respuesta cruda.
Tras eventual activacion: histogramas/alertas por perdida de margen, Indeterminate,
Running envejecido/P5, drenaje y crecimiento; revisiones de cadencia/retencion
aprobadas, no autotuning silencioso. A30s:120 Renew/hora/job continuo mas Claim;
replays no agregan filas, nuevos IDs sin extension si; sin purga volumen acumulativo.

## 6. Recuperacion minima pendiente, no implementada

Regla comun H: no basta dejar vencer una lease para liberar replacement. Policy
considera Running activo aun vencido. Edicion documental exige cero jobs/intentos.
La tabla describe estado conocido en lectura, no inexistencia de trabajo remoto.

| Pendiente | DB disponible / no reconstruible | Requirement y replacement | Recomendacion para decision D5 |
|---|---|---|---|
| Running sin intento | Claim/lease/IDs; no prueba de ausencia futura de Start hasta fencing; sin paquete | Normalmente Pending; Running bloquea replacement. Fail sin intento deja Pending y tampoco habilita replacement ordinario | Observar; si abandono confirmado, cancelacion comercial explicita y cierre autorizado, no fabricar intento ni Failed para desbloquear |
| Running + Processing | Intento/correlacion/inicio, posible evidencia; no prueba de envio ni respuesta | Processing bloquea replacement | Detener/fence publicacion vieja y cerrar abandono por politica aprobada; nunca reiniciar ese intento |
| Evidencia sin paquete | Manifest/hash/registro persistidos; spool no recuperable por contrato, ni prueba de proveedor no invocado | Processing con job activo bloquea | Preservar evidencia; no crear paquete desde DB. Cierre separado antes de autorizar version/job nuevos |
| Proveedor posiblemente invocado, sin publicacion | Puede haber intento/evidencia, no recibo de consumo remoto ni respuesta durable | Processing/Running usual; ausencia de grafo no libera | Admitir incertidumbre comercial; cancelacion de procesamiento local no cancela remoto. Nuevo envio requiere aprobacion explicita del riesgo |
| Publicacion/cierre con respuesta incierta | Puede existir terminal/recibo/grafo; ausencia no prueba rollback. Candidate local puede haberse perdido | Succeeded suele Processed, Failed con intento Failed, Cancelled con intento Pending; hasta observar no inferir | Primero reconocer terminal read-only. No repetir mutacion como consulta ni proveedor. Si inconsistente, escalar sin reparar |
| CancellationRequested + expiry vencida (P5) | Solicitud/lease/attempt; no permiso vigente para Confirm actual | Running activo; con intento normalmente Processing | Aprobar cierre de cancelacion abandonada bajo fence de vencimiento y autoridad actual; no gracia Renew, no reasignacion. Obligatorio antes de activar |
| MissingIncorporation / ContentMismatch | Intento iniciado y referencia/evidencia de incorporacion, registro puede faltar; no autentica bytes actuales ni habilita backfill | Running/Processing bloquea nueva version | Aprobar salida de abandono/cancelacion y luego replacement autorizado. No inventar codigo Failed ni OBSERVED_NOW |
| Construccion no clasificada | Puede haber evidencia/envio y respuesta solo local; no snapshot publicado recuperable garantizado | Running/Processing bloquea | Diagnosticar sin datos sensibles; misma salida separada, no reutilizar automaticamente respuesta o paquete |

Tres operaciones DISTINTAS:
1. Reconocer terminal: lectura autorizada, valida recibos/evidencia y preserva IDs,
   no exige lease actual ni toca successor; sin convertirlo en permiso de trabajo.
2. Cerrar abandono: nueva autorizacion explicita, no reconocimiento. Recomendacion
   minima: via administrativa acotada al JobId, voluntad de cancelacion persistida,
   lease vencida comprobada con reloj DB bajo locks del protocolo, identidad/attempt
   exactos y guard final; no Succeeded previo, no renovacion/reasignacion. Proponer
   efectos Cancelled coherentes con el cierre actual, nunca afirmar remoto cancelado.
   El Confirm ACTUAL no permite ese cierre vencido: requiere contrato/cambio propio,
   pruebas, trazabilidad del actor/motivo y aprobacion; no SQL manual como solucion.
3. Autorizar nuevo job: accion comercial nueva tras cerrar/reconocer el anterior y
   satisfacer capacidades. Preferir nueva version documental cuando corresponda;
   nuevas identidades y decision explicita sobre posible duplicacion remota. No
   convertir replay ni successor en enqueue, ni prometer automaticidad.

Para Cancelled coherente (un job y intento compatible o ninguno), policy permite
replacement Pending current; Processed/Failed current requieren ausencia de activos.
No aplicar esto a estados corruptos o multiples terminales ambiguos. Si owner/usuario
ya no autorizan cierre, primero resolver autoridad por proceso aprobado; no un
bypass Admin tecnico. Si no se puede establecer autoridad/ruta/evidencia, mantener
bloqueado y escalar a responsable identificado con plazo operativo, no inventar
terminal. Primera activacion exige procedimiento de atencion y ruta autorizada de
salida probados, incluso si son humanos; no framework generico ni barrido automatico.

## 7. Delegacion y topologia pendientes (P/D)

H: API autentica humanos mediante JWT e ICurrentUser derivado de HttpContext.
Stores aceptan Guid actor y revalidan DB, pero no autentican un proceso remoto
ni certifican origen de ese Guid. Jobs auditados provienen de fixtures/factory
explicita; no se ha autorizado creacion automatica desde intake.

Recomendacion inicial: ejecutor en el mismo proceso/API y misma frontera backend,
con scopes DB independientes y concurrencia acotada, NO por asumir topologia ya
decidida sino para evitar nuevo canal de credenciales. Coste: CPU/disco compartidos,
reciclados/despliegues interrumpen tareas; no garantiza disponibilidad ni unica replica.
Proceso separado aisla recursos/reinicios, pero exige autenticar canal/ejecutor,
credenciales de minimo privilegio y almacenamiento accesible; no se aprueba ni se
introducen tokens firmados/cuentas privilegiadas por anticipacion. D6 decide.

Propuesta original de delegacion D7, NO aprobada como mecanismo: autorizacion
con procedencia verificable para creacion de job v2 exacto y ejecucion acotada;
guardar procedencia de actor/JobId/Requirement/request, instante, vencimiento de
delegacion, alcance y revocacion verificables. La politica aprobada el2026-10-27
excluye exigir aprobacion manual del comercial para cada proyecto Experience:
debe definirse autorizacion interna explicita del flujo automatico, sin inferir
credenciales de CreatedBy/RequestedBy. No basta un RequestedBy historico
ni inventar un usuario de servicio. No retener JWT humano, HttpContext ni passwords
para usar fuera del request. En mismo proceso, entrega de contexto interno desde
frontera autentica mas grant auditable; su schema/atomicidad con creacion de job
deben aprobarse, no estan resueltos por una variable local.

Permiso propuesto: claim inicial exacto, una ejecucion local, Renew durante esa
invocacion y cierre conocido segun contratos; no selector, otros jobs, nuevo envio,
reasignacion, confirmacion comercial, cambio de owner o recovery. La cancelacion
comercial requiere solicitud persistida; un grant de ejecutar no es orden de cancelar.
Lectura para observar terminal separada de mutacion; no revelar metadata a ajenos.

Cada operacion futura verifica grant vigente/no revocado ADEMAS de owner/usuario/
contexto DB. Cambio de owner, usuario inactivo o revocacion: Stop, no adoptar al
nuevo owner ni renovar bajo otro actor. Inactividad Client/Project deniega trabajo;
Request/Confirm existentes tienen excepciones de actividad para cierre, pero no
bypass de usuario/ownership. El grant debe definir tambien ese alcance de cierre.

Reinicio: InstanceId nuevo, no recuperar owner anterior para retomar; grant viejo
no revive lease ni spool. Observar pendientes, cerrar por via aprobada, autorizar
otra ejecucion separadamente. API reciclada no puede tratar jobs Running como
Queued. Antes de activar multiples replicas, demostrar exclusiones DB y que la
delegacion no concede a todas permiso de adoptar el mismo intento ya iniciado.

## 8. Decisiones pendientes numeradas

| ID | Decision y opcion recomendada (NO aprobada) |
|---|---|
| D1 | Elegir A irreversible ante Renew incierto; no B ni reanudacion. Incluir rechazo/fallo de renovacion como motivo conservador de Stop |
| D2 | Solo Claim Claimed inicia coordinador; AlreadyApplied incluso propio detiene v1. Aceptar Running sin intento y su salida separada, no permiso implicito desde Live |
| D3 | Gate de admision/no pause y notificacion Started; lectura autorizada de snapshot/terminal sin DML. Adoptar frontera de etapa admitida, no promesa de cero efectos posteriores a Stop |
| D4 | Un renovador, ticks tras Started, sin retry CAS/AttemptConflict; cancelacion comercial con Stop/join y Confirm controlado si lease sigue viva. Definir prioridad terminal y tokens separados |
| D5 | Salida de abandono/P5 por accion explicita autorizada y fence de vencimiento, sin proveedor/reasignacion; reconocer terminal, cerrar abandono y autorizar nuevo job por separado. Definir voluntad comercial, trazabilidad y tratamiento de revocacion/incoherencia |
| D6 | Mismo proceso API inicialmente, scopes/concurrencia separados; validar capacidad/reinicios antes de aceptar topologia. No canal remoto nuevo por defecto |
| D7 | Delegacion minima auditable ligada a job/actor autentico, vigencia/revocacion/alcance. Aprobar quien crea jobs y como se entrega; sin cuenta privilegiada ni RequestedBy como credencial |
| D8 | Mantener120s aprobados/30s orientativos, medir antes de fijar presupuestos y concurrencia; definir tiempo de apagado y escalacion por drenaje bloqueado. Retencion/capacidad siguen pendientes |
| D9 | Exponer estado de llamada separado de terminal durable y observacion read-only. No transformar OCE en exito tardio ni degradar Succeeded por heartbeat; definir atencion operativa de pendientes antes del piloto |

No hay contradiccion que requiera editar contratos existentes. El tratamiento
conservador de Claim AlreadyApplied y los gates son decisiones nuevas, no hechos
heredados. P1-P7 permanecen aprobadas solo en su alcance previo.

## 9. Pruebas futuras de aceptacion (no ejecutadas)

PostgreSQL aislado real, constraints activas, archivos reales, proveedor simulado;
barreras, PIDs distintos y pg_blocking_pids cuando deba existir bloqueo. Tiempo
monotono/fake scheduler solo para ticks; reloj PostgreSQL para guards/expiry.

| Garantia futura | Evidencia exigida |
|---|---|
| Admission/Stop | Barreras antes/despues de cada TryEnterStage: Stop ganador impide etapa posterior; etapa previamente admitida puede completar/cancelar. Cero segundo proveedor, sin afirmar rollback por OCE |
| Claim y replay | Claimed una invocacion; AlreadyApplied Live/historico/commit reconocido no inicia; Indeterminate/OCE conservan posible Running sin attempt y cero efectos dependientes |
| Ciclo unico | Ticks lentos no se solapan/acumulan; un OperationId por solicitud inmutable; cero refresh+retry tras stale/attempt. Start antes de primer tick suministra intento real; retraso hasta expiry no revive |
| P4 | Renew aplicado sin extension no dispara rafaga; nuevo periodo distinto no se confunde con replay; CAS y dos recibos sin extension conservan limites aprobados |
| Incertidumbre A | Commit real con respuesta perdida/observador fallido mientras copia/proveedor/publicacion; Stop/join sin nuevo ID ni retry. Comprobar commits reales y estado conocido separado |
| Competencia terminal | Renew/Publish/Confirm en ambos ordenes bajo locks observados; Succeeded/Cancelled/Failed confirmado no degradado por error tardio. OCE postcommit requiere observacion, nunca nuevo envio |
| Cancelacion | Caller/host no escriben Request; solicitud DB real detiene renovacion, Confirm solo vivo. Drenaje que agota lease deja P5 sin falso Cancelled hasta cierre separado aprobado |
| Recursos | Lectura que termina tarde, transporte que ignora cancelacion, cleanup/logger fallidos; no dispose de handle activo, originales intactos, joins y CTS limpios. Registrar limite de apagado sin certificar crash |
| Read-only | Snapshot y observacion con actor/owner/grant, no metadata ajena, cero DML en ausencia/terminal/timeout; no usar APIs mutantes como consulta; candidatos distintos no reconocidos como propios |
| Recovery acotado | Expiry vivo/vencido DB, carrera con terminal/renovacion, autoridad/revocacion y Request exacta; cierre+attempt/Requirement/actividad atomicos, sin proveedor; replacement permitido solo tras policy real |
| Delegacion | Grant expirado/revocado/job ajeno, cambio owner/usuario/contexto, restart/nueva instancia; ninguna adopcion ni privilegio Admin. Separacion entre autenticacion humana y ejecutor |
| No activacion accidental | Legacy/defaultv1, manual/foto/descargas y dos HTTP intake/replay Pending sin jobs/attempts/evidencia/recibos/llamadas automaticas hasta integracion expresamente autorizada |

## 10. Bloques por garantia y condicion de activacion

Orden propuesto, cada bloque con aprobacion, pruebas y cierre propios:
1. Resolver D1-D9; contrato de delegacion/creacion de job, observacion y salida de
   abandono. Son decisiones de autoridad/lifecycle, no clases sueltas a codificar.
2. Lecturas autorizadas y delegacion auditable (persistencia solo si aprobada),
   mas cierre acotado de abandono/P5 con reconocimiento y fencing. Cerrar garantias
   de seguridad/salida sin ejecutar automaticamente trabajos.
3. Control local de etapas/tokens y supervision de UNA asignacion exacta, incluido
   ciclo Renew unico, terminales y cleanup; composicion explicita de prueba, sin DI
   productiva, selector o heartbeat autonomo instalado.
4. Auditoria integrada y pruebas de carga/apagado. Solo despues: bloque separado
   de integracion/topologia/admision de jobs y piloto autorizado; no activar por
   mera existencia de servicios o configuracion compilable.

Checklist bloqueante antes de activar:
- [ ] D1-D9 aprobadas con semantica de incertidumbre y OCE sin exito tardio.
- [ ] Autorizacion de creacion y delegacion autenticada, vigente y revocable probadas.
- [ ] Stop/gates/ciclo unico y prioridad de terminal verificados en concurrencia.
- [ ] Observacion read-only disponible; no mutaciones usadas como consultas.
- [ ] P5 y Running abandonados tienen salida autorizada probada, responsable y plazo.
- [ ] Politica comercial sobre posible duplicacion remota antes de nuevos jobs.
- [ ] Drenaje/apagado, cuotas de disco/concurrencia y residuos tras crash definidos.
- [ ] Medidas de latencia/margen/recibos/retencion y alertas con umbrales acordados.
- [ ] Topologia/replicas/almacenamiento y limites del piloto validados.
- [ ] Auditoria de integracion aprobada; activacion explicitamente autorizada aparte.

## 11. Integridad de este entregable

Solo este archivo nuevo. Analisis estatico, sin builds/tests/DB/proveedores;
ninguna migracion, proceso supervisor, cambio a contratos anteriores ni activacion.
No se repiten auditorias/suites cerradas. Sin DB/API habitual, frontend, commit,
push, merge o despliegue. Instrucciones AGENTS aplicables: ninguna encontrada en
la inspeccion del repo/ancestros. No restaurar contra HEAD ni tocar trabajo previo.

Baseline inmediato:927 rutas tracked/untracked no ignoradas,55 tracked modificados,
151 untracked,0 staged; rama feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79. Manifiestos SHA256 antes/despues y estado
Git en C:/Users/USUARIO/AppData/Local/Temp/sng-supervision-contract-1b63bdd358c348f59e21a00ca4ca341d.
Comparacion final:928 rutas, un nuevo (este documento), cero anteriores modificadas
o eliminadas;927 anteriores byte-identicas. Rama/HEAD preservados;55 tracked
modificados,152 untracked,0 staged. git diff --check=0; avisos LF/CRLF previos.
La aprobacion futura de este diseno no equivale a autorizacion de implementacion
o activacion en esta tarea.

## 12. Observacion autorizada y control de etapas, sin activacion

2026-10-27. Bloque implementado explicitamente como preparacion del supervisor A.
No nuevo estado persistido, codigo terminal, migracion, DI productiva ni endpoint.
No modifica Claim/Renew, duraciones, formulas, consumidor, transporte o finalizadores.
Las ejecuciones de secciones anteriores siguen siendo historia; no se suman aqui.

### Politica aprobada y pendientes

El usuario aprobo automatizar el recorrido normal y, ante Renew Indeterminate,
detener cooperativamente esa invocacion sin reanudar ni reenviar automaticamente;
conservar commits/resultados confirmados y no fabricar terminales por incertidumbre,
lease perdida o cancelacion local. Una recuperacion posterior debera ser explicita
por owner vigente autorizado, con comprobaciones transaccionales. Experience
automatico NO debe exigir aprobacion manual del comercial para cada proyecto:
su autorizacion interna debera definirse expresamente, sin inferir credenciales
de CreatedBy/RequestedBy. No se implementa ni activa esa automatizacion aqui.

No aprobacion global D1-D9: alternativa A queda aprobada como politica, pero los
detalles de supervisor/ciclo, tratamiento de Claim AlreadyApplied, delegacion,
hosting, presupuestos operativos, observacion de commit propio externo, codigos
terminales y recovery siguen pendientes. Tampoco se aprueba en este bloque el
Confirm separado posterior a Stop. Un control detenido no admite Termination.

### Observacion implementada

ObserveAssignedExecutionService.ObserveAsync recibe JobId/RequirementId/
ExecutionRequestId/ClaimOperationId exactos y actor explicito. Valida UUIDs antes
de DB; AssignedExecutionObservationStore repite validacion. No acepta digests,
streams ni un candidato de commit. Resultados Observed, Rejected con failure
preciso y sin snapshot, o Unavailable sin snapshot si la lectura falla. OCE del
caller se propaga con su token original; no se traduce en terminal ni snapshot.

Contexto propio sin tracking; descubrimiento de ruta no autoriza. Transaccion
corta, locks SHARE en orden Submission si aplica, User, Client, Project, Quote,
Requirement, Job, Attempts por Id y Claim. Relee relaciones bajo locks. Usuario
activo y owner actual, sin bypass Admin. Requiere v2 y Claim valido del mismo
job/request/Requirement/actor, autorreferencia/owner/formula/generacion, asignacion
igual a Started del job. No adoptar un Renew como Claim ni metadata ajena.

Intento vinculado: pertenencia al Requirement, count/ID, correlacion, inicio no
anterior a asignacion y estado compatible con job. Terminal requiere Finished,
fecha/error y outcome concordantes; Succeeded no admite falta de intento. Running
requiere Pending sin intento o Processing con intento y sin otro activo incompatible.
Un terminal historico puede tener successor; no compara su estado comercial con
la historia ni lo modifica. Client/Project inactivos o Requirement no current se
reflejan como ContextChanged, sin permiso operativo; actor inactivo/ajeno rechaza.

Snapshot sealed de valores inmutables, sin entidades EF/navegaciones/streams:
identidades exactas, actor, JobState/IsTerminal, solicitud persistida, intento/
count/state/outcome, owner de asignacion y owner actual separados, generacion,
AssignedAtUtc, expiry/FinishedAtUtc y ObservedAtUtc de clock_timestamp PostgreSQL.
Lease observada Live/Expired/Released separada de Authorization ActiveContext/
ContextChanged. ActiveContext solo describe autoridad/contexto en esa lectura:
no es CanSend/CanPublish ni delegacion; todas las acciones conservan guards propios.
Expired no significa Failed/Cancelled. Released corresponde al terminal, no lease
revivida. ClaimOperationId y asignacion reconocen un registro existente; NO prueban
el commit propio de una operacion incierta sin candidato completo. Tampoco certifica
el grafo/recibo de publicacion completo: JobState observado no es Publish replay.

Cero INSERT/UPDATE/DELETE/SaveChanges; solo SELECT y locks. No invoca Claim, Renew,
Publish o Confirm como consulta. Transaccion/contexto se disponen antes de devolver;
no commit de negocio, almacenamiento ni proveedor. Coherencia bajo protocolo/locks
existentes, no contra SQL privilegiado que ignore invariantes. Lectura puede esperar
locks y ser cancelada; no presupuesto operativo nuevo ni latencia garantizada.

### Control local implementado

AssignedExecutionControl, objeto por invocacion, agrega Stop, StopToken, IsStopped,
TryAdmit(stage), y Task Started con contexto AssignedJobExecution confirmado o null
si termina sin conocer Started. Task usa continuaciones asincronicas, no callback
de usuario dentro de locks. Notifica solo Started y el ProcessingAttemptId real
del recibo; AlreadyStarted/Indeterminate no notifican inicio. Task se completa una
sola vez. No reconstruye paquete ni permite reutilizar el control en otro Execute.

Stop irreversible/idempotente y TryAdmit comparten un mutex breve. Stop ganador
impide admisiones posteriores. Admision ganadora permite que etapa ya admitida
alcance llamada/commit despues de Stop: no atomicidad memoria/DB/red. Cancelacion
se solicita fuera del mutex; callbacks defectuosos no revierten Stop ni impiden
cancelar los restantes. No promesa de retorno acotado de Stop si un callback bloquea.
TryAdmit no ejecuta trabajo ni concede autorizacion durable; no salta ningun guard.

CoordinateAssignedExecutionService conserva ExecuteAsync(execution,actor,ct) y
agrega overload explicito con control. Sin registro DI ni consumidores nuevos.
Se admite Start, Documents, Consumption, Preparation, Publication y Termination.
No bypass de cierre tras Stop. El token enlazado lleva Stop y caller a las etapas;
si caller esta cancelado, OCE conserva token original incluso compitiendo con Stop.
Solo Started continua; notificacion puede existir aunque Stop posterior impida
Documents, pues el inicio ya se conoce confirmado. Inicio incierto no se notifica.

Reglas anteriores de errores/cierre, ownership del paquete y drenaje intactas.
finally libera paquete aun propio; consumidor drena lectores transferidos antes
de disponer. Complete cierra nuevas admisiones y completa Started con null cuando
corresponde. Caller dispone control solo despues de esperar Execute; Dispose en
curso rechaza. Stop posterior a resultado confirmado no cambia ese resultado ni
DB. Si compite con commit, caller puede recibir OCE y DB conservar terminal; no se
fabrica exito tardio, compensacion o rollback. La observacion separada lo informa
sin invocar de nuevo al proveedor. Nunca RequestCancellation por Stop/caller.

### Verificacion focal y trazabilidad

Pruebas nuevas: AssignedExecutionControlTests y dos partials PostgreSQL,
AssignedExecutionObservationPostgreSqlTests y AssignedExecutionControlPostgreSqlTests.
Reutilizan fixtures/servicios reales y almacenamiento real; proveedor simulado.
SQL de fixtures para incompatibilidades/expiry mantiene constraints activas, no
representa comportamiento generado por servicios ni defensa ante SQL arbitrario.

Matriz de aceptacion:
- Observacion: antes/despues del intento, solicitud, expiry, Failed/Cancelled con
  y sin intento, Succeeded real y successor; identidad, claim, actor/ownership e
  intento incompatibles; snapshot null ante rechazo, cero DML/SaveChanges,
  snapshots independientes iguales y locks liberados comprobados con NOWAIT.
- Coherencia: lector SHARE pausado antes de leer Claim; Request de otra conexion
  espera Quote UPDATE, PIDs distintos y pg_blocking_pids antes de liberar lector.
  Primera observacion sin solicitud, segunda con solicitud; sin DML del observador.
- Control: ambos ordenes Stop/admision y Stop concurrente, callbacks fuera de gate,
  reutilizacion/disposal en curso rechazados, Started real unico o null exacto.
- Integracion: Stop antes de Start, durante copia/proveedor y tras paquete/respuesta;
  no etapa posterior/compensacion, evidencia y originales conservados. Lectura en
  vuelo deliberadamente no cooperativa bloquea disposal hasta liberar barrera.
- Commits: Stop despues de commits reales de inicio/publicacion/fallo conserva
  Running/Succeeded/Failed; una llamada proveedor como maximo, sin Request inventado.
  Cancelacion comercial real confirma Cancelled; cancelacion caller conserva OCE.
- Regresion: suite focal Coordinator existente y dos HTTP intake/replay sin
  automatismos; no repeticion indiscriminada de Claim/Renew/modelo.

Primer rebuild completo build1:2 errores (CS0619/xUnit2014, overload Assert.Throws
con Func<Task>) y113 warnings; no tests sobre ese build. Se corrigio la assertion
al overload Action para el rechazo sincronico de Attach y se agregaron tokens
de test a dos Task.Run/WaitAsync que originaban warnings nuevos. Produccion no
se cambio para ajustar expectativas. Build2 completo:0 errores/111 warnings.
Los resultados de ejecucion y controles finales se consignan al cerrar debajo.

Baseline inmediato928 rutas, rama feature/public-experience-intake, HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79;55 tracked modificados,152 untracked,
0 staged. Artefactos externos y manifiestos SHA256 incluidos untracked:
C:/Users/USUARIO/AppData/Local/Temp/sng-supervision-primitives-1f3a454e5cf143a8993ed7b0e254d7ba.
Sin restauracion desde HEAD, migracion nueva, DB/API habitual, proveedores reales,
frontend, commit/push/merge/despliegue o activacion. Pendientes: supervisor/ciclo
Renew, autorizacion interna Experience/delegacion, hosting/presupuestos, recovery
explicito por owner vigente y su auditoria. Ninguno inferido de estas primitivas.

### Resultado final e integridad

| Ejecucion | Passed | Failed | Skipped | Alcance |
|---|---:|---:|---:|---|
| preflight.trx, build2 | 44 | 0 | 0 | Casos nuevos antes de los refuerzos finales |
| final.trx, final-build | 83 | 0 | 0 | 46 nuevos, 35 regresiones Coordinator y dos HTTP |

Preflight dura3.4362 minutos; final6.3095 minutos. Los44 nombres reaparecen en
final, no son127 casos distintos. Despues del preflight solo se reforzaron tests:
assertion fuera del callback que Stop puede capturar, dos rechazos de identidad
(job inexistente y Claim ajeno) y comprobacion del segundo original tras drenaje.
Ninguna fuente productiva cambio despues de build2. Final repite todas esas pruebas.
No tests fallidos ni omitidos en ambas corridas. El fallo preliminar de compilacion
queda registrado arriba; no se presenta como prueba ejecutada ni defecto productivo.

Los46 nuevos son tres unitarios del control,29 de observacion PostgreSQL y14 de
integracion del control. Estos ultimos incluyen observacion de Succeeded real.
Los35 Coordinator mantienen la entrada anterior sin control, incluido reconocimiento,
autoridad, incertidumbre, cierres, concurrencia y preservacion del sucesor. Dos HTTP
DIRECT_CONTACT/ANDRES_CANDELA conservan Pending y cero jobs/attempts/evidencia de
ejecucion/recibos lease/llamadas automaticas. No suites Claim/Renew/modelo completas.

Build final completo aislado SupervisionPrimitives:0 errores/111 warnings,1:01.29.
warning-delta.csv vacio contra build.log de CombinedAudit, normalizando numeros de
linea y deduplicando por archivo/mensaje. No se atribuye mejora a compilacion parcial.
Comando: dotnet build Tests/CotizadorBackend.Tests.csproj -c SupervisionPrimitives
--no-restore --no-incremental --verbosity minimal. Tests mismo proyecto/configuracion,
--no-build, loggers TRX/console normal; filtro exacto en final-filter.txt. run.ps1
conserva comandos, variables child-local y ciclo del cluster. cases.csv/executions.csv
separan corridas; final-cases.csv conserva los83 nombres. Solo documentacion cambio
despues del rebuild final.

Coherencia actual: lector PID7016, Request bloqueado PID6120; pg_blocking_pids
observado antes de liberar, reader DML=0. Regresion de coordinadores: ganador26380,
bloqueado35320, bloqueo observado. NOWAIT durante E/S en conexiones17656,18716,23780.
Barreras deterministas y consultas de locks, no sleeps como prueba de orden. Drenaje
mantiene cero disposals mientras una lectura no cooperativa esta pendiente; al liberar
su barrera dispone las dos copias. Estos casos no certifican latencia acotada si un
transporte o callback no termina, consumo remoto, fsync, crash fisico ni Unix.

Inventario respecto de initial.csv del worktree, no respecto de HEAD:
- Nuevo Application/PreQuotes/ProcessRequirement/ObserveAssignedExecutionService.cs.
- Nuevo Infrastructure/Persistence/Repositories/AssignedExecutionObservationStore.cs.
- Nuevo Application/PreQuotes/ProcessRequirement/AssignedExecutionControl.cs.
- Nuevo Tests/Application/PreQuotes/AssignedExecutionControlTests.cs.
- Nuevo Tests/Infrastructure/Persistence/AssignedExecutionObservationPostgreSqlTests.cs.
- Nuevo Tests/Infrastructure/Persistence/AssignedExecutionControlPostgreSqlTests.cs.
- Modificado Application/PreQuotes/ProcessRequirement/CoordinateAssignedExecutionService.cs.
- Modificado docs/assigned-execution-supervision-contract.md.

Manifiestos initial/final:928/934 rutas, seis nuevas, dos modificadas, cero ausentes,
926 anteriores byte-identicas. Rama/HEAD preservados;55 tracked modificados,
158 untracked,0 staged. git diff --check=0 con avisos LF/CRLF anteriores. Claim/Renew,
modelos/EF/migraciones, DI, consumidores productivos, Experience/manual/foto/descargas,
cliente y finalizadores intactos. Busqueda de referencias: nuevos usos explicitos solo
en tests; overload opcional no activa el recorrido. Bin/obj SupervisionPrimitives
ignorados son artefactos de build, no fuentes. Scripts/logs/reportes fuera del repo.

PostgreSQL17 propio127.0.0.1:55557, usuarioaudit, data bajo artifact root. Cada corrida
retira variable child-local y detiene servidor en finally. final-remaining.txt=0;
final-server-status.txt=pg_ctl:no server running. Cluster parado y logs retenidos,
no servicio activo. Migraciones existentes solo en fixtures temporales. Las63 raices
distintas de storage registradas entre ambas corridas estan ausentes; residuals.csv
vacio para sng-sealed, sng-execution-docs y sng-receipt-unit. Originales comprobados
antes del teardown; sin barrido ni borrado de recursos ajenos. No DB/API habitual,
reinicios, proveedores reales, commit/push/merge/despliegue o activacion.

Veredicto: implementado y verificado en el alcance descrito. Observacion y control
de etapas preparados para supervision; sin heartbeat, recovery ni activacion
automatica. La politica aprobada ante Renew Indeterminate aun requiere su futuro
supervisor: estos tests llaman Stop explicitamente, no implementan ese ciclo.

## 13. Supervisor explicito de una ejecucion concreta

Implementacion 2026-10-27 conforme a autorizacion separada del usuario. Reutiliza
Claim, observacion, coordinador con control y Renew reales. No modifica sus stores,
formulas, duracion120s, estados persistidos, codigos terminales, transporte ni
presupuestos independientes de reconciliacion. No worker/selector/enqueue, recovery,
reasignacion, permisos/delegacion, migracion, DI, endpoint o activacion productiva.
Las secciones anteriores conservan su evidencia historica; no son runtime actual.

### Entrada, secuencia y propiedad

SuperviseAssignedExecutionService.ExecuteAsync recibe ClaimInitialLease completo
(JobId/RequirementId/ExecutionRequestId/OperationId/InstanceId), actor explicito y
token. Identidad Claim estable aportada por caller backend confiable; no se infiere
actor de CreatedBy/RequestedBy, no se crean jobs ni se descubre otro candidato.
Invoca Claim una vez. Solo Claimed con recibo/observacion compatibles inicia una
invocacion del coordinador. AlreadyApplied devuelve historia sin retomar; rechazo
o incertidumbre tampoco abre documentos ni inicia intento desde el supervisor.
Excepcion no clasificable de Claim retorna Indeterminate sin acciones dependientes.

Control y CTS del ciclo son propios por invocacion. AssignedJobExecution se forma
desde recibo Claim, sin inventar intento. El coordinador notifica Started real;
AlreadyStarted/inicio incierto completan sin notificacion de intento y sin ticks.
El supervisor no lee ni dispone streams/copias. Coordinador/consumidor mantienen
ownership y drenaje auditados. Al terminar el coordinador se cancela el token del
ciclo y se esperan ambas tareas antes de disponer control/CTS o devolver resultado.
No mutex local sobre DB/E/S/callbacks ni transaccion global; cada store crea sus
contextos. Dos supervisores compiten mediante Claim/locks/constraints existentes.

### Programacion y continuidad

TimeProvider controla GetTimestamp/GetElapsedTime y Task.Delay; referencia30s,
sin usar DateTime de maquina para decidir lease viva. El origen monotono se toma
al recibir Claimed confirmado. No intenta reconstruir el instante DB del commit
con reloj local. Primera oportunidad30s desde esa recepcion, pero nunca antes de
Started. Si Started llega despues, observa inmediatamente. La espera de Claim no
se cuenta como tiempo de ejecucion renovable; una lease ya vencida se rechaza por
observacion/guards. No se garantiza margen minimo ante demoras arbitrarias.

Hay un solo ciclo serial. Antes de cada Renew, observacion autorizada nueva valida
job/request/Requirement/Claim/actor, asignacion/owner/generacion, intento exacto,
Running/Processing/current/contexto activo, lease Live/expiry>ObservedAt y ausencia
de solicitud. No permiso permanente: Renew vuelve a validar en su transaccion.
Cada operacion nueva genera OperationId nuevo UNA vez y congela ExpectedExpiry e
ExpectedAttempt de esa lectura. Ningun rechazo/incertidumbre genera retry, refresco
CAS ni otro ID sustituto. Renewed/AlreadyApplied de esa misma llamada solo continuan
con recibo de tipo Renew, IDs/actor/inputs/asignacion exactos, formula de dominio y
observacion Live compatible con intento y vencimiento del recibo.

Tras operacion valida se programa30s desde su terminacion, no desde ticks perdidos.
Renew aplicado sin extension es valido y espera igual: no bucle inmediato para
forzar extension ni garantia de unico recibo entre IDs distintos si expiry no cambia.
No se cambia L=120s ni se presentan30/120 como latencias productivas medidas.
Programador controlado de tests solo mueve reloj monotono local; reloj PostgreSQL
real sigue determinando fechas, formulas y guard de lease. No reloj DB simulado.

### Matriz final de decisiones

Matriz previa a fuentes: decisions.txt en artifact root. Resultado no es nuevo
estado de job; la politica A no autoriza recuperar una invocacion detenida.

| Evento | Stop / siguiente accion | Resultado separado |
|---|---|---|
| Claimed compatible | Crear control y coordinador una vez | Recibo Claim, no permiso perpetuo |
| AlreadyApplied/rechazo Claim | No iniciar ni retomar | NotStarted con resultado Claim |
| Claim Indeterminate/excepcion | Sin coordinador ni Renew | Indeterminate, no rollback supuesto |
| Renewed/AlreadyApplied compatible, con/sin extension | Esperar nueva oportunidad30s | LastRenewal original; no retry inmediato |
| Renew Indeterminate/PersistenceError/rechazo/StaleLease/AttemptConflict | Stop irreversible; drenar, sin otro Renew | Stopped/RenewalNotConfirmed y resultado preciso Renew |
| Observacion no disponible | Stop; ninguna mutacion dependiente | ObservationUnavailable |
| Autoridad/identidad/contexto/lease incompatibles | Stop; no renovar ni fabricar terminal | IncompatibleObservation |
| Solicitud persistida observada o rechazo CancellationRequested de Renew | Stop; no Request ni Confirm posterior | CancellationRequested; pendiente si coordinador no confirmo |
| Excepcion inesperada ciclo | Stop; join, no compensacion | UnexpectedCycleFailure |
| Coordinador termina | Cerrar ciclo/cancelar Renew en curso y esperar | Resultado coordinador conservado |
| Terminal confirmado por coordinador y fallo tardio del ciclo | No degradar terminal conocido | CoordinatorCompleted con Published/Failed/Cancelled real |
| Caller cancela | Cancelacion cooperativa, Stop y join | OCE con token ORIGINAL; commits conservados |

Finalizar el coordinador cancela solo el token caller de Renew; no reemplaza ni
cancela directamente el token independiente10s de reconciliacion del store. Por
ello el join puede esperar esa observacion y una lectura no cooperativa pendiente.
Callbacks de cancelacion defectuosos no sustituyen el resultado conocido al cerrar
el ciclo. No shutdown/Stop/drenaje acotados ante callbacks o lecturas que no terminan.

### Resultado local y estado durable

AssignedSupervisionResult separa Outcome local, Claim, Coordinator (si regreso un
resultado), CoordinatorCancelled, StopReason, LastRenewal y Observation final.
CoordinatorCompleted describe fin de esa invocacion, NO exito comercial: consultar
Coordinator.Outcome. Un coordinador puede regresar Rejected/AlreadyStarted o
Indeterminate. Si fue cancelado cooperativamente no se inventa su resultado.
LastRenewal puede ser null si el store lanzo OCE despues de un commit; la DB puede
contenerlo. No reconstruir su candidato desde observacion del job.

Tras join, salvo caller cancelado, se hace lectura final autorizada con token caller,
no con token Stop, sin mutacion ni reanudacion. Fallo devuelve Unavailable, no degrada
un terminal conocido del coordinador. Esta lectura no tiene nuevo timeout operativo;
respeta cancelacion y puede esperar locks. Un Succeeded observado sin resultado
Published del coordinador NO produce Published. No certifica grafo integral ni
commit propio de operacion incierta. Ninguna lectura favorable reabre el control.
Si caller cancela, se propaga OCE original despues de drenar; no hay respuesta tardia
exitosa ni RequestCancellation implicito. DB puede conservar Running o terminal.

Cancelacion comercial conserva su ruta existente solo si el coordinador la alcanzo
antes de Stop ganador. No Confirm post-Stop, no nuevo codigo Failed ni Cancelled
ficticio. P5 vencida, terminalizacion controlada futura y recovery explicito quedan
pendientes, con autoridad/presupuesto aprobados aparte.

### Pruebas y fronteras

Partials nuevos Supervisor_* reutilizan archivos y PostgreSQL reales; probes
transparentes cuentan llamadas y delegan a stores reales. Proveedor/catalogos
simulados. ManualSupervisionClock implementa TimeProvider/ITimer para barreras de
programacion; no altera PostgreSQL. No sleeps como prueba de orden.
Cuatro normales publico/manual y copia/proveedor verifican extension efectiva,
publicacion v2 completa con RequiresReview del fixture, un intento/proveedor,
recibos/identidades/evidencia concordantes y originales intactos. Sin extension
usa horizonte SQL valido con constraints activas, no Renew previo inventado.
Started tardio adelanta solo reloj local y prueba cero observaciones/ticks antes
de confirmacion, lectura inmediata despues y ausencia de rafaga.

Replays/rechazos/incertidumbre Claim y Start no retoman. Concurrencia de Claim usa
PIDs distintos y pg_blocking_pids; perdedor sin copias ni disposal del ganador.
Stop automatico se observa mediante token del proveedor, SIN llamar Stop desde
harness. Indeterminate usa commit real y observador indisponible; rollback conocido,
stale/attempt, expiracion DB, autoridad/contexto, observacion offline/excepcion y
solicitud se comprueban separadamente, sin nuevo Renew ni terminal ficticio.
Harness libera barreras de lectura SOLO despues de comprobar Stop autonomo.

Carreras complementarias: publicacion con respuesta Renew retenida (incluida perdida
tardia), observacion bloqueada por publicacion, Request antes/despues de Renew,
caller cancelado en Renew/reconciliacion independiente y lectura no cooperativa.
No se confunden interceptores tras commit con crash fisico; SQL de fixtures con
constraints activas no certifica defensa ante SQL arbitrario. Sin proveedores reales.

### Ejecucion e integridad del bloque

Baseline inmediato934rutas, feature/public-experience-intake,
HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79;55tracked modificados,158untracked,
0staged. Manifiestos initial/final SHA256 incluyen untracked, baseline worktree
no reconstruido desde HEAD. Artefactos externos:
C:/Users/USUARIO/AppData/Local/Temp/sng-supervisor-7207dd68ce174c7b912a9f1c295d0b3b.

Preflight build1 completo0errores/111warnings,34.79s. preflight.trx24passed/1failed/
0skipped,1.8156min. Fallo en Supervisor_StopsItself_NoRetryOrFictitiousTerminal
(rollback): esperaba PersistenceError y obtuvo Indeterminate. Interceptor lanzaba
con reader abierto, impidiendo demostrar rollback; no defecto productivo. Se
cerro reader antes de lanzar, igual al fixture de rollback Renew existente, sin
relajar expectativa ni modificar store. El snapshot no mostraba mutacion residual,
pero eso no convierte retrospectivamente Indeterminate en rollback conocido.
Se agregaron despues seis casos de carreras/drenaje, sin cambiar produccion.
Resultados posteriores e inventario final se consignan al cerrar debajo.

second.trx repite los25 anteriores e incorpora seis carreras/drenaje:31passed,
0failed,0skipped,2.1665min; build2 completo0errores/111warnings,13.37s. Luego se
agrego SOLO Supervisor_StopAfterPublicationCommit_ObservedSucceededIsNotPublished:
Renew confirmado con respuesta retenida, publicacion confirmada tambien con
respuesta retenida; perdida de respuesta Renew y observador indisponible provocan
Stop AUTOMATICO antes de que el coordinador reciba su resultado. DB conserva
Succeeded/grafo/recibo; resultado local Stopped, CoordinatorCancelled=true y
Coordinator=null, nunca Published inventado desde lectura. Replay posterior del
supervisor reconoce Claim historico pero no retoma ni reenvia. No fuente productiva
cambio despues del primer rebuild. Corridas superpuestas no se suman.

### Cierre final, aceptacion y preservacion

| Corrida | Passed | Failed | Skipped | Fuentes |
|---|---:|---:|---:|---|
| preflight.trx | 24 | 1 | 0 | build1, inyeccion de rollback aun con reader abierto |
| second.trx | 31 | 0 | 0 | build2, fixture corregido y seis carreras nuevas |
| final.trx | 115 | 0 | 0 | final-build,32 supervisor y83 regresiones focales |

Final dura6.7585min. Incluye los25 nombres del preflight, incluido el fallo corregido,
los31 de second y un nuevo caso de Succeeded observado sin Published conocido.
Los83 existentes son29 observacion,14 control integrado,3 unitarios del control,
35 Coordinator y2HTTP. No son171 casos distintos ni una suite completa del repo.
cases.csv/executions.csv/failures.csv conservan resultados por corrida; final-cases.csv
los115 nombres finales. Las83 regresiones fueron ejecutadas AHORA, no contadas desde
la auditoria anterior. No fuente productiva/test cambio despues del rebuild final;
solo documentacion de cierre. Ningun test final omitido ni fallido.

| Criterio | Escenarios ejecutados y alcance |
|---|---|
| A normal | Supervisor_Normal_PublicManual_CopyProvider,4casos: Claim/coordinador/Renew reales, barreras copia/proveedor, extension efectiva, v2/grafo/recibo/evidencia, un intento/proveedor, originales preservados |
| A sin extension | NoExtension_OneOperationPerOpportunity: dos IDs distintos y recibos sin extension,30s entre operaciones y maximo un Renew activo; horizonte SQL de fixture, no reloj DB simulado |
| B inicio | LateStarted_NoTicksBeforeConfirmation_NoCatchUp; NoResume_ClaimOrStart,5casos; ConcurrentClaims_PostgreSqlExclusion_NoLoserResources: no ticks previos,95s monotono tardio, no reanudacion y exclusiones DB |
| C Stop autonomo | StopsItself_NoRetryOrFictitiousTerminal,10casos: indeterminate real, rollback conocido, stale, intento, expired, autoridad, contexto, observacion offline, excepcion y solicitud; proveedor recibe cancelacion del supervisor antes del release del harness |
| C reconciliacion positiva | ReconciledRenew_AlreadyApplied_ContinuesWithoutRetry: una llamada/ID, commit real perdido reconocido por store, continua sin reintento |
| D cierre/Renew | PublishedWhileRenewResponsePending_TerminalNotDegraded,2casos; PublicationLocksBeforeTick_NoRenewAfterCoordinatorCompletes; StopAfterPublicationCommit_ObservedSucceededIsNotPublished: joins, commit conservado y resultado local/durable separados |
| D cancelacion/drenaje | RequestVersusRenew_NoPostStopConfirmation,2ordenes; CallerCancellation_RenewOrReconciliation_JoinsAndKeepsCommit,2casos; IndeterminateWithOutstandingRead_StopsAndDrainsWithoutHarnessDecision: token original/observador independiente, sin Confirm post-Stop y lectura drenada |
| E compatibilidad | 83regresiones anteriores: entrada coordinador sin control preservada, primitivas y dos HTTP DIRECT_CONTACT/ANDRES_CANDELA Pending/cero jobs/attempts/evidencia de ejecucion/recibos/llamadas automaticas |

Concurrencia supervisor final: Claim ganadorPID35896/bloqueado6148; publicacion
reteniendo locksPID27904/observacion bloqueada13664. Conexiones distintas y
pg_blocking_pids observados antes de liberar barreras. Regresiones: coordinador
34760/17908; observador29180/Request35692; NOWAIT durante E/S36856,37828,37080.
Request/Renew nuevos ordenan eventos con barreras/commits reales; no afirman bloqueo
SQL donde no lo observaron. Commit retenido DESPUES de confirmar no retiene locks.
runtime-evidence.txt y TRX preservan mensajes individuales. No promesa exactly-once,
consumo remoto, fsync, crash fisico, Unix o latencia productiva.

Los tres rebuilds completos ExplicitSupervisor:0errores/111warnings. Final12.82s;
warning-delta.csv vacio frente build.log de SupervisionAudit, normalizando numeros
de linea/deduplicando archivo y mensaje. Comando: dotnet build
Tests/CotizadorBackend.Tests.csproj -c ExplicitSupervisor --no-restore
--no-incremental --verbosity minimal. Tests mismo proyecto/configuracion --no-build,
filtros por corrida en *-filter.txt, loggers TRX/console normal; run.ps1 registra
el ciclo del cluster. No correcciones de warnings inferidas del tiempo de build.
Intentos de patch documental con contexto no coincidente no editaron archivos;
se repitieron con contexto exacto. No fueron fallos de build/test.

Inventario respecto del baseline inmediato:
- Nuevo Application/PreQuotes/ProcessRequirement/SuperviseAssignedExecutionService.cs.
- Nuevo Tests/Infrastructure/Persistence/SupervisedExecutionTestSupport.cs.
- Nuevo Tests/Infrastructure/Persistence/SupervisedExecutionPostgreSqlTests.cs.
- Nuevo Tests/Infrastructure/Persistence/SupervisedExecutionBoundaryPostgreSqlTests.cs.
- Nuevo Tests/Infrastructure/Persistence/SupervisedExecutionRacePostgreSqlTests.cs.
- Modificado docs/assigned-execution-supervision-contract.md.

Manifiestos934->939rutas:5nuevas,1modificada,0ausentes,933anteriores byte-identicas.
Rama/HEAD preservados;55tracked modificados,163untracked,0staged. diff-check=0 con
avisos LF/CRLF anteriores. Produccion PREVIA completa intacta, incluido coordinador,
Claim/Renew/observacion/control, cliente, modelos/EF/migraciones, DI, finalizadores,
Experience/manual/foto/descargas. Consumers.txt confirma uso nuevo solo en tests;
no registro productivo. Bin/obj ExplicitSupervisor ignorados son artefactos de build.

PostgreSQL17 propio127.0.0.1:55561, usuarioaudit, data bajo artifact root. Cada corrida
retira variable child-local y para el servidor en finally. final-remaining.txt=0;
final-server-status.txt=pg_ctl:no server running. Cluster parado/logs retenidos como
evidencia, no servicio activo. Migraciones EXISTENTES solo en bases temporales.
137raices distintas de almacenamiento de las tres corridas comprobadas ausentes;
storage-check.csv todosExists=false, residuals.csv vacio para sng-sealed,
sng-execution-docs y sng-receipt-unit. Originales se comprobaron antes de teardown.
No barrido/borrado de recursos ajenos, DB/API habitual, reinicios, proveedores reales,
frontend, commit/push/merge/despliegue o activacion.

Pendientes antes de activar: autorizacion interna/delegacion Experience sin aprobacion
comercial manual por proyecto; hosting/cadencia y presupuestos medidos; politica y
operacion de recovery P5/Running pendientes, confirmacion controlada posterior a Stop,
observacion/reconocimiento explicito de candidatos inciertos fuera de esta llamada,
capacidad/retencion de recibos y auditoria del supervisor. No aprobacion global D1-D9.

Veredicto: **Supervisor explicito implementado y verificado, sin worker,
enqueue, recuperacion ni activacion productiva**.
