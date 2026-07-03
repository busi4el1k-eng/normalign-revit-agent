using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace NormalignRevitAgent.Services
{
    /// <summary>
    /// Client HTTP pentru backend-ul Normalign. Autentificare prin cheia de
    /// desktop (Bearer) — vezi src/lib/desktop-auth.ts în aplicația web.
    ///
    /// POST /api/chat      -> răspuns { content, chatId, followUpQuestions, metadata.citations }
    /// GET  /api/history   -> [{ id, title, createdAt }]
    /// GET  /api/messages  -> [{ role, content, metadata }]
    /// </summary>
    public class NormalignApi
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            if (!string.IsNullOrEmpty(Config.ApiKey))
                c.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", Config.ApiKey);
            return c;
        }

        /// <summary>Trimite o întrebare + contextul live din Revit (ca ifcContext).</summary>
        public async Task<JsonNode> SendChatAsync(string message, string? chatId, string fileName, string context)
        {
            var body = new JsonObject
            {
                ["message"] = message,
                ["chatId"] = chatId,
                ["deepThink"] = false,
                ["ifcContext"] = new JsonObject
                {
                    ["fileName"] = fileName,
                    ["summary"] = context
                }
            };
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using HttpResponseMessage resp = await Http.PostAsync($"{Config.WebUrl}/api/chat", content);
            return await ParseAsync(resp);
        }

        public async Task<JsonNode> GetHistoryAsync()
        {
            using HttpResponseMessage resp = await Http.GetAsync($"{Config.WebUrl}/api/history");
            return await ParseAsync(resp);
        }

        public async Task<JsonNode> GetMessagesAsync(string chatId)
        {
            using HttpResponseMessage resp = await Http.GetAsync(
                $"{Config.WebUrl}/api/messages?chatId={Uri.EscapeDataString(chatId)}");
            return await ParseAsync(resp);
        }

        private static async Task<JsonNode> ParseAsync(HttpResponseMessage resp)
        {
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                string hint = (int)resp.StatusCode == 401
                    ? " Verifică apiKey din %APPDATA%\\NormalignRevitAgent\\config.json și DESKTOP_API_KEY pe server."
                    : "";
                throw new Exception($"Server {(int)resp.StatusCode}.{hint}");
            }
            return JsonNode.Parse(json) ?? new JsonObject();
        }
    }
}
