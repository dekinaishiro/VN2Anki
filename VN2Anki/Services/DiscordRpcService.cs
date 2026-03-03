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

        // Você pode usar o seu AppId ou usar o do GSM (1441571345942052935) para testar!
        private const string AppId = "1478238502486540288";

        public DiscordRpcService()
        {
            // Tenta conectar em background assim que o serviço é injetado
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

                // Handshake obrigatório do Discord
                var handshake = new { v = 1, client_id = AppId };
                await SendFrameAsync(0, JsonSerializer.Serialize(handshake));

                // Inicia um loop apenas para manter o pipe lendo (o Discord exige isso)
                _ = Task.Run(ReadLoopAsync);
            }
            catch (Exception)
            {
                // Discord fechado, ignorar silenciosamente
            }
        }

        public async Task UpdatePresenceAsync(string gameName, string details, string state, DateTime? startTimestamp, string imageUrl)
        {
            if (_pipe == null || !_pipe.IsConnected) await ConnectAsync();
            if (_pipe == null || !_pipe.IsConnected) return;

            // Usando o Dictionary para omitir o relógio com precisão militar
            var activity = new System.Collections.Generic.Dictionary<string, object>
    {
        { "name", gameName }, // Título Gigante em Negrito (Ex: Summer Pockets)
        { "type", 0 }, // 0 = PLAYING
        { "details", details }, // Primeira linha abaixo do título (Ex: Mining / Paused)
        { "state", state }, // Segunda linha (Ex: 10 chars | 00:15:30)
        { "assets", new { large_image = imageUrl, large_text = gameName } }
    };

            // Só manda a chave de tempo pro Discord se estiver rodando
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
            catch { /* Ignorar caso o Discord tenha sido fechado */ }
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