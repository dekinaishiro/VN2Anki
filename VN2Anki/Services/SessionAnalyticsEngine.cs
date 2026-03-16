using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        public int LookupDurationSeconds { get; set; }
        public int CharactersRead { get; set; }
    }

    public interface ISessionAnalyticsEngine
    {
        Task<SessionAnalyticsResult> ProcessSessionLogAsync(string logFilePath, int totalDurationSeconds);
        Task ProcessAndSaveSessionAsync(SessionRecord session);
    }

    public class SessionAnalyticsEngine : ISessionAnalyticsEngine
    {
        private readonly IVnDatabaseService _dbService;

        public SessionAnalyticsEngine(IVnDatabaseService dbService)
        {
            _dbService = dbService;
        }

        public async Task ProcessAndSaveSessionAsync(SessionRecord session)
        {
            if (string.IsNullOrEmpty(session.LogFilePath) || !File.Exists(session.LogFilePath))
                return;

            var result = await ProcessSessionLogAsync(session.LogFilePath, session.DurationSeconds);
            
            session.EffectiveDurationSeconds = result.EffectiveDurationSeconds;
            session.LookupCount = result.LookupCount;
            session.LookupDurationSeconds = result.LookupDurationSeconds;
            // update chars read just in case it was refined by deduplication
            session.CharactersRead = result.CharactersRead; 
            session.IsProcessed = true;

            await _dbService.UpdateSessionAsync(session);
        }

        public async Task<SessionAnalyticsResult> ProcessSessionLogAsync(string logFilePath, int totalDurationSeconds)
        {
            if (!File.Exists(logFilePath))
                return new SessionAnalyticsResult { TotalDurationSeconds = totalDurationSeconds, EffectiveDurationSeconds = totalDurationSeconds };

            var events = new List<SessionLogEvent>();
            using (var reader = new StreamReader(logFilePath))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var ev = JsonSerializer.Deserialize<SessionLogEvent>(line);
                        if (ev != null) events.Add(ev);
                    }
                    catch { /* ignore malformed lines */ }
                }
            }

            var result = new SessionAnalyticsResult
            {
                TotalDurationSeconds = totalDurationSeconds,
                EffectiveDurationSeconds = totalDurationSeconds
            };

            if (events.Count == 0) return result;

            // 1. Parse & Deduplication & Extract SPC
            var hookEvents = new List<SessionLogEvent>();
            var lookups = new List<SessionLogEvent>();
            
            string lastText = "";
            foreach (var ev in events.OrderBy(e => e.t))
            {
                if (ev.e == "HOOK")
                {
                    string text = ev.d.GetProperty("text").GetString() ?? "";
                    if (text == lastText || string.IsNullOrWhiteSpace(text)) continue; // basic deduplication
                    
                    lastText = text;
                    hookEvents.Add(ev);
                }
                else if (ev.e == "LOOKUP" || ev.e == "YOMITAN_LOOKUP") 
                {
                    // Assumes a lookup event is triggered. Need to sync with actual log name.
                    lookups.Add(ev);
                }
            }

            // Calculate SPCs
            var spcs = new List<double>();
            int charsRead = 0;
            for (int i = 0; i < hookEvents.Count - 1; i++)
            {
                var current = hookEvents[i];
                var next = hookEvents[i + 1];
                
                string text = current.d.GetProperty("text").GetString() ?? "";
                int len = text.Length;
                charsRead += len;

                double seconds = (next.t - current.t).TotalSeconds;
                if (seconds < 0) seconds = 0;

                double spc = seconds / len;
                spcs.Add(spc);
            }
            if (hookEvents.Count > 0)
            {
                 charsRead += (hookEvents.Last().d.GetProperty("text").GetString()?.Length ?? 0);
            }

            result.CharactersRead = charsRead;

            // 2. MAD Calculation
            if (spcs.Count > 2)
            {
                spcs.Sort();
                double medianSpc = GetMedian(spcs);
                
                var deviations = spcs.Select(val => Math.Abs(val - medianSpc)).ToList();
                deviations.Sort();
                double mad = GetMedian(deviations);

                if (mad == 0) mad = 0.01; // prevent division by zero

                double afkPenalty = 0;
                
                // 3. Classification
                for (int i = 0; i < hookEvents.Count - 1; i++)
                {
                    var current = hookEvents[i];
                    var next = hookEvents[i + 1];
                    int len = (current.d.GetProperty("text").GetString() ?? "").Length;
                    double seconds = (next.t - current.t).TotalSeconds;
                    double spc = seconds / len;

                    double zScore = 0.6745 * (spc - medianSpc) / mad;

                    if (zScore > 3.5)
                    {
                        // AFK detected
                        double acceptableSpc = medianSpc + (2 * mad); // Ceiling
                        double acceptableSeconds = acceptableSpc * len;
                        
                        // We also need to see if a lookup happened during this time!
                        var lookupsInBetween = lookups.Where(l => l.t >= current.t && l.t <= next.t).ToList();
                        
                        if (lookupsInBetween.Any())
                        {
                            // If they were looking up words, it's study time, not AFK!
                            result.LookupCount += lookupsInBetween.Count;
                            
                            // Estimate lookup duration: from first lookup to the next hook
                            var firstLookup = lookupsInBetween.First();
                            double lookupTime = (next.t - firstLookup.t).TotalSeconds;
                            result.LookupDurationSeconds += (int)lookupTime;
                            
                            // Adjust acceptable seconds to include the lookup time
                            acceptableSeconds += lookupTime;
                        }
                        
                        double penalty = seconds - acceptableSeconds;
                        if (penalty > 0)
                        {
                            afkPenalty += penalty;
                        }
                    }
                    else if (zScore < 0.3 * medianSpc)
                    {
                        // Skip/Fast reading - could subtract charsRead here if wanted
                    }
                }
                
                result.EffectiveDurationSeconds = Math.Max(0, totalDurationSeconds - (int)afkPenalty);
            }

            return result;
        }

        private double GetMedian(List<double> sortedList)
        {
            int count = sortedList.Count;
            if (count == 0) return 0;
            int mid = count / 2;
            return (count % 2 != 0) ? sortedList[mid] : (sortedList[mid] + sortedList[mid - 1]) / 2.0;
        }
    }
}
