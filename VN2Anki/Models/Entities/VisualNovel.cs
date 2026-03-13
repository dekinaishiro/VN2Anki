using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

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
        public string? CoverImageUrl { get; set; }
        public string? Architecture { get; set; }

        // time & stats
        public int TotalTimePlayedSeconds { get; set; }
        public int EffectiveTimePlayedSeconds { get; set; } // Tempo corrigido (futuro)
        public int TotalCharactersRead { get; set; }
        public int TotalCardsMined { get; set; }

        // date
        public DateTime? StartDate { get; set; }
        public DateTime? FinishDate { get; set; }
        public DateTime? LastPlayed { get; set; }

        public string? OriginalTitle { get; set; }

        // relationships
        public List<SessionRecord> Sessions { get; set; } = new();

        // non-mapped properties for display
        [NotMapped]
        public string FormattedTotalTime => TimeSpan.FromSeconds(TotalTimePlayedSeconds).ToString(@"hh\:mm\:ss");

        [NotMapped]
        public int SessionCount => Sessions?.Count ?? 0;

        public string? OverlayConfigJson { get; set; }
    }
}