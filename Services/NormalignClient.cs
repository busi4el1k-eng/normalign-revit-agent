using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NormalignRevitAgent.Services
{
    /// <summary>Parsed answer from the Normalign backend.</summary>
    public class ChatResult
    {
        public string Content = "";
        public string? ChatId;
        public List<string> FollowUpQuestions = new();
    }

    /// <summary>
    /// Thin HTTP client for POST /api/chat. Sends the question plus the live
    /// Revit model summary as ifcContext, exactly like the web app does, so the
    /// full RAG pipeline (SPLADE + dense + rerank + Claude) runs unchanged.
    /// </summary>
    public class NormalignClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(120) // deep RAG answers can be slow
        };

        public async Task<ChatResult> AskAsync(string question, string modelSummary, string? chatId)
        {
            var body = new Dictionary<string, object?>
            {
                ["message"] = question,
                ["chatId"] = chatId,
                ["deepThink"] = false,
                ["ifcContext"] = new Dictionary<string, object?>
                {
                    ["fileName"] = "Model Revit (live)",
                    ["summary"] = modelSummary
                }
            };

            using var req = new HttpRequestMessage(HttpMethod.Post, Config.ChatUrl)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(Config.ApiKey))
                req.Headers.Add("Authorization", $"Bearer {Config.ApiKey}");

            using HttpResponseMessage resp = await Http.SendAsync(req);
            string json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Backend {(int)resp.StatusCode}: {Truncate(json, 300)}");

            return Parse(json);
        }

        private static ChatResult Parse(string json)
        {
            var result = new ChatResult();
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("content", out JsonElement content))
                result.Content = content.GetString() ?? "";

            if (root.TryGetProperty("chatId", out JsonElement cid) && cid.ValueKind == JsonValueKind.String)
                result.ChatId = cid.GetString();

            if (root.TryGetProperty("followUpQuestions", out JsonElement fus) && fus.ValueKind == JsonValueKind.Array)
                foreach (JsonElement q in fus.EnumerateArray())
                    if (q.ValueKind == JsonValueKind.String)
                        result.FollowUpQuestions.Add(q.GetString()!);

            if (string.IsNullOrEmpty(result.Content))
                result.Content = "(răspuns gol de la server)";

            return result;
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
