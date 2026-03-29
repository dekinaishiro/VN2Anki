using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VN2Anki.Models.Entities;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.ViewModels.Hub
{
    public class SessionDetailItem : ObservableObject
    {
        public int LineIndex { get; set; }
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public string RawJson { get; set; } = string.Empty;
    }

    public partial class SessionDetailViewModel : ObservableObject
    {
        private readonly IVnDatabaseService _dbService;
        private readonly ISessionAnalyticsEngine _analyticsEngine;

        [ObservableProperty]
        private SessionRecord _session = null!;

        [ObservableProperty]
        private VisualNovel? _visualNovel;

        [ObservableProperty]
        private SessionAnalyticsResult? _analyticsResult;

        [ObservableProperty]
        private PointCollection? _speedDistributionPoints;

        [ObservableProperty]
        private bool _hasSpeedDistribution;

        public ObservableCollection<SessionDetailItem> LogItems { get; } = new();

        [ObservableProperty]
        private bool _isLoadingLogs;

        [ObservableProperty]
        private bool _isAdvancedView = false;

        public ObservableCollection<SessionDetailItem> FilteredLogItems { get; } = new();

        public SessionDetailViewModel(IVnDatabaseService dbService, ISessionAnalyticsEngine analyticsEngine)
        {
            _dbService = dbService;
            _analyticsEngine = analyticsEngine;
        }

        public async Task InitializeAsync(SessionRecord session)
        {
            Session = session;
            if (session.VisualNovelId.HasValue)
            {
                var vns = await _dbService.GetAllVisualNovelsAsync();
                VisualNovel = vns.FirstOrDefault(v => v.Id == session.VisualNovelId.Value);
            }
            
            await LoadLogsAsync();
        }

        partial void OnIsAdvancedViewChanged(bool value)
        {
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            FilteredLogItems.Clear();
            var items = IsAdvancedView ? LogItems : LogItems.Where(x => x.EventType == "HOOK" && !string.IsNullOrWhiteSpace(x.Details));
            foreach (var item in items)
            {
                FilteredLogItems.Add(item);
            }
        }

        [RelayCommand]
        private async Task LoadLogsAsync()
        {
            if (string.IsNullOrEmpty(Session?.LogFilePath) || !File.Exists(Session.LogFilePath))
                return;

            IsLoadingLogs = true;
            LogItems.Clear();

            try
            {
                // 1. Recalculate full analytics for the detailed view
                AnalyticsResult = await _analyticsEngine.ProcessSessionLogAsync(Session.LogFilePath, Session.DurationSeconds);
                GenerateDistributionChart();

                // 2. Load log items
                var lines = await File.ReadAllLinesAsync(Session.LogFilePath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var jsonDoc = JsonDocument.Parse(line);
                        var root = jsonDoc.RootElement;
                        
                        string t = root.GetProperty("t").GetString() ?? "";
                        string e = root.GetProperty("e").GetString() ?? "";
                        string details = "";

                        if (root.TryGetProperty("d", out var dataElement))
                        {
                            details = GetDetailsForEvent(e, dataElement);
                        }

                        LogItems.Add(new SessionDetailItem
                        {
                            LineIndex = i,
                            Timestamp = DateTime.Parse(t),
                            EventType = e,
                            Details = details,
                            RawJson = line
                        });
                    }
                    catch { /* ignore */ }
                }

                ApplyFilter();
            }
            finally
            {
                IsLoadingLogs = false;
            }
        }

        private void GenerateDistributionChart()
        {
            if (AnalyticsResult?.SpcDistribution == null || !AnalyticsResult.SpcDistribution.Any())
            {
                SpeedDistributionPoints = null;
                HasSpeedDistribution = false;
                return;
            }

            // Remove extreme outliers (top 5%) instead of a hardcoded threshold to adapt to different reading speeds
            var sortedSpcs = AnalyticsResult.SpcDistribution.OrderBy(x => x).ToList();
            int cutoffIndex = (int)(sortedSpcs.Count * 0.95);
            if (cutoffIndex == 0) cutoffIndex = sortedSpcs.Count;
            
            var validSpcs = sortedSpcs.Take(cutoffIndex).ToList();
            
            if (!validSpcs.Any()) 
            {
                SpeedDistributionPoints = null;
                HasSpeedDistribution = false;
                return;
            }

            double min = validSpcs.Min();
            double max = validSpcs.Max();
            if (max - min < 0.05) max = min + 0.1; // avoid division by zero if all values are identical

            int binCount = 20;
            double binWidth = (max - min) / binCount;
            int[] bins = new int[binCount];

            foreach (var spc in validSpcs)
            {
                int binIndex = (int)((spc - min) / binWidth);
                if (binIndex >= binCount) binIndex = binCount - 1;
                if (binIndex < 0) binIndex = 0;
                bins[binIndex]++;
            }

            int maxFreq = bins.Max();
            if (maxFreq == 0) maxFreq = 1;

            var points = new PointCollection();
            double width = 260; // virtual canvas width
            double height = 80;  // virtual canvas height

            // Start at bottom left
            points.Add(new System.Windows.Point(0, height));

            // Create smoothish curve points
            for (int i = 0; i < binCount; i++)
            {
                double x = (i / (double)(binCount - 1)) * width;
                double normalizedY = bins[i] / (double)maxFreq;
                
                // leave top padding (10px) so the peak doesn't touch the very top
                double y = height - (normalizedY * (height - 10)); 
                points.Add(new System.Windows.Point(x, y));
            }

            // Close the shape at bottom right
            points.Add(new System.Windows.Point(width, height));
            
            SpeedDistributionPoints = points;
            HasSpeedDistribution = true;
        }

        private string GetDetailsForEvent(string e, JsonElement dataElement)
        {
            return e switch
            {
                "HOOK" => dataElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
                "APP_STATE" => dataElement.TryGetProperty("focus", out var f) ? $"Foco: {f.GetString()}" : (dataElement.TryGetProperty("state", out var s) ? $"Estado: {s.GetString()}" : ""),
                "LOOKUP" => $"Consulta: {dataElement.GetRawText()}",
                "MINE" => $"Minerado: {(dataElement.TryGetProperty("card", out var c) ? c.GetString() : "")}",
                "CLICK" => $"Clique ({(dataElement.TryGetProperty("source", out var src) ? src.GetString() : "")}): Target={(dataElement.TryGetProperty("target", out var trg) ? trg.GetString() : "")}",
                _ => dataElement.GetRawText()
            };
        }

        [RelayCommand]
        private async Task DeleteItemsAsync(System.Collections.IList items)
        {
            if (items == null || items.Count == 0 || string.IsNullOrEmpty(Session?.LogFilePath)) return;

            var itemsToDelete = items.Cast<SessionDetailItem>().ToList();
            var indicesToDelete = new HashSet<int>(itemsToDelete.Select(x => x.LineIndex));

            string originalFilePath = Session.LogFilePath;

            try
            {
                var allLines = await File.ReadAllLinesAsync(originalFilePath);
                var remainingLines = allLines.Where((line, index) => !indicesToDelete.Contains(index)).ToList();
                await File.WriteAllLinesAsync(originalFilePath, remainingLines);

                await LoadLogsAsync(); // Reload everything
                
                // Update session in DB via analytics engine call
                await _analyticsEngine.ProcessAndSaveSessionAsync(Session);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete log lines: {ex.Message}");
            }
        }
    }
}