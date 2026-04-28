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
        public int RawCharsPerHour => TotalDurationSeconds > 0 ? (int)((CharactersRead / (double)TotalDurationSeconds) * 3600) : 0;
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
        public double PausedSeconds { get; set; }
        public double ActiveReadingSeconds => Math.Max(0, (EndTime - StartTime).TotalSeconds - LatencySeconds - StudySeconds - DistractionSeconds - PausedSeconds);
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

            // 2. State Tracking Initialization
            bool isPaused = false;
            bool isExternal = false;
            
            // Determine initial state by scanning events before the first block
            foreach (var ev in events.Where(e => e.t < blocks.First().StartTime))
            {
                UpdateState(ev, ref isPaused, ref isExternal);
            }

            // 3. Identify "Pure" blocks for Median calculation
            var pureSpcs = new List<double>();
            foreach (var b in blocks)
            {
                var dist = GetBlockTimeDistribution(b, isPaused, isExternal);
                isPaused = dist.EndPaused;
                isExternal = dist.EndExternal;

                bool hasLookups = b.Events.Any(e => e.e == "LOOKUP" || e.e == "MINE");
                int validChars = JapaneseRegex.Matches(b.Text).Count;

                if (!hasLookups && dist.DistractionSeconds == 0 && dist.PausedSeconds == 0 && validChars > 0)
                {
                    // For pure blocks, duration is ActiveSeconds minus the trailing latency (active time after last click)
                    double readingActiveTime = Math.Max(0, dist.ActiveSeconds - dist.ActiveSecondsAfterLastClick);
                    if (readingActiveTime > 0)
                    {
                        pureSpcs.Add(readingActiveTime / validChars);
                    }
                }
            }

            // Fallback for Median
            if (!pureSpcs.Any())
            {
                isPaused = false; isExternal = false;
                foreach (var ev in events.Where(e => e.t < blocks.First().StartTime)) UpdateState(ev, ref isPaused, ref isExternal);

                foreach (var b in blocks)
                {
                    var dist = GetBlockTimeDistribution(b, isPaused, isExternal);
                    isPaused = dist.EndPaused; isExternal = dist.EndExternal;

                    int validChars = JapaneseRegex.Matches(b.Text).Count;
                    double readingActiveTime = Math.Max(0, dist.ActiveSeconds - dist.ActiveSecondsAfterLastClick);
                    if (validChars > 0 && readingActiveTime > 0) 
                        pureSpcs.Add(readingActiveTime / validChars);
                }
            }

            double medianSpc = GetMedian(pureSpcs);
            double mad = GetMad(pureSpcs, medianSpc);
            if (mad <= 0) mad = 0.05;

            // 4. Detailed block processing (Final Pass)
            double totalLatency = 0, totalStudy = 0, totalDistraction = 0, totalReading = 0, totalPaused = 0;
            int charsRead = 0, lookupCount = 0, miningCount = 0, distractionCount = 0;

            isPaused = false; isExternal = false;
            foreach (var ev in events.Where(e => e.t < blocks.First().StartTime)) UpdateState(ev, ref isPaused, ref isExternal);

            foreach (var b in blocks)
            {
                var dist = GetBlockTimeDistribution(b, isPaused, isExternal);
                isPaused = dist.EndPaused;
                isExternal = dist.EndExternal;

                b.PausedSeconds = dist.PausedSeconds;
                totalPaused += dist.PausedSeconds;
                
                int validChars = JapaneseRegex.Matches(b.Text).Count;
                bool hasJapanese = validChars > 0;
                
                if (hasJapanese) charsRead += validChars;
                else b.LatencySeconds += dist.ActiveSeconds; 
                
                // A. Latency (Post-Reading Wait Time)
                // Use only the active time that occurred AFTER the last click
                double activeTime = dist.ActiveSeconds;
                if (hasJapanese)
                {
                    double latency = Math.Max(0, Math.Min(dist.ActiveSecondsAfterLastClick, activeTime));
                    b.LatencySeconds += latency;
                    activeTime -= latency;
                }
                totalLatency += b.LatencySeconds;

                // B. Study Time
                var studyEvents = b.Events.Where(e => e.e == "LOOKUP" || e.e == "MINE").ToList();
                lookupCount += b.Events.Count(e => e.e == "LOOKUP");
                miningCount += b.Events.Count(e => e.e == "MINE");

                if (hasJapanese && studyEvents.Any())
                {
                    // Use a more lenient threshold for study (Median + 1 MAD) 
                    // to avoid overestimating small reading speed variations as study time
                    double readingThreshold = validChars * (medianSpc + mad);
                    b.StudySeconds = Math.Max(0, activeTime - readingThreshold);
                    totalStudy += b.StudySeconds;
                    activeTime -= b.StudySeconds;
                }

                // C. Distractions & AFK
                if (dist.FocusLostInBlock) distractionCount++;
                b.DistractionSeconds = dist.DistractionSeconds;

                if (hasJapanese)
                {
                    double zScore = mad > 0 ? 0.6745 * (activeTime / validChars - medianSpc) / mad : 0;
                    if (!studyEvents.Any() && zScore > 3.5)
                    {
                        double acceptableSeconds = (medianSpc + 2 * mad) * validChars;
                        double afkSeconds = Math.Max(0, activeTime - acceptableSeconds);
                        b.DistractionSeconds += afkSeconds;
                        activeTime -= afkSeconds;
                        
                        if (afkSeconds > 5 && !dist.FocusLostInBlock) distractionCount++;
                    }
                }

                totalDistraction += b.DistractionSeconds;
                totalReading += activeTime;
                
                foreach(var m in b.Events.Where(e => e.e == "MINE"))
                {
                    string card = m.d.TryGetProperty("card", out var c) ? c.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(card)) result.MinedWords.Add(card);
                }

                // SPC Distribution: To avoid the "median spike", only include blocks WITHOUT lookups.
                // This shows the user's natural reading speed variance (the Gaussian).
                if (hasJapanese && validChars > 0 && activeTime > 0 && !studyEvents.Any())
                {
                    result.SpcDistribution.Add(activeTime / JapaneseRegex.Matches(b.Text).Count);
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
            result.EffectiveDurationSeconds = (int)(totalReading + totalStudy);

            return result;
        }

        private void UpdateState(SessionLogEvent ev, ref bool isPaused, ref bool isExternal)
        {
            if (ev.e == "APP_STATE")
            {
                if (ev.d.TryGetProperty("state", out var s))
                    isPaused = s.GetString() == "BUFFER_STOPPED";
                
                if (ev.d.TryGetProperty("focus", out var f))
                {
                    string focus = f.GetString() ?? "";
                    isExternal = (focus == "external" || focus == "main");
                }
            }
        }

        private class TimeDistribution
        {
            public double PausedSeconds { get; set; }
            public double DistractionSeconds { get; set; }
            public double ActiveSeconds { get; set; }
            public double ActiveSecondsAfterLastClick { get; set; }
            public bool EndPaused { get; set; }
            public bool EndExternal { get; set; }
            public bool FocusLostInBlock { get; set; }
        }

        private TimeDistribution GetBlockTimeDistribution(SentenceBlock b, bool startPaused, bool startExternal)
        {
            var dist = new TimeDistribution { EndPaused = startPaused, EndExternal = startExternal };
            bool currentPaused = startPaused;
            bool currentExternal = startExternal;
            DateTime lastT = b.StartTime;

            // Track when the last click happened to calculate post-click active time
            var lastClick = b.Events.LastOrDefault(e => e.e == "CLICK" && e.t >= b.StartTime && e.t < b.EndTime);
            DateTime? lastClickTime = lastClick?.t;

            var sortedEvents = b.Events.OrderBy(e => e.t).ToList();
            
            foreach (var ev in sortedEvents)
            {
                if (ev.t < b.StartTime) continue;
                if (ev.t > b.EndTime) break;

                double delta = (ev.t - lastT).TotalSeconds;
                if (delta > 0)
                {
                    if (currentPaused) dist.PausedSeconds += delta;
                    else if (currentExternal) dist.DistractionSeconds += delta;
                    else
                    {
                        dist.ActiveSeconds += delta;
                        // If this segment is after the last click, it counts as trailing latency
                        if (lastClickTime != null && lastT >= lastClickTime.Value)
                        {
                            dist.ActiveSecondsAfterLastClick += delta;
                        }
                        else if (lastClickTime != null && ev.t > lastClickTime.Value)
                        {
                            // Segment straddles the click time
                            dist.ActiveSecondsAfterLastClick += (ev.t - lastClickTime.Value).TotalSeconds;
                        }
                    }
                }

                if (ev.e == "APP_STATE")
                {
                    if (ev.d.TryGetProperty("state", out var s))
                        currentPaused = s.GetString() == "BUFFER_STOPPED";
                    
                    if (ev.d.TryGetProperty("focus", out var f))
                    {
                        string focus = f.GetString() ?? "";
                        bool wasExternal = currentExternal;
                        currentExternal = (focus == "external" || focus == "main");
                        if (!wasExternal && currentExternal) dist.FocusLostInBlock = true;
                    }
                }
                lastT = ev.t;
            }

            // Final segment until EndTime
            double finalDelta = (b.EndTime - lastT).TotalSeconds;
            if (finalDelta > 0)
            {
                if (currentPaused) dist.PausedSeconds += finalDelta;
                else if (currentExternal) dist.DistractionSeconds += finalDelta;
                else
                {
                    dist.ActiveSeconds += finalDelta;
                    if (lastClickTime != null && lastT >= lastClickTime.Value)
                    {
                        dist.ActiveSecondsAfterLastClick += finalDelta;
                    }
                    else if (lastClickTime != null && b.EndTime > lastClickTime.Value)
                    {
                        dist.ActiveSecondsAfterLastClick += (b.EndTime - lastClickTime.Value).TotalSeconds;
                    }
                }
            }

            dist.EndPaused = currentPaused;
            dist.EndExternal = currentExternal;
            return dist;
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

                // Find events strictly belonging to this block's timeframe
                block.Events = events.Where(e => e.t >= currentHook.t && e.t < block.EndTime).ToList();
                
                // Only the very last block gets events up to <= EndTime (inclusive)
                if (nextHook == null)
                {
                    block.Events = events.Where(e => e.t >= currentHook.t && e.t <= block.EndTime).ToList();
                }

                // Keep preceding clicks specifically for the first block's pre-latency
                if (i == 0)
                {
                    var precedingClicks = events.Where(e => e.e == "CLICK" && e.t < currentHook.t).ToList();
                    block.Events.InsertRange(0, precedingClicks);
                }

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