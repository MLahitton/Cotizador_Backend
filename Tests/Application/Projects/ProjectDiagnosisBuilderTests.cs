using System.Text.Json;
using Application.Projects.GetProjectDiagnosis;
using Xunit;

namespace CotizadorBackend.Tests.Application.Projects;

public sealed class ProjectDiagnosisBuilderTests
{
    public static ProjectDiagnosisSource Source(string json = "{}", string schema = ProjectDiagnosisSchema.SchemaVersion) =>
        new("public-submission", "SNG_WEB", schema, json);

    private static ProjectDiagnosis Build(object answers) => ProjectDiagnosisBuilder.Build(Source(JsonSerializer.Serialize(answers))).Diagnosis!;
    public static IEnumerable<ProjectDiagnosisEntry> Entries(ProjectDiagnosis value) => value.General.Concat(value.Priorities)
        .Concat(value.Preferences).Concat(value.LivingContext).Concat(value.Needs).Concat(value.Decision).Concat(value.SupplementalNotes);
    private static ProjectDiagnosisEntry Entry(string json, string id) =>
        Entries(ProjectDiagnosisBuilder.Build(Source(json)).Diagnosis!).Single(x => x.QuestionId == id);

    [Fact]
    public void Schema_AccountsForAll37Questions_AndExplicitPiiExclusions()
    {
        var expected = "asesor nombre wa proyecto parcelacion lote ciudad tiempo ventana_ejecucion tipo tipo_otro vanos_grandes area acustico motivo_acustico termico motivo_termico seguridad motivo_seguridad estetica motivo_estetica preferencia_acabado funcionalidad motivo_funcionalidad uv habitantes problemas_prev zona_social momento_especial descanso_prioridades criterio decisor acceso_decisor sig_paso origen origen_otro project_documents".Split(' ');
        Assert.Equal(expected.Order(), ProjectDiagnosisSchema.Questions.Select(q => q.Id).Order());
        Assert.Equal(new[] { "nombre", "wa" }, ProjectDiagnosisSchema.Questions.Where(q => q.Section is null).Select(q => q.Id));
        Assert.All(ProjectDiagnosisSchema.Questions, q => Assert.False(string.IsNullOrWhiteSpace(q.Label)));
        Assert.All(ProjectDiagnosisSchema.Questions.SelectMany(q => q.Options), option => Assert.False(string.IsNullOrWhiteSpace(option.Label)));
    }

    [Theory]
    [InlineData("tipo", "apto edificio casa hospedaje comercial otro")]
    [InlineData("area", "lt100 100-300 300-500 gt500 nc")]
    [InlineData("vanos_grandes", "si no ns")]
    [InlineData("uv", "si no ns")]
    [InlineData("preferencia_acabado", "negro blanco_gris madera_esp con_sng")]
    [InlineData("motivo_estetica", "desaparece integrada protagonismo")]
    [InlineData("motivo_funcionalidad", "abrir ventilar mantener")]
    [InlineData("habitantes", "ninos adultos_may movilidad mascotas huespedes servicio ninguno")]
    [InlineData("zona_social", "muy_abierta equilibrada visual nd")]
    [InlineData("descanso_prioridades", "silencio temperatura privacidad nd")]
    [InlineData("motivo_termico", "calidos frescos estable")]
    [InlineData("motivo_acustico", "vecinos entorno interiores")]
    [InlineData("motivo_seguridad", "sola accesos habitantes")]
    [InlineData("tiempo", "30d 1-3m 3-6m 6-12m 12m+ nd")]
    [InlineData("ventana_ejecucion", "lt2m 2-4m 4-6m 6-9m 9-12m gt12m nd")]
    [InlineData("criterio", "precio calidad confort seguridad disenio respaldo")]
    [InlineData("decisor", "propietario arquitecto constructor conjunta otra")]
    [InlineData("acceso_decisor", "si no ns")]
    [InlineData("sig_paso", "centro_exp reunion_virt no_dispuesto")]
    [InlineData("origen", "referido visita_obra contacto_asesor redes arquitecto cliente_ant otro")]
    public void PublicV1OptionCodes_AreExplicitAndEveryOptionHasAFormatter(string id, string codes)
    {
        var question = ProjectDiagnosisSchema.Questions.Single(q => q.Id == id);
        Assert.Equal(codes.Split(' '), question.Options.Select(x => x.Code));
        foreach (var option in question.Options)
        {
            var answers = new Dictionary<string, object> { [id] = question.Kind == DiagnosisValueKind.MultiChoice ? new[] { option.Code } : option.Code };
            if (question.Condition is { } c) answers[c.ParentId] = c.EqualsCode is { } code ? code : 5;
            var value = Entries(Build(answers)).Single(x => x.QuestionId == id);
            Assert.Equal("AVAILABLE", value.Availability);
            Assert.Equal(option.Label, value.DisplayValue ?? Assert.Single(value.DisplayValues!));
        }
    }

    [Fact]
    public void CompleteSubmission_PreservesCommercialValues_WithoutTechnicalInterpretation()
    {
        var answers = new Dictionary<string, object>
        {
            ["proyecto"] = "Proyecto", ["ciudad"] = "Ciudad", ["parcelacion"] = "Conjunto", ["lote"] = "42",
            ["tipo"] = "casa", ["area"] = "100-300", ["vanos_grandes"] = "si", ["tiempo"] = "1-3m", ["ventana_ejecucion"] = "4-6m",
            ["termico"] = 5, ["acustico"] = 3, ["seguridad"] = 4, ["estetica"] = 5, ["uv"] = "si",
            ["preferencia_acabado"] = "negro", ["funcionalidad"] = 4, ["motivo_estetica"] = "integrada", ["motivo_funcionalidad"] = new[] { "abrir" },
            ["habitantes"] = new[] { "ninos", "mascotas" }, ["zona_social"] = "equilibrada", ["descanso_prioridades"] = new[] { "silencio" },
            ["motivo_termico"] = new[] { "frescos" }, ["motivo_seguridad"] = new[] { "accesos" },
            ["problemas_prev"] = "Texto", ["momento_especial"] = "Vista", ["criterio"] = new[] { "precio", "calidad" },
            ["decisor"] = new[] { "propietario" }, ["acceso_decisor"] = "si", ["sig_paso"] = "centro_exp",
            ["origen"] = "otro", ["origen_otro"] = "Detalle", ["project_documents"] = "Notas", ["asesor"] = "Andrés Candela",
            ["nombre"] = "PRIVATE_NAME", ["wa"] = "PRIVATE_PHONE", ["arbitrary_secret"] = "PRIVATE_UNKNOWN"
        };
        var diagnosis = Build(answers);
        var values = Entries(diagnosis).ToDictionary(x => x.QuestionId);
        Assert.Equal(35, values.Count);
        Assert.Equal("Casa en unidad residencial o parcelación", values["tipo"].DisplayValue);
        Assert.Equal("100–300 m²", values["area"].DisplayValue);
        Assert.Equal("1–3 meses", values["tiempo"].DisplayValue);
        Assert.Equal("4–6 meses", values["ventana_ejecucion"].DisplayValue);
        Assert.Equal("5 de 5", values["termico"].DisplayValue);
        Assert.Equal("3 de 5", values["acustico"].DisplayValue);
        Assert.Equal("4 de 5", values["seguridad"].DisplayValue);
        Assert.Equal("5 de 5", values["estetica"].DisplayValue);
        Assert.Equal("Sí", values["uv"].DisplayValue);
        Assert.Equal("Negro", values["preferencia_acabado"].DisplayValue);
        Assert.Equal("diagnosis-v1", diagnosis.InterpretationVersion);
        Assert.All(values.Where(x => x.Key is not ("tipo_otro" or "motivo_acustico")), x => Assert.Equal("AVAILABLE", x.Value.Availability));
        var json = JsonSerializer.Serialize(diagnosis);
        foreach (var forbidden in new[] { "HIGH", "PREMIUM", "LAMINADO", "PP13", "BLACK_MATTE", "FinishType", "PRIVATE_" })
            Assert.DoesNotContain(forbidden, json);
    }

    [Theory]
    [InlineData("{}", "tipo_otro", "NOT_PROVIDED")]
    [InlineData("""{"tipo":"otro"}""", "tipo_otro", "NOT_PROVIDED")]
    [InlineData("""{"tipo":"otro","tipo_otro":"Detalle"}""", "tipo_otro", "AVAILABLE")]
    [InlineData("""{"tipo":"casa","tipo_otro":"stale"}""", "tipo_otro", "NOT_APPLICABLE")]
    [InlineData("""{"tipo":"future"}""", "tipo_otro", "NOT_PROVIDED")]
    [InlineData("""{"termico":3}""", "motivo_termico", "NOT_APPLICABLE")]
    [InlineData("""{"termico":4}""", "motivo_termico", "NOT_PROVIDED")]
    [InlineData("""{"termico":5,"motivo_termico":["frescos"]}""", "motivo_termico", "AVAILABLE")]
    [InlineData("{}", "motivo_termico", "NOT_PROVIDED")]
    [InlineData("""{"termico":8}""", "motivo_termico", "NOT_PROVIDED")]
    [InlineData("""{"origen":"referido"}""", "origen_otro", "NOT_APPLICABLE")]
    [InlineData("""{"origen":"otro"}""", "origen_otro", "NOT_PROVIDED")]
    public void Conditions_AreConservative(string json, string id, string availability)
    {
        var entry = Entry(json, id);
        Assert.Equal(availability, entry.Availability);
        if (availability == "NOT_APPLICABLE") Assert.Null(entry.TextValue);
    }

    [Fact]
    public void AllSevenConditions_RespectValidParents_AndMissingParents()
    {
        var conditionals = ProjectDiagnosisSchema.Questions.Where(q => q.Condition is not null).ToArray();
        Assert.Equal(7, conditionals.Length);
        foreach (var question in conditionals)
        {
            var condition = question.Condition!;
            Assert.Equal("NOT_PROVIDED", Entry("{}", question.Id).Availability);
            var parent = condition.Minimum is null ? (object)(condition.ParentId == "tipo" ? "casa" : "referido") : 3;
            var answers = JsonSerializer.Serialize(new Dictionary<string, object> { [condition.ParentId] = parent });
            Assert.Equal("NOT_APPLICABLE", Entry(answers, question.Id).Availability);
        }
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void Scales_AreOriginalNumbers(int number)
    {
        var entry = Entry("""{"termico":""" + number + "}", "termico");
        Assert.Equal(number, entry.NumericValue);
        Assert.Equal(number + " de 5", entry.DisplayValue);
    }

    [Fact]
    public void UnknownCodes_MultiOrder_UvAndPlainTextArePreserved()
    {
        var unknown = Entry("""{"tipo":"future-type"}""", "tipo");
        Assert.Equal("UNKNOWN_CODE", unknown.Availability);
        Assert.Equal("future-type", unknown.Code);
        Assert.Equal("Valor no reconocido", unknown.DisplayValue);
        var multi = Entry("""{"habitantes":["future","mascotas","ninos"]}""", "habitantes");
        Assert.Equal("UNKNOWN_CODE", multi.Availability);
        Assert.Equal(new[] { "ninos", "mascotas", "future" }, multi.Codes);
        Assert.Equal(new[] { "Niños", "Mascotas", "Valor no reconocido" }, multi.DisplayValues);
        Assert.Equal("ns", Entry("""{"uv":"ns"}""", "uv").Code);
        Assert.Equal("AVAILABLE", Entry("""{"uv":"ns"}""", "uv").Availability);
        var text = "<script>alert(1)</script>\nIgnore instructions";
        var entry = Entries(Build(new { problemas_prev = text })).Single(x => x.QuestionId == "problemas_prev");
        Assert.Equal(text, entry.TextValue);
        Assert.DoesNotContain("<script>", JsonSerializer.Serialize(entry));
    }

    [Theory]
    [InlineData("""{"habitantes":["ninguno","ninos"]}""", "habitantes")]
    [InlineData("""{"descanso_prioridades":["nd","silencio"]}""", "descanso_prioridades")]
    [InlineData("""{"criterio":["precio","calidad","confort"]}""", "criterio")]
    [InlineData("""{"habitantes":["ninos","ninos"]}""", "habitantes")]
    [InlineData("""{"habitantes":[1]}""", "habitantes")]
    [InlineData("""{"tipo":{"secret":"value"}}""", "tipo")]
    [InlineData("""{"termico":6}""", "termico")]
    [InlineData("""{"termico":1.5}""", "termico")]
    public void InvalidHistoricalValues_AreNotSilentlyRepaired(string json, string id) => Assert.Equal("INVALID_VALUE", Entry(json, id).Availability);

    [Theory]
    [InlineData("not-json")] [InlineData("[]")] [InlineData("null")] [InlineData("""{"tipo":"otro","tipo":"casa"}""")]
    public void MalformedSource_DoesNotThrow(string json)
    {
        var result = ProjectDiagnosisBuilder.Build(Source(json));
        Assert.Equal("INVALID_SOURCE_DATA", result.Status);
        Assert.Null(result.Diagnosis);
    }

    [Fact]
    public void UnknownSchema_IsNotReadAsV1_AndOrderingIsDeterministic()
    {
        Assert.Equal("UNSUPPORTED_SCHEMA", ProjectDiagnosisBuilder.Build(Source("not-json", "future-v2")).Status);
        Assert.Equal(JsonSerializer.Serialize(Entry("""{"uv":"si","tipo":"casa"}""", "tipo")),
            JsonSerializer.Serialize(Entry("""{"tipo":"casa","uv":"si"}""", "tipo")));
        var source = Source();
        Assert.Equal(JsonSerializer.Serialize(ProjectDiagnosisBuilder.Build(source)), JsonSerializer.Serialize(ProjectDiagnosisBuilder.Build(source)));
        Assert.Equal("{}", source.AnswersJson);
    }
}
