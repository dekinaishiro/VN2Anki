using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Data;
using VN2Anki.Messages;
using VN2Anki.Models.Entities;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class SessionManagerService : ISessionManagerService
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly DiscordRpcService _discordRpc;
        private readonly IWindowService _windowService;
        private readonly IServiceProvider _serviceProvider;
        private readonly SessionTracker _tracker;

        public bool IsBufferActive { get; set; }

        public SessionManagerService(
            SessionTracker tracker,
            MiningService miningService,
            IConfigurationService configService,
            DiscordRpcService discordRpc,
            IWindowService windowService,
            IServiceProvider serviceProvider)
        {
            _tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _discordRpc = discordRpc;
            _windowService = windowService;
            _serviceProvider = serviceProvider;
        }

        public bool ToggleBuffer(VisualNovel currentVN)
        {
            if (!IsBufferActive)
            {
                var config = _configService.CurrentConfig;

                if (string.IsNullOrEmpty(config.Media.VideoWindow))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Video Source vazia! Selecione o vídeo.", IsError = true }));
                    return false;
                }

                var procs = System.Diagnostics.Process.GetProcessesByName(config.Media.VideoWindow);
                bool isRunning = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                foreach (var p in procs) p.Dispose();

                if (!isRunning)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "A janela alvo está fechada! Abra o jogo e tente novamente.", IsError = true }));
                    return false;
                }

                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == config.Media.AudioDevice)?.Id;

                if (string.IsNullOrEmpty(deviceId))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Configure o Áudio primeiro!", IsError = true }));
                    return false;
                }

                if (currentVN == null && _tracker.ValidCharacterCount == 0 && _tracker.Elapsed.TotalSeconds == 0)
                {
                    bool result = _windowService.ShowConfirmation(
                        "Nenhuma Visual Novel está vinculada a esta sessão.\nDeseja iniciar o rastreamento avulso mesmo assim?",
                        "Aviso de Sessão Vazia",
                        true);

                    if (!result) return false;
                }

                _miningService.StartBuffer(deviceId);
                IsBufferActive = true;

                string vnTitle = currentVN?.Title ?? "Reading a VN";
                string imageUrl = currentVN?.CoverImageUrl ?? "default_icon";
                DateTime startTime = DateTime.UtcNow.Subtract(_tracker.Elapsed);
                _ = _discordRpc.UpdatePresenceAsync(
                    vnTitle,
                    "Reading",
                    $"{_tracker.ValidCharacterCount} chars",
                    startTime,
                    imageUrl
                );
            }
            else
            {
                _miningService.StopBuffer();
                IsBufferActive = false;

                string vnTitle = currentVN?.Title ?? "Reading a VN";
                string imageUrl = currentVN?.CoverImageUrl ?? "default_icon";
                string elapsedStr = _tracker.Elapsed.ToString(@"hh\:mm\:ss");
                _ = _discordRpc.UpdatePresenceAsync(
                    vnTitle,
                    "Paused",
                    $"{_tracker.ValidCharacterCount} chars | {elapsedStr}",
                    null,
                    imageUrl
                );
            }

            return IsBufferActive;
        }

        public void EndSession(VisualNovel currentVN)
        {
            bool hasProgress = _tracker.Elapsed.TotalSeconds > 0 || _tracker.ValidCharacterCount > 0;

            if (hasProgress)
            {
                int? vnIdToSave = currentVN?.Id;

                if (currentVN == null)
                {
                    bool result = _windowService.ShowConfirmation(
                        "Você leu alguns caracteres, mas nenhuma Visual Novel está selecionada.\nDeseja salvar este progresso no histórico para vinculá-lo a um jogo mais tarde?",
                        "Salvar Sessão Órfã?");

                    if (!result)
                    {
                        hasProgress = false;
                    }
                }

                if (hasProgress)
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var record = new SessionRecord
                        {
                            VisualNovelId = vnIdToSave,
                            StartTime = System.DateTime.Now - _tracker.Elapsed,
                            EndTime = System.DateTime.Now,
                            DurationSeconds = (int)_tracker.Elapsed.TotalSeconds,
                            CharactersRead = _tracker.ValidCharacterCount,
                            CardsMined = 0
                        };
                        db.Sessions.Add(record);
                        db.SaveChanges();
                    }

                    WeakReferenceMessenger.Default.Send(new SessionSavedMessage());
                }
            }

            _ = _discordRpc.ClearPresenceAsync();
            if (IsBufferActive)
            {
                _miningService.StopBuffer();
                IsBufferActive = false;
            }
            _tracker.Reset();
            foreach (var slot in _miningService.HistorySlots) slot.Dispose();
            _miningService.HistorySlots.Clear();
            
            WeakReferenceMessenger.Default.Send(new SessionEndedMessage());
        }
    }
}