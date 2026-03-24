using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Threading;

namespace VN2Anki.Services
{
    #nullable enable
    public class AnkiHandler
    {
        private readonly HttpClient _client;
        private string _ankiUrl = "http://127.0.0.1:8765";
        private int _timeoutSeconds = 10;

        public AnkiHandler(HttpClient client)
        {
            _client = client;
            UpdateSettings("http://127.0.0.1:8765", 10);
        }

        public void UpdateSettings(string? url, int timeoutSeconds)
        {
            _ankiUrl = string.IsNullOrWhiteSpace(url) ? "http://127.0.0.1:8765" : url!;
            _timeoutSeconds = timeoutSeconds;
        }

        private class AnkiRequest
        {
            [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
            [JsonPropertyName("version")] public int Version { get; set; } = 6;
            [JsonPropertyName("params")] public object? Params { get; set; }
        }

        private class AnkiResponse<T>
        {
            [JsonPropertyName("result")] public T? Result { get; set; }
            [JsonPropertyName("error")] public string? Error { get; set; }
        }

        private async Task<(T? result, string? error)> InvokeWithDetailsAsync<T>(string action, object? parameters = null)
        {
            try
            {
                var requestObj = new AnkiRequest { Action = action, Params = parameters ?? new object() };
                string jsonString = JsonSerializer.Serialize(requestObj);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds)))
                using (var content = new StringContent(jsonString, Encoding.UTF8, "application/json"))
                using (var response = await _client.PostAsync(_ankiUrl, content, cts.Token))
                {
                    response.EnsureSuccessStatusCode();

                    string responseJson = await response.Content.ReadAsStringAsync();
                    var ankiResponse = JsonSerializer.Deserialize<AnkiResponse<T>>(responseJson);

                    if (ankiResponse == null) return (default, "Empty response from Anki");
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

        public async Task<(int Version, string? Error)> IsConnectedAsync()
        {
            var (version, error) = await InvokeWithDetailsAsync<int>("version");
            return (version, error);
        }

        public async Task<(List<string> Decks, string? Error)> GetDecksAsync()
        {
            var (result, error) = await InvokeWithDetailsAsync<List<string>>("deckNames");
            return (result ?? new List<string>(), error);
        }

        public async Task<(List<string> Models, string? Error)> GetModelsAsync()
        {
            var (result, error) = await InvokeWithDetailsAsync<List<string>>("modelNames");
            return (result ?? new List<string>(), error);
        }

        public async Task<(List<string> Fields, string? Error)> GetModelFieldsAsync(string modelName)
        {
            var (result, error) = await InvokeWithDetailsAsync<List<string>>("modelFieldNames", new { modelName });
            return (result ?? new List<string>(), error);
        }

        public async Task<(bool Success, string? Error)> StoreMediaAsync(string filename, byte[] dataBytes)
        {
            if (dataBytes == null || dataBytes.Length == 0) return (false, "Media data is empty");
            
            string base64Data = Convert.ToBase64String(dataBytes);
            var (result, error) = await InvokeWithDetailsAsync<string>("storeMediaFile", new { filename, data = base64Data });
            
            return (result == filename, error);
        }
    }
}