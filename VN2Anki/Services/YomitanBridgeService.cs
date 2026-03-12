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
        private readonly ISessionLoggerService _sessionLogger;
        
        private WebApplication? _app;
        private CancellationTokenSource? _cts;

        public string ActiveHoverSlotId { get; set; } = string.Empty;

        public YomitanBridgeService(
            IConfigurationService configService,
            ILogger<YomitanBridgeService> logger,
            AnkiHandler ankiHandler,
            MediaService mediaService,
            MiningService miningService,
            HttpClient httpClient,
            ISessionLoggerService sessionLogger)
        {
            _configService = configService;
            _logger = logger;
            _ankiHandler = ankiHandler;
            _mediaService = mediaService;
            _miningService = miningService;
            _httpClient = httpClient;
            _sessionLogger = sessionLogger;

            Start();
        }

        private void Start()
        {
            var config = _configService.CurrentConfig;
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
                    } catch (OperationCanceledException ex) { _logger.LogDebug(ex, "Kestrel server start was cancelled."); }
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
            catch (Exception ex) { _logger.LogDebug(ex, "Error while stopping Kestrel server."); }

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
                var jsonNode = JsonNode.Parse(originalBody);
                if (jsonNode == null) return originalBody;

                string? action = jsonNode["action"]?.ToString();

                if (action == "multi")
                {
                    var actions = jsonNode["params"]?["actions"] as JsonArray;
                    if (actions != null)
                    {
                        foreach (var actionItem in actions)
                        {
                            var subAction = actionItem?["action"]?.ToString();
                            var subParams = actionItem?["params"];
                            
                            await ProcessActionAsync(subAction, subParams);
                        }
                    }
                }
                else
                {
                    await ProcessActionAsync(action, jsonNode["params"]);
                }

                return jsonNode.ToJsonString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media injection failed, continuing with original body.");
                return originalBody;
            }
        }

        private async Task ProcessActionAsync(string? action, JsonNode? parameters)
        {
            if (parameters == null || (action != "addNote" && action != "guiAddCards" && action != "addNotes"))
                return;

            LogMiningEvent(action, parameters);
            if (action == "addNotes") return;

            var config = _configService.CurrentConfig;
            if (string.IsNullOrWhiteSpace(config.Anki.AudioField) && string.IsNullOrWhiteSpace(config.Anki.ImageField))
                return;

            if (_miningService.HistorySlots.Count == 0) return;

            // verify origin of the lookup (overlay vs history)
            MiningSlot? targetSlot = ResolveTargetSlot();
            if (targetSlot == null) return;

            // extract fields object from the request parameters to know where to inject media
            var fields = ExtractFields(action, parameters);
            if (fields == null) return;

            // ensures that slots are sealed
            if (targetSlot.IsOpen) _miningService.SealSlotAudio(targetSlot, DateTime.Now);

            // injects media in anki
            await InjectMediaAsync(targetSlot, fields, config);
        }

        private void LogMiningEvent(string action, JsonNode parameters)
        {
            string cardInfo = "Unknown";
            try
            {
                var fields = ExtractFields(action, parameters);
                if (fields != null && fields.Count > 0)
                {
                    var firstField = fields.First();
                    cardInfo = $"{firstField.Key}: {firstField.Value?.ToString()}";
                }
            }
            catch { /* Ignorar erro de log */ }

            _ = _sessionLogger.LogEventAsync("MINE", new { source = "yomitan", action, card = cardInfo });
        }

        private JsonObject? ExtractFields(string action, JsonNode parameters)
        {
            // O Yomitan envia o campo 'note' para addNote e guiAddCards
            return parameters["note"]?["fields"] as JsonObject;
        }

        private MiningSlot? ResolveTargetSlot()
        {
            var config = _configService.CurrentConfig;
            var history = _miningService.HistorySlots;
            
            if (history.Count == 0) return null;

            // 1. Get window in focus and its process PID
            IntPtr foregroundWindow = Win32InteropService.GetForegroundWindow();
            Win32InteropService.GetWindowThreadProcessId(foregroundWindow, out uint foregroundPid);
            uint currentAppPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

            // 2. Identify Game Process
            bool isGameInFocus = false;
            if (!string.IsNullOrEmpty(config.Media.VideoWindow))
            {
                var gameProcesses = System.Diagnostics.Process.GetProcessesByName(config.Media.VideoWindow);
                isGameInFocus = gameProcesses.Any(p => (uint)p.Id == foregroundPid);
                foreach (var p in gameProcesses) p.Dispose();
            }

            // 3. Decision Logic:
            
            // IF Game is in focus -> CERTAINLY OVERLAY context.
            if (isGameInFocus)
            {
                _logger.LogDebug("Context identified: OVERLAY (Game window active). Using most recent capture.");
                return history[0];
            }

            // IF App itself is in focus -> Either HISTORY list or manual OVERLAY interaction.
            if (foregroundPid == currentAppPid)
            {
                if (!string.IsNullOrEmpty(ActiveHoverSlotId))
                {
                    var slot = history.FirstOrDefault(s => s.Id == ActiveHoverSlotId);
                    if (slot != null)
                    {
                        _logger.LogDebug($"Context identified: HISTORY (App window active, Slot {ActiveHoverSlotId} hovered).");
                        ActiveHoverSlotId = string.Empty; // Consume the hover state
                        return slot;
                    }
                }
                
                _logger.LogDebug("Context identified: APP (App window active, no hover). Defaulting to most recent.");
                return history[0];
            }

            // fall back = uses most recent
            _logger.LogDebug("Context identified: EXTERNAL. Defaulting to most recent.");
            ActiveHoverSlotId = string.Empty; 
            return history[0];
        }

        private async Task InjectMediaAsync(MiningSlot slot, JsonObject fields, AppConfig config)
        {
            string uniqueId = Guid.NewGuid().ToString("N")[..8];

            // Audio injection
            if (slot.AudioBytes != null && !string.IsNullOrWhiteSpace(config.Anki.AudioField))
            {
                try
                {
                    string filename = $"miner_{uniqueId}.mp3";
                    if (await _ankiHandler.StoreMediaAsync(filename, slot.AudioBytes))
                        fields[config.Anki.AudioField] = $"[sound:{filename}]";
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to inject audio."); }
            }

            // Image injection
            if (slot.ScreenshotBytes != null && !string.IsNullOrWhiteSpace(config.Anki.ImageField))
            {
                try
                {
                    string filename = $"miner_{uniqueId}.jpg";
                    if (await _ankiHandler.StoreMediaAsync(filename, slot.ScreenshotBytes))
                        fields[config.Anki.ImageField] = $"<img src=\"{filename}\">";
                }
                catch (Exception ex) { _logger.LogError(ex, "Failed to inject screenshot."); }
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}