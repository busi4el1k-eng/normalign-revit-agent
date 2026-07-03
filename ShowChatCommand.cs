using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace NormalignRevitAgent
{
    /// <summary>
    /// Ribbon button command: shows (or focuses) the Normalign chat pane.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class ShowChatCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            DockablePane pane = commandData.Application.GetDockablePane(App.PaneId);
            pane.Show();
            return Result.Succeeded;
        }
    }
}
