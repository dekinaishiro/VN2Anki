using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VN2Anki.Services.Interfaces;
using VN2Anki.Models;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace VN2Anki.Services
{
    public class YomitanBridgeService : IBridgeService
    {
        private readonly IConfigurationService _configService;
        private readonly ILogger<YomitanBridgeService> _logger;
        private readonly AnkiHandler _ankiHandler;
        private readonly MediaService _mediaService;
        private readonly MiningService _miningService;
        private readonly HttpClient _httpClient;
        
        private WebApplication? _app;
        private CancellationTokenSource? _cts;

        public string ActiveHoverSlotId { get; set; } = string.Empty;

        public YomitanBridgeService(
            IConfigurationService configService, 
            ILogger<YomitanBridgeService> logger,
            AnkiHandler ankiHandler,
            MediaService mediaService,
            MiningService miningService,
            HttpClient httpClient)
        {
            _configService = configService;
            _logger = logger;
            _ankiHandler = ankiHandler;
            _mediaService = mediaService;
            _miningService = miningService;
            _httpClient = httpClient;
            
            Start();
        }

        private void Start()
        {
            var config = _configService.CurrentConfig;
            if (!config.Anki.EnableYomitanBridge) return;

            int port = config.Anki.YomitanBridgePort;
            _logger.LogInformation($"Starting Kestrel Yomitan Bridge on port {port}...");

            try
            {
                _cts = new CancellationTokenSource();
                
                var builder = WebApplication.CreateBuilder();
                
                // Configure Kestrel to listen only on localhost for security
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ListenLocalhost(port);
                });

                // Add CORS to handle browser extension preflights automatically
                builder.Services.AddCors(options =>
                {
                    options.AddDefaultPolicy(policy =>
                    {
                        policy.AllowAnyOrigin()
                              .AllowAnyMethod()
                              .AllowAnyHeader()
                              .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
                    });
                });

                _app = builder.Build();

                // Middleware for CORS
                _app.UseCors();

                // The single endpoint that Yomitan calls (usually root / or /api)
                _app.MapPost("/", async (HttpContext context) =>
                {
                    try 
                    {
                        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                        string requestBody = await reader.ReadToEndAsync();
                        
                        _logger.LogDebug("Received request from Yomitan/Browser.");

                        // 1. Intercept and Inject Media
                        string interceptedBody = await InterceptAndInjectMediaAsync(requestBody);

                        // 2. Proxy to AnkiConnect
                        var ankiUrl = _configService.CurrentConfig.Anki.Url;
                        var httpContent = new StringContent(interceptedBody, Encoding.UTF8, "application/json");
                        
                        var proxyResponse = await _httpClient.PostAsync(ankiUrl, httpContent, _cts.Token);
                        
                        // 3. Return response with Private Network headers
                        context.Response.Headers["Access-Control-Allow-PrivateNetwork"] = "true";
                        context.Response.ContentType = "application/json";
                        context.Response.StatusCode = (int)proxyResponse.StatusCode;
                        
                        byte[] responseBody = await proxyResponse.Content.ReadAsByteArrayAsync();
                        await context.Response.Body.WriteAsync(responseBody, 0, responseBody.Length);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing bridge request.");
                        context.Response.StatusCode = 500;
                        await context.Response.WriteAsync("Bridge Error");
                    }
                });

                // Start the server in a background task
                var token = _cts.Token;
                Task.Run(async () => {
                    try {
                        if (_app != null) await _app.StartAsync(token);
                    } catch (OperationCanceledException) { }
                    catch (Exception ex) { _logger.LogError(ex, "Kestrel server error."); }
                }, token);
                
                _logger.LogInformation("Yomitan Bridge (Kestrel) is now running.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start Yomitan Bridge with Kestrel.");
            }
        }

        private void Stop()
        {
            if (_app == null) return;
            _cts?.Cancel();
            
            try 
            {
                _app.StopAsync().Wait(1000); 
            } 
            catch { }

            _app = null;
            _cts?.Dispose();
            _cts = null;
        }

        public void Restart(int newPort) 
        { 
            Stop(); 
            Start(); 
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

                MiningSlot? targetSlot = null;
                if (!string.IsNullOrEmpty(ActiveHoverSlotId))
                {
                    targetSlot = _miningService.HistorySlots.FirstOrDefault(s => s.Id == ActiveHoverSlotId);
                    ActiveHoverSlotId = string.Empty;
                }
                
                if (targetSlot == null && _miningService.HistorySlots.Count > 0)
                    targetSlot = _miningService.HistorySlots[0];

                if (targetSlot == null) return originalBody;

                string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
                
                if (targetSlot.AudioBytes != null && !string.IsNullOrWhiteSpace(targetAudioField))
                {
                    try {
                        byte[] audio = targetSlot.AudioBytes;
                        string filename = $"miner_{uniqueId}.mp3";
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
            catch (Exception ex)
            { 
                _logger.LogWarning(ex, "Media injection failed, continuing with original body.");
                return originalBody; 
            }
        }

        public void Dispose() 
        { 
            Stop(); 
            GC.SuppressFinalize(this);
        }
    }
}
