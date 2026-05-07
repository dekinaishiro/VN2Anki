using CommunityToolkit.Mvvm.Messaging;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VN2Anki.Services
{
    public class MpvHook : ITextHook
    {
        private readonly IConfigurationService _configService;
        private CancellationTokenSource _cts;
        private bool _isRunning;
        private string _lastText = string.Empty;

        public MpvHook(IConfigurationService configService)
        {
            _configService = configService;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            _cts?.Dispose();
        }

        private async Task RunLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var pipeName = _configService.CurrentConfig.Hook.MpvPipeName;
                try
                {
                    using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                    {
                        DebugLogger.Log($"[MPV-HOOK] Connecting to pipe: {pipeName}...");
                        await client.ConnectAsync(2000, ct);
                        DebugLogger.Log("[MPV-HOOK] Connected!");

                        using (var reader = new StreamReader(client, new UTF8Encoding(false)))
                        using (var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true })
                        {
                            // Observe sub-text property
                            var observeCmd = JsonSerializer.Serialize(new { command = new object[] { "observe_property", 1, "sub-text" } });
                            await writer.WriteLineAsync(observeCmd);

                            while (!ct.IsCancellationRequested && client.IsConnected)
                            {
                                var line = await reader.ReadLineAsync();
                                if (line == null) break;

                                ProcessMpvMessage(line);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[MPV-HOOK Error] {ex.Message}. Retrying in 2s...");
                    await Task.Delay(2000, ct);
                }
            }
            DebugLogger.Log("[MPV-HOOK] Loop ended.");
        }

        private void ProcessMpvMessage(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("event", out var eventProp) && eventProp.GetString() == "property-change")
                    {
                        if (root.TryGetProperty("name", out var nameProp) && nameProp.GetString() == "sub-text")
                        {
                            if (root.TryGetProperty("data", out var dataProp))
                            {
                                // data can be null if no sub is being displayed
                                string text = dataProp.ValueKind == JsonValueKind.String ? dataProp.GetString() : string.Empty;
                                text = text?.Trim();

                                if (!string.IsNullOrWhiteSpace(text) && text != _lastText)
                                {
                                    _lastText = text;
                                    DebugLogger.Log($"[MPV-HOOK] Dispatching: {text}");
                                    WeakReferenceMessenger.Default.Send(new Messages.TextCopiedMessage(text, DateTime.Now));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[MPV-HOOK Parse Error] {ex.Message}");
            }
        }
    }
}