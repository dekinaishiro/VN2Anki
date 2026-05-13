using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VN2Anki.Models;

namespace VN2Anki.Services
{
    public class VndbService
    {
        private readonly HttpClient _client;
        private readonly string _coversDirectory;

        public VndbService(HttpClient client)
        {
            _client = client;
            
            // creates the folder
            _coversDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VN2Anki", "Covers");
            Directory.CreateDirectory(_coversDirectory);
        }

        // searches the vndb api and returns a list of results with title and cover url (if available)
        public async Task<(List<VndbResult> Results, string? Error)> SearchVisualNovelAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return (new List<VndbResult>(), null);

            System.Diagnostics.Debug.WriteLine($"[VNDB] Searching for: {query}");

            var requestBody = new
            {
                filters = new object[] { "search", "=", query },
                fields = "title, alttitle, image.url"
            };

            string jsonBody = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            try
            {
                var response = await _client.PostAsync("https://api.vndb.org/kana/vn", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[VNDB API Error] Status: {response.StatusCode}, Content: {errorContent}");
                    return (new List<VndbResult>(), $"VNDB API Error: {response.StatusCode}");
                }

                string responseJson = await response.Content.ReadAsStringAsync();
                var vndbResponse = JsonSerializer.Deserialize<VndbResponse>(responseJson);

                var results = vndbResponse?.Results ?? new List<VndbResult>();
                System.Diagnostics.Debug.WriteLine($"[VNDB] Found {results.Count} results.");
                return (results, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VNDB Exception] {ex.Message}");
                return (new List<VndbResult>(), ex.Message);
            }
        }

        // dls the cover image and saves it locally, returning the local path
        public async Task<string> DownloadCoverAsync(string imageUrl, string vndbId)
        {
            if (string.IsNullOrEmpty(imageUrl) || string.IsNullOrEmpty(vndbId)) return null;

            try
            {
                string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
                if (string.IsNullOrEmpty(extension)) extension = ".jpg";

                string localPath = Path.Combine(_coversDirectory, $"{vndbId}{extension}");

                // cache loading
                if (File.Exists(localPath)) return localPath;

                var imageBytes = await _client.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(localPath, imageBytes);

                return localPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VNDB Download Error] {ex.Message}");
                return null;
            }
        }
    }
}