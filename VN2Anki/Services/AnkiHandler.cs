#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VN2Anki.Services
{
    public class AnkiHandler
    {
        private readonly HttpClient _client;
        private const string ANKI_URL = "http://127.0.0.1:8765";

        public AnkiHandler()
        {
            var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        }

        private class AnkiRequest
        {
            [JsonPropertyName("action")] public string Action { get; set; }
            [JsonPropertyName("version")] public int Version { get; set; } = 6;
            [JsonPropertyName("params")] public object Params { get; set; }
        }

        private class AnkiResponse<T>
        {
            [JsonPropertyName("result")] public T Result { get; set; }
            [JsonPropertyName("error")] public string Error { get; set; }
        }

        private async Task<T> InvokeAsync<T>(string action, object parameters = null)
        {
            try
            {
                var requestObj = new AnkiRequest { Action = action, Params = parameters ?? new object() };
                string jsonString = JsonSerializer.Serialize(requestObj);

                // 'using' forces disposal of HttpContent and HttpResponseMessage
                using (var content = new StringContent(jsonString, Encoding.UTF8, "application/json"))
                using (var response = await _client.PostAsync(ANKI_URL, content))
                {
                    response.EnsureSuccessStatusCode();

                    string responseJson = await response.Content.ReadAsStringAsync();
                    var ankiResponse = JsonSerializer.Deserialize<AnkiResponse<T>>(responseJson);

                    if (!string.IsNullOrEmpty(ankiResponse.Error)) return default;
                    return ankiResponse.Result;
                }
            }
            catch { return default; }
        }

        public async Task<bool> IsConnectedAsync() => await InvokeAsync<int>("version") > 0;
        public async Task<List<string>> GetDecksAsync() => await InvokeAsync<List<string>>("deckNames") ?? new List<string>();
        public async Task<List<string>> GetModelsAsync() => await InvokeAsync<List<string>>("modelNames") ?? new List<string>();
        public async Task<List<string>> GetModelFieldsAsync(string modelName) => await InvokeAsync<List<string>>("modelFieldNames", new { modelName }) ?? new List<string>();

        public async Task<bool> StoreMediaAsync(string filename, byte[] dataBytes)
        {
            if (dataBytes == null || dataBytes.Length == 0) return false;
            string base64Data = Convert.ToBase64String(dataBytes);
            return await InvokeAsync<string>("storeMediaFile", new { filename, data = base64Data }) == filename;
        }

        public async Task<(bool success, string message)> UpdateLastCardAsync(string deckName, string audioField, string imageField, string audioFilename, string imageFilename)
        {
            var noteIds = await InvokeAsync<List<long>>("findNotes", new { query = $"\"deck:{deckName}\" added:1" });
            if (noteIds == null || noteIds.Count == 0) return (false, "Nenhuma carta adicionada hoje neste deck.");

            long lastNoteId = noteIds[noteIds.Count - 1];
            var fieldsToUpdate = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(audioField) && !string.IsNullOrEmpty(audioFilename)) fieldsToUpdate[audioField] = $"[sound:{audioFilename}]";
            if (!string.IsNullOrEmpty(imageField) && !string.IsNullOrEmpty(imageFilename)) fieldsToUpdate[imageField] = $"<img src=\"{imageFilename}\">";
            if (fieldsToUpdate.Count == 0) return (false, "Nenhum campo para atualizar.");

            await InvokeAsync<object>("updateNoteFields", new { note = new { id = lastNoteId, fields = fieldsToUpdate } });
            return (true, "Carta atualizada!");
        }
    }
}