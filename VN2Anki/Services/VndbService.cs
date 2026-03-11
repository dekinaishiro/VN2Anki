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
        public async Task<List<VndbResult>> SearchVisualNovelAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<VndbResult>();

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
                response.EnsureSuccessStatusCode();

                string responseJson = await response.Content.ReadAsStringAsync();
                var vndbResponse = JsonSerializer.Deserialize<VndbResponse>(responseJson);

                return vndbResponse?.Results ?? new List<VndbResult>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VNDB API Error] {ex.Message}");
                return new List<VndbResult>();
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