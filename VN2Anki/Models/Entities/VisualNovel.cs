using System;
using System.Collections.Generic;

namespace VN2Anki.Models.Entities
{
    public class VisualNovel
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string? ProcessName { get; set; }
        public string? ExecutablePath { get; set; }
        public string? VndbId { get; set; }     
        public string? CoverImagePath { get; set; }
        public int TotalTimePlayedSeconds { get; set; }
        public int TotalCharactersRead { get; set; }
        public List<SessionRecord> Sessions { get; set; } = new();
    }
}