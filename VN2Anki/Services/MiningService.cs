using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VN2Anki.Models;
using VN2Anki.Services;
using System.Text.RegularExpressions;
using System.Windows.Media.Animation;

namespace VN2Anki.Services
{
    public class MiningService
    {
        public AudioEngine Audio { get; }
        public VideoEngine Video { get; }
        public ClipboardMonitor Clipboard { get; }
        public AnkiHandler Anki { get; }
        public SessionTracker Tracker { get; }

        public ObservableCollection<MiningSlot> HistorySlots { get; }

        private readonly DispatcherTimer _idleTimer;

        // events for UI updates (status text, slot captured, etc)
        public event Action<string> OnStatusChanged;
        public event Action<MiningSlot> OnSlotCaptured;

        public event Action OnBufferStoppedUnexpectedly;

        // 
        public string TargetVideoWindow { get; set; }
        public int MaxSlots { get; set; } = 25;
        public double IdleTimeoutFixo { get; set; } = 30.0;
        public bool UseDynamicTimeout { get; set; } = true;
        public int MaxImageWidth { get; set; } = 1280;

        public MiningService(
            AudioEngine audio, VideoEngine video, ClipboardMonitor clipboard, AnkiHandler anki, SessionTracker tracker)
        {
            Audio = audio;
            Video = video;
            Clipboard = clipboard;
            Anki = anki;
            Tracker = tracker;

            HistorySlots = new ObservableCollection<MiningSlot>();

            _idleTimer = new DispatcherTimer();
            _idleTimer.Tick += IdleTimer_Tick;

            Clipboard.OnTextCopied += ProcessCaptureSequence;
            Audio.OnRecordingError += HandleAudioError;
        }

        public void StartBuffer(string audioDeviceId)
        {
            Audio.Start(audioDeviceId);
            Clipboard.Start();
            Tracker.Start();
            OnStatusChanged?.Invoke("Buffer Rodando...");
        }

        public void StopBuffer()
        {
            SealAllOpenSlots(DateTime.Now);

            Audio.Stop();
            Clipboard.Stop();
            _idleTimer.Stop();
            Tracker.Pause();
            OnStatusChanged?.Invoke("Buffer Parado.");
        }

        private void ProcessCaptureSequence(string text, DateTime timestamp)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _idleTimer.Stop();

                SealAllOpenSlots(DateTime.Now);

                byte[] imgBytes = string.IsNullOrEmpty(TargetVideoWindow) ? null : Video.CaptureWindow(TargetVideoWindow, MaxImageWidth);
                string safeText = text.Length > 1000 ? text.Substring(0, 1000) + " [...]" : text;

                var newSlot = new MiningSlot
                {
                    Text = safeText,
                    Timestamp = timestamp,
                    ScreenshotBytes = imgBytes
                };

                HistorySlots.Insert(0, newSlot);

                
                char[] pauseChars = new[] { '。', '、', '？', '！', '…', '　' };
                int spokenCharCount = CountJapaneseCharacters(safeText);
                Tracker.AddCharacters(spokenCharCount);

                // memory management: remove old slots if we exceed max
                while (HistorySlots.Count > MaxSlots)
                {
                    var oldSlot = HistorySlots[HistorySlots.Count - 1];
                    oldSlot.Dispose(); // Libera os bytes
                    HistorySlots.RemoveAt(HistorySlots.Count - 1);
                }

                // DYNAMIC TIMEOUT CALCULATION
                double finalSeconds;

                // make it user configurable in the future after testing
                // algo might require adjustments too
                double baseSeconds = 0.75;
                double perCharSeconds = 0.25;
                double perPauseSeconds = 0.50;
                double minSeconds = 2.0;

                // count commas and periods
                // Calculate dynamic timeout based on spoken characters and pauses, with a minimum threshold
                if (UseDynamicTimeout)
                {
                    int pauseCount = safeText.Count(c => pauseChars.Contains(c));                                     
                    finalSeconds = Math.Max(minSeconds, baseSeconds + (spokenCharCount * perCharSeconds) + (pauseCount * perPauseSeconds));
                }
                else
                {
                    // fixed timeout if enabled 
                    finalSeconds = IdleTimeoutFixo;
                }

                _idleTimer.Interval = TimeSpan.FromSeconds(finalSeconds);
                _idleTimer.Start();

                string modeText = UseDynamicTimeout ? "Dinâmico" : "Fixo";
                OnStatusChanged?.Invoke($"Slot capturado! Fechando em {finalSeconds:F1}s ({modeText})");
                OnSlotCaptured?.Invoke(newSlot);
            });
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            _idleTimer.Stop();

            if (SealAllOpenSlots(DateTime.Now) > 0)
            {
                SealSlotAudio(HistorySlots[0], DateTime.Now);
                OnStatusChanged?.Invoke("Slot selado por inatividade.");
            }
        }

        public void SealSlotAudio(MiningSlot slot, DateTime endTime)
        {
            if (!slot.IsOpen) return;

            double paddingSeconds = 0.250;
            DateTime startT = slot.Timestamp.AddSeconds(-paddingSeconds);

            double startAgo = (DateTime.Now - startT).TotalSeconds;
            double endAgo = (DateTime.Now - endTime).TotalSeconds;

            if (endAgo < 0) endAgo = 0;
            if (startAgo <= endAgo) startAgo = endAgo + 5.0;

            // temp: fix persisten audio slots being open
            // slot.AudioBytes = Audio.ExportSegment(startAgo, endAgo);
            slot.AudioBytes = Audio.ExportSegment(startAgo, endAgo) ?? Array.Empty<byte>();
        }

        private void HandleAudioError(string msg)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StopBuffer();
                OnStatusChanged?.Invoke($"⚠️ ERRO: {msg}");
                OnBufferStoppedUnexpectedly?.Invoke(); // notifies ui
            });
        }

        public async Task<(bool success, string message)> ProcessMiningToAnki(MiningSlot slot, string deck, string model, string audioField, string imageField)
        {
            if (string.IsNullOrEmpty(deck)) return (false, "Selecione um Deck!");

            OnStatusChanged?.Invoke("Preparando mídia...");

            if (slot.IsOpen) SealSlotAudio(slot, DateTime.Now);

            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string audioFilename = $"miner_{uniqueId}.wav";
            string imageFilename = $"miner_{uniqueId}.jpg";

            bool hasAudio = slot.AudioBytes != null && slot.AudioBytes.Length > 0;
            bool hasImage = slot.ScreenshotBytes != null && slot.ScreenshotBytes.Length > 0;

            if (hasAudio) await Anki.StoreMediaAsync(audioFilename, slot.AudioBytes);
            if (hasImage) await Anki.StoreMediaAsync(imageFilename, slot.ScreenshotBytes);

            var result = await Anki.UpdateLastCardAsync(deck, audioField, imageField, hasAudio ? audioFilename : null, hasImage ? imageFilename : null);

            // Force GC collecting after adding stuff to anki
            _ = Task.Run(async () =>
            {
                // prevents UI freezes by waiting Anki string64
                await Task.Delay(1000);

                // makes LOH compact large obj
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

                // forces collect; blocking = false tries to run in bg; compacting = true
                GC.Collect(2, GCCollectionMode.Forced, false, true);
            });

            return result;
        }

        private int SealAllOpenSlots(DateTime endTime)
        {
            // Pega todos os abertos (o ToList() evita erros de modificação de coleção durante o loop)
            var openSlots = HistorySlots.Where(s => s.IsOpen).ToList();

            foreach (var slot in openSlots)
            {
                SealSlotAudio(slot, endTime);
            }

            return openSlots.Count; // Retorna quantos foram selados
        }

        public void DeleteSlot(MiningSlot slot)
        {
            if (HistorySlots.Contains(slot))
            {
                int spokenCharCount = CountJapaneseCharacters(slot.Text);
                Tracker.RemoveCharacters(spokenCharCount);

                // release bytes
                slot.Dispose();
                HistorySlots.Remove(slot);
            }
        }

        private int CountJapaneseCharacters(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            return Regex.Matches(text, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]").Count;
        }
    }
}