using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Services.Interfaces;
using System.Timers;
using System.Threading.Tasks;
using System.Threading.Channels;
using VN2Anki.Locales;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using Timer = System.Timers.Timer;

namespace VN2Anki.Services
{
    public class MiningService : IRecipient<TextCopiedMessage>, IRecipient<AudioErrorMessage>
    {
        public ITextHook TextHook { get; }
        public SessionTracker Tracker { get; }
        public AudioEngine Audio { get; }

        private readonly MediaService _mediaService;
        private readonly Timer _idleTimer;
        private readonly IConfigurationService _configService;

        private readonly object _slotsLock = new object();
        private readonly List<MiningSlot> _historySlots = new List<MiningSlot>();

        public IReadOnlyList<MiningSlot> HistorySlots 
        {
            get { lock(_slotsLock) return _historySlots.ToList(); }
        }

        public void ClearHistorySlots()
        {
            lock (_slotsLock)
            {
                foreach (var slot in _historySlots) slot.Dispose();
                _historySlots.Clear();
            }
            WeakReferenceMessenger.Default.Send(new HistoryClearedMessage());
        }

        private readonly Channel<TextCopiedMessage> _textChannel;
        private readonly CancellationTokenSource _cts = new();

        public MiningService(ITextHook textHook, SessionTracker tracker, AudioEngine audio, MediaService mediaService, IConfigurationService configService)
        {
            TextHook = textHook;
            Tracker = tracker;
            Audio = audio;
            _mediaService = mediaService;
            _configService = configService;

            _idleTimer = new Timer();
            _idleTimer.Elapsed += IdleTimer_Tick;

            _textChannel = Channel.CreateUnbounded<TextCopiedMessage>();
            _ = ProcessTextChannelAsync(); // Dispara o consumidor em background

            WeakReferenceMessenger.Default.RegisterAll(this);
        }

        private void SendStatus(string message)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage(message));
        }

        public void StartBuffer(string audioDeviceId)
        {
            Audio.Start(audioDeviceId);
            TextHook.Start();
            Tracker.Start();
            SendStatus("Buffer running...");
        }

        public void StopBuffer()
        {
            SealAllOpenSlots(DateTime.Now);
            Audio.Stop();
            TextHook.Stop();
            _idleTimer.Stop();
            Tracker.Pause();
            SendStatus("Buffer stopped.");
        }

        public void Receive(TextCopiedMessage message)
        {
            DebugLogger.Log($"[3-MINING-SVC] Received in Messenger. Inserting into Channel | Text: {message.Text}");
            bool inserted = _textChannel.Writer.TryWrite(message);
            if (!inserted) DebugLogger.Log($"[ERROR-MINING-SVC] Failed to write to Channel!");
        }

        private void IdleTimer_Tick(object? sender, ElapsedEventArgs e)
        {
            _idleTimer.Stop();

            if (SealAllOpenSlots(DateTime.Now) > 0)
            {
                var slots = HistorySlots;
                if (slots.Count > 0)
                {
                    SealSlotAudio(slots[0], DateTime.Now);
                    SendStatus("Slot sealed due to inactivity.");
                }
            }
        }

        public void SealSlotAudio(MiningSlot slot, DateTime endTime)
        {
            if (!slot.IsOpen) return;

            var sessionConfig = _configService.CurrentConfig.Session;
            double paddingSeconds = sessionConfig.AudioPaddingSeconds;
            DateTime startT = slot.Timestamp.AddSeconds(-paddingSeconds);

            double startAgo = (DateTime.Now - startT).TotalSeconds;
            double endAgo = (DateTime.Now - endTime).TotalSeconds;

            if (endAgo < 0) endAgo = 0;
            if (startAgo <= endAgo) startAgo = endAgo + sessionConfig.AudioFallbackSeconds;

            byte[] wavBytes = _mediaService.GetAudioSegment(startAgo, endAgo);
            int bitrate = _configService.CurrentConfig.Media.AudioBitrate;
            
            if (bitrate > 0)
            {
                slot.AudioBytes = _mediaService.ConvertWavToMp3(wavBytes, bitrate);
            }
            else
            {
                // Fallback if somehow bitrate is 0, use 128 kbps
                slot.AudioBytes = _mediaService.ConvertWavToMp3(wavBytes, 128);
            }
        }

        private int SealAllOpenSlots(DateTime endTime)
        {
            var openSlots = HistorySlots.Where(s => s.IsOpen).ToList();
            foreach (var slot in openSlots)
            {
                SealSlotAudio(slot, endTime);
            }
            return openSlots.Count;
        }

        public void DeleteSlot(MiningSlot slot)
        {
            bool removed = false;
            lock (_slotsLock)
            {
                if (_historySlots.Contains(slot))
                {
                    _historySlots.Remove(slot);
                    removed = true;
                }
            }
            
            if (removed)
            {
                int spokenCharCount = CountJapaneseCharacters(slot.Text);
                Tracker.RemoveCharacters(spokenCharCount);
                slot.Dispose();
                WeakReferenceMessenger.Default.Send(new SlotRemovedMessage(slot));
            }
        }

        private int CountJapaneseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return Regex.Matches(text, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]").Count;
        }

        public void Receive(AudioErrorMessage message)
        {
            StopBuffer();
            SendStatus($"⚠️ ERROR: {message.Value}");
            WeakReferenceMessenger.Default.Send(new BufferStoppedMessage());
        }

        private async Task ProcessTextChannelAsync()
        {
            await foreach (var message in _textChannel.Reader.ReadAllAsync(_cts.Token))
            {
                var config = _configService.CurrentConfig;
                string targetWin = config.Media.VideoWindow;
                int maxWidth = config.Media.MaxImageWidth;

                Task<byte[]> screenshotTask = Task.Run(() =>
                {
                    return string.IsNullOrEmpty(targetWin) ? null : _mediaService.CaptureScreenshot(targetWin, maxWidth);
                });

                DebugLogger.Log($"[4-CHANNEL-READER] Pulled from channel by background thread | Text: {message.Text}");
                
                // Processa livremente em background sem amarras de UI
                _ = Task.Run(async () =>
                {
                    try
                    {
                        DebugLogger.Log($"[5-BACKGROUND] Starting Mining Slot creation | Text: {message.Text}");
                        _idleTimer.Stop();
                        SealAllOpenSlots(DateTime.Now);

                        byte[] screenshot = await screenshotTask;

                        string safeText = message.Text.Length > 1000 ? message.Text.Substring(0, 1000) + " [...]" : message.Text;

                        var newSlot = new MiningSlot
                        {
                            Text = safeText,
                            Timestamp = message.Timestamp,
                            ScreenshotBytes = screenshot
                        };

                        var bridge = App.Current.Services.GetService<IBridgeService>();
                        if (bridge != null) bridge.ActiveHoverSlotId = string.Empty;

                        int spokenCharCount = CountJapaneseCharacters(safeText);
                        Tracker.AddCharacters(spokenCharCount);

                        lock (_slotsLock)
                        {
                            _historySlots.Insert(0, newSlot);

                            int maxSlots = 30;
                            if (int.TryParse(config.Session.MaxSlots, out int parsedMax)) maxSlots = parsedMax;

                            if (_historySlots.Count > maxSlots)
                            {
                                var oldSlot = _historySlots[_historySlots.Count - 1];
                                oldSlot.Dispose();
                                _historySlots.RemoveAt(_historySlots.Count - 1);
                            }
                        }

                        double finalSeconds;
                        var sessionConfig = config.Session;

                        if (sessionConfig.UseDynamicTimeout)
                        {
                            char[] pauseChars = new[] { '。', '、', '？', '！', '…', '　' };
                            int pauseCount = safeText.Count(c => pauseChars.Contains(c));
                            finalSeconds = Math.Max(sessionConfig.DynamicMinSeconds, sessionConfig.DynamicBaseSeconds + (spokenCharCount * sessionConfig.DynamicPerCharSeconds) + (pauseCount * sessionConfig.DynamicPerPauseSeconds));
                        }
                        else
                        {
                            if (!double.TryParse(sessionConfig.IdleTime, out finalSeconds)) finalSeconds = 30;
                        }

                        _idleTimer.Interval = finalSeconds * 1000;
                        _idleTimer.Start();

                        DebugLogger.Log($"[6-BACKGROUND] Slot created in list. Dispatching SlotCapturedMessage to System.");
                        WeakReferenceMessenger.Default.Send(new SlotCapturedMessage(newSlot));

                        newSlot.ScreenshotBytes = await screenshotTask;
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[ERROR-BACKGROUND] Exception while processing slot: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[Background Process Error] {ex.Message}");
                    }
                });
            }
        }
    }
}