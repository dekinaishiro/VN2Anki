using CommunityToolkit.Mvvm.Messaging;
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
        //public event Action<string, DateTime> OnTextCopied;

        private readonly IConfigurationService _configService;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;

        private string _lastText = string.Empty;
        private DateTime _lastTime = DateTime.MinValue;

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
                    _webSocket.Options.KeepAliveInterval = System.Threading.Timeout.InfiniteTimeSpan;
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
                        await Task.Delay(500, _cancellationTokenSource.Token);
                    }
                }
            }
        }

        private async Task ReceiveLoopAsync()
        {
            DebugLogger.Log($"[0-WEBSOCKET] Starting port reading loop...");
            try
            {
                var buffer = new byte[4096];
                while (_webSocket.State == WebSocketState.Open)
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    DebugLogger.Log($"[0.5-RAW] Frame received. Type: {result.MessageType} | Size: {ms.Length}");

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        DebugLogger.Log("[WEBSOCKET-CLOSED] Server closed connection peacefully.");
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        string rawMessage = Encoding.UTF8.GetString(ms.ToArray());
                        DebugLogger.Log($"[1-WEBSOCKET] RAW packet decoded: {rawMessage}");

                        try
                        {
                            string message = rawMessage.Trim(); // Substitua pela sua lógica

                            if (string.IsNullOrWhiteSpace(message)) continue;

                            DebugLogger.Log($"[2-WEBSOCKET] Dispatching TextCopiedMessage via Messenger | Text: {message}");

                            Task.Run(() =>
                            {
                                WeakReferenceMessenger.Default.Send(new Messages.TextCopiedMessage(message, DateTime.Now));
                            });
                        }
                        catch (Exception parseEx)
                        {
                            DebugLogger.Log($"[PARSE-ERROR] Code failed to handle raw string! Error: {parseEx.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // SE O LOG MOSTRAR ISSO AQUI, ACHAMOS O ASSASSINO DAS FRASES
                DebugLogger.Log($"[FATAL-CRASH] WebSocket loop crashed and will restart! Error: {ex.Message}");
            }
            DebugLogger.Log($"[0-WEBSOCKET] Reading loop ended.");
        }
    }
}