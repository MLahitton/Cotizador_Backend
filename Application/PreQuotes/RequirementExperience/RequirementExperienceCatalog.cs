namespace Application.PreQuotes.RequirementExperience;

public interface IRequirementExperienceCatalogProvider
{
    RequirementExperienceCatalog Current { get; }

    RequirementExperienceCatalog? FindByVersion(string catalogVersion);
}

public sealed class RequirementExperienceCatalogProvider : IRequirementExperienceCatalogProvider
{
    public RequirementExperienceCatalog Current { get; } = RequirementExperienceCatalog.CreateCurrent();

    public RequirementExperienceCatalog? FindByVersion(string catalogVersion)
    {
        return string.Equals(catalogVersion, Current.Version, StringComparison.Ordinal)
            ? Current
            : null;
    }
}

public sealed record RequirementExperienceCatalog(
    string Version,
    IReadOnlyList<RequirementExperienceQuestion> Questions,
    IReadOnlyList<RequirementExperienceSpace> Spaces)
{
    public static RequirementExperienceCatalog CreateCurrent()
    {
        return new RequirementExperienceCatalog(
            
"sng-experience-v2-draft-001"
,
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

