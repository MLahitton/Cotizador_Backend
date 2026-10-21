namespace Application.Projects.GetProjectDiagnosis;

public enum DiagnosisValueKind { Text, Choice, MultiChoice, Scale }

public sealed record DiagnosisOption(string Code, string Label);
public sealed record DiagnosisCondition(string ParentId, string? EqualsCode = null, int? Minimum = null);
public sealed record DiagnosisQuestion(
    string Id, string Label, string? Section, DiagnosisValueKind Kind,
    IReadOnlyList<DiagnosisOption> Options, DiagnosisCondition? Condition = null,
    string? ExclusiveCode = null, int? MaxSelections = null);

// Versioned Backend definition of sng-public-experience-v1, not a runtime dependency on Next.js.
public static class ProjectDiagnosisSchema
{
    public const string SchemaVersion = "sng-public-experience-v1";
    public const string InterpretationVersion = "diagnosis-v1";

    public static IReadOnlyList<DiagnosisQuestion> Questions { get; } = Array.AsReadOnly<DiagnosisQuestion>([
        Text("proyecto", "Proyecto", "general"),
        Text("ciudad", "Municipio / ciudad", "general"),
        Text("parcelacion", "Parcelación / conjunto", "general"),
        Text("lote", "Lote o apartamento", "general"),
        Choice("tipo", "Tipo de proyecto", "general", [("apto", "Apartamento"), ("edificio", "Edificio / propiedad horizontal"), ("casa", "Casa en unidad residencial o parcelación"), ("hospedaje", "Hospedaje / renta corta"), ("comercial", "Comercial, institucional o corporativo"), ("otro", "Otro")]),
        Text("tipo_otro", "Detalle del tipo de proyecto", "general", new("tipo", "otro")),
        Choice("area", "Área construida aproximada", "general", [("lt100", "Menos de 100 m²"), ("100-300", "100–300 m²"), ("300-500", "300–500 m²"), ("gt500", "Más de 500 m²"), ("nc", "No la conozco")]),
        Choice("vanos_grandes", "Vanos de más de 3 metros de altura", "general", [("si", "Sí"), ("no", "No"), ("ns", "No estoy seguro")]),
        Scale("termico", "Confort térmico", "priorities"),
        Scale("acustico", "Silencio y confort acústico", "priorities"),
        Scale("seguridad", "Seguridad", "priorities"),
        Scale("estetica", "Estética y diseño", "priorities"),
        Choice("uv", "Protección UV", "priorities", [("si", "Sí"), ("no", "No"), ("ns", "No sabe / por definir")]),
        Choice("preferencia_acabado", "Preferencia de acabado", "preferences", [("negro", "Negro"), ("blanco_gris", "Blanco, gris o metálico"), ("madera_esp", "Madera o acabado especial"), ("con_sng", "Definir con Steel & Glass")]),
        Scale("funcionalidad", "Facilidad y experiencia de uso", "preferences"),
        Choice("motivo_estetica", "Papel de la ventanería en la arquitectura", "preferences", [("desaparece", "Que casi desaparezca"), ("integrada", "Que se integre discretamente"), ("protagonismo", "Que tenga protagonismo")], new("estetica", Minimum: 4)),
        Multi("motivo_funcionalidad", "Facilidad en el uso diario", "preferences", [("abrir", "Abrir y cerrar con comodidad"), ("ventilar", "Conectar y ventilar fácilmente"), ("mantener", "Mantener la ventanería con facilidad")], new("funcionalidad", Minimum: 4)),
        Multi("habitantes", "Usuarios habituales de los espacios", "livingContext", [("ninos", "Niños"), ("adultos_may", "Adultos mayores"), ("movilidad", "Personas con movilidad reducida o discapacidad"), ("mascotas", "Mascotas"), ("huespedes", "Huéspedes frecuentes"), ("servicio", "Personal de servicio"), ("ninguno", "Ninguna condición particular")], exclusive: "ninguno"),
        Choice("zona_social", "Relación de la zona social con el exterior", "livingContext", [("muy_abierta", "Muy abierta"), ("equilibrada", "Equilibrada"), ("visual", "Más visual"), ("nd", "Aún no está definido / No aplica en este proyecto")]),
        Multi("descanso_prioridades", "Prioridades de descanso", "livingContext", [("silencio", "Silencio y tranquilidad"), ("temperatura", "Temperatura y confort"), ("privacidad", "Privacidad y sensación de refugio"), ("nd", "No aplica en este proyecto")], exclusive: "nd"),
        Multi("motivo_termico", "Necesidades de confort térmico", "needs", [("calidos", "Sentirlos más cálidos"), ("frescos", "Evitar exceso de calor"), ("estable", "Sentir una temperatura más estable")], new("termico", Minimum: 4)),
        Multi("motivo_acustico", "Ruido que desea reducir", "needs", [("vecinos", "Vecinos y actividad cercana"), ("entorno", "Ruido del entorno"), ("interiores", "Ruido entre espacios interiores")], new("acustico", Minimum: 4)),
        Multi("motivo_seguridad", "Necesidades de seguridad", "needs", [("sola", "Vivienda protegida cuando queda sola"), ("accesos", "Mayor control en puertas, accesos y grandes aperturas"), ("habitantes", "Mayor tranquilidad para quienes habitan la casa")], new("seguridad", Minimum: 4)),
        Text("problemas_prev", "Experiencias previas", "needs"),
        Text("momento_especial", "Espacio, vista o elemento especial", "needs"),
        Choice("tiempo", "Tiempo estimado para decidir", "decision", [("30d", "En los próximos 30 días"), ("1-3m", "1–3 meses"), ("3-6m", "3–6 meses"), ("6-12m", "6–12 meses"), ("12m+", "Más de 12 meses"), ("nd", "Aún no lo tengo definido")]),
        Choice("ventana_ejecucion", "Instalación estimada", "decision", [("lt2m", "Menos de 2 meses"), ("2-4m", "2–4 meses"), ("4-6m", "4–6 meses"), ("6-9m", "6–9 meses"), ("9-12m", "9–12 meses"), ("gt12m", "Más de 12 meses"), ("nd", "Aún no está definido")]),
        Multi("criterio", "Criterios de decisión", "decision", [("precio", "Inversión / precio"), ("calidad", "Calidad y durabilidad"), ("confort", "Confort y desempeño"), ("seguridad", "Seguridad"), ("disenio", "Diseño y acabados"), ("respaldo", "Respaldo y acompañamiento")], max: 2),
        Multi("decisor", "Participantes en la decisión", "decision", [("propietario", "Propietario"), ("arquitecto", "Arquitecto / diseñador"), ("constructor", "Constructor"), ("conjunta", "Decisión conjunta"), ("otra", "Otra persona")]),
        Choice("acceso_decisor", "Reunión con quien decide", "decision", [("si", "Sí"), ("no", "No"), ("ns", "Aún no lo sé")]),
        Choice("sig_paso", "Próximo paso", "decision", [("centro_exp", "Visitar el Centro de Experiencia SNG"), ("reunion_virt", "Reunión virtual desde el Centro de Experiencia SNG"), ("no_dispuesto", "Por ahora no está dispuesto a estas opciones")]),
        Choice("origen", "Origen del contacto", "decision", [("referido", "Referido"), ("visita_obra", "Visita de nuestro equipo en obra"), ("contacto_asesor", "Contacto / llamada de un asesor"), ("redes", "Redes sociales"), ("arquitecto", "Arquitecto / diseñador"), ("cliente_ant", "Ya ha trabajado con SNG"), ("otro", "Otro")]),
        Text("origen_otro", "Detalle del origen", "decision", new("origen", "otro")),
        Text("project_documents", "Necesidades y detalles adicionales", "supplementalNotes"),
        Choice("asesor", "Asesor indicado por el cliente", "supplementalNotes", [("Andrés Candela", "Andrés Candela"), ("Valeria Álvarez", "Valeria Álvarez"), ("Alejandra Del Río", "Alejandra Del Río"), ("Julián Sierra", "Julián Sierra"), ("Paula Chica", "Paula Chica"), ("No sé / contacto directo", "No sé / contacto directo")]),
        // Deliberately excluded from Diagnosis: already available in the client context.
        Text("nombre", "Nombre completo", null),
        Text("wa", "WhatsApp", null)
    ]);

    private static DiagnosisQuestion Text(string id, string label, string? section, DiagnosisCondition? condition = null) =>
        new(id, label, section, DiagnosisValueKind.Text, [], condition);
    private static DiagnosisQuestion Scale(string id, string label, string section) =>
        new(id, label, section, DiagnosisValueKind.Scale, []);
    private static DiagnosisQuestion Choice(string id, string label, string section,
        (string Code, string Label)[] options, DiagnosisCondition? condition = null) =>
        new(id, label, section, DiagnosisValueKind.Choice,
            Array.AsReadOnly(options.Select(x => new DiagnosisOption(x.Code, x.Label)).ToArray()), condition);
    private static DiagnosisQuestion Multi(string id, string label, string section,
        (string Code, string Label)[] options, DiagnosisCondition? condition = null, string? exclusive = null, int? max = null) =>
        Choice(id, label, section, options, condition) with
        { Kind = DiagnosisValueKind.MultiChoice, ExclusiveCode = exclusive, MaxSelections = max };
}
