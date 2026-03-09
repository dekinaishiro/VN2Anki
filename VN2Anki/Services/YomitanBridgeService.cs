using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VN2Anki.Services.Interfaces;
using VN2Anki.Models;
using System.Linq;

namespace VN2Anki.Services
{
    public class YomitanBridgeService : IBridgeService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<YomitanBridgeService> _logger;
        private readonly AnkiHandler _ankiHandler;
        private readonly MediaService _mediaService;
        private readonly MiningService _miningService;
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private readonly HttpClient _httpClient;

        public Guid? ActiveHoverSlotId { get; set; }

        public YomitanBridgeService(
            IConfigurationService configService, 
            ILogger<YomitanBridgeService> logger,
            AnkiHandler ankiHandler,
            MediaService mediaService,
            MiningService miningService)
        {
            _configService = configService;
            _logger = logger;
            _ankiHandler = ankiHandler;
            _mediaService = mediaService;
            _miningService = miningService;
            _httpClient = new HttpClient();
            
            Start();
        }

        private void Start()
        {
            var config = _configService.CurrentConfig;
            if (!config.Anki.EnableYomitanBridge) return;

            int port = config.Anki.YomitanBridgePort;
            _logger.LogInformation($"Starting Yomitan Bridge on port {port}...");

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
                _cts = new CancellationTokenSource();
                _listenerTask = Task.Run(() => ListenAsync(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Yomitan Bridge.");
            }
        }

        private void Stop()
        {
            if (_listener == null) return;
            _cts?.Cancel();
            try { _listener.Stop(); } catch { }
            _listener = null;
            _listenerTask?.Wait(1000);
            _listenerTask = null;
        }

        public void Restart(int newPort) { Stop(); Start(); }

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(token);
                    _ = ProcessClientAsync(client, token);
                }
                catch { break; }
            }
        }

        private async Task<string> InterceptAndInjectMediaAsync(string originalBody)
        {
            try
            {
                if (_miningService.HistorySlots.Count == 0) return originalBody;
                var config = _configService.CurrentConfig;
                string targetAudioField = config.Anki.AudioField;
                string targetImageField = config.Anki.ImageField;

                if (string.IsNullOrWhiteSpace(targetAudioField) && string.IsNullOrWhiteSpace(targetImageField))
                    return originalBody;

                var jsonNode = JsonNode.Parse(originalBody);
                if (jsonNode == null) return originalBody;

                string? action = jsonNode["action"]?.ToString();
                if (action != "addNote" && action != "guiAddCards") return originalBody;

                var parameters = jsonNode["params"];
                if (parameters == null) return originalBody;

                JsonObject? fields = null;
                if (action == "addNote") fields = parameters["note"]?["fields"] as JsonObject;
                else if (action == "guiAddCards") fields = parameters["note"]?["fields"] as JsonObject;

                if (fields == null) return originalBody;

                MiningSlot targetSlot = null;
                if (ActiveHoverSlotId.HasValue)
                {
                    targetSlot = _miningService.HistorySlots.FirstOrDefault(s => s.Id == ActiveHoverSlotId.Value.ToString());
                    ActiveHoverSlotId = null;
                }
                
                if (targetSlot == null && _miningService.HistorySlots.Count > 0)
                    targetSlot = _miningService.HistorySlots[0];

                if (targetSlot == null) return originalBody;

                string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                
                if (targetSlot.AudioBytes != null && !string.IsNullOrWhiteSpace(targetAudioField))
                {
                    try {
                        byte[] audio = targetSlot.AudioBytes;
                        if (config.Media.AudioBitrate > 0) audio = _mediaService.ConvertWavToMp3(audio, config.Media.AudioBitrate);
                        string filename = $"miner_{uniqueId}.{(config.Media.AudioBitrate > 0 ? "mp3" : "wav")}";
                        if (await _ankiHandler.StoreMediaAsync(filename, audio)) fields[targetAudioField] = $"[sound:{filename}]";
                    } catch { }
                }

                if (targetSlot.ScreenshotBytes != null && !string.IsNullOrWhiteSpace(targetImageField))
                {
                    try {
                        string filename = $"miner_{uniqueId}.jpg";
                        if (await _ankiHandler.StoreMediaAsync(filename, targetSlot.ScreenshotBytes)) fields[targetImageField] = $"<img src=\"{filename}\">";
                    } catch { }
                }

                return jsonNode.ToJsonString();
            }
            catch { return originalBody; }
        }

        private async Task ProcessClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    // 1. Read Request Headers (byte by byte to avoid over-reading)
                    var headerBuilder = new StringBuilder();
                    int lastByte = -1;
                    while (true)
                    {
                        int b = stream.ReadByte();
                        if (b == -1) return;
                        headerBuilder.Append((char)b);
                        if (lastByte == '\n' && b == '\r') {
                            int next = stream.ReadByte();
                            if (next == '\n') break; // Found \r\n\r\n
                            headerBuilder.Append((char)next);
                            b = next;
                        }
                        lastByte = b;
                    }

                    string headers = headerBuilder.ToString();
                    string firstLine = headers.Split('\n')[0];
                    string method = firstLine.Split(' ')[0];

                    int contentLength = 0;
                    foreach (var line in headers.Split('\n'))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            int.TryParse(line.Substring(15).Trim(), out contentLength);
                    }

                    // 2. Handle CORS Options
                    if (method == "OPTIONS")
                    {
                        byte[] resp = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: POST, GET, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\nAccess-Control-Allow-Private-Network: true\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(resp, 0, resp.Length);
                        return;
                    }

                    // 3. Read Body strictly by bytes
                    byte[] bodyBytes = new byte[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        int read = await stream.ReadAsync(bodyBytes, totalRead, contentLength - totalRead, token);
                        if (read == 0) break;
                        totalRead += read;
                    }

                    string requestBody = Encoding.UTF8.GetString(bodyBytes);
                    string interceptedBody = await InterceptAndInjectMediaAsync(requestBody);

                    // 4. Proxy to Anki
                    var httpContent = new StringContent(interceptedBody, Encoding.UTF8, "application/json");
                    var proxyResponse = await _httpClient.PostAsync(_configService.CurrentConfig.Anki.Url, httpContent, token);
                    byte[] responseBody = await proxyResponse.Content.ReadAsByteArrayAsync();

                    // 5. Send Response back
                    string responseHeaders = $"HTTP/1.1 {(int)proxyResponse.StatusCode} OK\r\nContent-Type: application/json\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Private-Network: true\r\nContent-Length: {responseBody.Length}\r\nConnection: close\r\n\r\n";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(responseHeaders);

                    await stream.WriteAsync(headerBytes, 0, headerBytes.Length);
                    await stream.WriteAsync(responseBody, 0, responseBody.Length);
                    await stream.FlushAsync();
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "Bridge Error"); }
        }

        public void Dispose() { Stop(); _httpClient.Dispose(); }
    }
}
