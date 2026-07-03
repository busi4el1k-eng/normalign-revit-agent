using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// Exportă view-ul activ ca imagine PNG și o întoarce base64 — "ochii"
    /// agentului pe desen. AgentRunner recunoaște câmpul image_base64 și îl
    /// împachetează ca bloc de imagine pentru Claude (vision).
    /// </summary>
    public class GetViewSnapshotTool : IRevitTool
    {
        public string Name => "get_view_snapshot";
        public string Description => "Captură de imagine a view-ului activ din Revit (plan, secțiune, 3D...). Folosește când trebuie să VEZI desenul: așezarea în plan, adnotări, ce anume e vizibil. Rezultatul e o imagine pe care o poți analiza direct.";
        public string InputSchema => """{"type":"object","properties":{}}""";
        public bool IsWrite => false;

        public string Execute(UIApplication app, string argsJson)
        {
            Document? doc = app.ActiveUIDocument?.Document;
            View? view = doc?.ActiveView;
            if (doc == null || view == null) return ToolHelpers.NoModel;
            if (!view.CanBePrinted) return ToolHelpers.Error($"view-ul '{view.Name}' nu poate fi exportat ca imagine.");

            // Folder temporar propriu, ca să găsim sigur fișierul generat de Revit
            // (ExportImage adaugă sufixe la numele cerut).
            string dir = Path.Combine(Path.GetTempPath(), "NormalignSnapshot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var opts = new ImageExportOptions
                {
                    FilePath = Path.Combine(dir, "view"),
                    ExportRange = ExportRange.VisibleRegionOfCurrentView,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = 1024, // echilibru claritate / mărimea transcriptului
                    FitDirection = FitDirectionType.Horizontal,
                    ImageResolution = ImageResolution.DPI_72,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                };
                doc.ExportImage(opts);

                string? file = Directory.GetFiles(dir, "*.png").FirstOrDefault();
                if (file == null) return ToolHelpers.Error("Revit nu a generat imaginea.");

                byte[] bytes = File.ReadAllBytes(file);
                var result = new JsonObject
                {
                    ["view"] = view.Name,
                    ["media_type"] = "image/png",
                    ["image_base64"] = Convert.ToBase64String(bytes),
                };
                return result.ToJsonString();
            }
            catch (Exception ex) { return ToolHelpers.Error(ex.Message); }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }
    }
}
