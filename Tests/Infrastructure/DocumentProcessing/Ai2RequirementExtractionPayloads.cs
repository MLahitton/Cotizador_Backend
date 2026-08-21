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

    public const string RealCurrentAi2Shape =
        """
        {
          "requirement": {
            "project_name": {"value":"Casa Prueba","status":"explicit","confidence":0.98,"evidence_ids":["ev-project"]},
            "client_name": {"value":"Cliente Prueba","status":"explicit","confidence":0.94,"evidence_ids":["ev-project"]},
            "location": {"value":"Bucaramanga","status":"inferred","confidence":0.76,"evidence_ids":["ev-project"]}
          },
          "sources": [
            {"id":"source-pdf","file_name":"casa.pdf","media_type":"application/pdf","page_count":3}
          ],
          "elements": [
            {
              "id":"element-pv06",
              "reference":{"value":"PV-06","raw":"PV-06","status":"explicit","confidence":0.99,"evidence_ids":["ev-item"]},
              "name":{"value":"Puerta vidriera corrediza","raw":"Puerta vidriera corrediza","status":"explicit","confidence":0.96,"evidence_ids":["ev-item"]},
              "category":{"normalized":"PUERTA VIDRIERA","raw":"Puerta vidriera","status":"explicit","confidence":0.92,"evidence_ids":["ev-item"]},
              "measurements":[
                {"type":"width","value":3740,"unit":"mm","status":"explicit","confidence":0.99,"evidence_ids":["ev-item"]},
                {"type":"height","value":2500,"unit":"mm","status":"explicit","confidence":0.99,"evidence_ids":["ev-item"]},
                {"type":"area","value":9.35,"unit":"m2","status":"explicit","confidence":0.80,"evidence_ids":["ev-area"]}
              ],
              "quantity":{"value":1,"status":"explicit","confidence":0.99,"evidence_ids":["ev-item"]},
              "functional_type":{"normalized":"SLIDING_DOOR","raw":"Puerta corrediza","status":"inferred","confidence":0.86,"evidence_ids":["ev-item"]},
              "configuration":{
                "raw_description":"Corrediza 4 paneles",
                "operation":{"normalized":"SLIDING","raw":"corrediza","status":"explicit","confidence":0.90,"evidence_ids":["ev-item"]},
                "panel_count":{"value":4,"status":"explicit","confidence":0.88,"evidence_ids":["ev-item"]},
                "movable_panel_count":{"value":2,"status":"explicit","confidence":0.88,"evidence_ids":["ev-item"]},
                "fixed_panel_count":{"value":2,"status":"explicit","confidence":0.88,"evidence_ids":["ev-item"]},
                "special_features":["POCKET", "LOWER_FIXED_PANEL"],
                "status":"explicit",
                "evidence_ids":["ev-item"]
              },
              "geometry":{"normalized_type":"RECTANGULAR","raw":"rectangular","status":"explicit","confidence":0.95,"evidence_ids":["ev-item"]},
              "glass":[{
                "type":{"normalized":"TEMP_6","raw":"Templado","status":"explicit","confidence":0.94,"evidence_ids":["ev-glass"]},
                "thickness":{"value":6,"unit":"mm","status":"explicit","confidence":0.94,"evidence_ids":["ev-glass"]},
                "treatment":{"normalized":"CLEAR","raw":"claro","status":"inferred","confidence":0.70,"evidence_ids":["ev-glass"]},
                "status":"explicit",
                "confidence":0.94,
                "evidence_ids":["ev-glass"]
              }],
              "profiles":[{"code":{"value":"3831","status":"explicit","confidence":0.93,"evidence_ids":["ev-system"]},"status":"explicit","confidence":0.93,"evidence_ids":["ev-system"]}],
              "finish":{"normalized_type":"BLACK_MATTE","raw_description":"Negro pintura al horno","status":"explicit","confidence":0.91,"evidence_ids":["ev-finish"]},
              "evidence_ids":["ev-item", "ev-ambiguous"],
              "missing_fields":[],
              "confidence":0.91
            }
          ],
          "evidence": [
            {"id":"ev-project","source_id":"source-pdf","page_number":1,"extracted_text":"Casa Prueba","status":"explicit","confidence":0.98},
            {"id":"ev-item","source_id":"source-pdf","page_number":2,"extracted_text":"PV-06 Puerta vidriera corrediza 3740 x 2500","status":"explicit","confidence":0.96},
            {"id":"ev-area","source_id":"source-pdf","page_number":2,"extracted_text":"Area reportada 9.35 m2","status":"explicit","confidence":0.80},
            {"id":"ev-glass","source_id":"source-pdf","page_number":2,"extracted_text":"Vidrio templado 6 mm claro","status":"explicit","confidence":0.94},
            {"id":"ev-system","source_id":"source-pdf","page_number":2,"extracted_text":"Sistema 3831","status":"explicit","confidence":0.93},
            {"id":"ev-finish","source_id":"source-pdf","page_number":2,"extracted_text":"Negro pintura al horno","status":"explicit","confidence":0.91},
            {"id":"ev-ambiguous","source_id":"source-pdf","page_number":null,"extracted_text":"Nota ambigua sin pagina","status":"ambiguous","confidence":0.40}
          ],
          "relationships":[],
          "conflicts":[],
          "warnings":[
            {"code":"MEASUREMENT_AREA_MISMATCH","message":"Area reportada difiere del calculo.","element_id":"element-pv06","evidence_ids":["ev-area"]}
          ],
          "extraction_metadata":{
            "schema_version":"1.0",
            "model_provider":"gemini",
            "model":"configured-model",
            "processing_time_ms":null,
            "source_count":1,
            "element_count":1,
            "partial":false,
            "status":"completed",
            "pipeline_version":"ai2-v1"
          }
        }
        """;
}
