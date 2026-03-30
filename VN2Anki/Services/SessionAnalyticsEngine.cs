using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class SessionLogEvent
    {
        public DateTime t { get; set; }
        public string e { get; set; } = string.Empty;
        public JsonElement d { get; set; }
    }

    public class SessionAnalyticsResult
    {
        public int TotalDurationSeconds { get; set; }
        public int EffectiveDurationSeconds { get; set; }
        public int LookupCount { get; set; }
        public int MiningCount { get; set; }
        public int DistractionCount { get; set; }
        public int LookupDurationSeconds { get; set; }
        public int ReadingDurationSeconds { get; set; }
        public int AfkDurationSeconds { get; set; }
        public int CharactersRead { get; set; }
        public int LatencySeconds { get; set; } // "Gordura" entre clique e texto
        public List<double> SpcDistribution { get; set; } = new();
        public List<string> MinedWords { get; set; } = new();
        public List<SentenceBlock> Blocks { get; set; } = new();

        public int CharsPerHour => EffectiveDurationSeconds > 0 ? (int)((CharactersRead / (double)EffectiveDurationSeconds) * 3600) : 0;
    }

    public interface ISessionAnalyticsEngine
    {
        Task<SessionAnalyticsResult> ProcessSessionLogAsync(string logFilePath, int totalDurationSeconds);
        Task ProcessAndSaveSessionAsync(SessionRecord session);
        Task ReprocessAllSessionsAsync();
    }

    public class SentenceBlock
    {
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<SessionLogEvent> Events { get; set; } = new();
        
        public double LatencySeconds { get; set; }
        public double StudySeconds { get; set; }
        public double DistractionSeconds { get; set; }
        public double ActiveReadingSeconds => Math.Max(0, (EndTime - StartTime).TotalSeconds - LatencySeconds - StudySeconds - DistractionSeconds);
    }

    public class SessionAnalyticsEngine : ISessionAnalyticsEngine, CommunityToolkit.Mvvm.Messaging.IRecipient<VN2Anki.Messages.SessionEndedMessage>
    {
        private readonly IVnDatabaseService _dbService;
        private static readonly Regex JapaneseRegex = new(@"[\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FAF]", RegexOptions.Compiled);

        public SessionAnalyticsEngine(IVnDatabaseService dbService)
        {
            _dbService = dbService;
            CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.RegisterAll(this);
        }

        public void Receive(VN2Anki.Messages.SessionEndedMessage message)
        {
            if (message.Session != null)
            {
                _ = ProcessAndSaveSessionAsync(message.Session);
            }
        }

        public async Task ReprocessAllSessionsAsync()
        {
            var sessions = await _dbService.GetAllSessionsAsync();
            foreach (var session in sessions)
            {
                if (!string.IsNullOrEmpty(session.LogFilePath) && File.Exists(session.LogFilePath))
                {
                    await ProcessAndSaveSessionAsync(session);
                }
            }
        }

        public async Task ProcessAndSaveSessionAsync(SessionRecord session)
        {
            if (string.IsNullOrEmpty(session.LogFilePath) || !File.Exists(session.LogFilePath))
                return;

            var result = await ProcessSessionLogAsync(session.LogFilePath, session.DurationSeconds);
            
            session.EffectiveDurationSeconds = result.EffectiveDurationSeconds;
            session.LookupCount = result.LookupCount;
            session.LookupDurationSeconds = result.LookupDurationSeconds;
            session.CharactersRead = result.CharactersRead; 
            session.IsProcessed = true;

            await _dbService.UpdateSessionAsync(session);
        }

        public async Task<SessionAnalyticsResult> ProcessSessionLogAsync(string logFilePath, int totalDurationSeconds)
        {
            var events = await LoadEventsAsync(logFilePath);
            var result = new SessionAnalyticsResult
            {
                TotalDurationSeconds = totalDurationSeconds,
                EffectiveDurationSeconds = totalDurationSeconds
            };

            if (events.Count == 0) return result;

            // 1. Group into Sentence Blocks
            var blocks = CreateSentenceBlocks(events);
            if (!blocks.Any()) return result;

            // 2. Identify "Pure" blocks for Median calculation
            var pureSpcs = new List<double>();
            foreach (var b in blocks)
            {
                bool hasLookups = b.Events.Any(e => e.e == "LOOKUP" || e.e == "MINE");
                bool hasFocusLoss = b.Events.Any(e => e.e == "APP_STATE" && e.d.TryGetProperty("focus", out var f) && (f.GetString() == "external" || f.GetString() == "main"));
                
                if (!hasLookups && !hasFocusLoss && b.Text.Length > 0 && JapaneseRegex.IsMatch(b.Text))
                {
                    double duration = (b.EndTime - b.StartTime).TotalSeconds;
                    if (duration > 0) // Previne apenas divisão por zero
                    {
                        pureSpcs.Add(duration / b.Text.Length);
                    }
                }
            }

            // Fallback: se a sessão foi muito fragmentada e não há blocos puros, usamos a média de todos os blocos possíveis
            if (!pureSpcs.Any())
            {
                foreach (var b in blocks.Where(x => x.Text.Length > 0 && JapaneseRegex.IsMatch(x.Text)))
                {
                    double duration = (b.EndTime - b.StartTime).TotalSeconds;
                    if (duration > 0) pureSpcs.Add(duration / b.Text.Length);
                }
            }

            double medianSpc = GetMedian(pureSpcs);
            double mad = GetMad(pureSpcs, medianSpc);
            if (mad == 0) mad = 0.05;

            // 3. Detailed block processing
            double totalLatency = 0;
            double totalStudy = 0;
            double totalDistraction = 0;
            double totalReading = 0;
            int charsRead = 0;
            int lookupCount = 0;
            int miningCount = 0;
            int distractionCount = 0;

            foreach (var b in blocks)
            {
                bool hasJapanese = JapaneseRegex.IsMatch(b.Text);
                
                if (hasJapanese)
                {
                    charsRead += b.Text.Length;
                }
                else
                {
                    // Non-Japanese sentences (punctuation only, etc) are treated as pure overhead
                    b.LatencySeconds += (b.EndTime - b.StartTime).TotalSeconds;
                }
                
                // A. Latency (Click -> Hook)
                var lastClick = b.Events.LastOrDefault(e => e.e == "CLICK" && e.t < b.StartTime);
                if (lastClick != null)
                {
                    double clickLatency = (b.StartTime - lastClick.t).TotalSeconds;
                    b.LatencySeconds += clickLatency;
                }
                totalLatency += b.LatencySeconds;

                // B. Study Time
                var studyEvents = b.Events.Where(e => e.e == "LOOKUP" || e.e == "MINE").ToList();
                lookupCount += b.Events.Count(e => e.e == "LOOKUP");
                miningCount += b.Events.Count(e => e.e == "MINE");
                
                if (studyEvents.Any())
                {
                    // If studying, we assume the time spent beyond normal reading is StudyTime
                    double totalBlockSeconds = (b.EndTime - b.StartTime).TotalSeconds;
                    double expectedReadingTime = b.Text.Length * medianSpc;
                    b.StudySeconds = Math.Max(0, totalBlockSeconds - expectedReadingTime);
                    totalStudy += b.StudySeconds;
                }

                // C. Distractions & AFK
                double currentBlockSeconds = (b.EndTime - b.StartTime).TotalSeconds;
                double currentSpc = b.Text.Length > 0 ? currentBlockSeconds / b.Text.Length : 0;
                double zScore = 0.6745 * (currentSpc - medianSpc) / mad;

                // Time in external focus
                double externalSeconds = 0;
                DateTime? externalStart = null;
                bool focusLostInBlock = false;
                foreach(var ev in b.Events.Where(e => e.e == "APP_STATE"))
                {
                    if (ev.d.TryGetProperty("focus", out var focusProp))
                    {
                        string focus = focusProp.GetString() ?? "";
                        if ((focus == "external" || focus == "main") && externalStart == null) 
                        {
                            externalStart = ev.t;
                            focusLostInBlock = true;
                        }
                        else if ((focus == "game" || focus == "overlay") && externalStart != null)
                        {
                            externalSeconds += (ev.t - externalStart.Value).TotalSeconds;
                            externalStart = null;
                        }
                    }
                }
                if (externalStart != null) externalSeconds += (b.EndTime - externalStart.Value).TotalSeconds;
                
                if (focusLostInBlock) distractionCount++;

                b.DistractionSeconds = externalSeconds;

                // Statistical AFK (if block is too long without study events)
                if (!studyEvents.Any() && zScore > 3.5)
                {
                    double acceptableSeconds = (medianSpc + 2 * mad) * b.Text.Length;
                    double afkSeconds = Math.Max(0, currentBlockSeconds - acceptableSeconds - b.DistractionSeconds);
                    b.DistractionSeconds += afkSeconds;
                    
                    if (afkSeconds > 5 && !focusLostInBlock) distractionCount++; // Count long pure AFKs as distractions too
                }

                totalDistraction += b.DistractionSeconds;
                totalReading += b.ActiveReadingSeconds;
                
                // Track mined words
                foreach(var m in b.Events.Where(e => e.e == "MINE"))
                {
                    string card = m.d.TryGetProperty("card", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(card)) result.MinedWords.Add(card);
                }

                // Add to distribution using EFFECTIVE SPC (only for blocks with Japanese reading time)
                if (hasJapanese && b.Text.Length > 0)
                {
                    double effectiveSpc = Math.Max(0, b.ActiveReadingSeconds) / b.Text.Length;
                    result.SpcDistribution.Add(effectiveSpc);
                }
            }

            result.Blocks = blocks;
            result.CharactersRead = charsRead;
            result.LookupCount = lookupCount;
            result.MiningCount = miningCount;
            result.DistractionCount = distractionCount;
            result.LookupDurationSeconds = (int)totalStudy;
            result.LatencySeconds = (int)totalLatency;
            result.AfkDurationSeconds = (int)totalDistraction;
            result.ReadingDurationSeconds = (int)totalReading;
            
            double totalFat = totalLatency + totalDistraction;
            result.EffectiveDurationSeconds = Math.Max(0, totalDurationSeconds - (int)totalFat);

            return result;
        }

        private async Task<List<SessionLogEvent>> LoadEventsAsync(string logFilePath)
        {
            var events = new List<SessionLogEvent>();
            if (!File.Exists(logFilePath)) return events;

            using var reader = new StreamReader(logFilePath);
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var ev = JsonSerializer.Deserialize<SessionLogEvent>(line);
                    if (ev != null) events.Add(ev);
                }
                catch { }
            }
            return events.OrderBy(e => e.t).ToList();
        }

        private List<SentenceBlock> CreateSentenceBlocks(List<SessionLogEvent> events)
        {
            var blocks = new List<SentenceBlock>();
            var hookEvents = events.Where(e => e.e == "HOOK").ToList();

            for (int i = 0; i < hookEvents.Count; i++)
            {
                var currentHook = hookEvents[i];
                var nextHook = (i + 1 < hookEvents.Count) ? hookEvents[i + 1] : null;

                var block = new SentenceBlock
                {
                    StartTime = currentHook.t,
                    EndTime = nextHook?.t ?? events.Last().t,
                    Text = currentHook.d.GetProperty("text").GetString() ?? ""
                };

                // Find events belonging to this block (including the leading clicks)
                DateTime windowStart = (i > 0) ? hookEvents[i - 1].t : events.First().t;
                block.Events = events.Where(e => e.t >= windowStart && e.t <= block.EndTime).ToList();

                blocks.Add(block);
            }
            return blocks;
        }

        private double GetMedian(List<double> list)
        {
            if (!list.Any()) return 0;
            var sorted = list.OrderBy(n => n).ToList();
            int mid = sorted.Count / 2;
            return (sorted.Count % 2 != 0) ? sorted[mid] : (sorted[mid] + sorted[mid - 1]) / 2.0;
        }

        private double GetMad(List<double> list, double median)
        {
            if (!list.Any()) return 0;
            var deviations = list.Select(x => Math.Abs(x - median)).ToList();
            return GetMedian(deviations);
        }
    }
}