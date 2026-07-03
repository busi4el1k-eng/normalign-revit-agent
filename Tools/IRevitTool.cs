using Autodesk.Revit.UI;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// O capabilitate a add-in-ului asupra modelului Revit deschis.
    ///
    /// v2 (agentic): bucla de agent de pe server (Claude tool-use, /api/agent)
    /// alege un tool după <see cref="Name"/>; îl executăm aici, pe thread-ul
    /// Revit (prin ExternalEvent), și întoarcem rezultatul text/JSON.
    /// Tool-urile cu <see cref="IsWrite"/> = true modifică modelul (fiecare în
    /// propria tranzacție, deci cu Undo separat) și sunt declarate serverului
    /// doar în modul Edit.
    /// </summary>
    public interface IRevitTool
    {
        /// <summary>Numele stabil folosit de LLM, ex. "get_model_summary".</summary>
        string Name { get; }

        /// <summary>Descrierea pentru LLM (ce face, când se folosește).</summary>
        string Description { get; }

        /// <summary>JSON Schema (ca string) pentru argumentele tool-ului.</summary>
        string InputSchema { get; }

        /// <summary>True dacă tool-ul modifică modelul — expus doar în modul Edit.</summary>
        bool IsWrite { get; }

        /// <summary>
        /// Rulează tool-ul. Se apelează DOAR pe thread-ul Revit (din
        /// RevitRequestHandler.Execute). argsJson este "{}" când nu există argumente.
        /// </summary>
        string Execute(UIApplication app, string argsJson);
    }
}
