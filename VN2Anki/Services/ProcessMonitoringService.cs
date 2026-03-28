using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class ProcessMonitoringService : IProcessMonitoringService
    {
        private readonly IVnDatabaseService _vnDatabaseService;
        private readonly ILogger<ProcessMonitoringService> _logger;

        private ManagementEventWatcher? _startWatcher;
        private ManagementEventWatcher? _stopWatcher;
        private bool _isMonitoring = false;

        // Keeps track of known active processes: PID -> ProcessName
        private Dictionary<int, string> _activeProcessIds = new Dictionary<int, string>();
        // Keeps track of VNs we have actively alerted as started: PID -> VisualNovel
        private Dictionary<int, VisualNovel> _activeVns = new Dictionary<int, VisualNovel>();

        public event EventHandler<VnProcessEventArgs>? VnProcessStarted;
        public event EventHandler<VnProcessEventArgs>? VnProcessStopped;

        public ProcessMonitoringService(IVnDatabaseService vnDatabaseService, ILogger<ProcessMonitoringService> logger)
        {
            _vnDatabaseService = vnDatabaseService;
            _logger = logger;
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            _logger.LogInformation("Starting Process Monitoring (WMI Event mode)...");

            RefreshActiveProcessesSnapshot();
            
            try
            {
                // Watch for process starts
                var startQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
                _startWatcher = new ManagementEventWatcher(startQuery);
                _startWatcher.EventArrived += OnProcessStarted;
                _startWatcher.Start();

                // Watch for process stops
                var stopQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process'");
                _stopWatcher = new ManagementEventWatcher(stopQuery);
                _stopWatcher.EventArrived += OnProcessStopped;
                _stopWatcher.Start();

                _isMonitoring = true;
                _logger.LogInformation("WMI Process monitoring started successfully.");

                // Scan for already running VNs
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var runningVns = await GetRunningVnsAsync();
                        foreach (var item in runningVns)
                        {
                            var vn = item.Vn;
                            var process = item.Process;
                            
                            if (!_activeVns.ContainsKey(process.Id))
                            {
                                _activeVns[process.Id] = vn;
                                _logger.LogInformation($"Detected already running VN during startup: {vn.Title} (PID: {process.Id})");
                                
                                VnProcessStarted?.Invoke(this, new VnProcessEventArgs
                                {
                                    VisualNovel = vn,
                                    Process = process,
                                    ProcessId = process.Id
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to scan for already running VNs on startup.");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start WMI Process monitoring. Ensure the application has the necessary permissions.");
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _logger.LogInformation("Stopping WMI process monitoring...");

            try
            {
                if (_startWatcher != null)
                {
                    _startWatcher.EventArrived -= OnProcessStarted;
                    _startWatcher.Stop();
                    _startWatcher.Dispose();
                    _startWatcher = null;
                }

                if (_stopWatcher != null)
                {
                    _stopWatcher.EventArrived -= OnProcessStopped;
                    _stopWatcher.Stop();
                    _stopWatcher.Dispose();
                    _stopWatcher = null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping WMI watchers.");
            }

            _isMonitoring = false;
            _activeProcessIds.Clear();
            _activeVns.Clear();

            _logger.LogInformation("Process monitoring stopped.");
        }

        private void RefreshActiveProcessesSnapshot()
        {
            _activeProcessIds.Clear();
            try
            {
                var currentProcesses = Process.GetProcesses();
                foreach (var p in currentProcesses)
                {
                    if (!IsSystemProcess(p.ProcessName))
                    {
                        _activeProcessIds[p.Id] = p.ProcessName;
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to refresh active processes snapshot."); }
        }

        private async void OnProcessStarted(object sender, EventArrivedEventArgs e)
        {
            try
            {
                using var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                int processId = Convert.ToInt32(targetInstance["ProcessId"]);
                string processName = targetInstance["Name"]?.ToString() ?? string.Empty;
                string executablePath = targetInstance["ExecutablePath"]?.ToString() ?? string.Empty;

                if (IsSystemProcess(processName)) return;

                // Remove .exe extension if present for ProcessName matching consistency
                string processNameWithoutExt = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) 
                    ? processName.Substring(0, processName.Length - 4) 
                    : processName;

                _activeProcessIds[processId] = processNameWithoutExt;

                var vns = await _vnDatabaseService.GetAllVisualNovelsAsync();
                
                VisualNovel? matchingVn = vns.FirstOrDefault(v =>
                    string.Equals(v.ProcessName, processNameWithoutExt, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(v.ProcessName, processName, StringComparison.OrdinalIgnoreCase));

                if (matchingVn == null && !string.IsNullOrEmpty(executablePath))
                {
                    matchingVn = vns.FirstOrDefault(v =>
                        string.Equals(v.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
                }

                if (matchingVn != null)
                {
                    Process? process = null;
                    try
                    {
                         process = Process.GetProcessById(processId);
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Could not get process object for PID {ProcessId}.", processId); }

                    _activeVns[processId] = matchingVn;
                    _logger.LogInformation($"Detected VN start via WMI: {matchingVn.Title} (PID: {processId})");
                    
                    VnProcessStarted?.Invoke(this, new VnProcessEventArgs
                    {
                        VisualNovel = matchingVn,
                        Process = process,
                        ProcessId = processId
                    });
                }
            }
            catch (Exception ex)
            {
                 _logger.LogError(ex, "Error processing WMI process start event.");
            }
        }

        private void OnProcessStopped(object sender, EventArrivedEventArgs e)
        {
             try
             {
                 using var targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                 int processId = Convert.ToInt32(targetInstance["ProcessId"]);

                 if (_activeProcessIds.ContainsKey(processId))
                 {
                     _activeProcessIds.Remove(processId);
                 }

                 if (_activeVns.TryGetValue(processId, out var vn))
                 {
                     _activeVns.Remove(processId);
                     _logger.LogInformation($"Detected VN stop via WMI: {vn.Title} (PID: {processId})");
                     
                     VnProcessStopped?.Invoke(this, new VnProcessEventArgs
                     {
                         VisualNovel = vn,
                         Process = null,
                         ProcessId = processId
                     });
                 }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Error processing WMI process stop event.");
             }
        }

        public async Task<List<(VisualNovel Vn, Process Process)>> GetRunningVnsAsync()
        {
            var runningVns = new List<(VisualNovel, Process)>();
            
            try 
            {
                var vns = await _vnDatabaseService.GetAllVisualNovelsAsync();
                if (vns == null || !vns.Any()) return runningVns;

                var allProcesses = Process.GetProcesses();

                foreach (var process in allProcesses)
                {
                    if (IsSystemProcess(process.ProcessName)) continue;

                    try
                    {
                        var processNameWithExt = process.ProcessName + ".exe";
                        
                        var vn = vns.FirstOrDefault(v => 
                            string.Equals(v.ProcessName, processNameWithExt, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(v.ProcessName, process.ProcessName, StringComparison.OrdinalIgnoreCase));

                        if (vn == null)
                        {
                            try
                            {
                                var path = process.MainModule?.FileName;
                                if (!string.IsNullOrEmpty(path))
                                {
                                    vn = vns.FirstOrDefault(v => string.Equals(v.ExecutablePath, path, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                            catch (Exception ex) { _logger.LogDebug(ex, "Access denied getting MainModule for process {ProcessName}.", process.ProcessName); }
                        }

                        if (vn != null)
                        {
                            runningVns.Add((vn, process));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error processing a running process for VN detection.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting running VNs.");
            }

            return runningVns;
        }

        public bool IsAnyInstanceRunning(int vnId)
        {
            return _activeVns.Values.Any(v => v.Id == vnId);
        }

        private bool IsSystemProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return true;

            string lowerName = processName.ToLowerInvariant();
            
            if (lowerName == "svchost" || lowerName == "conhost" || lowerName == "dllhost" || 
                lowerName == "cmd" || lowerName == "taskhostw" || lowerName == "explorer" || 
                lowerName == "csrss" || lowerName == "lsass" || lowerName == "winlogon" || 
                lowerName == "services" || lowerName == "smss" || lowerName == "idle" || 
                lowerName == "system" || lowerName == "registry" || lowerName == "fontdrvhost" ||
                lowerName == "wmiprvse")
            {
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            StopMonitoring();
            GC.SuppressFinalize(this);
        }
    }
}
