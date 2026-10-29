# Autorizacion interna de extraccion inicial Experience

Estado: **D1-D4 APROBADAS; I.1-I.3/II AUDITADOS; III IMPLEMENTADO, DESACTIVADO POR DEFECTO**, 2026-10-29.
La propuesta original del 2026-10-27 se conserva abajo como historia. La seccion12
conserva I.1; la seccion13 registra admision y revocacion I.2. La seccion14 registra
consumo del permiso en ejecucion explicita. La seccion15 registra emision atomica II;
la seccion16 registra el host III, aun apagado. Activacion pendiente.
Las secciones anteriores conservan su historia.
El procesamiento inicial automatico de Experience, sin
aprobacion comercial manual por proyecto, YA esta aprobado. No se vuelve a pedir
esa decision. Este contrato concreta como delegarlo sin convertir datos de negocio
en credenciales. La seccion12 distingue estructura de operaciones futuras.

Se conservan los cierres auditados de [lease](assigned-job-lease-contract.md),
[supervision](assigned-execution-supervision-contract.md) y
[tratamiento de detenidos](stopped-execution-resolution-contract.md), incluido B.
No se reabre su auditoria ni se certifica una integracion aun inexistente.
Las pruebas de las secciones1-10 son aceptacion futura de integracion, no runtime
de I.1. La seccion12 registra por separado su verificacion estructural.

## 1. Hechos comprobados y puntos de integracion

Referencias relativas al repositorio; los simbolos, no numeros historicos, fijan
la localizacion. H = hecho inspeccionado; R = recomendacion, no implementacion.

| Fuente y simbolo [H] | Comportamiento actual / consecuencia |
|---|---|
| `Api/Controllers/PublicExperienceSubmissionsController.cs`, POST | AllowAnonymous; llama intake, procesamiento de Submission y preparacion secuencialmente. Replay tambien recorre esos pasos. Conflicto de intake corta antes. Responde Received con enlaces; no invoca supervisor ni crea job |
| `Application/PublicExperience/PublicExperienceIntakeService.cs`, ExecuteAsync/replay | Valida/captura bytes y fingerprint; SubmissionId publico es clave de repeticion, NO credencial. Conflicto de contenido no concede otra operacion. La foto tiene comprobacion propia. Guarda incorporacion, no una autorizacion de ejecucion |
| `Application/PublicExperience/ProcessPublicExperienceSubmissionService.cs`, ProcessAsync | Resuelve `PublicExperienceOptions.OwnerEmail` contra usuario persistido activo; no elige owner desde advisorCode. Crea Client/Project con ese owner. El email configurado selecciona identidad de negocio, no autentica un ejecutor |
| `Infrastructure/Persistence/Repositories/PublicExperienceProcessingRepository.cs`, ProcessLockedAsync | Transaccion con Submission bloqueada; Client/Project y enlaces de procesamiento se confirman juntos. Una Submission ya procesada se reconoce; no se recrea el proyecto |
| `Application/PublicExperience/PreparePublicExperienceRequirementService.cs`, ExecuteAsync | Lee snapshot, verifica originales, genera destinos/evidencia y candidato Pending. Si ya hay resultado preparado puede retornar antes de copiar. Verificacion de bytes puede producir evidencia nueva desde fuentes antiguas compatibles; no confundir esto con confiar en evidencia parcial |
| `Infrastructure/Persistence/Repositories/PublicExperienceRequirementPreparationRepository.cs`, LoadAsync/WriteAsync/ValidateCandidate | Contextos propios; Submission y autoridad/ruta protegidas. Commit de PreQuote/Requirement/archivos verificados y enlaces de preparacion. Reconcilia incertidumbre con lectura independiente; no vuelve a mutar. Es el punto R de insercion atomica del job autorizado |
| Misma fuente, rama de preparacion existente | Enlaces/CreatedBy/fecha/error/archivos se contrastan, pero IsSuccess historico NO es certificacion universal de current, evidencia completa, ausencia de jobs o permiso automatico. Cambio de owner puede producir InconsistentLinks; no reparar para emitir delegacion |
| `Domain/PreQuotes/RequirementProcessingJob.cs`, Create/CreateForVerifiedPublication | Factory general requiere v1 por defecto; factory explicita crea Queued v2. PublicPreparation existe como trigger. Crear job no ejecuta Claim ni crea intento. Busqueda en Application/Infrastructure/Api no encontro llamadas productivas a estas factories |
| `Infrastructure/Persistence/Configurations/RequirementProcessingJobConfiguration.cs` | Unico ExecutionRequestId, unico intento y unico job activo por Requirement. NO hay unicidad permanente de intencion automatica por Submission: el indice activo no impide otra tras terminal |
| `Application/PreQuotes/ProcessRequirement/SuperviseAssignedExecutionService.cs`, ExecuteAsync | Actor y ClaimInitialLease explicitos. Claim una vez; solo Claimed compatible entra al coordinador. AlreadyApplied no reanuda. Control, ciclo Renew y joins existentes; sin selector ni emisor de autorizaciones |
| `InitialLeaseClaimStore`, `AssignedJobStartStore`, `ActiveLeaseRenewalStore`, `AssignedExecutionObservationStore` en `Infrastructure/Persistence/Repositories/` | Usuario/owner vivos, identidades y guards transaccionales; Start v2 tambien valida Claim/actor. No son autenticacion de un proceso externo. Un Guid recibido de codigo Backend confiable sigue sin ser una credencial |
| `ExecutionDocumentPreparationStore.cs` y `.Consumption.cs`, mismo directorio | Releen ruta/job/intento y documentos bajo locks; registro/reserva real y copias verificadas. No mantener la transaccion durante proveedor/E/S. Estos puntos tambien necesitan consumir la futura delegacion |
| `PrepareJobPublicationService` y `CoordinateAssignedExecutionService`, `Application/PreQuotes/ProcessRequirement/`; `JobPublicationStore`, Infrastructure | Preparar propuesta no concede permiso; publicacion v2 final valida evidencia, actor, estado, lease y solicitud bajo protocolo DB. Resultado RequiresReview no es confirmacion comercial |
| `JobTerminationStore`, `ExecutionClosureObservationStore`, `ExpiredCancellationStore`, Infrastructure | Request, A y B conservan autoridad de cierre del owner vigente, distinta de autoridad historica para ejecutar. B no acredita fin remoto ni autoriza otra ejecucion |
| `Api/Program.cs`, `Api/Authentication/CurrentUser.cs` | JWT Bearer y principal HTTP; ICurrentUser obtiene sub/rol. No hay credencial existente de worker que pueda deducirse de CreatedBy. DI actual registra intake/preparacion, no consumidor productivo del supervisor explicito |
| `Api/Controllers/RequirementsProcessingController.cs`, `Application/PreQuotes/ProcessRequirement/ProcessRequirementService.cs` | Entrada manual autenticada existente, distinta del intake anonimo. La historia gestionada por jobs no se elude por esta entrada |
| `Application/PublicExperience/ProcessPublicExperienceRequirementService.cs` | Adaptador con actor explicito y ownership que usa ProcessRequirementCoreService legacy; no es el supervisor v2. NO conectarlo al POST anonimo pasando el GUID configurado |

Tres commits distintos hoy: incorporacion -> Client/Project -> preparacion
documental. El commit del tercer paso es el evento emisor recomendado. No insertar
la intencion despues desde el controller: un corte entre ambos commits dejaria
preparacion confirmada sin intencion. El ejecutor posterior no debe depender del
CancellationToken, HttpContext, sesion ni vida del request original.

## 2. Solucion principal recomendada

**Politica Backend focal `ExperienceInitialExtraction/v1`, delegacion ligada al job
inicial y ejecutor alojado en el mismo Backend.** No tabla generica de grants,
cuenta Admin, token personal, broker/outbox adicional ni servicio remoto nuevo.

Quien autoriza es la politica de aplicacion aprobada y habilitada por el operador
del despliegue, no el visitante anonimo ni una aprobacion comercial del owner. El
evento es una preparacion NUEVA validada que confirma su grafo documental. La
politica delega exclusivamente esa extraccion bajo la responsabilidad del owner
actual activo. El owner se registra como actor vinculado; no se simula que solicito
personalmente el proceso. `RequestedByUserId` permanece null para esta intencion.

Tres conceptos separados:

| Concepto | Prueba / alcance |
|---|---|
| Identidad de negocio | Project.CreatedByUserId, actor historico del Claim y RequestedBy describen relaciones/autoria; ninguno autentica al ejecutor |
| Autoridad delegada | Registro focal emitido atomicamente con el job por la politica Backend; exacto Submission interno/Requirement/job/ExecutionRequest y actor vinculado |
| Identidad tecnica / credencial | Componente alojado confiable dentro del proceso Backend, compuesto privadamente y con acceso DB del Backend. InstanceId identifica una invocacion, no la autentica. No se emiten JWT de comerciales |

La procedencia se acredita mediante el unico camino de escritura autorizado,
version de politica, ruta persistida y admision controlada; no mediante un booleano
del DTO publico. Un permiso interno no serializable se emite solo tras admision
confirmada y se consume dentro del Backend. Constructor restringido y ausencia de
binding HTTP son fronteras de composicion, no criptografia ni defensa contra codigo
Backend hostil, reflection, DB comprometida o SQL arbitrario.

No exponer endpoints que acepten ese permiso o un actor por parametro. Un worker
en OTRO proceso requeriria autenticacion de servicio y protocolo propios; queda
fuera de esta solucion. No interpretar un GUID configurado como ese mecanismo.

## 3. Persistencia minima y emision atomica [R]

Ampliar el job existente, no crear una segunda cola con el mismo estado. Se
proponen columnas aditivas null para filas manuales/historicas. Sin backfill.

| Dato propuesto | Justificacion y regla |
|---|---|
| AutomaticAuthorizationPolicyVersion | Valor 1 identifica politica focal y origen automatico; no es un permiso generico. Solo PublicPreparation, v2, SubmissionId no null y RetryOfJobId null |
| AuthorizedActorUserId | FK RESTRICT a usuario: owner vinculado al emitir; inmutable. No sustituye credenciales ni equivale a RequestedBy |
| AuthorizedAtUtc / AuthorizationExpiresAtUtc | Tiempo DB de emision y limite finito de nuevas operaciones; UTC/microsegundos, finitos y expires > authorized. 24 horas aprobadas por D2, no renovables; independiente de lease120s y cadencia30s |
| AuthorizationRevokedAtUtc | Nullable, monotono null->fecha DB, nunca se limpia. Revoca delegacion, NO crea Request ni terminal. No motivo libre ni maquina de estados adicional necesaria para la seguridad minima |
| DispatchAdmittedAtUtc / DispatchInstanceId / DispatchClaimOperationId | Los tres null o presentes; marca durable de UNA admision al supervisor, con identidades Backend estables. Una vez fijados no se reciclan. Impide que un reinicio convierta Queued ambiguo en un reintento automatico |

JobId, RequirementId, SubmissionId interno, ExecutionRequestId, CorrelationId,
trigger y version ya existen: reutilizarlos, sin duplicar Project/Quote ni hashes
que ya se prueban recorriendo la ruta y evidencia. Fingerprint de incorporacion
prueba idempotencia de contenido, no autoria. ClaimOperationId de dispatch se fija
ANTES del Claim y por eso no puede exigir FK a un recibo aun inexistente.

Agregar UNIQUE parcial de SubmissionId donde AutomaticAuthorizationPolicyVersion
no sea null, PERMANENTE incluso tras terminal, ademas de indices/checks existentes.
Una Submission solo origina esta intencion inicial. Tambien verificar bajo locks
historia previa del Requirement: no adoptar un job antiguo sin delegacion ni crear
otro automatico sobre esa historia. UUIDs no vacios; grupos null completos; fechas
finitas y coherentes. EF protege campos de emision inmutables; admision/revocacion
solo por updates condicionados. FK/CHECK no prueban procedencia ni autoridad.

En `PublicExperienceRequirementPreparationRepository.WriteAsync`, solo en la rama
de candidato nuevo: validar evidencia/destinos/ruta y politica; tomar tiempo DB;
crear job mediante CreateForVerifiedPublication con trigger PublicPreparation,
ExecutionRequestId/CorrelationId nuevos Backend, y delegacion vinculada. Confirmar
en la MISMA transaccion que Quote/Requirement/files/enlaces. No proveedor aqui.

Si politica activa y falla emitir/insertar, rollback del conjunto de preparacion
DB; no degradar silenciosamente a preparacion exitosa sin intencion. La propiedad
y limpieza de destinos siguen el protocolo existente de storage/commit incierto;
no purgar archivos para averiguar si hubo commit. Si politica apagada, comportamiento
previo sin job; esos preparados no se incorporan retroactivamente al encenderla.

La reconciliacion de preparacion debe ampliarse a comprobar el job/delegacion
confirmados ademas del candidato documental. Recibo HTTP perdido no crea nuevas
identidades al repetirse: leer el ganador bajo la misma Submission; nunca regenerar
job para reparar incertidumbre. Si no se demuestra, informar incertidumbre y no
mutar otra vez. Un commit real puede ser descubierto por el ejecutor aunque el
caller HTTP no lo conozca; eso es la primera ejecucion de una intencion durable,
no reenvio de una extraccion ya iniciada.

## 4. Admision y limites del ejecutor [R]

El componente futuro de admision descubre exclusivamente Queued automaticos sin
marca de dispatch. Descubrimiento no autoriza. En contexto/transaccion propios,
orden global Submission/User/Client/Project/Quote/Requirement/Job, revalida ruta,
usuario/owner activo, contexto activo, current/Pending, evidencia, politica y plazo,
sin solicitud ni intento ni historia incompatible. CAS fija las tres columnas de
dispatch si siguen null; IDs generados una vez por candidato local. Commit corto.

Solo el ganador que conoce su admision confirmada obtiene `ExperienceExecutionPermit`
interno y llama UNA vez al supervisor, fuera de esa transaccion, con ClaimOperationId,
InstanceId, ExecutionRequestId, JobId y actor exactos. No invoca proveedor por fuera
del coordinador. Claim sigue siendo la exclusion durable de asignacion; la marca
no es Claim, lease, permiso permanente ni prueba de que el supervisor empezo.

Si se pierde la respuesta del commit de admision, NO llamar supervisor ni repetir
mutacion automaticamente. Si el proceso muere entre admision y Claim, otro proceso
NO recupera esa admision. Puede quedar Queued marcado sin Claim: mostrar pendiente
de resolucion, no resetear marca ni fabricar Failed. Si la transaccion realmente
revirtio, una futura primera admision de una fila aun sin marca no repite Claim ni
proveedor; el caller incierto no inicia un bucle de retry para determinarlo.
Este coste de disponibilidad es D3, no una promesa de exactly-once remoto.

El host no invoca de nuevo un supervisor que devolvio NotStarted/Indeterminate,
OCE, fallo o Stop, ni uno interrumpido por shutdown. No reanuda Running. El ciclo
Renew serial aprobado no cambia a retry de negocio. Antes de Started no se inventa
intento; despues de Stop no se reabre control ni se reconstruyen paquetes.

Permisos: Claim unico, Start, preparacion/registro, reserva/consumo verificado,
preparacion de resultado, publicacion v2, Observe y Renew de ESA ejecucion, y rutas
normales de cierre que el coordinador ya admite conforme a sus guards. No crear
otros jobs, cambiar owner, editar originales congelados, renovar otras asignaciones,
pedir cancelacion para ocultar incertidumbre, invocar B, hacer A post-Stop, aprobar
selecciones/precios comerciales ni reenviar bytes por otro canal.

## 5. Revalidacion y cambios concretos necesarios [R]

No basta un wrapper que lea la delegacion y pase actorGuid a APIs antiguas. Agregar
una entrada interna tipada y propagar el contexto focal; los stores comparan con
persistencia bajo sus locks. Para jobs con politica automatica, entradas de
EJECUCION solo-actor sin permiso deben rechazar, aunque el GUID sea el correcto.
Para jobs manuales/v1 sin politica se conserva su contrato anterior.

| Componente existente | Cambio futuro acotado |
|---|---|
| SuperviseAssignedExecutionService / CoordinateAssignedExecutionService | Overload interno con permiso exacto; propagarlo por todas las etapas, sin duplicar algoritmo de supervision ni alterar Stop/joins |
| ClaimInitialLeaseService / InitialLeaseClaimStore | Validar delegacion, admision y actor bajo locks antes de Claim nuevo o divulgacion. ClaimOperationId/InstanceId deben coincidir con dispatch, sin generar sustitutos |
| StartAssignedJobService / AssignedJobStartStore y reconocimiento interno | Aplicar guard a Start v2 directo, reconocimiento y observacion de commit incierto; conservar guard H1 de Claim.Actor. No bypass mediante otra entrada |
| ObserveAssignedExecutionService/Store; RenewActiveLeaseService/Store | Autoridad actual y procedencia del permiso; delegacion vencida/revocada no habilita continuidad ni Renew. Mantener CAS, candidato propio y tokens independientes |
| IExecutionDocumentPreparation en ExecutionDocumentPreparation.cs / ExecutionDocumentPreparationStore, incluida Consumption | Propagar permiso a lectura, registro y reserva; verificar antes de admitir E/S/consumo y en escrituras finales. No transaccion sobre archivos/proveedor |
| ConsumeExecutionDocumentsService / PrepareJobPublicationService / JobPublicationStore | Contexto exacto, ninguna fabricacion de autoridad en mapper; validar permiso persistido en publicacion final junto a evidencia/lease/solicitud |
| JobTerminationService/Store, entradas del coordinador | Cierre automatico normal sujeto a su contexto interno y reglas existentes. Mantener separadas las entradas de accion explicita del owner para Request/A/B, sin exigirles la delegacion de ejecucion revocada |
| Repositorio de preparacion, dominio/config job, snapshot/migracion futuros | Emision atomica, unicidad permanente y campos focales; no mutar recibos Claim/Renew/cierre anteriores |

Los puntos autoritativos de documentos son ReadLockedAsync, RegisterAsync y la
reserva en `ExecutionDocumentPreparationStore.Consumption.cs`. No crear una capa
paralela que solo cambie nombres. Un validador especifico compartido puede evitar
duplicacion, pero no desplazar la comprobacion fuera de la transaccion.

En cada nueva mutacion automatica: comparar Job/Requirement/Submission/request,
actor vinculado=current owner activo, politica reconocida, dispatch exacto, no
revocada y plazo vivo por reloj PostgreSQL; despues los guards existentes de la
etapa. En guards SQL finales incluir plazo/revocacion sin usar reloj local como
autoridad. Ordenar locks como el protocolo existente, nunca Job antes de Project.

Reconocer historia NO es admitir una nueva etapa. Tras commit incierto se puede
leer el candidato propio exacto aun si entretanto vencio el plazo, siempre con
autoridad actual y correspondencias validas; no convertirlo en permiso renovado.
Revocacion no borra commits/recibos ni exige lease presente en terminal. El servicio
debe separar reconocimiento historico de elegibilidad nueva. Si no puede autorizar
la lectura o probar candidato, Indeterminate sin metadata; nunca exito inventado.

No usar observacion generica para reconstruir certeza del commit. Mantener checks
del token original tras reconciliacion y no sustituir los presupuestos independientes
existentes por el CancellationToken HTTP. Un permiso en memoria no evita una
revocacion posterior; el guard DB decide. Etapa ya admitida puede enviar bytes antes
de enterarse: no revocacion remota ni atomicidad memoria/DB/red prometidas.

## 6. Revocacion y cambios de owner [R]

Revocacion explicita por accion interna autenticada del owner vigente, con locks
de ruta y CAS de AuthorizationRevokedAtUtc; sin Admin ajeno. Puede bloquear futuras
etapas pero no cancelar comercialmente: Request sigue siendo otra accion voluntaria.
No terminal automatico al vencer autorizacion. El host solicita Stop cooperativo al
detectar invalidez; los guards rechazan nuevas mutaciones aunque tarde en observar.
Lectura historica autorizada y cierres A/B continuan disponibles por sus propias reglas.

Apagar configuracion detiene emision y nuevas admisiones, y solicita parada local;
NO equivale a revocar transaccionalmente todos los jobs en todos los procesos. Para
esa garantia usar revocacion persistida por job. Ningun callback/cliente que nunca
termina queda convertido por este contrato en drenaje con plazo garantizado.

| Momento del cambio A->B | Regla recomendada |
|---|---|
| Antes del commit de preparacion/job | Se revalida owner bajo locks. Un candidato nuevo puede emitirse para B activo si toda la ruta concuerda; si snapshot de A cambio, rechazar/releer en otra accion, no sustituir actor dentro de llamada. No existe Claim que adoptar |
| Preparacion historica sin job | No backfill desde replay ni reparacion de CreatedBy. Requiere politica historica separada, fuera del objetivo inmediato |
| Queued ya autorizado a A | B no adopta autorizacion; A pierde ownership. Invalidar/revocar, no cambiar AuthorizedActor ni crear job sustituto. Owner vigente puede Request explicito para cancelar Queued |
| Claim de A, antes de Start | A falla ownership; B falla Claim/delegacion. Stop sin intento ficticio, sin reasignacion ni nuevo Claim |
| Durante procesamiento | Mismas prohibiciones; conservar commits que ganaron serializacion. Nuevas etapas/guards deniegan, Stop cooperativo; bytes remotos pueden seguir procesandose. B puede observar para cierre y actuar A/B si sus condiciones propias cumplen |
| Despues de terminal | Historia/actor/recibos inmutables, lectura del owner vigente separada. No nuevo envio por cambio de owner ni por replay |

El codigo de Project inspeccionado no expone un mecanismo productivo de transferencia
de owner. Una futura transferencia autorizada debe revocar monotonicamente las
delegaciones afectadas en su misma transaccion y respetar orden de locks. Comparar
solo owner actual NO detecta A->B->A: no prometer revocacion irreversible por historial
de cambios sin ese escritor coordinado. Antes de permitir transferencias productivas,
exigir ese protocolo (D4). SQL manual arbitrario fuera de la garantia, como en los
bloques auditados; fixtures de transferencia no son una API de negocio implementada.

Inactividad de usuario/Client/Project deniega ejecucion conforme a cada store. La
excepcion comercial de inactividad para cancelacion A/B no se hereda a ejecutar ni
a replacement. Una invocacion detenida no renace al reactivar contexto. Para un job
aun no admitido puede evaluarse elegibilidad por primera vez mientras su autorizacion
siga viva/no revocada; no es recuperacion de una ejecucion previa.

## 7. Matriz de flujos propuesta

La politica D1-D4 esta aprobada; implementar los permisos operativos siguientes
sigue pendiente y no pertenece a I.1. Rechazos no devuelven metadata
operacional a actores no autorizados. El visitante anonimo solo tiene el contrato
de incorporacion/replay, nunca el permiso de ejecucion.

| Flujo | Autoridad y accion permitida | Efecto durable / repeticion | Prohibido |
|---|---|---|---|
| Envio nuevo valido y preparacion nueva | Politica Backend + owner/contexto activos + evidencia verificada | Commit unico preparacion/job Queued/delegacion; una admision posterior | Actor/IDs ejecutivos elegidos por cliente; llamar proveedor desde POST |
| Replay exacto | Intake verifica fingerprint/foto; repositorio reconoce enlaces | Mismo job/autorizacion, no nuevas identidades ni DML de emision | Otro job por request HTTP o invocar supervisor desde replay |
| Conflicto SubmissionId | Ninguna nueva autoridad; conflicto del intake | Preservar ganador, respuesta409 existente | Procesar payload perdedor |
| Preparacion confirmada, HTTP perdido | Politica ya durable; host puede admitir sin sesion | Job unico descubierto; replay lee ganador | Dependencia del token HTTP o segunda emision |
| Preparacion fallida antes de commit | No delegacion emitida | Sin job de esa preparacion; respuesta/error actual | Ejecutar desde archivos huerfanos o Pending por si solo |
| Preparacion incierta | Solo reconciliacion read-only del candidato completo | Commit real puede contener job; ausencia no demuestra rollback por si sola | Repetir escritura/job/proveedor para averiguar |
| Owner no resuelto/inactivo | Denegar emision/admision, conservar estado previo | Error existente o pendiente no admitido; no actor sustituto | Admin de servicio, elegir otro comercial silenciosamente |
| Owner cambiado | Tabla de seccion6 y revalidacion transaccional | Sin reescribir autor ni repetir ejecucion; cierre por owner actual separado | Adoptar Claim/autorizacion de A como B |
| Project/Client inactivo | No nueva ejecucion; cierre explicito segun A/B | Conservar job/intencion; Stop si habia ejecucion | Usar excepcion de cancelacion para ejecutar/reemplazar |
| Historico sin evidencia suficiente | No emision desde IsSuccess o fechas/enlaces solos | Sin job automatico/backfill; diagnostico separado | Fabricar evidencia, copiar autorizacion de otra version |
| Requirement reemplazado | Current requerido para mutacion nueva; historia autorizada legible | No job automatico para sucesor por esta politica | Reutilizar permiso, Claim o request anterior |
| Queued automatico sin dispatch | Validacion completa y CAS de primera admision | Una marca estable; perdedor no invoca supervisor | Tomar cualquier Queued manual/historico |
| Queued con dispatch / Claim incierto | Solo lectura y resolucion explicita posterior | Marca conservada aunque no haya Claim visible | Resetear marca o reintentar supervisor tras reinicio |
| Running existente | Solo invocacion original aun viva, permiso/guards vigentes | Renew/etapas propias o Stop; lectura no reanuda | Nuevo host que adopte Running o segundo proveedor |
| Terminal existente | Lectura autorizada de historia, replays propios sin mutacion | Recibos/fechas/grafo intactos | Otro job por quedar libre indice activo, degradar terminal |
| Detenida/cancelada | Stop irreversible; Request voluntario y A/B separados | Running pendiente o terminal real segun accion confirmada | Inferir Cancelled de OCE, B automatico, nuevo envio |

Separacion de productos: (1) inicial automatico de nueva preparacion Experience
es el unico emisor de esta politica; (2) extraccion manual actual conserva login y
sus guards, sin permiso automatico implicito; (3) replacement conserva version y
policy propia, no hereda delegacion; (4) retry/recovery excepcional necesita decision
expresa nueva sobre identidad y posible duplicacion remota. Ninguno de los tres
ultimos se activa por descubrir un terminal o una lease vencida.

## 8. Consulta de estados para Frontend [R]

Primera entrega recomendada: consulta autenticada para owner vigente, reutilizando
JWT/ICurrentUser y revalidando usuario/ruta, sin bypass Admin ajeno. No ejecutar nada
al consultar. Separar incorporacion/preparacion, intencion Queued y admision, estado
durable job/lease, solicitud, outcome/publicacion y causas locales conocidas.

No persistir otro estado del job para presentar UI: Queued con dispatch no demuestra
que el supervisor este vivo; Running vencido no es Failed; ausencia de resultado
local tras restart es desconocimiento, no prueba de Stop. Succeeded observado no
certifica por si solo grafo publicado completo. La proyeccion de resultado publicado
debe leer evidencia/recibo con sus correspondencias, no inventarlo desde un booleano.
No mostrar leaseOwner/IDs internos como capacidades reutilizables.

No habilitar polling anonimo por SubmissionId o enlaces recibidos: no son secretos
de autorizacion. Un seguimiento publico detallado requeriria contrato distinto de
acceso/capacidad opaca, minimizacion y caducidad. No es necesario para ejecutar
automaticamente; queda fuera de esta recomendacion y de su activacion inicial.

## 9. Decisiones realmente nuevas

Actualizacion 2026-10-28: D1-D4 APROBADAS por el usuario, incluidas24h no
renovables, revocacion irreversible, admision unica sin redispatch, actor fijo,
solo preparaciones nuevas y sin backfill. La tabla conserva recomendaciones y
alternativas de la propuesta original; no son preguntas nuevamente pendientes.

No aprobacion global D1-D9 de contratos anteriores. Estas D son locales a este
documento y no reabren Stop, cierre A/B, ownership, no adopcion o automatismo aprobado.

| Decision aprobada | Solucion aprobada y efecto | Alternativa historica / coste |
|---|---|---|
| D1. Delegacion e identidad tecnica | Politica focal Backend, job ligado a owner, permiso interno y ejecutor mismo proceso; metadata aditiva sin backfill | Worker separado exige autenticacion de servicio/protocolo adicionales; no sustituirlo por GUID/token personal |
| D2. Validez/revocacion | 24h desde emision DB, no extensible; revocacion persistida por job, sin Request implicito; cifra inicial NO medida | Plazo diferente debe aprobarse/medirse antes de activar. Sin caducidad dejaria intenciones ejecutables indefinidamente; no recomendado |
| D3. Una admision durable sin retomarla | Marker previo al Claim; tras crash/commit incierto no redispatch, aunque quede Queued. Priorizar no reenvio sobre disponibilidad | Recuperar admisiones precisaria otro contrato y pruebas de certeza; no habilitarlo como retry oculto |
| D4. Transferencia y historicos | Owner vigente antes de primera emision; actor fijo despues. Futuro escritor de transferencia revoca tambien para impedir ABA. Solo preparaciones nuevas con politica activa | Migrar preparados antiguos o reasignar delegaciones requiere politica explicita, evidencia y riesgo de reejecucion; no backfill automatico |

La consulta autenticada es el alcance recomendado de Frontend. Si producto requiere
consulta publica detallada, decidirla por separado, sin bloquear el automatismo ni
usar consentimiento comercial por proyecto como sustituto de seguridad.

## 10. Plan finito de implementacion y aceptacion

I.1 (estructura de dominio/EF/migracion/pruebas del bloque I) queda autorizado y
se detalla en seccion12. El resto necesita autorizacion de implementacion; no se
ejecuta aqui. Sin capas genericas sin consumidor: cada bloque termina en un camino
real ejercitado, y ninguna bandera se activa por haber generado una migracion.

### I. Delegacion focal consumida por ejecucion explicita

- Componentes: dominio/config del job, migracion aditiva, contrato interno de permiso,
  admision/revocacion y entradas de servicios/stores de seccion5. DI aun sin host.
- Persistencia: columnas/constraints/indice de seccion3; sin tabla de grants,
  backfill, cambio de estado ni modificacion de recibos anteriores.
- Pruebas: unidad forma/no conversion desde DTO; PostgreSQL upgrade e inmutabilidad;
  invocacion real de supervisor con permiso, entradas solo-Guid rechazadas para auto,
  v1/manual intactos, vencimiento/revocacion, reconocimiento incierto/terminal.
- Carreras: dos admisiones, revocacion contra Claim/Start/publicacion, owner transfer
  en ambos ordenes, nunca locks sobre proveedor. Recibo confirmado no degradado.
- Dependencias D1-D4. Terminado: delegacion realmente consumida bajo locks en todas
  las entradas pertinentes, sin bypass, pruebas focales y no consumidor automatico.

### II. Intencion idempotente en preparacion real

- Componentes: PublicExperienceRequirementPreparationRepository, servicio de
  preparacion y DTOs internos de resultado/reconciliacion si hacen falta; controller
  solo conserva significado de respuesta, no ejecuta supervisor. Reusa esquema I.
- Pruebas HTTP/PostgreSQL: preparacion nueva con archivos/evidencia reales -> un job
  v2 y delegacion; replay/conflicto/HTTP perdido; fallo antes decommit y commitreal
  perdido; politica apagada -> cero jobs; historia sin backfill; actor publico ignorado.
- Carreras: dos preparaciones de misma Submission, transferencia/inactividad entre
  snapshot y commit, job historico/Requirement sustituido. Un ganador sin duplicados.
- Dependencias I, D1/D4. Terminado: no ventana de preparacion nueva confirmada sin
  su job cuando politica activa; replays nunca crean otra intencion, sin host activo.

### III. Ejecutor alojado y supervisor

- Componentes: host focal futuro en Backend, scopes propios, descubrimiento y
  admision I, supervisor/coordinador reales; registros DI explicitos, off por defecto.
- Persistencia: ninguna cola adicional; consume marker existente, no registry local
  como exclusion durable. Sin nuevo proveedor/transportes ni retry de ejecucion.
- Pruebas: host con dos instancias, despacho unico, independencia del HTTP, pausas
  reales de copia/proveedor, Stop autonomo, no-extension, resultado/commit incierto,
  reinicio entre dispatch/Claim y durante ejecucion, shutdown con lectura en vuelo.
- Carreras: host perdedor sin recursos, Request/owner/revocacion/publicacion; locks
  observados con PIDs distintos cuando se afirme bloqueo. No sleeps como evidencia.
- Dependencias I-II y D3. Terminado: una llamada supervisor por admision confirmada,
  nunca recupera marcador/Running, drenaje observado, proveedor simulado una vez,
  sin aprobacion comercial manual ni ejecucion en proceso HTTP.

### IV. Lectura de estados

- Componentes: query focal Backend y endpoint autenticado usando convenciones de
  controllers existentes; Frontend consumidor en tarea separada aprobada. Sin nuevas
  escrituras o persistencia de un estado local fingido.
- Pruebas HTTP/DB: owner actual, viejo/Admin ajeno/inactivo, terminal con sucesor,
  marker sin Claim, Running detenido/desconocido, publicacion con evidencia y sin
  confirmacion comercial; cero DML/proveedor en consulta.
- Carreras: lectura consistente con Claim/terminal/transferencia; historia conservada.
- Dependencias II-III y alcance autenticado de seccion8. Terminado: estado fiel,
  no filtracion operacional ni capacidad de mutacion desde consulta; no API publica
  detallada improvisada.

### V. Validacion operativa y activacion controlada

- Componentes: configuracion de emision/host, runbook y metricas; no codigo de
  recovery escondido. Antes de activar medir capacidad/concurrencia, retraso de cola,
  margen de lease/cadencia, timeouts, drenaje, limites de carga anonima, coste proveedor
  y retencion de recibos. 24h/120s/30s no se presentan como mediciones productivas.
- Pruebas controladas: off conserva intake sin automatismos; on solo preparaciones
  nuevas; apagado detiene admisiones sin borrar commits; jobs ambiguos permanecen
  visibles; A/B solo por accion explicita autorizada, sin nuevo envio posterior.
- Carreras: cambio de bandera/admisiones en curso, apagado con commit/proveedor en
  vuelo y ausencia de revocacion remota. No promesa de shutdown acotado sin capacidad
  real del cliente y politica adicional aprobada.
- Dependencias I-IV, D1-D4 y revision de presupuesto/limites reales. Terminado:
  auditoria de integracion, procedimiento de pendientes y permiso expreso de
  activacion con reversa de emision/host, nunca restauracion destructiva de datos.

## 11. Integridad de la entrega documental original (2026-10-27)

Solo se agrega este documento. No codigo/tests/config/migraciones ni documentos
anteriores modificados. No builds/tests/DB/API/proveedor/procesos de ejecucion nuevos;
no commit/push/merge/despliegue/activacion. No se repiten auditorias cerradas.
No AGENTS aplicables encontrados en las rutas del repo/docs y ancestros consultados.

Baseline del worktree, incluidos untracked no ignorados: 962 rutas SHA256; rama
`feature/public-experience-intake`, HEAD
`56ace9c3845d2e3327a1ca811c27f8e1562e5a79`. Artefactos inicial/final y delta en
`C:/Users/USUARIO/AppData/Local/Temp/sng-experience-delegation-contract-20261027`.
Comprobacion de cierre: 962 anteriores byte-identicos, un documento nuevo, 963
totales, cero modificados/eliminados anteriores; rama/HEAD preservados y
`git diff --check=0`. El manifiesto final contiene el hash de este documento.
Sin evidencia runtime nueva: hechos contrastados por lectura, diseno propuesto
y aceptacion futura explicitamente separados.

## 12. I.1: modelo y persistencia (2026-10-28)

### Matriz previa a implementacion

Registrada antes de editar dominio/configuracion. No servicio/store ni permiso
ejecutable. Todas las fechas nuevas UTC/microsegundos exactos, sin normalizar.

| Forma / transicion | Regla estructural |
|---|---|
| Jobs anteriores/manuales/v1/v2 | Ocho campos null; factories y comportamiento anteriores preservados, incluida su generacion historica de JobId |
| Construccion automatica explicita | JobId/Requirement/Submission/request/correlation/actor no vacios aportados por caller; politica1, PublicPreparation/v2, RequestedBy/Retry null; CreatedAt=AuthorizedAt explicito, Expires=AuthorizedAt+24h; Queued sin lease/intento/dispatch/revocacion |
| Autorizacion | Cuatro campos de emision todos presentes o todos null. Actor fijo, fechas finitas y duracion exacta86400segundos; no renovacion |
| Admision local | Tres valores juntos; IDs no vacios, AuthorizedAt<=Admitted<Expires; no revocacion previa; una vez fijados no se sustituyen ni limpian. Segundo intento lanza incluso con mismos valores: no simula replay operativo |
| Revocacion local | Fecha>=AuthorizedAt; primera fecha se conserva. Repeticion valida es no-op, no sustituye fecha; permite vencida/admitida/terminal, no borra dispatch ni modifica estado/Request |
| Historia persistida | No condicionar metadata a Queued/Running/current o lease presente. Revoked>=AuthorizedAt; si dispatch existe, Revoked>=Admitted. No exigir revoked<expires; no exigir terminal ni Claim existente |
| Fronteras | CHECK valida formas/orden, no reloj vivo/autoridad/CAS/procedencia. Mutacion SQL puede saltar monotonia; sin triggers. Admission puede preceder a Claim, sin FK entre ambos |

La nueva factory no genera IDs ni consulta reloj. Las factories previas conservan
su contrato (incluido Guid.NewGuid historico); no se modifica legacy para imponerle
una firma nueva. La admision local no exige un estado actual especifico del job:
la elegibilidad Queued y exclusion concurrente pertenecen al futuro store. Tampoco
se convierte una materializacion con metadata en permiso para los servicios actuales.
Tiempo SQL: diferencia entre timestamptz = interval '24 hours', no suma de un dia
calendario dependiente de timezone. UTC original y submicrosegundos son validacion
de dominio; PostgreSQL normaliza/redondea inputs SQL y no certifica esos originales.

### Implementacion y evidencia por capa

R = runtime focal actual; S = inspeccion de fuentes/SQL. No se infieren garantias
por cantidad de casos. No se ha implementado ningun permiso operativo.

| Capa | Fuente/escenario y evidencia | Limite |
|---|---|---|
| Dominio [R/S] | `RequirementProcessingJob.CreateForAutomaticExperience`, AdmitAutomaticDispatch y RevokeAutomaticAuthorization; 30 casos ExperienceAuthorizationTests. IDs/fechas explicitos, politica1, 24h/overflow, UTC/microsegundos, misma identidad entre espacios permitida; campos privados. Admission unica, revocacion conserva primera fecha y dispatch, terminal/revocada vencida validos | Las factories antiguas siguen generando su JobId como antes; la nueva no genera ninguno. No reloj vivo, autoridad, CAS ni efecto comercial |
| Compatibilidad [R] | PreviousFactories mas 16 RequirementProcessingJobTests existentes; rutas previas con metadata null y lifecycle conservado | No se repiten suites completas de Claim/Renew/supervisor/A/B |
| EF [R/S] | Cuatro casos EmissionEfThrow_ExactProperty_NoDml_IndependentRead: InvalidOperationException con propiedad precisa, contador DML0 y relecturas independientes de job/negocio. Admission/revocacion locales persisten sin cambiar emision | Throw protege actualizacion EF de emision, no DELETE/SQL/bulk. Monotonia concurrente de marcas requiere futuro store |
| SQL [R/S] | Checks_RejectPartialGroupsAndInvalidValues y AllFourEmissionFields: 14 grupos parciales de emision, seis grupos parciales de dispatch, UUID/policy/forma/fechas finitas, errores23514 con constraint exacta. NoAuthorization rechaza marcas sin emision. FK actor ausente/delete23503, nombre exacto y confdeltype=r | FK simple acredita existencia, no ownership/procedencia. Constraints activas; no triggers |
| Tiempo [R] | Roundtrip en UTC/America-New_York/Europe-Berlin:86400segundos. CalendarDayAcrossDst verifica82800segundos para dia calendario NY del8marzo2026 y rechazo23514; 24h transcurridas si pasan | SQL normaliza offsets/redondea fracciones: validacion del input original pertenece al dominio |
| Unicidad [R/S] | ActorForeignKeyRestrict_AndPermanentSubmissionUniqueness: primer job Cancelled, segundo job en OTRO Requirement de misma Submission rechazado23505/ux_processing_jobs_automatic_submission. Job historico null permitido | No afirmar exclusividad solo por indice activo del Requirement ni delegacion por identidad de Submission |
| Concurrencia [R] | ConcurrentInsert: ganadorPID27028 y bloqueadoPID7396, conexiones distintas; transaccion retiene INSERT, WaitBlocked observa Lock/pg_blocking_pids ANTES de commit. Un recibo de intencion (fila job), perdedor23505/indice automatic_submission; Queued sin dispatch/intento y negocio intacto | Unicidad estructural, NO admision operativa, Claim, autorizacion o emision atomica desde preparacion. Polling no usa delay como prueba de orden |
| Limites [R/S] | DirectSqlLimits puede fijar y limpiar marcas y borrar job por SQL valido; Business independiente intacto, sin Claim. Confirma ausencia de FK DispatchClaimOperationId->Claim | No proteccion frente SQL arbitrario; insert/materializacion no concede permiso |
| Upgrade [R/S] | UpgradeFromImmediatePredecessor, desde20261027195507: cuatro jobs v1/v2 Queued/Succeeded, dos intentos/grafos no vacios, ocho Claim/Renew, recibos de publicacion v1/v2 y un recibo cierre. Comparacion JSONB independiente antes/despues, metadata nueva null, HasPendingModelChanges=false | Historia estructural con constraints, no ejecucion real de cierre/proveedor. Fixture se pobla, baja al esquema anterior sin metadata y prueba upgrade; no DB habitual |
| HTTP [R/S] | Dos Post_CreatesPersonWithConfiguredOwner_ReplaysAndRejectsConflict existentes (DIRECT_CONTACT/ANDRES_CANDELA), expectativas intactas: Pending/cero jobs/automatismos/proveedor | No activacion; catalogos/proveedor/storage de HTTP simulados |

### Migracion, inventario y ejecuciones

`20261028131744_AddExperienceAutomaticAuthorization`: ocho columnas nullable sin
default, tres CHECK, FK actor RESTRICT y dos indices nuevos. Conserva indice previo
de Submission. Down elimina solo esos agregados. SQL regenerado en preflight/final
byte-identico, SHA256
`C1999713D0796D036AF8EB728EACE7D4DEF86F7E9D5D4A17EB90AC046007D710`.
Solo ALTER/CREATE necesarios y metadata INSERT de __EFMigrationsHistory; sin
backfill, DML de negocio, trigger/purga o cambio a migraciones anteriores.

La primera generacion EF proponia retirar el indice historico de Submission por
considerar suficiente el nuevo parcial. Se explicito el indice previo en config
y se ajustaron la migracion nueva/Designer/snapshot ANTES del preflight; SQL final
aditivo y modelo sin diferencias verificados. Ninguna migracion intermedia aplicada
a DB habitual. Primer intento de generacion fallo por sandbox al crear bin aislado,
sin tests: `migration-sandbox-denied.log`. Se repitio con permisos. No fallo de
codigo/compilacion ni prueba ocultado. Busqueda auxiliar con wildcard de ruta se
repitio focalmente; no constituye resultado de test.

| Corrida | Passed | Failed | Skipped | Duracion tests |
|---|---:|---:|---:|---|
| preflight.trx | 96 | 0 | 0 | 3.2540min |
| final.trx | 98 | 0 | 0 | 3.7152min |

Los96 nombres reaparecen; dos nuevos: AllFourEmissionFields y CalendarDayAcrossDst.
Final:30dominio nuevo +50PostgreSQL nuevos +16dominio job previo +2HTTP. No194 casos
distintos. Ademas se reforzo comparacion de evidencia/documentos en upgrade y nombre
exacto FK; ninguna expectation relajada ni codigo productivo cambiado entre corridas.
Rebuilds completos ExperienceAuthorization --no-restore --no-incremental: ambos
0errores/111warnings, preflight20.59s y final1:06.11. Warning-delta vacio respecto
ExpiredClosureAudit, normalizando coordenadas y deduplicando archivo/mensaje.
Solo documentacion cambio despues del rebuild final. No fallos de tests ni skips.

Nuevos (cinco): migracion y Designer anteriores;
`Tests/Domain/PreQuotes/ExperienceAuthorizationTests.cs`;
`Tests/Infrastructure/Persistence/ExperienceAuthorizationPostgreSqlTests.cs`;
`Tests/Infrastructure/Persistence/ExperienceAuthorizationMigrationPostgreSqlTests.cs`.
Modificados (cuatro): `Domain/PreQuotes/RequirementProcessingJob.cs`,
`Infrastructure/Persistence/Configurations/RequirementProcessingJobConfiguration.cs`,
`Infrastructure/Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` y este
contrato. Sin nuevo DbSet/tabla/cola, servicios o dependencias de ejecucion.

### Integridad, limpieza y frontera final

Artefactos, SQL/TRX/CSV, runners y manifiestos incluidos untracked:
`C:/Users/USUARIO/AppData/Local/Temp/sng-experience-authorization-model-20261028`.
963->968rutas:5nuevas/4modificadas/0ausentes,959anteriores byte-identicas. Rama
feature/public-experience-intake y HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79
preservados, sin restaurar HEAD; diff-check0. Fuentes excluidas y migraciones previas
intactas por manifiesto. No AGENTS aplicables encontrados en rutas/ancestros revisados.
Bin/obj ExperienceAuthorization ignorados; artefactos de comprobacion fuera del repo.

PostgreSQL17 propio127.0.0.1:55581,usuarioaudit; preflight/final-remaining=0 y ambos
server-status=no server running. Servidor parado y variable child-local retirada
en finally por corrida; cluster/logs retenidos, no proceso activo. Modelo no abre
archivos/proveedor; helpersHTTP conservan cleanup. Residuals vacio para sng-sealed,
sng-execution-docs y sng-receipt-unit. No barrido ni borrado de recursos ajenos.

Solo hay definiciones de factory/transiciones en dominio, sin llamadas productivas
en Application/Infrastructure/Api. Los stores actuales AUN NO consumen esta metadata;
no se certifica proteccion operativa por agregar columnas. Sin emisor/admission store,
permiso ejecutable, DI, worker, enqueue, endpoints, frontend ni nueva ejecucion.
Sin DB/API habituales, reinicios, proveedor real, commit/push/merge/despliegue.

**Modelo y persistencia de autorizacion Experience implementados; emision, admision
operativa, consumo del permiso y activacion pendientes.**

## 13. I.2: admision unica y revocacion transaccionales

### Contrato previo (2026-10-28)

Registrado antes de editar fuentes I.2. D1-D4 e I.1 permanecen intactos.
APIs de composicion Backend, sin endpoints, DI ni emisor automatico:

```csharp
Task<ExperienceAdmissionResult> AdmitAsync(Guid jobId, CancellationToken ct);
Task<ExperienceRevocationResult> RevokeAsync(Guid jobId, Guid actorUserId, CancellationToken ct);
```

ExperienceAdmissionService crea una sola pareja InstanceId/ClaimOperationId por
llamada antes de invocar el store una vez. El store recibe ese candidato estable;
no recibe actor para admitir: resuelve exclusivamente AuthorizedActorUserId de DB.
ExperienceExecutionPermit tiene constructor internal y propiedades solo lectura;
el servicio lo construye solo tras un retorno NewlyAdmitted confirmado. No es DTO
HTTP, no tiene constructor de binding ni consumidor en entradas de ejecucion.
Contiene Job/Requirement/Submission/request/correlation/actor/Instance/Claim,
politica y fechas de autorizacion/admision. Identidades no son credenciales.
La frontera es Backend confiable, no defensa contra reflection/codigo hostil/SQL.

Resultados locales separados de estados persistidos: Admission Admitted,
AlreadyAdmitted, Rejected, PersistenceError o Indeterminate; solo Admitted tiene
permiso. Revocation Revoked, AlreadyRevoked, Rejected, PersistenceError o
Indeterminate; solo los dos primeros tienen fecha. AlreadyRevoked observa historia,
NO reconoce autor/candidato propio. No recibo ni OperationId de revocacion.
Failures focales: InvalidRequest, NotFound, InactiveUser, InactiveContext,
IdentityConflict, MetadataConflict, StateConflict, HistoryConflict,
DocumentsConflict, Revoked, NotYetValid, Expired, TimeConflict. Sin nuevos codigos
terminales. NotYetValid/Expired proceden de la lectura DB protegida; TimeConflict
describe cero filas del CAS temporal final sin inventar el valor de su reloj ni
atribuirlo necesariamente a vencimiento (tambien puede retroceder el reloj).

| Situacion | Admision | Revocacion |
|---|---|---|
| Actor inactivo o no owner actual | Rechazo antes de metadata | Rechazo, sin bypass Admin |
| Owner nuevo distinto del actor vinculado | No adopta autorizacion | Puede revocar sin reescribir actor historico |
| Contexto comercial inactivo / terminal / expiry vencido | Rechazo para nueva admision | Permitido si ruta y metadata coherentes |
| Dispatch existente, mismos o distintos IDs | AlreadyAdmitted sin permiso ni DML | Conserva dispatch; revoca o reconoce primera fecha |
| Revocacion persistida | Rejected/Revoked sin permiso | AlreadyRevoked sin DML, primera fecha |
| Nueva admision elegible | CAS reloj DB, solo tres columnas, commit conocido y un permiso | No aplica |
| Error previo con rollback demostrado | PersistenceError, sin permiso | PersistenceError |
| Commit intentado incierto / rollback desconocido | Indeterminate, sin observacion que reconstruya permiso | Indeterminate, sin atribuir commit propio |
| Caller cancelado, incluso postcommit | OCE token original, sin permiso/compensacion | OCE token original, conserva fecha persistida |

Contextos propios; orden Submission/User/Client/Project/Quote/Requirement/Jobs,
Attempts y documentos relacionados. Discovery no autoriza. Releer ruta y ownership
bajo locks; consultar metadata documental completa sin abrir archivos. Admision
exige Queued v2 PublicPreparation/policy1, Pending/current, sin solicitud, lease,
inicio, intentos ni historia incompatible. CAS final PostgreSQL en intervalo
[AuthorizedAt,Expires), marcas null y no revocada. Revocacion CAS null->reloj DB
con fechas coherentes, sin exigir vigencia/current/Queued/contexto activo.
No SaveChanges, mutacion de negocio, retry, selector ni tareas abandonadas.

Revocacion que gana impide admision; admision que gana puede devolver permiso y
ser revocada despues: no supresion retroactiva ni permiso permanente. Project SHARE
serializa transferencia; no API de transferencia ni solucion ABA nueva. La futura
transferencia coordinada debe revocar. Entradas actuales aun NO consumen permiso:
I.2 no certifica proteccion integral contra bypass ni llama supervisor/Claim/A/B.

### Implementacion y evidencia focal

R = escenarios ejecutados en final.trx; S = inspeccion. No garantia derivada del
numero de pruebas. No se modificaron servicios/stores previos ni el modelo I.1.

| Propiedad | Evidencia R / S | Frontera |
|---|---|---|
| Permiso nuevo una vez | ExperienceAdmissionService genera dos Guid una vez e invoca store una vez [S]. Admission_ExactEffects compara tres columnas exactas, todas las tablas core/identity por conexiones nuevas, identidades/fechas del permiso y un DML; repeticion identica/distinta devuelve AlreadyAdmitted sin Confirmed/Permit ni DML [R] | Constructor internal, propiedades inmutables, System.Text.Json rechaza serializacion/deserializacion [R]. No credencial ni proteccion ante Backend hostil; ConfirmedExperienceAdmission es metadata, no permiso |
| Autoridad/ruta | Store descubre sin autoridad y relee bajo Submission/User/Client/Project SHARE; Quote/Requirement/Jobs ordenados UPDATE. Actor de admision solo desde emision persistida; owner activo revalidado. Rejections owner/inactive-user/route, OwnerTransferBothOrders [R]; comparaciones completas [S] | Sin actor elegido por caller de admision, Admin no es bypass. No API de transferencia ni solucion ABA |
| Elegibilidad | Metadata reconstruye forma I.1 sin persistir otro job, luego Queued/current/Pending/sin request-lease-inicio. Attempts ordenados UPDATE, cero intentos/otros jobs/recibos/propuestas. Rejections history/terminal/requirement y PriorAttemptRejected [R] | Extraccion pertenece a intento: conjunto vacio impide historia normal de ese grafo. No se desactivan constraints ni se fabrican combinaciones prohibidas |
| Documentos | RequirementFiles y SubmissionFiles SHARE ordenados; igualdad de conteos, secuencias unicas, nombre/MIME normalizados como preparacion, longitudes/hash/version/fechas presentes y enlaces completos [S]. documents/evidence/source-evidence/mismatch rechazan sin DML [R] | Metadata DB, no lectura fisica ni recalculo de bytes. No garantiza que un archivo no haya sido alterado fuera de DB |
| Forma/fechas | InvalidInput seis casos sin abrir conexion; future/expired, metadata/version [R]. Validacion completa por factory I.1 [S] | Policy automatica con version v1 la impide CHECK: caso version elimina primero la autorizacion y obtiene NotFound por ausencia de actor vinculado; NO se atribuye a ese test una corrupcion v1+policy1 aceptada |
| Reloj final | CAS WITH clock MATERIALIZED, tres columnas solamente, [Authorized,Expires), no revocada/marcas null/Queued e identidades exactas, sin SaveChanges [S]. Waits observa viva, Lock/pg_blocking_pids y reloj DB vencido antes de release [R] | FinalGuardZero fuerza predicado imposible por interceptor: una sentencia sin filas, TimeConflict, snapshot intacto [R]. No simula retroceso fisico de reloj ni deduce su valor despues |
| Revocacion | Siete ExactEffects: plain/admitted/expired/terminal/project/client/new-owner. Solo fecha cambia, preserva dispatch/historia/actor; repeticion AlreadyRevoked misma fecha y cero DML [R] | Current/Pending y contexto activo no son requisitos para retirar autoridad. No Request, Stop, A/B ni permisos de replacement |
| Rechazos revocacion | other/admin/inactive/missing/route/metadata/future: failure preciso, fecha null, snapshots completos [R] | Future ejecuta una CTE sin filas; no se llama cero comandos SQL. Los demas rechazos medidos no escriben |
| Incertidumbre | PostWriteRollback seis casos: reader dispuesto tras SQL real, IOException, rollback conocido o conexion cerrada que impide establecerlo; PersistenceError versus Indeterminate [R]. CommitLoss cuatro: interceptor DESPUES de commit real, caller normal/cancelado y ambas operaciones [R] | Sin observador en estas operaciones. Unico DML, sin permiso/fecha ante incertidumbre; OCE token original aun con marca/fecha persistida. Lectura independiente del test no reconstruye certeza del caller |
| Concurrencia | Races same/different/admit-revoke/revoke-admit/revoke-revoke y OwnerTransferBothOrders [R]: conexiones/PIDs distintos, barrera precommit y WaitBlocked consulta Lock/pg_blocking_pids antes de liberar. Perdedor sin nuevo permiso, primera fecha conservada, snapshots independientes intactos salvo campos esperados | Admision que gana puede emitir permiso antes de revocacion posterior; no revocacion retroactiva. Transferencia SQL de fixture, no API productiva |
| Compatibilidad/no activacion | 30 ExperienceAuthorizationTests de dominio, cuatro EmissionEfThrow y LocalMarksPersist; dos HTTP DIRECT_CONTACT/ANDRES_CANDELA Pending/cero jobs-attempts-evidencia ejecucion-recibos lease/cierre-proveedor [R]. Consumers solo definiciones y composicion nueva [S] | Ninguna suite completa Claim/Renew/supervisor/A/B; entradas actuales no consumen permiso, sin garantia integral contra bypass |

Los snapshots recorren todas las tablas base de core/identity y ordenan JSONB por
contenido en una conexion nueva; no son solo conteos. Se excluyen SOLO las columnas
esperadas en efectos positivos y se verifican sus valores aparte. En transferencia
se excluye tambien Project.created_by_user_id y se comprueba nuevo owner exacto.
No consultas de snapshots mientras una transaccion no confirmada pretende acreditar
su propio resultado. Probe cuenta Reader async WITH clock/INSERT/UPDATE/DELETE;
es suficiente para el store (solo Reader SQL, ningun SaveChanges), no monitor SQL
universal. ConnectionProbe acredita cero aperturas en entradas invalidas/caller
cancelado antes de entrar. Los locks salen con await using antes de retornar.

### Ejecuciones e incidencias conservadas

| Corrida | Passed | Failed | Skipped | Minutos TRX |
|---|---:|---:|---:|---:|
| preflight3.trx | 0 | 46 | 0 | 2.7186 |
| second.trx | 60 | 0 | 0 | 3.1388 |
| final.trx | 98 | 0 | 0 | 4.4987 |

No sumar corridas. Final = 61 casos I.2 +37 regresiones focales (30 dominio,
5 PostgreSQL modelo y2HTTP). Los46 y60 nombres anteriores reaparecen en final;
38 adiciones respecto a second = guard final nuevo +37 regresiones. Entre second
y final se reforzo snapshot de transferencia; no se relajaron expectativas.

Los46 fallos preflight3 fueron23505 IX_public_experience_submission_files_submission_id_sequence
en AdmissionSetup: Create ya incluia una fuente legacy secuencia0 y se agregaba
otra. Se sustituye SOLO esa fuente de la fixture por evidencia completa antes de
medir el store; FK/CHECK/indices activos. Copia anterior y TRX/failures conservados;
no se presenta ningun Failed como Passed retrospectivo. No fallo de guards I.2
reproducido por ese error de preparacion. Otros14 casos se incorporaron en second.

Builds fallidos previos, sin tests sobre ellos: build1 1error/16warnings (Trigger
en lugar de TriggerKind); build2 1/111 (namespace Identity de test); preflight
3/111 (namespace Repositories ambiguo y dos errores derivados); preflight2 1/16
(RequirementExtractionResult no tiene RequirementId, su vinculo es el intento).
Se corrigieron referencias nuevas, no legacy. preflight3/second/final completos
0errores/111warnings; duraciones55.28s/13.70s/26.91s. Config ExperienceAdmission,
--no-restore --no-incremental, tests --no-build. Warning-delta vacio contra
ExperienceAuthorizationAudit normalizando coordenadas/deduplicando archivo-mensaje.
Solo documentacion cambio despues del rebuild final.

### Inventario, integridad y limites

Nuevos (cinco):
- Application/PreQuotes/ProcessRequirement/ExperienceAdmissionService.cs
- Infrastructure/Persistence/Repositories/ExperienceAuthorizationStore.cs
- Tests/Infrastructure/Persistence/ExperienceAdmissionPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ExperienceAdmissionCoordinationPostgreSqlTests.cs
- Tests/Infrastructure/Persistence/ExperienceAdmissionBoundaryPostgreSqlTests.cs

Un existente modificado: este contrato. Artefactos/manifiestos/TRX/CSV/scripts en
`C:/Users/USUARIO/AppData/Local/Temp/sng-experience-admission-20261028`.
968->973 rutas SHA256 tracked/untracked no ignoradas:5nuevas/1modificada/0ausentes,
967anteriores byte-identicas. Branch feature/public-experience-intake y HEAD
56ace9c3845d2e3327a1ca811c27f8e1562e5a79 preservados; diff-check0. Modelo, EF,
migraciones, helpers previos, HTTP, DI y todas las entradas de ejecucion intactos.
Sin restauracion desde HEAD. Bin/obj ExperienceAdmission ignorados son salidas
de build, no fuentes adicionales. Sin AGENTS aplicables encontrados.

PostgreSQL17 propio127.0.0.1:55585, audit. Cada corrida detiene servidor en finally,
retira variable child-local y registra cero bases de fixture restantes. Cluster
parado/logs retenidos; final-server-status=no server running. Sin storage fisico
en I.2; fixtures documentales son metadata, HTTP usa dobles y cleanup existente.
Residuals vacio, sin barrido/borrado de temporales ajenos. No DB/API habituales,
proveedor real, emisor productivo, migracion nueva, DI/endpoints/host/worker/enqueue,
frontend, commit/push/merge/despliegue ni activacion.

Solo Windows/PostgreSQL17. Interceptores prueban limites de commit/rollback, no
crash/red fisica/fsync/consumo remoto. No SLA nuevo ni deadline global: caller y
driver gobiernan espera; rollback dispone presupuesto independiente de limpieza.
D3 no observa ni reintenta para recuperar permiso. Revocacion incierta no atribuye
autor/commit; otra llamada explicita puede observar primera fecha sin atribuirla.
Sin integracion con ejecucion ni nueva promesa exactly-once. Transferencia ABA,
hosting/capacidad/retencion y presupuestos medidos siguen pendientes.

**Admision unica y revocacion transaccionales implementadas;
consumo del permiso, emision desde Experience y activacion pendientes.**

## 14. I.3: permiso en ejecucion explicita

Implementacion de consumo verificada, 2026-10-28. No emisor, host, DI nueva
ni activacion. Las secciones anteriores conservan la evidencia historica de I.1
e I.2; sus limites de consumo pendiente describen esos cierres, no nuevos grants.
Matriz previa externa: `sng-experience-permit-20261028/entry-matrix.txt`.
Artefactos de implementacion y corridas separadas:
`C:/Users/USUARIO/AppData/Local/Temp/sng-experience-permit-resume-20261028`.

### Contexto y fronteras

Las solicitudes internas ClaimInitialLease, AssignedJobExecution,
AssignedExecutionObservationRequest y RenewActiveLease transportan la referencia
tipada `ExperienceExecutionPermit? Permit`. No convierten metadata en permiso.
Constructor/serializacion/admisor I.2 no cambiaron. El overload del supervisor
recibe el permiso genuino y usa InstanceId/ClaimOperationId admitidos; no los
regenera. El coordinador conserva su algoritmo y propaga el mismo contexto.

ExperienceExecutionGuard compara, dentro de la transaccion que ya autorizo la
ruta, job/Submission/Requirement/request/correlation/actor/policy/fechas/dispatch.
Para una fila no automatica exige ausencia de permiso y conserva su protocolo.
Para una automatica exige permiso exacto, v2/PublicPreparation y actor vinculado.
Usuario activo/ownership y ruta permanecen bajo los locks propios de cada store.
LiveAsync usa reloj PostgreSQL; solo se aplica para admitir operaciones nuevas.
Guardas mutantes finales cotejan metadata de autorizacion/dispatch, revocacion y
vigencia, ademas de sus predicados anteriores. No SaveChanges posterior al guard.

AuthorizationConflict identifica rechazo del contexto de ejecucion (ausencia,
discordancia, revocacion o vigencia); no es nuevo estado/codigo terminal ni
pretende revelar metadata al caller no autorizado. InactiveUser/NotFound y las
precedencias existentes de identidad/version se conservan. Un guard final puede
devolver el conflicto de fencing existente sin atribuir una causa temporal exacta.

| Entrada autoritativa | Operacion nueva / guard | Historia y alternativa explicita |
|---|---|---|
| Supervisor/coordinador | Overloads tipados; sin duplicar algoritmo; contexto en todas las llamadas | AlreadyApplied/AlreadyStarted no reanudan. El wrapper no sustituye stores |
| InitialLeaseClaimStore.RunAsync | Matches antes de prior; IDs de Claim exactos; LiveAsync y CTE final | Reconciliacion conserva permiso, candidato completo y token independiente; no exige vigencia para reconocer historia |
| AssignedJobStartStore.RunAsync | Matches y Assignment antes de reconocer; LiveAsync antes de crear intento; UPDATE final | StartVerified, StartAsync v2 y Recognize comparten guard H1. Terminal no exige lease liberada. Caller automatico conserva OCE original |
| AssignedExecutionObservationStore.ReadAsync | Matches y vigencia antes de snapshot operacional | No reconocimiento de commit propio; lectura explicita para cierre sigue en su store propio |
| ActiveLeaseRenewalStore.RunAsync | Permiso/Claim/owner exactos; LiveAsync y CTE con CAS existente | Replay y observador independiente conservan permiso/candidato; no habilitan otro Renew tras revocacion |
| ExecutionDocumentPreparationStore.ReadLockedAsync/ValidateLiveAsync | Procedencia antes de evidencia y vigencia antes de abrir fuentes | Existing devuelve historia sin paquete. Reconciliacion compara candidato y no vuelve a copiar |
| RegisterAsync | Relee con mismos guards y fencing final tras evidencia | Rollback de registro si pierde guard; no locks sobre copia |
| AuthorizeConsumptionAsync | ReadLockedAsync y ValidateLiveAsync antes de abrir lectores/proveedor | Reserva local sigue siendo de un solo ganador; no es permiso DB |
| AuthorizePreparationAsync | Lectura focal del resultado verificado, evidencia exacta y guards de ejecucion | Libera scopes antes del builder; no persistencia nueva ni mapper autorizado global |
| PrepareJobPublicationService | Resultado verificado conserva ejecucion; requiere validador para permiso automatico | Transformaciones genericas/manuales no conceden publicacion. Ausencia de validador automatico rechaza |
| JobPublicationStore.RunAsync | Matches antes de historia, LiveAsync para nuevo resultado y UPDATE final | Reconocimiento exacto del candidato v2 sigue permitido tras revocacion; no deriva Published de observacion generica |
| FinalizeExecutionAsync(ExecutionTermination) | Contrato separado usado por coordinador; Matches/LiveAsync/guard final y contexto en reconciliacion | Sin permiso no cae al Confirm explicito; Request/Confirm owner, observacion de cierre, A y B conservan autoridad propia |

`explicitOwnerConfirm` es una condicion SQL derivada PRIVADAMENTE de la entrada
explicita elegida, no parametro de autorizacion ni bool suministrado por caller.
FinalizeJobFailed directo tambien exige permiso para jobs automaticos. El
coordinador no invoca Request, A o B ni el Confirm explicito como fallback.
Una CancellationRequested detectada en la lectura de preparacion conserva la
ruta de Confirm del coordinador, con gate y contexto de ejecucion; revocacion o
expiry no habilitan ese cierre. Stop ganador impide nuevas etapas, no revoca
retroactivamente una etapa admitida. No atomicidad memoria/DB/red ni fin remoto.

### Evidencia y limites de interpretacion

Pruebas nuevas usan admision I.2 REAL, no constructor de permiso. Fixtures preparan
job y evidencia de incorporacion; no son emision productiva desde Experience.
Archivos de Requirement/copias y PostgreSQL son reales, proveedor/catalogos
simulados. Fuente de incorporacion de fixture es metadata; no nueva verificacion
fisica de esa fuente por I.2. Pruebas y resultados definitivos se cierran abajo.

| Escenarios | Propiedad contrastada |
|---|---|
| ExperiencePermit_Integrated_AdmissionRenewPublicationAndNoResume, copia/proveedor | Admision/supervisor/Claim/Start/documentos/consumo/preparacion/publicacion v2; Renew mientras barrera cerrada, mismo permiso, extension efectiva, un proveedor, replay sin retomar |
| ExperiencePermit_AuthoritativeEntries_AbsentForeignRevoked, nueve entradas | Cada nombre recorre ausencia, permiso ajeno genuino, revocacion real, inactivo, owner cambiado, dispatch incompatible y expiry observado DB. Failure exacto, snapshot independiente igual, cero nuevas copias/proveedor/DML |
| ExperiencePermit_RevokeVersusClaimStart_PostgreSqlOrder | Ambos ordenes para Claim y Start; conexiones/PIDs distintos y pg_blocking_pids antes de liberar, sin revocacion retroactiva |
| ExperiencePermit_RevokeVersusPublication_BothPostgreSqlOrders | Publicacion y revocacion reales, orden serializado; proveedor ya consumido no se repite |
| ExperiencePermit_OwnCommitRecognizedAfterRevocation_NoNewStage | Claim/Start confirmados y respuesta perdida tras revocar; reconocimiento sin nueva etapa, actor-only no divulga recibo de Start |
| ExperiencePermit_PublicationOwnCommitAfterRevocation_ExactReceiptAndNoRedispatch | Recibo de publicacion propio, hash/fecha persistidos, no Published fabricado por observador |
| ExperiencePermit_StartExpiresAfterSave_FinalGuardRollsBack | Expiry real de autorizacion despues de SaveChanges, reloj DB, rollback entero de intento/Requirement/actividad; siguiente Start rechazado |
| ExperiencePermit_RevokeDuringCopyOrProvider_NoPublication | Originales/commits anteriores preservados; registro o preparacion rechazan sin publicacion nueva |
| ExperiencePermit_AutonomousStopAfterRevocation_DrainsOutstandingReader | Tick/observador causan Stop, lectura real no cooperativa; handles vivos hasta liberar lectura, luego disposal; harness no decide Stop |
| ExperiencePermit_CallerCancelledAfterCommit_PreservesOriginalToken | Claim/Start commit real, OCE original despues de observador, Running conservado |
| ExperiencePermit_NewOwnerCannotExecute_CanExplicitlyClose | Actor original pierde ownership, nuevo owner no adopta permiso; Request/lectura cierre/Confirm explicito o B siguen disponibles con autorizacion revocada |
| ExperiencePermit_CoordinatorFinalizer_ValidPermitAndBusinessRequest | Fail normal y cancelacion persistida usan cierre tipado del coordinador con permiso vivo; revocacion no fabrica terminal |
| ExperiencePermit_ClaimCannotReplaceAdmittedIdentities | InstanceId/ClaimOperationId sustitutos rechazados antes de DML |

Los snapshots CombinedSnapshot no incluyen toda autoridad Project/User; las
mutaciones de fixture de autoridad se efectuan antes de medir y los guards se
contrastan tambien por inspeccion. Permiso ajeno se obtiene de otra preparacion
real y se conserva antes del reset de fixture, no de dos jobs coexistentes.
Los contadores reconocen Reader/NonQuery y prefijos mutantes de estos stores,
no SQL arbitrario. Expiry15s es autorizacion de24h emitida cerca de su limite,
no cambio de politica. SQL de fixtures conserva constraints activas.

Regresiones focales: Claim atomico/replay; Renew formula/replay; Start v1; estados
de observacion; H1 directo; documentos manual/publico; coordinador exitoso e
incertidumbre; finalizacion v1/v2; transferencia/cierre A; lectura en vuelo; grafo
publicacion v1 y dos HTTP Pending/sin automatismos. No suites completas historicas.
Comparaciones completas del permiso, ausencia de locks durante E/S y conservacion
de rutas A/B se respaldan ademas por inspeccion; no garantia por numero de casos.

Inventario: seis nuevos (ExperienceExecutionGuard y cinco partials ExperiencePermit);
diecinueve fuentes Application/Infrastructure modificadas y este contrato.
Modelo, EF, migraciones, I.2, DI y helpers anteriores no modificados.
No emisor/selector/host/worker/enqueue/endpoints/frontend/proveedor real.

### Cierre runtime y trazabilidad

Todas las filas de escenarios anteriores tienen evidencia runtime aprobada por
nombre en `latest-cases.csv`. No todas pertenecen a una misma corrida. La tabla
de entradas describe codigo inspeccionado ademas de esos escenarios: no equivale
a inyectar cada campo corrupto ni a certificar cada carrera imaginable.

| Corrida de esta implementacion | Passed | Failed | Skipped |
|---|---:|---:|---:|
| integrated.trx | 2 | 0 | 0 |
| focal.trx | 11 | 9 | 0 |
| verification.trx | 69 | 0 | 0 |
| final.trx | 75 | 2 | 0 |
| closure.trx | 48 | 1 | 0 |
| drain.trx | 3 | 0 | 0 |

No sumar corridas superpuestas. 83 nombres distintos con ultima evidencia Passed:
32 nuevos I.3 y 51 regresiones seleccionadas. Final selecciono 32+45; closure
repitio los 32 y 17 controles del coordinador/finalizador despues de corregir su
ruta de cancelacion; drain repitio el caso de drenaje I.3 y dos controles existentes.
Los 77 nombres de final siguen presentes; cinco controles adicionales en closure
y uno en drain amplian la evidencia, no sustituyen nombres fallidos. No se declara
una corrida ficticia 83/0. cases/executions/failures conservan cada resultado.
verification probo una version previa de 24 casos I.3, con tres variantes negativas;
final/closure prueban siete variantes por cada una de las nueve entradas.

Incidencias conservadas, sin convertir Failed retrospectivamente en Passed:
- boundary-build: CS0234 en namespace de test, 1 error/111 advertencias. Se uso
  global::Application; no pruebas sobre ese build fallido.
- focal: nueve fallos por StorageKey fisica ya existente durante la preparacion
  del permiso ajeno. Se separo su raiz Files propia; no cambio de guards ni
  expectativas. Fuente previa completa conservada en artefactos.
- final: cancelacion de negocio esperaba Cancelled y obtenia Rejected. La nueva
  revalidacion de preparacion detectaba Request, pero el coordinador no enrutaba
  ese resultado a su cierre tipado. Se corrigio dentro del try existente, con
  gate/permiso/token; no Confirm explicito como fallback. closure aprobo las dos
  variantes nuevas y las regresiones de cancelacion del coordinador.
- final: timeout Npgsql abriendo conexion de H.Now en el polling de fixture del
  escenario publication/expired. No fallo de assertion del guard ni causa raiz
  de red/SO demostrada. Se conserva fuente previa y se usa una conexion propia
  para consultar clock_timestamp, con limite del test; expectativas intactas.
  Las nueve entradas y expiry tras SaveChanges aprobaron en closure.
- closure: drenaje esperaba CanRead=false inmediatamente despues del callback de
  cancelacion. Esa senal no ordena la entrada del consumidor en finally. Se agrego
  espera acotada por el estado observable del lector, sin liberar la lectura en
  vuelo ni cambiar assertions. drain aprobo el caso y controles Control/A. No
  cambio productivo por este ajuste; se conserva fuente previa completa.

Runtime: carreras Claim/Start/publicacion frente a revocacion usan conexiones/PIDs
distintos, barrera precommit y pg_blocking_pids observado antes de liberar.
StartAfterSave observa DB=2026-10-28T16:15:54.371339Z >= expiry16:15:54.369359Z
antes del guard, con rollback completo. No reloj local como autoridad. Resto de
predicados de fencing final se inspeccionan; no nueve expiraciones post-SaveChanges
ejecutadas. Reconocimiento tras revocacion tiene casos Claim/Start/publicacion;
tras expiry usa separacion de procedencia/vigencia inspeccionada, sin nuevo caso
dedicado para cada reconciliador. Cierre vivo automatico prueba Confirm explicito;
wrapper A con comprobante se conserva y tiene regresiones focales, no se atribuye
ese wrapper al nuevo caso que llama Confirm directamente.

Rebuilds completos aislados ExperiencePermit; ultimo drain:0 errores/111 warnings,
26.17s. closure:0/111,34.54s. warning-delta vacio frente al baseline111 de auditoria
I.2, normalizando coordenadas y deduplicando archivo/mensaje. Tests --no-build;
closure10.9356min, drain28.5972s. El ultimo cambio productivo es el enrutamiento
de CancellationRequested anterior a closure; despues solo sincronizacion del test
y esta documentacion. Hashes drain-built/final prueban solo documento postbuild.

### Inventario e integridad final

Modificados en Application/PreQuotes/ProcessRequirement:
ClaimInitialLeaseService.cs, ConsumeExecutionDocumentsService.cs,
CoordinateAssignedExecutionService.cs, ExecutionDocumentPreparation.cs,
JobTerminationService.cs, ObserveAssignedExecutionService.cs,
PrepareJobPublicationService.cs, PublishJobResultService.cs,
RenewActiveLeaseService.cs, StartAssignedJobService.cs,
SuperviseAssignedExecutionService.cs.

Modificados en Infrastructure/Persistence/Repositories:
ActiveLeaseRenewalStore.cs, AssignedExecutionObservationStore.cs,
AssignedJobStartStore.cs, ExecutionDocumentPreparationStore.Consumption.cs,
ExecutionDocumentPreparationStore.cs, InitialLeaseClaimStore.cs,
JobPublicationStore.cs, JobTerminationStore.cs. Otro modificado: este contrato.

Nuevos: Infrastructure/Persistence/Repositories/ExperienceExecutionGuard.cs;
Tests/Infrastructure/Persistence/ExperiencePermitPostgreSqlTests.cs,
ExperiencePermitBoundaryPostgreSqlTests.cs, ExperiencePermitRacePostgreSqlTests.cs,
ExperiencePermitClosurePostgreSqlTests.cs, ExperiencePermitPublicationPostgreSqlTests.cs.

Manifiestos SHA256 tracked/untracked no ignorados:973->979, seis nuevos, veinte
modificados, cero ausentes, 953 anteriores identicos. Branch
feature/public-experience-intake y HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79
preservados;55tracked modificados,203untracked,0staged. Sin restaurar desde HEAD.
diff-check0. Modelo/EF/migraciones/I.2/DI/helpers/HTTP y cierres A/B previos intactos.
Artefactos externos; solo bin/obj ExperiencePermit ignorados dentro del repositorio.

PostgreSQL17 propio127.0.0.1:55589,audit: cero bases de fixture restantes y servidor
detenido por finally en las seis corridas; variable child-local retirada. Cluster
parado/logs retenidos, runners terminados. 310 raices distintas Storage root de
los TRX comprobadas ausentes; originales comprobados por tests antes de teardown.
Sin barrido/borrado de recursos ajenos, DB/API habituales o proveedores reales.

Limites: backend confiable, sin defensa reflection/SQL hostil; no prueba de fin
remoto, crash/fsync/Unix/exactly-once. Drenaje no tiene plazo garantizado ante un
cliente/callback que nunca termine. ABA, hosting/presupuestos medidos, capacidad,
retencion y emision productiva pendientes. No nuevas migraciones, DI, endpoints,
selector/worker/enqueue/frontend, commit/push/merge/despliegue ni activacion.

**Permiso Experience integrado en ejecucion explicita y revalidado en sus etapas;
emision desde Experience, host y activacion pendientes.**

## 15. II: emision atomica durante preparacion nueva

Implementacion focal del 2026-10-28. D1-D4 conservadas. No migracion ni redisenos
de I.1/I.2/I.3, H1, supervisor o cierres A/B. La opcion confiable
`PublicExperience:EmitAutomaticExecution` es false por defecto. No se agrega ni
activa en appsettings. La vinculacion IOptions existente la obtiene exclusivamente
de configuracion Backend; no hay campo multipart, Answers o advisorCode que la elija.
El repositorio scoped captura el valor al construirse y lo conserva para lectura,
escritura y reconciliacion: cambiar el objeto de opciones durante una copia no
cambia esa invocacion. Una nueva instancia puede capturar otra configuracion.

### A1 separado de II

Solo cambia `ExperiencePermitPublicationPostgreSqlTests` para conservar resultado,
excepcion inesperada y finalizacion del callback fuera de SupervisionAfterCommit.
El cuerpo del test exige Revoked, fecha persistida en contexto independiente,
AlreadyPublished y recibo/hash/Finished preservados; rechaza observacion operacional
con el permiso revocado y publicacion solo-actor sin permiso. La IOException
postcommit deliberada permanece. No cambia JobPublicationStore para impedir que
capture perdida de respuesta. Se ejecutan el caso y los dos ordenes Publish/Revoke;
no se repite la auditoria I.3. `delta.csv` identifica A1 independientemente de II.

### Transaccion y reconocimiento

`PublicExperienceRequirementPreparationRepository.WriteAsync` emite solo en la
rama nueva, tras releer snapshot/ruta/owner activo y contexto valido bajo locks.
Submission se bloquea primero, luego User/Client/Project y enlaces existentes
Quote/Requirement/Jobs por Id/archivos. Las nuevas filas todavia no son visibles:
la FK y la transaccion protegen su insercion. No E/S ni proveedor bajo esos locks.
El snapshot cambiado no se adapta al nuevo owner. Se rechazan fuentes/evidencia
incompletas, secuencias duplicadas, historia de job por Submission/Requirement o
intentos anteriores. El candidato es current/Pending; conserva validacion de
archivos/copias/fuentes. No reparacion de enlaces para habilitar emision.

Tres UUID Backend se generan una vez en esa rama: JobId, ExecutionRequestId y
CorrelationId. AuthorizedAt proviene de clock_timestamp despues de locks; la
factory I.1 fija 24h exactas, politica1, v2/PublicPreparation, actor del owner
persistido, RequestedBy/Retry null. Job, autorizacion, Quote/Requirement/archivos/
evidencia y enlaces se guardan y confirman en la misma transaccion. No dispatch,
Claim, intento ni llamada a admision/supervisor. El error precommit revierte ese
conjunto; se conserva el registro de error de preparacion existente y los destinos
fisicos segun su politica anterior. No exito silencioso sin job con opcion activa.

El replay lee historia antes de intentar emitir. Preparado antiguo sin job sigue
sin job al habilitar la opcion; deshabilitarla no borra una intencion existente.
Para una intencion automatica existente se comprueban ruta, actor, forma inmutable,
grupos de admision/revocacion y correspondencia documental. No exige Queued,
autorizacion viva, Requirement current ni ausencia de dispatch para reconocer
historia. No amplia el replay anterior para reparar un cambio de owner/CreatedBy.
La unicidad permanente I.1 no se reemplaza por una unicidad solo de jobs activos.

Commit intentado incierto: scopes dispuestos, contexto/token independiente10s y
observacion read-only existente. Ademas del candidato documental exacto se compara
el job propio: Id, Requirement, Submission, request, correlacion, trigger, version,
RequestedBy, Retry, Created, politica, actor, Authorized y Expires. No basta algun
job del envio. Campos operativos pueden haber evolucionado legitimamente, incluso
por admision/Claim posterior real. Ausencia o candidato distinto no demuestra
commit y no provoca otra escritura ni recopia. Esta certeza NO entrega permiso
I.2 ni modifica D3. Finally preserva OCE del caller original incluso si la lectura
independiente falla. No compensacion ni plazo wall-clock nuevo.

### Matriz de evidencia

R = escenario de tests de esta entrega; S = inspeccion complementaria. Los TRX y
el informe externo separan corridas, incidencias y ultima evidencia por nombre.

| Propiedad | Evidencia y limites |
|---|---|
| Emision atomica/opcion | R NewPreparation: ambas opciones, cambio de opciones durante copia, reloj DB antes/despues, microsegundos, 24h, actor/IDs/Queued/Pending y cero intentos/recibos; S fuente IOptions, factory y SaveChanges unico |
| Fallos y archivos | R RollbackWholePreparation y JobInsertFailure: fallo documental, postSave y fallo del comando INSERT; cero grafo/job/enlaces de preparacion, destinos retenidos y cero Delete |
| Autoridad actual | R RevalidatesContextAfterCopy user/client/project y OwnerChangedAfterCopy; rechazos sin emitir para actor obsoleto. Transferencia SQL fixture con FK activa, no API ni carrera ABA |
| Historia | R ReplayPreservesHistory: opcion off/on, historico sin job, admitida, revocada, terminal y vencida. R ReplayAfterRealReplacement usa servicio real de documentos y conserva sucesor. Cancelacion previa de Queued es preparacion de dominio explicita del fixture |
| Incoherencias | R InconsistentReplay evidencia/actor: error exacto, relectura sin cambios. S guards de forma, ruta y ausencia de historia: no inyeccion runtime exhaustiva de todo campo |
| Incertidumbre | R CommitLost: commit real/respuesta perdida, contexto/token independientes, caller y observador fallido, archivos intactos. UnknownCandidate borra/cambia job por SQL fixture tras commit: no reconocimiento ni reparacion. No crash/red fisica |
| Historia despues del commit | R CommitLostAfterRealClaim: admision I.2 y Claim reales antes de devolver respuesta perdida; callback comprobado fuera del hook, emision reconocida sin reconstruir permiso |
| Concurrencia | R ConcurrentPreparations y ClockTakenAfterObservedLockWait: conexiones/PID distintos, pg_blocking_pids antes de liberar, un job y Authorized posterior al reloj DB de espera; no sleep como prueba de orden |
| Composicion | R RealPreparationThenExplicitAdmissionAndSupervisor: intake/procesamiento/preparacion reales, luego harness explicito I.2/I.3, Succeeded/v2, un intento/propuesta/proveedor simulado. No host ni invocacion desde POST |
| HTTP | R cuatro combinaciones opcion/foto: Created/replay/conflicto, mismo contrato publico, Pending, foto fuera de RequirementFiles, cero intentos/leases/recibos/llamadas automaticas; politica activa deja exactamente un Queued. Storage/proveedor HTTP dobles |
| Compatibilidad | R seleccion afectada de intake, procesamiento y preparacion, controles HTTP previos y tres A1; no suites completas Claim/Renew/supervisor/A/B |

### Inventario y fronteras

Modificados II: `Application/PublicExperience/ProcessPublicExperienceSubmissionService.cs`
(solo propiedad de PublicExperienceOptions),
`Infrastructure/Persistence/Repositories/PublicExperienceRequirementPreparationRepository.cs`
y este contrato. Modificado A1: el partial de publicacion indicado arriba.
Nuevo productivo: `Infrastructure/Persistence/Repositories/PublicExperienceRequirementPreparationRepository.Emission.cs`.
Nuevos tests: `Tests/Infrastructure/Persistence/ExperienceEmissionPostgreSqlTests.cs`,
`ExperienceEmissionCoordinationPostgreSqlTests.cs`, `ExperienceEmissionBoundaryPostgreSqlTests.cs`,
`ExperienceEmissionPipelinePostgreSqlTests.cs`; y
`Tests/Api/Integration/ExperienceEmissionHttpPostgreSqlTests.cs`.

Artefactos, manifests SHA256 con untracked, TRX, comandos y diagnosticos en
`C:/Users/USUARIO/AppData/Local/Temp/sng-experience-emission-20261028`.
No restauracion desde HEAD. Rebuild aislado ExperienceEmission y PostgreSQL17 propio
55593; no DB/API habituales. Los reportes conservan fallos preliminares y repeticiones,
sin sumar corridas solapadas. Los recursos se disponen por sus propietarios.
Sin migraciones, controller/DTO/DI nuevos, selector, host, worker, polling o enqueue.
Sin frontend, proveedor real, commit/push/merge/despliegue o activacion. Solo Windows;
no crash/fsync/SQL hostil/exactly-once remoto certificados. ABA, hosting, presupuestos
medidos, capacidad/retencion y activacion permanecen pendientes.

### Cierre de verificacion II

Corridas passed/failed/skipped, NO SUMAR: preflight38/0/0 (2.5279min),
third26/0/0 (5.3331min), final127/1/0 (59.5653min), recheck5/0/0 (1.3313min).
Final selecciona128:30II,3A1 y95regresiones afectadas (35preparacion PostgreSQL,
58unitarias intake/procesamiento/preparacion y2HTTP). Los38 y26 nombres reaparecen;
128 distintos con ultima evidencia Passed, no corrida128/0 inventada.

El Failed de final es CommitUncertainty(boundary:blocked), TimeoutException al
esperar probe.Reached en el harness previo, linea151. El caso registra52m15s;
no se atribuye causa raiz a SO/red/carga ni defecto del guard sin prueba. Se
conserva Failed y stack. Recheck repite sus cinco variantes con MISMO binario,
limites y expectativas, sin cambiar fuente/configuracion, y todas completan.
No se relaja el timeout ni se convierte retrospectivamente el Failed en Passed.

Builds completos ExperienceEmission: preflight0errores/111warnings (1:13.21),
second15errores/111 (Token y composicion del supervisor en test NUEVO),
third0/111 (16.56s), final0/111 (21.53s). No tests sobre second fallido; se
corrigieron imports/argumentos del harness, no produccion para pasar pruebas.
warning-delta vacio frente ExperiencePermitAudit111, coordenadas normalizadas
y diagnosticos deduplicados. Recheck --no-build; solo documentacion despues del
rebuild final. Una solicitud de escritura del partial de limites agoto el revisor
de permisos sin crear archivo; repetida una vez correctamente. No fallo de test.

Integridad979->985 rutas SHA256 tracked/untracked no ignoradas:6nuevas,
4modificadas,0ausentes,975anteriores identicas. Baseline coincide con auditoriaI.3.
Rama feature/public-experience-intake y HEAD56ace9c3845d2e3327a1ca811c27f8e1562e5a79
preservados. Motor/I.1/I.2/I.3 productivos, modelo/EF/migraciones/controllers/DI y
resto previo intactos. diff-check0; bin/obj ExperienceEmission ignorados.
PostgreSQL propio detenido en finally por corrida; cero bases de fixture restantes.
Manifiestos, limpieza de raices propias y resultados por nombre en artefactos.

**Emision atomica de intencion automatica Experience implementada,
desactivada por defecto; host y activacion pendientes.**

## 16. III: host de extraccion inicial, apagado por defecto

Implementacion del 2026-10-29. D1-D4 conservadas; II y A1 cerrados por auditoria
externa. No cambios a admision, supervisor, coordinador, guards, cierres A/B,
transporte, modelo, EF, migraciones o endpoints. Esta seccion no activa el sistema.

### Configuracion y composicion

| Opcion Backend | Default / efecto |
|---|---|
| `PublicExperience:EmitAutomaticExecution` | `false`; solo emite intencion al preparar documentos nuevos |
| `ExperienceExecutionHost:Enabled` | `false`; permite descubrir intenciones existentes, independientemente de emision |
| `ExperienceExecutionHost:BatchSize` | `16`, rango1..100; maximo de candidatos por consulta |
| `ExperienceExecutionHost:PollInterval` | `00:00:05`, positivo y maximo1dia; espera despues de cada lote, incluido error/vacio |

Valores iniciales tecnicos, no capacidad ni latencia productiva medidas. Una
ejecucion simultanea por INSTANCIA del host, no limite global entre replicas.
Sin semaforo distribuido, broker o segunda cola. Configuracion capturada al construir
host; no IOptionsMonitor ni recarga. Ambas opciones siguen apagadas en configuraciones
habituales. Apagar emision no revoca intenciones existentes ni detiene otro host.

`Infrastructure.Operations.ExperienceExecutionRegistration` se llama desde DI
Infrastructure existente. Registra BackgroundService singleton, opciones y reloj;
solo IServiceScopeFactory se conserva, nunca DbContext scoped. Reader, ejecutor,
I.2/I.3 y stores son scoped. Stores mantienen contextos propios. Mapper/builder
usan transformacion/catalogos existentes. No HttpContext, ICurrentUser ni token POST.
`IVerifiedExecutionDocumentClient` resuelve `SingleRequestAi2Client` scoped: handler
sin redirects/cookies/retry agregado, raw capture desactivado; no transporte legacy.
Su lifetime termina con el scope, despues de await del supervisor y su drenaje.

### Ciclo y frontera de autoridad

`ExperienceCandidateReader` consulta solo id/CreatedAt, policy1/v2/Queued, dispatch
completamente null, no revocacion/Request y tiempo PostgreSQL en [Authorized,Expires).
Sin DML, Claim, reserva, FOR UPDATE o transaccion abierta durante trabajo posterior.
Orden keyset `(CreatedAtUtc, Id)` comparado por PostgreSQL, limite parametrizado.
Cursor avanza por TODOS los candidatos, incluso rechazados; vuelve al principio
tras pagina incompleta/vacia. Se espera intervalo tras cada lote. Un candidato
permanentemente inelegible al principio no monopoliza las siguientes paginas.
Es lectura orientativa, no permiso ni garantia de que el candidato aun sea elegible.

Scope independiente por candidato: `ExperienceInitialExecution` llama I.2 una vez.
Solo Admitted con permiso genuino llama UNA vez al overload tipado del supervisor.
No construye permiso, actor legacy, job o IDs de Claim; I.2 mantiene sus IDs unicos.
AlreadyAdmitted/rechazo/Indeterminate/excepcion/OCE no reintentan supervisor. La
exclusion durable sigue siendo admision/Claim; cursor no es registry de ownership.
No reset de dispatch ni adopcion Running. Interrupcion tras admision pierde
disponibilidad aprobada por D3; recrear host no recupera esa fila. Si incertidumbre
revirtio realmente y sigue unadmitted, puede aparecer en otro ciclo, nunca como
retry inmediato de una operacion mutante ni repeticion de una ejecucion iniciada.

Errores de invocacion se registran y no matan el ciclo para otros jobs. Errores de
descubrimiento esperan intervalo. No otro heartbeat/renew/finalizador/recovery;
supervisor conserva control, Stop irreversible y joins. Host no llama Request/A/B.
Resultado local, coordinador conocido, cancelacion local y estado observado se
registran separados; Succeeded observado no fabrica Published ni OCE fabrica Cancelled.

### Shutdown y diagnostico

BackgroundService recibe cancelacion cooperativa, deja de consultar/admitir y
la propaga al supervisor propio. Scope permanece dentro de ExecuteAsync hasta
su retorno/drenaje, incluso si StopAsync agota presupuesto. No disposal anticipado
de paquetes, no borrado de dispatch/compensacion/cancelacion comercial.

Se prueba Generic Host .NET real con HostOptions.ShutdownTimeout=100ms SOLO en
fixture. StopAsync puede retornar con ExecuteTask pendiente: log estructurado
`DrainCompleted=false`, `BudgetExpired=true`; no anuncio de cleanup completo.
El propietario sigue observando la tarea, libera barrera y espera ExecuteTask
ANTES de disponer host en tests. Al completarse se registra fin de tarea.
En apagado cooperativo completado no queda esa tarea activa. El host no altera
el ShutdownTimeout habitual. No garantiza que un proceso sobrevivira al presupuesto
del supervisor externo/SO ni que callbacks no cooperativos terminen. Crash/kill real,
fsync, fin remoto y exactly-once no certificados. No ampliar timeout para ocultar
la falta de cooperacion ni confundir presupuesto con deadline duro de cleanup.

Diagnostico sin plataforma nueva: logs estructurados inicio/fin de descubrimiento,
admision y supervision, UTC y duracion monotona por fase; JobId/InstanceId internos,
state/failure/Stop/coordinator/durable separados. Fallos solo tipo de excepcion,
no mensaje/stack potencialmente sensible, Answers, tokens, documentos o respuesta AI.
Para una espera: correlacionar ultimo inicio sin fin por fase/JobId y shutdown;
comprobar tarea pendiente y locks DB desde herramientas operativas autorizadas.
No afirmar causa SO/red/carga desde silencio de logs. El timeout historico de II
sigue indeterminado; esta telemetria NO lo explica retroactivamente.

### Matriz de verificacion y limites

R son escenarios de pruebas nuevas; informe externo registra corridas/nombres y
build exactos. S complementa inspeccion, sin cobertura exhaustiva por conteo.

| Criterio | Evidencia / limite |
|---|---|
| HTTP termina antes de ejecucion [R] | WebApplication real, archivos reales, emision real; barrera discovery se libera tras Created. Host descubre y publica v2 mediante I.2/I.3 reales, un intento/propuesta/proveedor simulado; replay no vuelve a ejecutar |
| DI y opciones [R/S] | Grafo productivo AddApplication/AddInfrastructure; ValidateScopes en HTTP, resolucion del cliente verificado real sin red. Cuatro combinaciones emision/host; apagado cero descubrimiento; opciones invalidas fallan cerrado |
| Seleccion/paginacion [R] | SQL real excluye revocado/vencido/admitido/Running/terminal/manual-v1-sinmetadata. Job antiguo sin documentos primero y job valido segundo: pagina1 rechazada, pagina2 publica. Scheduling controlado prueba lotes/espera; error no ocupa bucle |
| Dos hosts [R] | Admision real retenida antes de commit, segundo PID espera Quote, pg_blocking_pids observado antes de release. Una admision efectiva, intento/Claim/proveedor1; perdedor sin permiso ni recursos del ganador |
| Incertidumbre/recreacion [R] | Interceptor tras commit real de admision: no Claim. Otra variante retiene retorno confirmado hasta shutdown antes de Claim. Marca queda; nuevo host no adopta. No crash fisico |
| Ejecucion detenida [R] | Renew real con copia/proveedor pausados, revocacion y Request reales; supervisor detiene, host no cierra. Shutdown durante ejecucion deja Running; nuevo host no lo descubre |
| Drenaje [R] | Lectura fisica que ignora token, presupuesto real .NET agotado, task pendiente/disposals0; release y join antes de2disposals. Originales conservados, no promesa de fin remoto |
| Commit tardio [R] | Publicacion real confirmada con respuesta retenida durante shutdown; Succeeded/grafo conservados, sin compensacion ni Request |
| Compatibilidad [R/S] | Regresiones focales de transporte verificado, noresume, permisos y HTTP. No suites completas historicas ni activacion |

Fixtures HostQueued/Selection preparan metadata de incorporacion valida con SQL
constraints activas; HostQueued limpia SOLO marca del job de SU fixture despues
de setup compartido para simular una intencion aun no admitida. Esto NO es operacion
productiva ni prueba de recovery/redispatch. El recorrido HTTP usa preparacion real
y nunca limpia dispatch. Simulacion de interrupcion usa recreacion/stop del host,
no muerte fisica de proceso. Clock controlado gobierna scheduling, nunca autoridad DB.

Inventario III: tres archivos productivos nuevos (host/ejecutor/opciones, registroDI,
reader), cinco partials de tests nuevos; una llamada agregada a Infrastructure DI
y esta documentacion. No migracion ni configuracion habitual alterada. Artefactos,
manifiestos y trazabilidad en `C:/Users/USUARIO/AppData/Local/Temp/sng-experience-host-20261029`.
Valores/capacidad global, ABA, retencion, estados de Frontend, validacion operativa
y activacion quedan pendientes; no permiso de desplegar/activar desde esta entrega.

### Cierre de verificacion III

Corridas separadas passed/failed/skipped: first8/0/0, third24/0/0,
final45/0/0 y closure45/0/0. Cierre2.9539min:28nuevos y17regresiones focales
(4transporte,5noresume,4HTTP II,2HTTPprevios,2permiso integrado). Mismos45nombres
final/closure; no suma de corridas. Closure agrega assertions, no casos: admision
ganadora Admitted/confirmada, perdedora AlreadyAdmitted/sin confirmacion y cero
Claim del perdedor, frente a un Claim del ganador. Sin relajar expectativas.

Build aislado ExperienceHost: preflight0/11129.12s; first0/11116.61s;
second11errores/112warnings por imports Token/Signal y referencia WaitBlocked en
tests nuevos, sin tests sobre ese build; third0/11257.11s; final0/11157.04s;
closure0/11119.23s. Warning xUnit2013 nuevo corregido con Assert.Single en lugar
de Assert.Equal para conteo1. Produccion sin cambios despues de preflight;
warning-delta vacio frente auditoria II, coordenadas normalizadas/deduplicadas.
Sin fallos de tests en estas corridas. Solo documentacion despues del build cierre.

Manifiestos incluyen untracked:985->993 archivos,8nuevos/2modificados/0ausentes,
983anteriores identicos. DI anterior reconstruida quitando SOLO nueva llamada:
hash inicial exacto. Rama/HEAD preservados;56tracked modificados,217untracked,
0staged. No restore desde HEAD. PostgreSQL17 temporal propio55597 detenido por
corrida en finally, cero bases de fixture. Recursos propios verificados en CSV;
ninguna limpieza general, DB/API habitual, proveedor real o migracion nueva.

**Host de extraccion inicial Experience implementado y probado con proveedor
simulado, desactivado por defecto; estados de Frontend, validacion operativa
y activacion pendientes.**

## 17. IV: lectura comercial e integracion, sin activacion

Implementacion 2026-10-29. Se amplia el GET autenticado existente
`/api/v2/projects/{projectId}/workspace`, sin endpoint publico por Submission,
migracion, estado persistido nuevo ni cambio al ejecutor. Emision y host siguen
desactivados. L1 se corrige solo en el comentario del transporte scoped.

`workflow.extraction` agrega `state`, `refreshRecommended`, `canAnalyze`. No devuelve
JobId, dispatch, permiso, actor historico, lease owner, rutas ni credenciales.
El lector propio revalida usuario activo y owner actual o Admin, como la lectura
workspace (Admin no recibe por ello permiso ejecutivo). Usa contexto/transaccion
RepeatableRead READ ONLY; autoridad y datos corresponden al mismo snapshot, no
un grant permanente. No locks de escritura, SaveChanges, storage o proveedor.
Una transferencia posterior al snapshot no vuelve retroactiva la lectura.

Se consulta el conjunto de jobs de la version seleccionada, no el ultimo job de
todo el proyecto. Ningun automatico: recorrido Manual; automatico con multiples
jobs: Inconsistent. Para automatico unico se exige ruta Submission/proyecto/quote/
Requirement y link/count/correlacion de intento concordantes. Una version nueva no
hereda resultado ni autorizacion del antecedente. No se repara historia incoherente.

| Hecho persistido | state / presentacion | Refresco | Acciones |
|---|---|---|---|
| Sin job automatico | Manual, recorrido anterior | Sin polling automatico IV | Analizar solo segun canAnalyze y guard final existente |
| Queued sin dispatch, autorizacion/contexto elegibles | Waiting: En espera de analisis | Acotado | No iniciar/cancelar/reintentar automaticamente |
| Queued con admision | Admitted: espera; inicio no confirmado | Acotado | No reconstruir permiso ni prometer reanudacion |
| Running, lease/autorizacion elegibles | Running: Analizando documentos | Acotado | No afirmar supervisor vivo |
| Solicitud durable no terminal | CancellationRequested | Acotado si condiciones temporales elegibles | No boton de cancelacion nuevo |
| Succeeded con propuesta/extraccion/intento y recibo v2 compatibles | Succeeded: Resultados disponibles | Termina; carga propuesta existente | Cards/capabilities anteriores |
| Failed o Cancelled durables, intento compatible si existe | Failed / Cancelled | Termina | Enlace a Documentos |
| Autorizacion revocada/vencida, contexto/owner historico no elegibles o lease vencida | ContinuityUnknown | Sin polling automatico | Consulta, no Failed/Cancelled inventados |
| Multiplicidad o correspondencia incompatible | Inconsistent | Sin polling | Sin reparacion ni resultados de otro intento |

Succeeded no basta: Requirement Processed, intento Finished con outcome de exito,
correlacion/CompletedAt=Finished del job, propuesta unica de ese Requirement e
intento, extraccion exacta y version/hash del recibo. La propuesta se obtiene despues
por su contrato existente. Esto verifica correspondencias de lectura, no reejecuta
la publicacion ni valida criptograficamente todo el grafo. Historia terminal no
requiere lease/autorizacion aun vigentes. canAnalyze es una capability informativa
del snapshot: owner/contexto activos, current Pending/Failed, linea comercial,
documentos, cero jobs y ningun intento activo. El endpoint manual revalida bajo
sus locks previos; cualquier job sigue bloqueando una ejecucion manual paralela.
Capabilities documentales existentes no cambian ni se derivan de labels.

Frontend integra el estado junto al listado de Documentos y en Configuracion.
No agrega subir/cancelar/reintentar automatico; conserva acciones documentales y
boton manual donde corresponde. Crear version no promete emision automatica nueva.
Refresco automatico: GET serial cada7s, max20 respuestas no terminales por sesion
proyecto/version; se detiene al terminar, ante error o al agotar conteo. No SLA de
duracion de cada HTTP. Abort al desmontar/cambiar proyecto/version/sesion; respuestas
tardias se descartan tambien por identidad/token. Error conserva ultimo estado y
mensaje de consulta, nunca un terminal falso. Tampoco el error del POST manual se
traduce ahora a Failed: conserva ultimo estado conocido y su mensaje de error.

Refresco modifica solo workflow y conserva referencias si no cambio. No sobrescribe
proyecto/publicContext, no desmonta el workspace durante polling ni reinicia la
propuesta por cada lectura. Transicion a resultados habilita la carga existente;
cards y borradores mantienen su ownership local. Cambio real de version usa su nueva
identidad. No hace falta observar todos los estados: primer GET puede ser terminal.

Verificacion y limites: PostgreSQL temporal real para lectura/autoridad/forma/
versiones y proveedor simulado solo al preparar terminales. Casos de workspace
unitarios y regresiones HTTP/preparacion afectadas; no suites completas I-III/A/B.
Frontend usa tests de estados/refresco/manual, regresiones documentos/capabilities/
borradores y Playwright desktop1440/mobile390 con TODAS las respuestas API simuladas.
Capturas, builds, corridas fallidas y finales, manifiestos y limpieza estan en
`C:/Users/USUARIO/AppData/Local/Temp/sng-experience-state-20261029`.
No se afirma integracion con API habitual ni extraccion operativa en demo.

Smoke pendiente: API y Frontend autorizados de staging, GET real con sesion/roles y
versiones conocidas, transicion a propuesta real y borradores comerciales, logout/
cambio de proyecto y fallos de red. Requiere entorno/credenciales autorizados, no
reiniciar API habitual para suplirlo. Activacion requiere validar capacidad global,
shutdown/proceso, observabilidad/retencion, ABA y permisos operativos; dos flags
independientes, sin activarlos desde IV. Sin commit/push/merge/despliegue.

### 17.1 Correccion focal H1/H2 de IV

2026-10-29. La auditoria IV encontro H1 (sin accion de consulta tras pausa/error)
y H2 (estado/version del primer read combinados con evidencia de un segundo
snapshot). Este complemento sustituye esa composicion y precisa el refresco;
no reabre III, cambia el protocolo ejecutivo ni activa opciones.

H1: `Consultar estado` es un comando de lectura visible en Documentos y
Configuracion para el recorrido automatico, tambien junto al aviso de pausa/error.
No llama retryWorkspace, no pone el workspace en loading global ni desmonta las
cards. Tiene carga accesible, deshabilitado mientras consulta explicitamente,
exclusion local compartida con el polling y cancelacion/descarte por contexto.
Error conserva datos anteriores y permite otra consulta. Exito equivalente conserva
referencias y no reinicia effects de propuesta/borradores; no hace POST ni cambia
seleccion comercial. Se ofrece tambien con resultados para consultar sin perder
ediciones. Manual mantiene su recorrido anterior.

Solo una accion explicita puede reiniciar el presupuesto dentro de la misma
sesion/version: hasta20 respuestas no terminales, contando la respuesta explicita
y hasta19 automaticas posteriores. El ciclo inicial sigue teniendo20 automaticas.
Renders/merges equivalentes no reinician nada. Terminal termina el ciclo; error
lo pausa. Cambio real de proyecto/version/sesion/token invalida el anterior. No
hay promesa de plazo duro por HTTP. Consulta fallida no fabrica Failed/Cancelled.

H2: `ReadCurrentAsync(project, preQuote, actor)` construye TODO el workflow
documental en una unica transaccion propia RepeatableRead READ ONLY: autoridad,
resolucion de quotes, seleccion current/historica conforme al repositorio existente,
RequirementStatus, extraction, CanAnalyze y propuesta compatible. Usa el MISMO
DbContext para esa seleccion y la evidencia. El servicio reemplaza el workflow
completo, no copia un status para disimular contradicciones. La lectura preliminar
no decide la version devuelta por este camino. Multiples quotes siguen Ambiguous;
evidencia automatica incompatible sigue Inconsistent, sin reparacion o reintento.

Publicacion/replacement antes de fijar snapshot se observan en conjunto. Una
escritura posterior puede dejar la respuesta anterior, pero coherente dentro de
su snapshot. No se promete que siga siendo current al llegar al navegador. No
transaccion global sobre proyecto/publicContext/E/S, no bloqueo de proveedor ni
retry ilimitado. Los datos generales del proyecto siguen sus lecturas existentes:
la garantia focal es version/estado/capability de analisis/propuesta/extraccion.
Capabilities documentales se consultan por version y escrituras siguen revalidando.
Lectura focal por ID anterior se conserva para diagnostico/tests, pero no se usa
para ensamblar el nuevo workflow. No informacion operacional nueva en DTO publico.

Evidencia: dos pruebas PostgreSQL de composicion fallaron ANTES del fix, publicacion
con PROCESSING y replacement con ID anterior. Despues pasan con writers reales
(supervisor/publicacion con proveedor simulado y servicio documental replacement).
Dos casos adicionales retienen SELECT ya ejecutado dentro del snapshot mientras
writer confirma, y comprueban respuesta anterior coherente y siguiente lectura
actual. Barreras deterministas, conexiones/PID distintos y DML0 del lector; no
se atribuye contencion por locks a un snapshot MVCC que permite escribir.
H1 se comprueba en React/Next real con API interceptada: limite20, consulta/resultados,
error/reconsulta, doble clic, respuestas tardias y borrador/seleccion/edicion local.
No smoke contra API habitual ni proveedor real. Artefactos, incidencias y matriz
runtime/limites: `C:/Users/USUARIO/AppData/Local/Temp/sng-experience-state-fix-20261029`.
No migraciones, activacion, reglas comerciales, transporte, commit o despliegue.
