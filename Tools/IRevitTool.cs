using Autodesk.Revit.DB;

namespace NormalignRevitAgent.Tools
{
    /// <summary>
    /// A single capability the add-in exposes over the live Revit model.
    ///
    /// This is the "seam" that makes v1 upgrade to an agentic v2 without a rewrite:
    /// today we call GetModelSummary directly; tomorrow the server-side agent loop
    /// will pick a tool by <see cref="Name"/>, and we execute it here on Revit's
    /// thread and return the JSON/text result. Every future capability
    /// (query_walls, tag_element, ...) is just another IRevitTool.
    /// </summary>
    public interface IRevitTool
    {
        /// <summary>Stable machine name the LLM will reference, e.g. "get_model_summary".</summary>
        string Name { get; }

        /// <summary>Human/LLM description of what the tool does.</summary>
        string Description { get; }

        /// <summary>Run the tool against the current document. argsJson is "{}" when there are no args.</summary>
        string Execute(Document? doc, string argsJson);
    }
}
