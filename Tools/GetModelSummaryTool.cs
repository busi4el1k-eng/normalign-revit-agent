using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// Reads the open Revit model and produces a compact text summary
    /// (levels, element counts per category, room names). This is the Revit
    /// equivalent of the web app's ifc-summary.ts, and it is sent to the backend
    /// as ifcContext.summary so the RAG pipeline can reason about the model.
    /// </summary>
    public class GetModelSummaryTool : IRevitTool
    {
        public string Name => "get_model_summary";
        public string Description => "Rezumatul modelului Revit curent: niveluri, categorii de elemente, camere.";

        public string Execute(Document? doc, string argsJson)
        {
            if (doc == null)
                return "Niciun model deschis în Revit.";

            var sb = new StringBuilder();
            sb.AppendLine($"Model Revit: {doc.Title}");

            // Levels / storeys
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            if (levels.Count > 0)
                sb.AppendLine($"Niveluri ({levels.Count}): " + string.Join(", ", levels.Select(l => l.Name)));

            // Element counts per (model) category
            var counts = new Dictionary<string, int>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                Category? cat = e.Category;
                if (cat == null || cat.CategoryType != CategoryType.Model)
                    continue;
                counts.TryGetValue(cat.Name, out int c);
                counts[cat.Name] = c + 1;
            }

            if (counts.Count > 0)
            {
                sb.AppendLine("Elemente pe categorie:");
                foreach (var kv in counts.OrderByDescending(k => k.Value).Take(25))
                    sb.AppendLine($"  - {kv.Key}: {kv.Value}");
            }

            // Rooms
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToElements()
                .Select(r => r.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .Take(40)
                .ToList();
            if (rooms.Count > 0)
                sb.AppendLine($"Camere ({rooms.Count}): " + string.Join(", ", rooms));

            return sb.ToString();
        }
    }
}
