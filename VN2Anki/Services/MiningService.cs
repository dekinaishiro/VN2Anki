using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using VN2Anki.Messages;
using VN2Anki.Models;
using VN2Anki.Services.Interfaces;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;
using VN2Anki.Locales;

namespace VN2Anki.Services
{
    public class MiningService : IRecipient<TextCopiedMessage>, IRecipient<AudioErrorMessage>
    {
        public ITextHook TextHook { get; }
        public SessionTracker Tracker { get; }
        public AudioEngine Audio { get; }

        private readonly MediaService _mediaService;
        private readonly DispatcherTimer _idleTimer;
        private readonly DispatcherTimer _videoCheckTimer;
        // injetar dispatcher service para evitar dependência direta do WPF
        private readonly IDispatcherService _dispatcherService;
        private readonly IConfigurationService _configService;

        public ObservableCollection<MiningSlot> HistorySlots { get; }

        public string TargetVideoWindow { get; set; }
        public int MaxSlots { get; set; } = 25;
        public double IdleTimeoutFixo { get; set; } = 30.0;
        public bool UseDynamicTimeout { get; set; } = true;
        public int MaxImageWidth { get; set; } = 1280;

        private readonly Channel<TextCopiedMessage> _textChannel;
        private readonly CancellationTokenSource _cts = new();

        public MiningService(ITextHook textHook, SessionTracker tracker, AudioEngine audio, MediaService mediaService, IDispatcherService dispatcherService, IConfigurationService configService)
        {
            TextHook = textHook;
            Tracker = tracker;
            Audio = audio;
            _mediaService = mediaService;
            _dispatcherService = dispatcherService;
            _configService = configService;

            HistorySlots = new ObservableCollection<MiningSlot>();

            _idleTimer = new DispatcherTimer();
            _idleTimer.Tick += IdleTimer_Tick;

            _videoCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _videoCheckTimer.Tick += VideoCheckTimer_Tick;

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
            _videoCheckTimer.Start();
            SendStatus("Buffer running...");
        }

        public void StopBuffer()
        {
            SealAllOpenSlots(DateTime.Now);
            Audio.Stop();
            TextHook.Stop();
            _idleTimer.Stop();
            Tracker.Pause();
            _videoCheckTimer.Start();
            SendStatus("Buffer stopped.");
        }

        public void Receive(TextCopiedMessage message)
        {
            DebugLogger.Log($"[3-MINING-SVC] Received in Messenger. Inserting into Channel | Text: {message.Text}");
            bool inserted = _textChannel.Writer.TryWrite(message);
            if (!inserted) DebugLogger.Log($"[ERROR-MINING-SVC] Failed to write to Channel!");
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            _idleTimer.Stop();

            if (SealAllOpenSlots(DateTime.Now) > 0)
            {
                SealSlotAudio(HistorySlots[0], DateTime.Now);
                SendStatus("Slot sealed due to inactivity.");
            }
        }
        private void VideoCheckTimer_Tick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(TargetVideoWindow)) return;

            var procs = Process.GetProcessesByName(TargetVideoWindow);
            bool isRunning = false;

            foreach (var p in procs)
            {
                if (p.MainWindowHandle != IntPtr.Zero) isRunning = true;
                p.Dispose();
            }

            if (!isRunning)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    StopBuffer();
                    SendStatus(Locales.Strings.StatusVideoDisconnected);
                    WeakReferenceMessenger.Default.Send(new BufferStoppedMessage());
                });
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
            if (HistorySlots.Contains(slot))
            {
                int spokenCharCount = CountJapaneseCharacters(slot.Text);
                Tracker.RemoveCharacters(spokenCharCount);
                slot.Dispose();
                HistorySlots.Remove(slot);
            }
        }

        private int CountJapaneseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return Regex.Matches(text, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]").Count;
        }

        public void Receive(AudioErrorMessage message)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StopBuffer();
                SendStatus($"⚠️ ERROR: {message.Value}");
                WeakReferenceMessenger.Default.Send(new BufferStoppedMessage());
            });
        }

        private async Task ProcessTextChannelAsync()
        {
            await foreach (var message in _textChannel.Reader.ReadAllAsync(_cts.Token))
            {
                // 1. FASE 3 (Fire-and-forget): Dispara a foto imediatamente no ThreadPool
                string targetWin = TargetVideoWindow;
                int maxWidth = MaxImageWidth;

                Task<byte[]> screenshotTask = Task.Run(() =>
                {
                    return string.IsNullOrEmpty(targetWin) ? null : _mediaService.CaptureScreenshot(targetWin, maxWidth);
                });

                DebugLogger.Log($"[4-CHANNEL-READER] Pulled from channel by background thread | Text: {message.Text}");
                // 2. Não travamos o consumidor! Jogamos para a UI e o loop volta a ler a rede no mesmo milissegundo.
                _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        DebugLogger.Log($"[5-UI-THREAD] Starting Mining Slot creation | Text: {message.Text}");
                        _idleTimer.Stop();
                        SealAllOpenSlots(DateTime.Now);

                        string safeText = message.Text.Length > 1000 ? message.Text.Substring(0, 1000) + " [...]" : message.Text;

                        var newSlot = new MiningSlot
                        {
                            Text = safeText,
                            Timestamp = message.Timestamp
                        };

                        HistorySlots.Insert(0, newSlot);

                        int spokenCharCount = CountJapaneseCharacters(safeText);
                        Tracker.AddCharacters(spokenCharCount);

                        while (HistorySlots.Count > MaxSlots)
                        {
                            var oldSlot = HistorySlots[HistorySlots.Count - 1];
                            oldSlot.Dispose();
                            HistorySlots.RemoveAt(HistorySlots.Count - 1);
                        }

                        double finalSeconds;
                        var sessionConfig = _configService.CurrentConfig.Session;

                        if (UseDynamicTimeout)
                        {
                            char[] pauseChars = new[] { '。', '、', '？', '！', '…', '　' };
                            int pauseCount = safeText.Count(c => pauseChars.Contains(c));
                            finalSeconds = Math.Max(sessionConfig.DynamicMinSeconds, sessionConfig.DynamicBaseSeconds + (spokenCharCount * sessionConfig.DynamicPerCharSeconds) + (pauseCount * sessionConfig.DynamicPerPauseSeconds));
                        }
                        else
                        {
                            finalSeconds = IdleTimeoutFixo;
                        }

                        _idleTimer.Interval = TimeSpan.FromSeconds(finalSeconds);
                        _idleTimer.Start();

                        // Envia a frase para a OverlayWindow AGORA
                        DebugLogger.Log($"[6-UI-THREAD] Slot created in list. Dispatching SlotCapturedMessage to Overlay.");
                        WeakReferenceMessenger.Default.Send(new SlotCapturedMessage(newSlot));

                        // 3. Aguarda silenciosamente a foto terminar e anexa ao slot (Trigga a UI via OnPropertyChanged)
                        newSlot.ScreenshotBytes = await screenshotTask;
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[ERROR-UI-THREAD] Exception while processing slot: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"[UI Process Error] {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }
    }
}