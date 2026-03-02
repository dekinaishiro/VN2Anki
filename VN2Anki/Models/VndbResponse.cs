using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VN2Anki.Models
{
    public class VndbResponse
    {
        [JsonPropertyName("results")]
        public List<VndbResult> Results { get; set; } = new();
    }

    public class VndbResult
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("image")]
        public VndbImage Image { get; set; }
    }

    public class VndbImage
    {
        [JsonPropertyName("url")]
        public string Url { get; set; }
    }
}