namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Central configuration for the add-in.
    ///
    /// AUTH NOTE: your /api/chat route is Clerk-protected (src/proxy.ts), so a
    /// desktop add-in cannot call it as-is. Before this works end-to-end you must
    /// EITHER:
    ///   (a) add a machine-to-machine API-key path to the chat route and put the
    ///       key in <see cref="ApiKey"/> (sent as a Bearer token), OR
    ///   (b) run the web app locally and point ChatUrl at http://localhost:3000.
    /// Until then the call will 401 / redirect to login.
    /// </summary>
    public static class Config
    {
        // Point at prod, or "http://localhost:3000/api/chat" for local dev.
        public static string ChatUrl = "https://normalign.com/api/chat";

        // Bearer token for the (to-be-added) API-key auth path. Leave empty for now.
        public static string ApiKey = "";
    }
}
