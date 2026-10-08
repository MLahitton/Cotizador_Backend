namespace Application.PreQuotes.RequirementExperience;

public interface IRequirementExperienceCatalogProvider
{
    RequirementExperienceCatalog Current { get; }

    RequirementExperienceCatalog? FindByVersion(string catalogVersion);
}

public sealed class RequirementExperienceCatalogProvider : IRequirementExperienceCatalogProvider
{
    private readonly RequirementExperienceCatalog _v2 = RequirementExperienceCatalog.CreateV2();
    private readonly RequirementExperienceCatalog _v3 = RequirementExperienceCatalog.CreateV3();
    private readonly RequirementExperienceCatalog _v4 = RequirementExperienceCatalog.CreateCurrent();

    public RequirementExperienceCatalog Current => _v4;

    public RequirementExperienceCatalog? FindByVersion(string catalogVersion)
    {
        if (string.Equals(catalogVersion, _v4.Version, StringComparison.Ordinal))
        {
            return _v4;
        }

        if (string.Equals(catalogVersion, _v3.Version, StringComparison.Ordinal))
        {
            return _v3;
        }

        return string.Equals(catalogVersion, _v2.Version, StringComparison.Ordinal)
            ? _v2
            : null;
    }
}

public sealed record RequirementExperienceCatalog(
    string Version,
    IReadOnlyList<RequirementExperienceQuestion> Questions,
    IReadOnlyList<RequirementExperienceSpace> Spaces)
{
    public const string V2Version = "sng-experience-v2-draft-001";

    public const string V3Version = "sng-experience-v3-001";

    public const string V4Version = "sng-experience-v4-001";

    public static RequirementExperienceCatalog CreateCurrent()
    {
        return CreateV4();
    }

    public static RequirementExperienceCatalog CreateV4()
    {
        return new RequirementExperienceCatalog(
            V4Version,
            [
                Question("THERMAL", "Confort termico", "Que nivel de confort termico necesita este espacio?", [
                    Option("THERMAL_LOW", "Baja", "Baja", ["Baja"]),
                    Option("THERMAL_MEDIUM", "Media", "Media", ["Media"]),
                    Option("THERMAL_HIGH", "Alta", "Alta", ["Alta"])
                ]),
                Question("ACOUSTIC", "Confort acustico", "Que nivel de reduccion de ruido espera?", [
                    Option("ACOUSTIC_LOW", "Baja", "Baja", ["Baja"]),
                    Option("ACOUSTIC_MEDIUM", "Media", "Media", ["Media"]),
                    Option("ACOUSTIC_HIGH", "Alta", "Alta", ["Alta"])
                ]),
                Question("SECURITY", "Seguridad", "Que nivel de seguridad necesita?", [
                    Option("SECURITY_LOW", "Baja", "Baja", ["Baja"]),
                    Option("SECURITY_MEDIUM", "Media", "Media", ["Media"]),
                    Option("SECURITY_HIGH", "Alta", "Alta", ["Alta"])
                ]),
                Question("UV", "Proteccion UV", "Necesita proteccion UV para interiores o acabados?", [
                    Option("UV_NO", "No", "No", ["Sin proteccion UV adicional"]),
                    Option("UV_YES", "Sí", "Sí", ["Proteccion UV requerida"])
                ]),
                Question("AESTHETICS", "Estetica", "Que nivel de protagonismo visual busca?", [
                    Option("AESTHETICS_LOW", "Baja", "Baja", ["Baja"]),
                    Option("AESTHETICS_MEDIUM", "Media", "Media", ["Media"]),
                    Option("AESTHETICS_HIGH", "Alta", "Alta", ["Alta"])
                ])
            ],
            []);
    }

    public static RequirementExperienceCatalog CreateV3()
    {
        return new RequirementExperienceCatalog(
            V3Version,
            [
                Question("THERMAL", "Confort termico", "Que nivel de confort termico necesita este espacio?", [
                    Option("THERMAL_1", "Basico", "Basico", ["Nivel 1"]),
                    Option("THERMAL_2", "Estandar", "Estandar", ["Nivel 2"]),
                    Option("THERMAL_3", "Confortable", "Confortable", ["Nivel 3"]),
                    Option("THERMAL_4", "Alto confort", "Alto confort", ["Nivel 4"]),
                    Option("THERMAL_5", "Maximo confort", "Maximo confort", ["Nivel 5"])
                ]),
                Question("ACOUSTIC", "Confort acustico", "Que nivel de reduccion de ruido espera?", [
                    Option("ACOUSTIC_1", "Basico", "Basico", ["Nivel 1"]),
                    Option("ACOUSTIC_2", "Estandar", "Estandar", ["Nivel 2"]),
                    Option("ACOUSTIC_3", "Confortable", "Confortable", ["Nivel 3"]),
                    Option("ACOUSTIC_4", "Alta reduccion", "Alta reduccion", ["Nivel 4"]),
                    Option("ACOUSTIC_5", "Maxima reduccion", "Maxima reduccion", ["Nivel 5"])
                ]),
                Question("SECURITY", "Seguridad", "Que nivel de seguridad necesita?", [
                    Option("SECURITY_1", "Basico", "Basico", ["Nivel 1"]),
                    Option("SECURITY_2", "Estandar", "Estandar", ["Nivel 2"]),
                    Option("SECURITY_3", "Reforzado", "Reforzado", ["Nivel 3"]),
                    Option("SECURITY_4", "Alta seguridad", "Alta seguridad", ["Nivel 4"]),
                    Option("SECURITY_5", "Maxima seguridad", "Maxima seguridad", ["Nivel 5"])
                ]),
                Question("UV", "Proteccion UV", "Necesita proteccion UV para interiores o acabados?", [
                    Option("UV_NO", "No", "No", ["Sin proteccion UV adicional"]),
                    Option("UV_YES", "Si", "Si", ["Proteccion UV requerida"])
                ]),
                Question("AESTHETICS", "Estetica", "Que nivel de protagonismo visual busca?", [
                    Option("AESTHETICS_1", "Funcional", "Funcional", ["Nivel 1"]),
                    Option("AESTHETICS_2", "Sobrio", "Sobrio", ["Nivel 2"]),
                    Option("AESTHETICS_3", "Equilibrado", "Equilibrado", ["Nivel 3"]),
                    Option("AESTHETICS_4", "Premium", "Premium", ["Nivel 4"]),
                    Option("AESTHETICS_5", "Maxima presencia", "Maxima presencia", ["Nivel 5"])
                ])
            ],
            []);
    }

    public static RequirementExperienceCatalog CreateV2()
    {
        return new RequirementExperienceCatalog(
            V2Version,
            [
                Question("B01", "Vista / diseño", "¿Qué protagonismo quiere darle al vidrio en este espacio?", [
                    Option("VIS_1", "Funcional", "Equilibrada", ["Dimensiones del vano", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("VIS_2", "Más vidrio", "Más amplitud", ["Dimensiones del vano", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("VIS_3", "Máxima limpieza visual", "Máxima vista", ["Dimensiones del vano", "HardRuleFlag=Sí", "UiVisibility=Sí"])
                ]),
                Question("B02", "Tranquilidad acústica", "¿Qué nivel de tranquilidad necesita en este espacio?", [
                    Option("ACU_1", "Estándar", "Estándar", ["Fuente de ruido / diagnóstico", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("ACU_2", "Reducir ruido cotidiano", "Menos ruido", ["Fuente de ruido / diagnóstico", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("ACU_3", "Alta tranquilidad", "Alta tranquilidad", ["Fuente de ruido / diagnóstico", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("ACU_4", "Es crítico", "Máxima prioridad", ["Fuente de ruido / diagnóstico", "HardRuleFlag=Sí", "UiVisibility=Sí"])
                ]),
                Question("B03", "Confort térmico", "¿Qué tan importante es mantener estable la temperatura de este espacio?", [
                    Option("TER_1", "Estándar", "Estándar", ["Clima + orientación/exposición", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("TER_2", "Importante", "Más estable", ["Clima + orientación/exposición", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("TER_3", "Muy importante", "Alto confort", ["Clima + orientación/exposición", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("TER_4", "Crítico", "Máximo confort", ["Clima + orientación/exposición", "HardRuleFlag=Sí", "UiVisibility=Sí"])
                ]),
                Question("B04", "Protección solar / UV", "¿Qué quiere proteger del sol en este espacio?", [
                    Option("SOL_1", "Sin necesidad especial", "Estándar", ["Orientación / exposición / acabados sensibles", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("SOL_2", "Proteger interiores y acabados", "Protección UV", ["Orientación / exposición / acabados sensibles", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("SOL_3", "Reducir calor y deslumbramiento", "Control solar", ["Orientación / exposición / acabados sensibles", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("SOL_4", "Alta exposición solar", "Alta protección", ["Orientación / exposición / acabados sensibles", "HardRuleFlag=No", "UiVisibility=Condicional"])
                ]),
                Question("B05", "Seguridad", "¿Qué nivel de protección quiere para esta ventana?", [
                    Option("SEG_1", "Estándar", "Estándar", ["Uso + piso + acceso exterior", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("SEG_2", "Reforzada", "Reforzada", ["Uso + piso + acceso exterior", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("SEG_3", "Alta seguridad", "Alta protección", ["Uso + piso + acceso exterior", "HardRuleFlag=Sí", "UiVisibility=Sí"])
                ]),
                Question("B06", "Apertura / ventilación", "¿Cómo quiere relacionar este espacio con el exterior?", [
                    Option("APE_1", "Apertura ocasional", "Ocasional", ["Tipo de vano + uso", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("APE_2", "Ventilación frecuente", "Frecuente", ["Tipo de vano + uso", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("APE_3", "Gran apertura", "Gran apertura", ["Tipo de vano + uso", "HardRuleFlag=Sí", "UiVisibility=Sí"]),
                    Option("APE_4", "Máxima conexión interior–exterior", "Máxima conexión", ["Tipo de vano + uso", "HardRuleFlag=Sí", "UiVisibility=Sí"])
                ]),
                Question("B07", "Hermeticidad", "¿Qué nivel de protección necesita frente a lluvia, viento y corrientes de aire?", [
                    Option("HER_1", "Estándar", "Estándar", ["Exposición + altura + fachada", "HardRuleFlag=Sí", "UiVisibility=Condicional"]),
                    Option("HER_2", "Alta", "Alta protección", ["Exposición + altura + fachada", "HardRuleFlag=Sí", "UiVisibility=Condicional"]),
                    Option("HER_3", "Exposición crítica", "Exposición crítica", ["Exposición + altura + fachada", "HardRuleFlag=Sí", "UiVisibility=Condicional"])
                ]),
                Question("B08", "Privacidad", "¿Qué nivel de privacidad necesita sin perder calidad de luz?", [
                    Option("PRI_1", "Transparente", "Abierto", ["Tipo de espacio", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("PRI_2", "Privacidad parcial", "Privacidad parcial", ["Tipo de espacio", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("PRI_3", "Alta privacidad", "Alta privacidad", ["Tipo de espacio", "HardRuleFlag=No", "UiVisibility=Condicional"])
                ]),
                Question("B09", "Paso / umbral", "¿Qué tan continua quiere sentir la transición hacia la terraza o exterior?", [
                    Option("UMB_1", "Convencional", "Convencional", ["Puerta a terraza / nivel exterior", "HardRuleFlag=Sí", "UiVisibility=Condicional"]),
                    Option("UMB_2", "Más limpio", "Transición limpia", ["Puerta a terraza / nivel exterior", "HardRuleFlag=Sí", "UiVisibility=Condicional"]),
                    Option("UMB_3", "Lo más continuo posible", "Mínimo umbral", ["Puerta a terraza / nivel exterior", "HardRuleFlag=Sí", "UiVisibility=Condicional"])
                ]),
                Question("B10", "Control de insectos", "¿Necesita poder ventilar este espacio sin ingreso de insectos?", [
                    Option("MOS_0", "No", "No requerido", ["Apertura disponible", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("MOS_1", "Sí", "Sí", ["Apertura disponible", "HardRuleFlag=No", "UiVisibility=Condicional"])
                ]),
                Question("B11", "Acabado", "¿Cómo quiere que la ventana dialogue con la arquitectura y los interiores?", [
                    Option("ACA_1", "Neutro / estándar", "Neutro", ["Paleta / arquitectura", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("ACA_2", "Integrado a la arquitectura", "Integrado", ["Paleta / arquitectura", "HardRuleFlag=No", "UiVisibility=Condicional"]),
                    Option("ACA_3", "Acabado protagonista", "Protagonista", ["Paleta / arquitectura", "HardRuleFlag=No", "UiVisibility=Condicional"])
                ])
            ],
            [
                Space("ESP_ALC_PPAL", "Alcoba principal", new Dictionary<string, int> { ["B01"] = 2, ["B02"] = 3, ["B03"] = 3, ["B04"] = 2, ["B05"] = 2, ["B06"] = 2, ["B07"] = 2, ["B08"] = 2, ["B09"] = 0, ["B10"] = 2, ["B11"] = 2 }),
                Space("ESP_ALC_AUX", "Alcoba auxiliar", new Dictionary<string, int> { ["B01"] = 2, ["B02"] = 3, ["B03"] = 3, ["B04"] = 2, ["B05"] = 2, ["B06"] = 2, ["B07"] = 2, ["B08"] = 2, ["B09"] = 0, ["B10"] = 2, ["B11"] = 1 }),
                Space("ESP_SALA", "Sala / estar", new Dictionary<string, int> { ["B01"] = 3, ["B02"] = 2, ["B03"] = 3, ["B04"] = 3, ["B05"] = 2, ["B06"] = 3, ["B07"] = 3, ["B08"] = 0, ["B09"] = 1, ["B10"] = 2, ["B11"] = 2 }),
                Space("ESP_COMEDOR", "Comedor", new Dictionary<string, int> { ["B01"] = 3, ["B02"] = 2, ["B03"] = 3, ["B04"] = 3, ["B05"] = 2, ["B06"] = 2, ["B07"] = 3, ["B08"] = 0, ["B09"] = 1, ["B10"] = 1, ["B11"] = 2 }),
                Space("ESP_COCINA", "Cocina", new Dictionary<string, int> { ["B01"] = 2, ["B02"] = 1, ["B03"] = 2, ["B04"] = 2, ["B05"] = 2, ["B06"] = 3, ["B07"] = 2, ["B08"] = 1, ["B09"] = 0, ["B10"] = 3, ["B11"] = 2 }),
                Space("ESP_ESTUDIO", "Estudio / oficina", new Dictionary<string, int> { ["B01"] = 2, ["B02"] = 3, ["B03"] = 3, ["B04"] = 2, ["B05"] = 2, ["B06"] = 2, ["B07"] = 2, ["B08"] = 1, ["B09"] = 0, ["B10"] = 1, ["B11"] = 2 }),
                Space("ESP_BANO", "Baño", new Dictionary<string, int> { ["B01"] = 1, ["B02"] = 1, ["B03"] = 1, ["B04"] = 1, ["B05"] = 2, ["B06"] = 3, ["B07"] = 2, ["B08"] = 3, ["B09"] = 0, ["B10"] = 2, ["B11"] = 1 }),
                Space("ESP_VESTIER", "Vestier", new Dictionary<string, int> { ["B01"] = 1, ["B02"] = 1, ["B03"] = 1, ["B04"] = 1, ["B05"] = 1, ["B06"] = 1, ["B07"] = 1, ["B08"] = 3, ["B09"] = 0, ["B10"] = 0, ["B11"] = 1 }),
                Space("ESP_TERRAZA", "Puerta terraza", new Dictionary<string, int> { ["B01"] = 3, ["B02"] = 1, ["B03"] = 2, ["B04"] = 2, ["B05"] = 3, ["B06"] = 3, ["B07"] = 3, ["B08"] = 0, ["B09"] = 3, ["B10"] = 2, ["B11"] = 2 }),
                Space("ESP_FACHADA", "Fachada fija", new Dictionary<string, int> { ["B01"] = 3, ["B02"] = 2, ["B03"] = 3, ["B04"] = 3, ["B05"] = 3, ["B06"] = 0, ["B07"] = 3, ["B08"] = 1, ["B09"] = 0, ["B10"] = 0, ["B11"] = 2 }),
                Space("ESP_SOC_EXT", "Zona social exterior", new Dictionary<string, int> { ["B01"] = 3, ["B02"] = 1, ["B03"] = 2, ["B04"] = 3, ["B05"] = 2, ["B06"] = 3, ["B07"] = 3, ["B08"] = 0, ["B09"] = 2, ["B10"] = 3, ["B11"] = 2 }),
                Space("ESP_CIRC", "Escalera / circulación", new Dictionary<string, int> { ["B01"] = 2, ["B02"] = 1, ["B03"] = 1, ["B04"] = 2, ["B05"] = 2, ["B06"] = 1, ["B07"] = 2, ["B08"] = 1, ["B09"] = 0, ["B10"] = 0, ["B11"] = 1 })
            ]);
    }

    private static RequirementExperienceQuestion Question(
        string benefitCode,
        string label,
        string question,
        IReadOnlyList<RequirementExperienceOption> options)
    {
        return new RequirementExperienceQuestion(
            benefitCode,
            label,
            question,
            options,
            false);
    }

    private static RequirementExperienceOption Option(
        string optionCode,
        string optionLabel,
        string shortLabel,
        IReadOnlyList<string> conditions)
    {
        return new RequirementExperienceOption(optionCode, optionLabel, shortLabel, conditions);
    }

    private static RequirementExperienceSpace Space(
        string code,
        string label,
        IReadOnlyDictionary<string, int> priorities)
    {
        return new RequirementExperienceSpace(code, label, priorities, false);
    }
}

public sealed record RequirementExperienceQuestion(
    string BenefitCode,
    string Label,
    string Question,
    IReadOnlyList<RequirementExperienceOption> Options,
    bool ReferenceOnly);

public sealed record RequirementExperienceOption(
    string OptionCode,
    string OptionLabel,
    string ShortLabel,
    IReadOnlyList<string> Conditions);

public sealed record RequirementExperienceSpace(
    string Code,
    string Label,
    IReadOnlyDictionary<string, int> Priorities,
    bool ReferenceOnly);

