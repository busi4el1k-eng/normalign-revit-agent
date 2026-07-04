using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace NormalignRevitAgent.Tools
{
    // Tool-uri care MODIFICĂ modelul (IsWrite = true) — declarate serverului doar
    // în modul Edit. Fiecare rulează în propria tranzacție cu nume descriptiv,
    // deci fiecare operație are intrarea ei de Undo în Revit.

    /// <summary>Setează valori de parametri pe elemente (batch).</summary>
    public class SetParametersTool : IRevitTool
    {
        public string Name => "set_parameters";
        public string Description => "Setează valori de parametri pe elemente. Valorile numerice cu unități se dau ca text așa cum apar în Revit (ex. '30 cm', '2.10'). Raportează per schimbare dacă a reușit.";
        public string InputSchema => """{"type":"object","properties":{"changes":{"type":"array","items":{"type":"object","properties":{"element_id":{"type":"integer"},"parameter":{"type":"string","description":"Numele exact al parametrului (vezi get_element_details)"},"value":{"type":"string"}},"required":["element_id","parameter","value"]}}},"required":["changes"]}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var args = ToolHelpers.ParseArgs(argsJson);
            if (args["changes"] is not JsonArray changes || changes.Count == 0)
                return ToolHelpers.Error("lipsește changes.");

            var report = new StringBuilder();
            int ok = 0;
            using (var t = new Transaction(doc, "Normalign: setare parametri"))
            {
                t.Start();
                foreach (JsonNode? n in changes.Take(100))
                {
                    if (n is not JsonObject ch) continue;
                    long idVal = (long)ToolHelpers.Num(ch, "element_id", -1);
                    string? pName = ToolHelpers.Str(ch, "parameter");
                    string? value = ToolHelpers.Str(ch, "value");
                    if (idVal < 0 || pName == null || value == null)
                    { report.AppendLine("  - schimbare invalidă (element_id/parameter/value)"); continue; }

                    Element? e = doc.GetElement(new ElementId(idVal));
                    if (e == null) { report.AppendLine($"  - [id {idVal}] nu există"); continue; }

                    Parameter? p = e.LookupParameter(pName)
                        ?? e.Parameters.Cast<Parameter>().FirstOrDefault(x =>
                            string.Equals(x.Definition.Name, pName, StringComparison.OrdinalIgnoreCase));
                    if (p == null) { report.AppendLine($"  - [id {idVal}] parametrul '{pName}' nu există"); continue; }
                    if (p.IsReadOnly) { report.AppendLine($"  - [id {idVal}] '{pName}' e read-only"); continue; }

                    try
                    {
                        bool set = p.StorageType switch
                        {
                            StorageType.String => p.Set(value),
                            StorageType.Integer => int.TryParse(value, out int iv)
                                ? p.Set(iv)
                                : p.SetValueString(value),
                            StorageType.Double => TrySetDouble(p, value),
                            _ => false,
                        };
                        if (set) { ok++; report.AppendLine($"  - [id {idVal}] '{pName}' → {value} ✓"); }
                        else report.AppendLine($"  - [id {idVal}] '{pName}' nu a acceptat valoarea");
                    }
                    catch (Exception ex) { report.AppendLine($"  - [id {idVal}] '{pName}': {ex.Message}"); }
                }
                if (ok > 0) t.Commit(); else t.RollBack();
            }
            return $"{ok} parametri setați:\n{report}";
        }

        private static bool TrySetDouble(Parameter p, string value)
        {
            // SetValueString interpretează unitățile de afișare ("30 cm", "2.10")
            try { if (p.SetValueString(value)) return true; } catch { }
            return double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out double d) && p.Set(d);
        }
    }

    /// <summary>Schimbă tipul (ElementType) unor elemente — inclusiv între familii din aceeași categorie.</summary>
    public class ChangeElementTypeTool : IRevitTool
    {
        public string Name => "change_element_type";
        public string Description => "Schimbă tipul (Family and Type) elementelor date la un tip țintă dat prin id (vezi list_family_types). Funcționează și între familii diferite din aceeași categorie (ex. consolidarea pereților importați din IFC pe un singur tip), dacă tipul e valid pentru element. Raportează per element dacă a reușit.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"}},"type_id":{"type":"integer","description":"Id-ul tipului țintă (ElementType) — din list_family_types sau get_element_details"}},"required":["element_ids","type_id"]}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var args = ToolHelpers.ParseArgs(argsJson);
            var ids = ToolHelpers.Ids(args);
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc element_ids.");
            long typeIdVal = (long)ToolHelpers.Num(args, "type_id", -1);
            if (typeIdVal < 0) return ToolHelpers.Error("lipsește type_id.");
            var typeId = new ElementId(typeIdVal);
            if (doc.GetElement(typeId) is not ElementType targetType)
                return ToolHelpers.Error($"type_id {typeIdVal} nu e un tip (ElementType) din proiect.");

            var report = new StringBuilder();
            int ok = 0;
            using (var t = new Transaction(doc, "Normalign: schimbare tip elemente"))
            {
                t.Start();
                foreach (ElementId id in ids.Take(200))
                {
                    Element? e = doc.GetElement(id);
                    if (e == null) { report.AppendLine($"  - [id {id.Value}] nu există"); continue; }
                    try
                    {
                        if (!e.GetValidTypes().Contains(typeId))
                        { report.AppendLine($"  - [id {id.Value}] tipul '{targetType.Name}' nu e valid pentru acest element"); continue; }
                        e.ChangeTypeId(typeId);
                        ok++;
                        report.AppendLine($"  - [id {id.Value}] → {targetType.FamilyName}: {targetType.Name} ✓");
                    }
                    catch (Exception ex) { report.AppendLine($"  - [id {id.Value}] {ex.Message}"); }
                }
                if (ok > 0) t.Commit(); else t.RollBack();
            }
            return $"{ok}/{ids.Count} elemente au primit tipul '{targetType.Name}':\n{report}";
        }
    }

    /// <summary>Mută elemente cu un vector dat în milimetri.</summary>
    public class MoveElementsTool : IRevitTool
    {
        public string Name => "move_elements";
        public string Description => "Mută elementele date cu un vector (dx, dy, dz) exprimat în MILIMETRI.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"}},"dx_mm":{"type":"number"},"dy_mm":{"type":"number"},"dz_mm":{"type":"number"}},"required":["element_ids"]}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var args = ToolHelpers.ParseArgs(argsJson);
            var ids = ToolHelpers.Ids(args);
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc element_ids.");

            const double mmToFt = 1.0 / 304.8;
            var vec = new XYZ(
                ToolHelpers.Num(args, "dx_mm") * mmToFt,
                ToolHelpers.Num(args, "dy_mm") * mmToFt,
                ToolHelpers.Num(args, "dz_mm") * mmToFt);
            if (vec.IsZeroLength()) return ToolHelpers.Error("vectorul de mutare e zero.");

            try
            {
                using var t = new Transaction(doc, "Normalign: mutare elemente");
                t.Start();
                ElementTransformUtils.MoveElements(doc, ids, vec);
                t.Commit();
                return $"{ids.Count} elemente mutate cu ({ToolHelpers.Num(args, "dx_mm")}, {ToolHelpers.Num(args, "dy_mm")}, {ToolHelpers.Num(args, "dz_mm")}) mm.";
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
        }
    }

    /// <summary>Șterge elemente din model.</summary>
    public class DeleteElementsTool : IRevitTool
    {
        public string Name => "delete_elements";
        public string Description => "ȘTERGE definitiv elementele date din model (cu Undo disponibil în Revit). Folosește DOAR când utilizatorul a cerut explicit ștergerea și elementele sunt clar identificate.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"}}},"required":["element_ids"]}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var ids = ToolHelpers.Ids(ToolHelpers.ParseArgs(argsJson));
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc element_ids.");

            try
            {
                using var t = new Transaction(doc, "Normalign: ștergere elemente");
                t.Start();
                ICollection<ElementId> deleted = doc.Delete(ids);
                t.Commit();
                return $"{ids.Count} elemente cerute, {deleted.Count} șterse efectiv (inclusiv dependențe).";
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
        }
    }

    /// <summary>Șterge tipurile fără nicio instanță (echivalentul Purge Unused pe tipuri).</summary>
    public class PurgeUnusedTypesTool : IRevitTool
    {
        public string Name => "purge_unused_types";
        public string Description => "Șterge tipurile (ElementType) care nu au NICIO instanță în model — echivalentul Purge Unused pentru tipuri, pe categorii de model. Opțional filtrat pe categorie (ex. 'Pereți'). Cu dry_run=true doar listează ce AR fi șters, fără să modifice. Operație în masă → cere confirmarea utilizatorului înainte (fără dry_run).";
        public string InputSchema => """{"type":"object","properties":{"category":{"type":"string","description":"Numele categoriei (potrivire parțială); lipsă = toate categoriile de model"},"dry_run":{"type":"boolean","description":"true = doar raportează, nu șterge"}}}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null) return ToolHelpers.NoModel;
            var args = ToolHelpers.ParseArgs(argsJson);
            string? cat = ToolHelpers.Str(args, "category");
            bool dryRun = ToolHelpers.Flag(args, "dry_run");

            // Tipurile folosite = GetTypeId() al fiecărei instanțe din model.
            var used = new HashSet<ElementId>();
            foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType())
            {
                ElementId tid = e.GetTypeId();
                if (tid != ElementId.InvalidElementId) used.Add(tid);
            }

            var candidates = new FilteredElementCollector(doc)
                .WhereElementIsElementType()
                .Cast<ElementType>()
                .Where(t => t.Category != null && t.Category.CategoryType == CategoryType.Model)
                .Where(t => cat == null || t.Category!.Name.IndexOf(cat, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(t => !used.Contains(t.Id))
                .OrderBy(t => t.Category!.Name).ThenBy(t => t.FamilyName).ThenBy(t => t.Name)
                .Take(300)
                .ToList();
            if (candidates.Count == 0) return "Niciun tip nefolosit găsit" + (cat != null ? $" în categoria '{cat}'." : ".");

            if (dryRun)
            {
                var sbd = new StringBuilder($"{candidates.Count} tipuri nefolosite (dry run, nimic șters):\n");
                foreach (var g in candidates.GroupBy(t => t.Category!.Name))
                    sbd.AppendLine($"  {g.Key} ({g.Count()}): " + string.Join(", ", g.Take(25).Select(t => t.Name)) + (g.Count() > 25 ? ", …" : ""));
                return sbd.ToString();
            }

            var report = new StringBuilder();
            int ok = 0;
            using (var t = new Transaction(doc, "Normalign: purge tipuri nefolosite"))
            {
                t.Start();
                foreach (ElementType et in candidates)
                {
                    // Un delete anterior poate șterge în cascadă tipuri dependente.
                    if (doc.GetElement(et.Id) == null) { ok++; continue; }
                    string label = $"{et.Category!.Name} / {et.FamilyName}: {et.Name}";
                    try { doc.Delete(et.Id); ok++; }
                    catch (Exception ex) { report.AppendLine($"  - {label}: {ex.Message}"); }
                }
                if (ok > 0) t.Commit(); else t.RollBack();
            }
            string refuzate = report.Length > 0 ? $"\nRefuzate de Revit:\n{report}" : "";
            return $"{ok}/{candidates.Count} tipuri nefolosite șterse (un singur pas de Undo).{refuzate}";
        }
    }

    /// <summary>Selectează + centrează view-ul pe elemente (feedback vizual).</summary>
    public class SelectAndShowTool : IRevitTool
    {
        public string Name => "select_and_show";
        public string Description => "Selectează elementele date în Revit și centrează view-ul pe ele — ca utilizatorul să vadă despre ce vorbești. Nu modifică modelul.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"}}},"required":["element_ids"]}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            UIDocument? uidoc = app.ActiveUIDocument;
            if (uidoc?.Document == null) return ToolHelpers.NoModel;
            var ids = ToolHelpers.Ids(ToolHelpers.ParseArgs(argsJson));
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc element_ids.");

            try
            {
                uidoc.Selection.SetElementIds(ids);
                uidoc.ShowElements(ids);
                return $"{ids.Count} elemente selectate și afișate.";
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
        }
    }

    /// <summary>Colorează elemente în view-ul activ (override grafic) sau resetează.</summary>
    public class OverrideColorInViewTool : IRevitTool
    {
        public string Name => "override_color_in_view";
        public string Description => "Colorează elementele date în view-ul activ (override grafic, nu schimbă materialele) — util pentru evidențiere QA/conformitate. Cu reset=true elimină override-ul.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"}},"r":{"type":"integer","description":"0-255"},"g":{"type":"integer"},"b":{"type":"integer"},"reset":{"type":"boolean"}},"required":["element_ids"]}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            View? view = doc?.ActiveView;
            if (doc == null || view == null) return ToolHelpers.NoModel;
            var args = ToolHelpers.ParseArgs(argsJson);
            var ids = ToolHelpers.Ids(args);
            if (ids.Count == 0) return ToolHelpers.Error("lipsesc element_ids.");

            OverrideGraphicSettings ogs;
            if (ToolHelpers.Flag(args, "reset"))
                ogs = new OverrideGraphicSettings();
            else
            {
                var color = new Color(
                    (byte)Math.Clamp((int)ToolHelpers.Num(args, "r", 255), 0, 255),
                    (byte)Math.Clamp((int)ToolHelpers.Num(args, "g", 0), 0, 255),
                    (byte)Math.Clamp((int)ToolHelpers.Num(args, "b", 0), 0, 255));
                ogs = new OverrideGraphicSettings()
                    .SetProjectionLineColor(color)
                    .SetSurfaceForegroundPatternColor(color)
                    .SetCutForegroundPatternColor(color);
                FillPatternElement? solid = new FilteredElementCollector(doc)
                    .OfClass(typeof(FillPatternElement)).Cast<FillPatternElement>()
                    .FirstOrDefault(f => f.GetFillPattern().IsSolidFill);
                if (solid != null)
                    ogs = ogs.SetSurfaceForegroundPatternId(solid.Id).SetCutForegroundPatternId(solid.Id);
            }

            try
            {
                using var t = new Transaction(doc, "Normalign: evidențiere elemente");
                t.Start();
                foreach (ElementId id in ids) view.SetElementOverrides(id, ogs);
                t.Commit();
                return ToolHelpers.Flag(args, "reset")
                    ? $"Override eliminat pentru {ids.Count} elemente."
                    : $"{ids.Count} elemente colorate în view-ul '{view.Name}'.";
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
        }
    }

    /// <summary>Izolează temporar elemente în view-ul activ / revine la normal.</summary>
    public class IsolateInViewTool : IRevitTool
    {
        public string Name => "isolate_in_view";
        public string Description => "Izolează temporar elementele date în view-ul activ (restul devine invizibil) sau, cu reset=true, dezactivează izolarea temporară.";
        public string InputSchema => """{"type":"object","properties":{"element_ids":{"type":"array","items":{"type":"integer"}},"reset":{"type":"boolean"}}}""";
        public bool IsWrite => true;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            View? view = doc?.ActiveView;
            if (doc == null || view == null) return ToolHelpers.NoModel;
            var args = ToolHelpers.ParseArgs(argsJson);

            try
            {
                using var t = new Transaction(doc, "Normalign: izolare în view");
                t.Start();
                if (ToolHelpers.Flag(args, "reset"))
                {
                    view.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
                    t.Commit();
                    return "Izolarea temporară a fost dezactivată.";
                }
                var ids = ToolHelpers.Ids(args);
                if (ids.Count == 0) { t.RollBack(); return ToolHelpers.Error("lipsesc element_ids."); }
                view.IsolateElementsTemporary(ids);
                t.Commit();
                return $"{ids.Count} elemente izolate temporar în '{view.Name}'.";
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
        }
    }
}
