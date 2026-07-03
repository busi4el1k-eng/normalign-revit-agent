using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// Rezumat compact al modelului deschis (niveluri, categorii, camere cu arii,
    /// inventar tipuri de pereți). Echivalentul ifc-summary.ts din aplicația web;
    /// trimis ca ifcContext.summary la fiecare întrebare și disponibil agentului
    /// ca tool.
    /// </summary>
    public class GetModelSummaryTool : IRevitTool
    {
        public string Name => "get_model_summary";
        public string Description => "Rezumatul modelului Revit curent: niveluri (cu cote), numărul de elemente pe categorie, camerele cu ariile lor, tipurile de pereți folosite. Punct de plecare pentru orice analiză a modelului.";
        public string InputSchema => """{"type":"object","properties":{}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
            => Summarize(app.ActiveUIDocument?.Document);

        /// <summary>Folosit și direct de RevitRequestHandler.BuildContext (chat).</summary>
        public static string Summarize(Document? doc)
        {
            if (doc == null)
                return "Niciun model deschis în Revit.";

            var sb = new StringBuilder();
            sb.AppendLine($"Model Revit: {doc.Title}");

            // Niveluri, cu cote în metri
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .ToList();
            if (levels.Count > 0)
                sb.AppendLine($"Niveluri ({levels.Count}): " + string.Join(", ",
                    levels.Select(l => $"{l.Name} (cota {l.Elevation * 0.3048:0.00} m)")));

            // Elemente pe categorie (doar categorii Model)
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

            // Camere, cu arii (m²) unde există
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .ToElements()
                .Select(r =>
                {
                    double areaFt2 = (r as Autodesk.Revit.DB.Architecture.Room)?.Area ?? 0;
                    string area = areaFt2 > 0 ? $" ({areaFt2 * 0.09290304:0.0} m²)" : "";
                    return string.IsNullOrWhiteSpace(r.Name) ? null : r.Name + area;
                })
                .Where(n => n != null)
                .Distinct()
                .Take(40)
                .ToList();
            if (rooms.Count > 0)
                sb.AppendLine($"Camere ({rooms.Count}): " + string.Join(", ", rooms));

            // Inventar tipuri de pereți folosite (utile pentru conformitate/fișe tehnice)
            var wallTypes = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .GroupBy(w => (doc.GetElement(w.GetTypeId()) as ElementType)?.Name ?? "?")
                .OrderByDescending(g => g.Count())
                .Take(15)
                .Select(g => $"{g.Key} ×{g.Count()}")
                .ToList();
            if (wallTypes.Count > 0)
                sb.AppendLine("Tipuri de pereți: " + string.Join(", ", wallTypes));

            return sb.ToString();
        }
    }
}
