#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VN2Anki.Services
{
    public class AnkiHandler
    {
        private readonly HttpClient _client;
        private string _ankiUrl;
        private int _timeoutSeconds = 10;

        public AnkiHandler(HttpClient client)
        {
            _client = client;
            UpdateSettings("http://127.0.0.1:8765", 10);
        }

        public void UpdateSettings(string url, int timeoutSeconds)
        {
            _ankiUrl = string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:8765" : url;
            _timeoutSeconds = timeoutSeconds;
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

                // Usa o CancellationToken para gerenciar o timeout sem modificar o HttpClient travado
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds)))
                using (var content = new StringContent(jsonString, Encoding.UTF8, "application/json"))
                using (var response = await _client.PostAsync(_ankiUrl, content, cts.Token))
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
                return (default, "Anki Timeout");
            }
            catch (HttpRequestException)
            {
                return (default, "Failed to connect to Anki");
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
    }
}