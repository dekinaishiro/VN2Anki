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
        private HttpClient _client;
        private string _ankiUrl;

        public AnkiHandler(string url = "http://127.0.0.1:8765", int timeoutSeconds = 10)
        {
            UpdateSettings(url, timeoutSeconds);
        }

        public void UpdateSettings(string url, int timeoutSeconds)
        {
            _ankiUrl = string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:8765" : url;
            var handler = new HttpClientHandler { UseProxy = false, Proxy = null };
            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10) };
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

        private async Task<(T result, string error)> InvokeWithDetailsAsync<T>(string action, object parameters = null)
        {
            try
            {
                var requestObj = new AnkiRequest { Action = action, Params = parameters ?? new object() };
                string jsonString = JsonSerializer.Serialize(requestObj);

                using (var content = new StringContent(jsonString, Encoding.UTF8, "application/json"))
                using (var response = await _client.PostAsync(_ankiUrl, content))
                {
                    response.EnsureSuccessStatusCode();

                    string responseJson = await response.Content.ReadAsStringAsync();
                    var ankiResponse = JsonSerializer.Deserialize<AnkiResponse<T>>(responseJson);

                    if (!string.IsNullOrEmpty(ankiResponse.Error)) return (default, ankiResponse.Error);
                    return (ankiResponse.Result, null);
                }
            }
            catch (TaskCanceledException)
            {
                // Aqui capturamos o Timeout!
                return (default, "Tempo limite esgotado. O Anki está fazendo backup ou sincronizando?");
            }
            catch (HttpRequestException)
            {
                return (default, "Falha de conexão. O Anki e o add-on AnkiConnect estão abertos?");
            }
            catch (Exception ex)
            {
                return (default, ex.Message);
            }
        }

        private async Task<T> InvokeAsync<T>(string action, object parameters = null)
        {
            var (result, _) = await InvokeWithDetailsAsync<T>(action, parameters);
            return result;
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
            // Substituimos a chamada simples pela detalhada para pegar os erros de timeout
            var (noteIds, error) = await InvokeWithDetailsAsync<List<long>>("findNotes", new { query = $"\"deck:{deckName}\" added:1" });

            // Propaga o erro de Timeout ou Conexão imediatamente para a UI!
            if (!string.IsNullOrEmpty(error)) return (false, error);

            if (noteIds == null || noteIds.Count == 0) return (false, "Nenhuma carta adicionada hoje neste deck.");

            long lastNoteId = noteIds[noteIds.Count - 1];
            var fieldsToUpdate = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(audioField) && !string.IsNullOrEmpty(audioFilename)) fieldsToUpdate[audioField] = $"[sound:{audioFilename}]";
            if (!string.IsNullOrEmpty(imageField) && !string.IsNullOrEmpty(imageFilename)) fieldsToUpdate[imageField] = $"<img src=\"{imageFilename}\">";
            if (fieldsToUpdate.Count == 0) return (false, "Nenhum campo para atualizar.");

            var (_, updateError) = await InvokeWithDetailsAsync<object>("updateNoteFields", new { note = new { id = lastNoteId, fields = fieldsToUpdate } });

            if (!string.IsNullOrEmpty(updateError)) return (false, updateError);

            return (true, "Carta atualizada!");
        }
    }
}