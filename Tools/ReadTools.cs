using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace NormalignRevitAgent.Tools
{
    /// <summary>Ajutoare comune pentru tool-uri (parsare argumente, formatare elemente).</summary>
    internal static class ToolHelpers
    {
        public static JsonObject ParseArgs(string argsJson)
        {
            try { return JsonNode.Parse(argsJson) as JsonObject ?? new JsonObject(); }
            catch { return new JsonObject(); }
        }

        public static string? Str(JsonObject args, string key)
            => args[key] is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrWhiteSpace(s) ? s : null;

        public static bool Flag(JsonObject args, string key)
            => args[key] is JsonValue v && v.TryGetValue(out bool b) && b;

        public static double Num(JsonObject args, string key, double fallback = 0)
        {
            if (args[key] is JsonValue v)
            {
                if (v.TryGetValue(out double d)) return d;
                if (v.TryGetValue(out int i)) return i;
            }
            return fallback;
        }

        public static List<ElementId> Ids(JsonObject args, string key = "element_ids")
        {
            var result = new List<ElementId>();
            if (args[key] is JsonArray arr)
                foreach (JsonNode? n in arr)
                {
                    if (n is JsonValue v)
                    {
                        if (v.TryGetValue(out long l)) result.Add(new ElementId(l));
                        else if (v.TryGetValue(out string? s) && long.TryParse(s, out long l2)) result.Add(new ElementId(l2));
                    }
                }
            return result;
        }

        /// <summary>O linie compactă per element: categorie, nume, tip, nivel, id.</summary>
        public static string ElementLine(Document doc, Element e)
        {
            string cat = e.Category?.Name ?? "—";
            string typeName = (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? "";
            string level = (e.LevelId != ElementId.InvalidElementId
                ? (doc.GetElement(e.LevelId) as Level)?.Name : null) ?? "";
            var line = new StringBuilder($"- {cat}: {e.Name}");
            if (!string.IsNullOrEmpty(typeName) && typeName != e.Name) line.Append($" (tip: {typeName})");
            if (!string.IsNullOrEmpty(level)) line.Append($", nivel {level}");
            line.Append($" [id {e.Id.Value}]");
            return line.ToString();
        }

        public static string Error(string message) => $"Eroare: {message}";
        public const string NoModel = "Eroare: niciun model deschis în Revit.";
    }

    /// <summary>
    /// Interogare filtrată a elementelor din model — echivalentul "ai_element_filter"
    /// din ecosistemul revit-mcp. Filtrele se combină (AND).
    /// </summary>
    public class QueryElementsTool : IRevitTool
    {
        public string Name => "query_elements";
        public string Description => "Caută elemente în model după filtre combinabile: categorie (ex. 'Pereți', 'Uși' — numele din get_model_summary), nivel, fragment din numele tipului, fragment din numele elementului, doar view-ul activ. Returnează max `limit` elemente cu id-uri (pentru get_element_details / tool-urile de modificare) plus numărul total de potriviri.";
        public string InputSchema => """{"type":"object","properties":{"category":{"type":"string","description":"Numele categoriei (potrivire parțială, fără diacritice obligatorii)"},"level":{"type":"string","description":"Numele nivelului"},"type_contains":{"type":"string","description":"Fragment din numele tipului"},"name_contains":{"type":"string","description":"Fragment din numele elementului"},"in_active_view":{"type":"boolean","description":"Doar elementele vizibile în view-ul activ"},"limit":{"type":"integer","description":"Max elemente returnate (implicit 30, max 50)"}}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            JsonObject args = ToolHelpers.ParseArgs(argsJson);

            string? cat = ToolHelpers.Str(args, "category");
            string? level = ToolHelpers.Str(args, "level");
            string? typeContains = ToolHelpers.Str(args, "type_contains");
            string? nameContains = ToolHelpers.Str(args, "name_contains");
            bool inView = ToolHelpers.Flag(args, "in_active_view");
            int limit = Math.Clamp((int)ToolHelpers.Num(args, "limit", 30), 1, 50);

            FilteredElementCollector collector;
            try
            {
                collector = inView && doc.ActiveView != null
                    ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                    : new FilteredElementCollector(doc);
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }

            var matches = new List<Element>();
            int total = 0;
            foreach (Element e in collector.WhereElementIsNotElementType())
            {
                Category? c = e.Category;
                if (c == null || c.CategoryType != CategoryType.Model) continue;
                if (cat != null && c.Name.IndexOf(cat, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (level != null)
                {
                    string lv = (e.LevelId != ElementId.InvalidElementId
                        ? (doc.GetElement(e.LevelId) as Level)?.Name : null) ?? "";
                    if (!string.Equals(lv, level, StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (typeContains != null)
                {
                    string tn = (doc.GetElement(e.GetTypeId()) as ElementType)?.Name ?? "";
                    if (tn.IndexOf(typeContains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                if (nameContains != null && e.Name.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;

                total++;
                if (matches.Count < limit) matches.Add(e);
            }

            if (total == 0) return "Niciun element nu corespunde filtrelor.";
            var sb = new StringBuilder($"{total} elemente găsite (afișez {matches.Count}):\n");
            foreach (Element e in matches) sb.AppendLine(ToolHelpers.ElementLine(doc, e));
            return sb.ToString();
        }
    }

    /// <summary>Toți parametrii + geometria de bază pentru elemente date prin id.</summary>
    public class GetElementDetailsTool : IRevitTool
    {
        public string Name => "get_element_details";
        public string Description => "Detalii complete pentru elemente date prin id: toți parametrii cu valori, nivelul, bounding box-ul (în metri). Max 10 elemente per apel.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"},"description":"Id-urile elementelor (din query_elements / selecție)"}},"required":["element_ids"]}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var ids = ToolHelpers.Ids(ToolHelpers.ParseArgs(argsJson));
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc element_ids.");

            var sb = new StringBuilder();
            foreach (ElementId id in ids.Take(10))
            {
                Element? e = doc.GetElement(id);
                if (e == null) { sb.AppendLine($"[id {id.Value}] — nu există."); continue; }

                sb.AppendLine(ToolHelpers.ElementLine(doc, e));
                BoundingBoxXYZ? bb = e.get_BoundingBox(null);
                if (bb != null)
                {
                    const double f = 0.3048;
                    sb.AppendLine($"  bbox (m): min({bb.Min.X * f:0.00}, {bb.Min.Y * f:0.00}, {bb.Min.Z * f:0.00}) max({bb.Max.X * f:0.00}, {bb.Max.Y * f:0.00}, {bb.Max.Z * f:0.00})");
                }
                int shown = 0;
                foreach (Parameter p in e.Parameters)
                {
                    if (!p.HasValue || shown >= 25) continue;
                    string? val = p.StorageType switch
                    {
                        StorageType.String => p.AsString(),
                        StorageType.Integer => p.AsValueString() ?? p.AsInteger().ToString(),
                        StorageType.Double => p.AsValueString(),
                        StorageType.ElementId => (doc.GetElement(p.AsElementId()) as Element)?.Name,
                        _ => null,
                    };
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    sb.AppendLine($"  {p.Definition.Name}: {val}");
                    shown++;
                }
            }
            if (ids.Count > 10) sb.AppendLine($"… {ids.Count - 10} elemente omise (max 10 per apel).");
            return sb.ToString();
        }
    }

    /// <summary>Selecția curentă a utilizatorului, cu detalii.</summary>
    public class GetSelectionTool : IRevitTool
    {
        public string Name => "get_selection";
        public string Description => "Elementele selectate ACUM de utilizator în Revit, cu categorie/tip/nivel/id. Folosește când utilizatorul zice 'elementele selectate', 'peretele ăsta' etc.";
        public string InputSchema => """{"type":"object","properties":{}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            Document? doc = uidoc?.Document;
            if (uidoc == null || doc == null) return ToolHelpers.NoModel;

            ICollection<ElementId> ids;
            try { ids = uidoc.Selection.GetElementIds(); }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
            if (ids.Count == 0) return "Nimic selectat în Revit.";

            var sb = new StringBuilder($"{ids.Count} elemente selectate:\n");
            foreach (ElementId id in ids.Take(30))
            {
                Element? e = doc.GetElement(id);
                if (e != null) sb.AppendLine(ToolHelpers.ElementLine(doc, e));
            }
            if (ids.Count > 30) sb.AppendLine($"… și încă {ids.Count - 30}.");
            return sb.ToString();
        }
    }

    /// <summary>Nivelurile (cu cote) și grilele din model.</summary>
    public class ListLevelsAndGridsTool : IRevitTool
    {
        public string Name => "list_levels_and_grids";
        public string Description => "Toate nivelurile (cu cote în metri) și grilele (axele) din model.";
        public string InputSchema => """{"type":"object","properties":{}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;

            var sb = new StringBuilder();
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.Elevation).ToList();
            sb.AppendLine($"Niveluri ({levels.Count}):");
            foreach (Level l in levels)
                sb.AppendLine($"  - {l.Name}: cota {l.Elevation * 0.3048:0.00} m [id {l.Id.Value}]");

            var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>()
                .OrderBy(g => g.Name).ToList();
            if (grids.Count > 0)
                sb.AppendLine($"Grile ({grids.Count}): " + string.Join(", ", grids.Take(40).Select(g => g.Name)));
            return sb.ToString();
        }
    }

    /// <summary>Tipurile de familii disponibile într-o categorie.</summary>
    public class ListFamilyTypesTool : IRevitTool
    {
        public string Name => "list_family_types";
        public string Description => "Tipurile (ElementType) disponibile în proiect pentru o categorie dată (ex. 'Uși', 'Pereți'), cu id-uri. Util înainte de a schimba tipul unui element.";
        public string InputSchema => """{"type":"object","properties":{"category":{"type":"string","description":"Numele categoriei (potrivire parțială)"}},"required":["category"]}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            string? cat = ToolHelpers.Str(ToolHelpers.ParseArgs(argsJson), "category");
            if (cat == null) return ToolHelpers.Error("lipsește category.");

            // Câte instanțe folosește fiecare tip — ca "nefolosit" să fie vizibil direct.
            var counts = new Dictionary<ElementId, int>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                ElementId tid = e.GetTypeId();
                if (tid == ElementId.InvalidElementId) continue;
                counts[tid] = counts.TryGetValue(tid, out int n) ? n + 1 : 1;
            }

            var types = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(t => t.Category != null && t.Category.Name.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t.FamilyName).ThenBy(t => t.Name)
                .Take(60)
                .ToList();
            if (types.Count == 0) return $"Niciun tip găsit pentru categoria '{cat}'.";

            var sb = new StringBuilder($"Tipuri în categoria potrivită cu '{cat}' ({types.Count}):\n");
            foreach (ElementType t in types)
            {
                string usage = counts.TryGetValue(t.Id, out int n) ? $"×{n}" : "nefolosit";
                sb.AppendLine($"  - {t.FamilyName}: {t.Name} [id {t.Id.Value}] ({usage})");
            }
            return sb.ToString();
        }
    }

    /// <summary>Detalii de TIP: parametri + structura stratificată (grosimi, materiale).</summary>
    public class GetTypeDetailsTool : IRevitTool
    {
        public string Name => "get_type_details";
        public string Description => "Detalii pentru tipuri (ElementType) date prin id (din list_family_types / get_element_details): parametrii de tip și, pentru tipuri stratificate (pereți, planșee, acoperișuri), structura compound: straturile cu funcție, material și grosime în cm + grosimea totală. Folosește ÎNAINTE de consolidări de tipuri, ca să compari structurile. Max 10 per apel.";
        public string InputSchema => """{"type":"object","properties":{"type_ids":{"type":"array","items":{"type":"integer"},"description":"Id-urile tipurilor (ElementType)"}},"required":["type_ids"]}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var ids = ToolHelpers.Ids(ToolHelpers.ParseArgs(argsJson), "type_ids");
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc type_ids.");

            var sb = new StringBuilder();
            foreach (ElementId id in ids.Take(10))
            {
                if (doc.GetElement(id) is not ElementType t)
                { sb.AppendLine($"[id {id.Value}] — nu e un tip (ElementType)."); continue; }

                sb.AppendLine($"- {t.Category?.Name ?? "—"} / {t.FamilyName}: {t.Name} [id {id.Value}]");

                // Structura stratificată (pereți/planșee/acoperișuri de sistem).
                if (t is HostObjAttributes host && host.GetCompoundStructure() is CompoundStructure cs)
                {
                    const double ftToCm = 30.48;
                    sb.AppendLine($"  structură ({cs.GetWidth() * ftToCm:0.0} cm total):");
                    foreach (CompoundStructureLayer layer in cs.GetLayers())
                    {
                        string mat = (doc.GetElement(layer.MaterialId) as Material)?.Name ?? "—";
                        sb.AppendLine($"    · {layer.Function}: {layer.Width * ftToCm:0.0} cm, material {mat}");
                    }
                }

                int shown = 0;
                foreach (Parameter p in t.Parameters)
                {
                    if (!p.HasValue || shown >= 20) continue;
                    string? val = p.StorageType switch
                    {
                        StorageType.String => p.AsString(),
                        StorageType.Integer => p.AsValueString() ?? p.AsInteger().ToString(),
                        StorageType.Double => p.AsValueString(),
                        StorageType.ElementId => (doc.GetElement(p.AsElementId()) as Element)?.Name,
                        _ => null,
                    };
                    if (string.IsNullOrWhiteSpace(val)) continue;
                    sb.AppendLine($"  {p.Definition.Name}: {val}");
                    shown++;
                }
            }
            if (ids.Count > 10) sb.AppendLine($"… {ids.Count - 10} tipuri omise (max 10 per apel).");
            return sb.ToString();
        }
    }

    /// <summary>Informații despre view-ul activ.</summary>
    public class GetActiveViewTool : IRevitTool
    {
        public string Name => "get_active_view";
        public string Description => "View-ul activ din Revit: nume, tip, scară, nivel asociat și câte elemente din fiecare categorie sunt vizibile în el.";
        public string InputSchema => """{"type":"object","properties":{}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            View? view = doc?.ActiveView;
            if (doc == null || view == null) return ToolHelpers.NoModel;

            var sb = new StringBuilder();
            sb.AppendLine($"View activ: {view.Name} ({view.ViewType}) [id {view.Id.Value}]");
            try { sb.AppendLine($"Scara: 1:{view.Scale}"); } catch { }
            if (view.GenLevel != null) sb.AppendLine($"Nivel: {view.GenLevel.Name}");

            var counts = new Dictionary<string, int>();
            try
            {
                foreach (Element e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
                {
                    Category? c = e.Category;
                    if (c == null || c.CategoryType != CategoryType.Model) continue;
                    counts.TryGetValue(c.Name, out int n);
                    counts[c.Name] = n + 1;
                }
                if (counts.Count > 0)
                {
                    sb.AppendLine("Vizibil în view:");
                    foreach (var kv in counts.OrderByDescending(k => k.Value).Take(15))
                        sb.AppendLine($"  - {kv.Key}: {kv.Value}");
                }
            }
            catch { /* unele view-uri (ex. legende) nu suportă colectare */ }
            return sb.ToString();
        }
    }

    /// <summary>Avertismentele active din model (doc.GetWarnings).</summary>
    public class GetModelWarningsTool : IRevitTool
    {
        public string Name => "get_model_warnings";
        public string Description => "Avertismentele (warnings) active din model, cu elementele implicate — pentru audit QA rapid.";
        public string InputSchema => """{"type":"object","properties":{}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;

            IList<FailureMessage> warnings;
            try { warnings = doc.GetWarnings(); }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
            if (warnings.Count == 0) return "Modelul nu are avertismente active.";

            var sb = new StringBuilder($"{warnings.Count} avertismente:\n");
            foreach (FailureMessage w in warnings.Take(30))
            {
                string idsTxt = string.Join(", ", w.GetFailingElements().Take(6).Select(i => i.Value));
                sb.AppendLine($"  - {w.GetDescriptionText()} [elemente: {idsTxt}]");
            }
            if (warnings.Count > 30) sb.AppendLine($"… și încă {warnings.Count - 30}.");
            return sb.ToString();
        }
    }
}
