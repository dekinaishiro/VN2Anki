using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
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

        public ObservableCollection<SessionDetailItem> LogItems { get; } = new();

        [ObservableProperty]
        private bool _isLoadingLogs;

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

        [RelayCommand]
        private async Task LoadLogsAsync()
        {
            if (string.IsNullOrEmpty(Session?.LogFilePath) || !File.Exists(Session.LogFilePath))
                return;

            IsLoadingLogs = true;
            LogItems.Clear();

            try
            {
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
                            if (e == "HOOK" && dataElement.TryGetProperty("text", out var textEl))
                            {
                                details = textEl.GetString() ?? "";
                            }
                            else if (e == "APP_STATE" && dataElement.TryGetProperty("state", out var stateEl))
                            {
                                details = stateEl.GetString() ?? "";
                            }
                            else
                            {
                                details = dataElement.GetRawText();
                            }
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
            }
            finally
            {
                IsLoadingLogs = false;
            }
        }

        [RelayCommand]
        private async Task DeleteItemsAsync(System.Collections.IList items)
        {
            if (items == null || items.Count == 0 || string.IsNullOrEmpty(Session?.LogFilePath)) return;

            var itemsToDelete = items.Cast<SessionDetailItem>().ToList();
            var indicesToDelete = new System.Collections.Generic.HashSet<int>(itemsToDelete.Select(x => x.LineIndex));

            if (indicesToDelete.Count == 0) return;

            string originalFilePath = Session.LogFilePath;
            string backupFilePath = originalFilePath + ".bak";

            // Create backup on first modification
            if (!File.Exists(backupFilePath) && File.Exists(originalFilePath))
            {
                File.Copy(originalFilePath, backupFilePath);
            }

            try
            {
                var allLines = await File.ReadAllLinesAsync(originalFilePath);
                var remainingLines = allLines.Where((line, index) => !indicesToDelete.Contains(index)).ToList();

                await File.WriteAllLinesAsync(originalFilePath, remainingLines);

                // Update UI visually
                foreach (var item in itemsToDelete)
                {
                    LogItems.Remove(item);
                }

                // Force Engine to Recalculate
                await _analyticsEngine.ProcessAndSaveSessionAsync(Session);

                // Refresh the Session reference to update the UI bindings with new values
                var allSessions = await _dbService.GetAllSessionsAsync();
                var updatedSession = allSessions.FirstOrDefault(s => s.Id == Session.Id);
                if (updatedSession != null)
                {
                    Session = updatedSession;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to delete log lines: {ex.Message}");
            }
        }
    }
}
