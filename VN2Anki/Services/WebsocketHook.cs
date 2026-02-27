using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VN2Anki.Services
{
    public class WebsocketHook
    {
        public event Action<string, DateTime> OnTextCopied;

        private readonly IConfigurationService _configService;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;

        public WebsocketHook(IConfigurationService configService)
        {
            _configService = configService;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _ = ConnectAndListenAsync();
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                _ = _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopped by user", CancellationToken.None);
            }

            _webSocket?.Dispose();
        }

        private async Task ConnectAndListenAsync()
        {
            var url = _configService.CurrentConfig.Hook.WebSocketUrl;

            while (_isRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                using (_webSocket = new ClientWebSocket())
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebSocket] Connecting to {url}...");
                        await _webSocket.ConnectAsync(new Uri(url), _cancellationTokenSource.Token);
                        System.Diagnostics.Debug.WriteLine("[WebSocket] Connected!");

                        await ReceiveLoopAsync();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebSocket Error] {ex.Message}");
                        await Task.Delay(3000, _cancellationTokenSource.Token);
                    }
                }
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[4096];

            while (_webSocket.State == WebSocketState.Open && !_cancellationTokenSource.Token.IsCancellationRequested)
            {
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closed", CancellationToken.None);
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string message = Encoding.UTF8.GetString(ms.ToArray());
                        OnTextCopied?.Invoke(message, DateTime.Now);
                    }
                }
            }
        }
    }
}