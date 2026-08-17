namespace CotizadorBackend.Tests.Infrastructure.DocumentProcessing;

internal static class Ai2RequirementExtractionPayloads
{
    public const string RealisticPdf =
        """
        {
          "requirement": {"project_name":{"value":"Proyecto PDF","status":"explicit"}},
          "sources": [{"id":"source-1","file_name":"requerimiento.pdf","media_type":"application/pdf","page_count":2}],
          "elements": [
            {
              "id":"e-v01","reference":{"value":"V-01","status":"explicit"},"name":{"value":"Ventanal tipo","status":"explicit"},
              "category":{"normalized":null,"raw":"VENTANAL TIPO","status":"explicit"},
              "measurements":[{"type":"width","value":1200,"unit":"mm"},{"type":"height","value":1500,"unit":"mm"},{"type":"area","value":1.8,"unit":"m2"}],
              "quantity":{"value":1,"status":"explicit"},"configuration":{"raw_description":"Corrediza","status":"explicit"},
              "glass":[{"type":{"normalized":null,"raw":"templado","status":"explicit"},"thickness":{"value":6.0,"unit":"mm","status":"explicit"},"composition":"monolitico","status":"explicit","evidence_ids":["ev-v01"]}],
              "profiles":[{"code":{"value":"3831","status":"explicit"},"status":"explicit"}],
              "finish":{"normalized_type":null,"code":null,"color":null,"raw_description":"Negro pintura al horno","status":"explicit"},
              "evidence_ids":["ev-v01"],"missing_fields":[],"confidence":0.95
            },
            {
              "id":"e-pv06","reference":{"value":"PV-06","status":"explicit"},"name":{"value":"Puerta vidriera","status":"explicit"},
              "category":{"normalized":null,"raw":"PUERTA VIDRIERA","status":"explicit"},
              "measurements":[{"type":"width","value":3740,"unit":"mm"},{"type":"height","value":2500,"unit":"mm"},{"type":"area","value":9.35,"unit":"m2"}],
              "quantity":{"value":1,"status":"explicit"},"configuration":{"raw_description":"Corrediza","status":"explicit"},
              "glass":[{"type":{"normalized":null,"raw":"templado","status":"explicit"},"thickness":{"value":6.0,"unit":"mm","status":"explicit"},"composition":"monolitico","status":"explicit","evidence_ids":["ev-pv06"]}],
              "profiles":[{"code":{"value":"3831","status":"explicit"},"status":"explicit"}],
              "finish":{"normalized_type":null,"code":null,"color":null,"raw_description":"Negro pintura al horno","status":"explicit"},
              "evidence_ids":["ev-pv06"],"missing_fields":[],"confidence":0.94
            },
            {
              "id":"e-v14","reference":{"value":"V-14","status":"explicit"},"name":{"value":"Ventanal tipo","status":"explicit"},
              "category":{"normalized":null,"raw":"VENTANAL TIPO","status":"explicit"},
              "measurements":[{"type":"width","value":2000,"unit":"mm"},{"type":"height","value":1800,"unit":"mm"},{"type":"area","value":3.6,"unit":"m2"}],
              "quantity":{"value":1,"status":"explicit"},"glass":[{"type":{"normalized":null,"raw":"templado","status":"explicit"},"thickness":{"value":8,"unit":"mm"},"composition":"monolitico","status":"explicit","evidence_ids":["ev-v14"]}],
              "profiles":[{"code":{"value":"K50","status":"explicit"},"status":"explicit"}],"evidence_ids":["ev-v14"],"missing_fields":[]
            },
            {
              "id":"e-pv15","reference":{"value":"PV-15","status":"explicit"},"name":{"value":"Puerta vidriera","status":"explicit"},
              "category":{"normalized":null,"raw":"PUERTA VIDRIERA","status":"explicit"},
              "measurements":[{"type":"width","value":5320,"unit":"mm"},{"type":"height","value":2500,"unit":"mm"},{"type":"area","value":1.33,"unit":"m2"}],
              "quantity":{"value":1,"status":"explicit"},"glass":[{"type":{"normalized":null,"raw":"templado","status":"explicit"},"thickness":{"value":10,"unit":"mm"},"composition":"monolitico","status":"explicit","evidence_ids":["ev-pv15"]}],
              "profiles":[{"code":{"value":"3831","status":"explicit"},"status":"explicit"}],"evidence_ids":["ev-pv15"],"missing_fields":[]
            }
          ],
          "evidence": [
            {"id":"ev-v01","source_id":"source-1","page_number":1,"extracted_text":"V-01","status":"explicit"},
            {"id":"ev-pv06","source_id":"source-1","page_number":1,"extracted_text":"PV-06","status":"explicit"},
            {"id":"ev-v14","source_id":"source-1","page_number":2,"extracted_text":"V-14","status":"explicit"},
            {"id":"ev-pv15","source_id":"source-1","page_number":2,"extracted_text":"PV-15 area 1.33","status":"explicit"},
            {"id":"ev-null-page","source_id":"source-1","page_number":null,"extracted_text":"Contexto sin pagina","status":"explicit"}
          ],
          "relationships":[],"conflicts":[],
          "warnings":[
            {"code":"MEASUREMENT_AREA_MISMATCH","message":"Area reportada diferente.","element_id":"e-pv15","evidence_ids":["ev-pv15"]},
            {"code":"enrichment_warning","message":"Enriquecimiento parcial.","evidence_ids":["ev-null-page"]}
          ],
          "extraction_metadata":{"schema_version":"1.0","model_provider":"gemini","model":"configured-model","started_at":null,"completed_at":null,"processing_time_ms":null,"source_count":1,"element_count":4,"partial":false,"status":"completed","token_usage":null,"pipeline_version":"ai2-v1"}
        }
        """;
}
