using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;

namespace VN2Anki.Services
{
    public class DiscordRpcService : IDisposable, IRecipient<BufferStartedMessage>, IRecipient<BufferStoppedMessage>, IRecipient<SessionEndedMessage>, IRecipient<CurrentVnChangedMessage>, IRecipient<CurrentVnUnlinkedMessage>
    {
        private NamedPipeClientStream _pipe;
        private CancellationTokenSource _cts;
        private const string AppId = "1478238502486540288";

        private readonly SessionTracker _tracker;
        private VisualNovel _currentVn;
        private bool _isBufferActive;

        public DiscordRpcService(SessionTracker tracker)
        {
            _tracker = tracker;
            _tracker.PropertyChanged += Tracker_PropertyChanged;

            WeakReferenceMessenger.Default.RegisterAll(this);

            // tries background connection to avoid blocking the UI on startup if Discord is not running
            _ = ConnectAsync();
        }

        private void Tracker_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SessionTracker.ValidCharacterCount) && _isBufferActive)
            {
                UpdatePresence();
            }
        }

        public void Receive(BufferStartedMessage message)
        {
            _isBufferActive = true;
            UpdatePresence();
        }

        public void Receive(BufferStoppedMessage message)
        {
            _isBufferActive = false;
            UpdatePresence();
        }

        public void Receive(SessionEndedMessage message)
        {
            _isBufferActive = false;
            _ = ClearPresenceAsync();
        }

        public void Receive(CurrentVnChangedMessage message)
        {
            _currentVn = message.Value;
            if (_isBufferActive || _tracker.ValidCharacterCount > 0)
            {
                UpdatePresence();
            }
        }

        public void Receive(CurrentVnUnlinkedMessage message)
        {
            _currentVn = null;
            if (_isBufferActive || _tracker.ValidCharacterCount > 0)
            {
                UpdatePresence();
            }
            else
            {
                _ = ClearPresenceAsync();
            }
        }

        private void UpdatePresence()
        {
            string vnTitle = "Reading a VN";
            if (_currentVn != null)
            {
                vnTitle = !string.IsNullOrWhiteSpace(_currentVn.OriginalTitle) ? _currentVn.OriginalTitle : _currentVn.Title;
            }

            string imageUrl = _currentVn?.CoverImageUrl ?? "default_icon";

            if (_isBufferActive)
            {
                DateTime startTime = DateTime.UtcNow.Subtract(_tracker.Elapsed);
                _ = UpdatePresenceAsync(vnTitle, "Reading", $"{_tracker.ValidCharacterCount} chars", startTime, imageUrl);
            }
            else
            {
                string elapsedStr = _tracker.Elapsed.ToString(@"hh\:mm\:ss");
                string state = _currentVn != null ? "Paused" : "No Session";
                string details = _currentVn != null ? $"{_tracker.ValidCharacterCount} chars | {elapsedStr}" : "Waiting...";
                _ = UpdatePresenceAsync(vnTitle, state, details, null, imageUrl);
            }
        }

        private async Task ConnectAsync()
        {
            if (_pipe != null && _pipe.IsConnected) return;

            _pipe = new NamedPipeClientStream(".", "discord-ipc-0", PipeDirection.InOut, PipeOptions.Asynchronous);
            _cts = new CancellationTokenSource();

            try
            {
                await _pipe.ConnectAsync(3000, _cts.Token);      

                // Handshake
                var handshake = new { v = 1, client_id = AppId };
                await SendFrameAsync(0, JsonSerializer.Serialize(handshake));

                // loop to keep pipe alive and read any incoming messages
                _ = Task.Run(ReadLoopAsync);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discord RPC connection failed: {ex.Message}");
            }
        }

        private async Task UpdatePresenceAsync(string gameName, string details, string state, DateTime? startTimestamp, string imageUrl)
        {
            if (_pipe == null || !_pipe.IsConnected) await ConnectAsync();
            if (_pipe == null || !_pipe.IsConnected) return;     

            var activity = new System.Collections.Generic.Dictionary<string, object>
            {
                { "name", gameName },
                { "type", 0 }, // 0 = PLAYING
                { "details", details },
                { "state", state },
                { "assets", new { large_image = imageUrl, large_text = gameName } }
            };

            if (startTimestamp.HasValue)
            {
                activity.Add("timestamps", new { start = ((DateTimeOffset)startTimestamp.Value).ToUnixTimeSeconds() });
            }

            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new
                {
                    pid = Environment.ProcessId,
                    activity = activity
                },
                nonce = Guid.NewGuid().ToString()
            };

            await SendFrameAsync(1, System.Text.Json.JsonSerializer.Serialize(payload));
        }

        private async Task ClearPresenceAsync()
        {
            if (_pipe == null || !_pipe.IsConnected) return;     

            var payload = new
            {
                cmd = "SET_ACTIVITY",
                args = new { pid = Environment.ProcessId },      
                nonce = Guid.NewGuid().ToString()
            };

            await SendFrameAsync(1, JsonSerializer.Serialize(payload));
        }

        private async Task SendFrameAsync(int opCode, string json)
        {
            try
            {
                var payloadBytes = Encoding.UTF8.GetBytes(json);
                var buffer = new byte[payloadBytes.Length + 8];  

                BitConverter.GetBytes(opCode).CopyTo(buffer, 0); 
                BitConverter.GetBytes(payloadBytes.Length).CopyTo(buffer, 4);
                payloadBytes.CopyTo(buffer, 8);

                await _pipe.WriteAsync(buffer, 0, buffer.Length, _cts.Token);
                await _pipe.FlushAsync(_cts.Token);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discord RPC send failed: {ex.Message}");
            }
        }

        private async Task ReadLoopAsync()
        {
            var buffer = new byte[1024];
            try
            {
                while (_pipe.IsConnected && !_cts.Token.IsCancellationRequested)
                {
                    await _pipe.ReadAsync(buffer, 0, buffer.Length, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Discord RPC read loop error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.UnregisterAll(this);
            if (_tracker != null)
            {
                _tracker.PropertyChanged -= Tracker_PropertyChanged;
            }

            _cts?.Cancel();
            _pipe?.Dispose();
        }
    }
}