using System;

namespace VN2Anki.Models.Entities
{
    public class SessionRecord
    {
        public int Id { get; set; }
        public int VisualNovelId { get; set; }
        public VisualNovel VisualNovel { get; set; } 

        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int DurationSeconds { get; set; }
        public int CharactersRead { get; set; }
        public int CardsMined { get; set; }
    }
}