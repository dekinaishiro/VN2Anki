using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace VN2Anki.Services
{
    public class DiscordRpcService : IDisposable
    {
        private NamedPipeClientStream _pipe;
        private CancellationTokenSource _cts;
        private const string AppId = "1478238502486540288";

        public DiscordRpcService()
        {
            // tries background connection to avoid blocking the UI on startup if Discord is not running
            _ = ConnectAsync();
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
            catch (Exception)
            {
                // ignore if discord not running
            }
        }

        public async Task UpdatePresenceAsync(string gameName, string details, string state, DateTime? startTimestamp, string imageUrl)
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

        public async Task ClearPresenceAsync()
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
            catch { /* ignore if discord has been closed */ }
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
            catch { }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _pipe?.Dispose();
        }
    }
}