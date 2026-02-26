using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models;

namespace VN2Anki.Services
{
    public class MiningService
    {
        public ITextHook TextHook { get; }
        public SessionTracker Tracker { get; }
        public AudioEngine Audio { get; }

        private readonly MediaService _mediaService;
        private readonly DispatcherTimer _idleTimer;

        public ObservableCollection<MiningSlot> HistorySlots { get; }
        public event Action<MiningSlot> OnSlotCaptured;
        public event Action OnBufferStoppedUnexpectedly;

        public string TargetVideoWindow { get; set; }
        public int MaxSlots { get; set; } = 25;
        public double IdleTimeoutFixo { get; set; } = 30.0;
        public bool UseDynamicTimeout { get; set; } = true;
        public int MaxImageWidth { get; set; } = 1280;

        public MiningService(ITextHook textHook, SessionTracker tracker, AudioEngine audio, MediaService mediaService)
        {
            TextHook = textHook;
            Tracker = tracker;
            Audio = audio;
            _mediaService = mediaService;

            HistorySlots = new ObservableCollection<MiningSlot>();

            _idleTimer = new DispatcherTimer();
            _idleTimer.Tick += IdleTimer_Tick;

            TextHook.OnTextCopied += ProcessCaptureSequence;
            Audio.OnRecordingError += HandleAudioError;
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

        private void ProcessCaptureSequence(string text, DateTime timestamp)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _idleTimer.Stop();
                SealAllOpenSlots(DateTime.Now);

                byte[] imgBytes = _mediaService.CaptureScreenshot(TargetVideoWindow, MaxImageWidth);
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

                while (HistorySlots.Count > MaxSlots)
                {
                    var oldSlot = HistorySlots[HistorySlots.Count - 1];
                    oldSlot.Dispose();
                    HistorySlots.RemoveAt(HistorySlots.Count - 1);
                }

                double finalSeconds;
                double baseSeconds = 0.75;
                double perCharSeconds = 0.25;
                double perPauseSeconds = 0.50;
                double minSeconds = 2.0;

                if (UseDynamicTimeout)
                {
                    int pauseCount = safeText.Count(c => pauseChars.Contains(c));
                    finalSeconds = Math.Max(minSeconds, baseSeconds + (spokenCharCount * perCharSeconds) + (pauseCount * perPauseSeconds));
                }
                else
                {
                    finalSeconds = IdleTimeoutFixo;
                }

                _idleTimer.Interval = TimeSpan.FromSeconds(finalSeconds);
                _idleTimer.Start();

                string modeText = UseDynamicTimeout ? "Dynamic" : "Fixed";
                SendStatus($"Slot captured! Sealing in {finalSeconds:F1}s ({modeText})");
                OnSlotCaptured?.Invoke(newSlot);
            });
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

        public void SealSlotAudio(MiningSlot slot, DateTime endTime)
        {
            if (!slot.IsOpen) return;

            double paddingSeconds = 0.250;
            DateTime startT = slot.Timestamp.AddSeconds(-paddingSeconds);

            double startAgo = (DateTime.Now - startT).TotalSeconds;
            double endAgo = (DateTime.Now - endTime).TotalSeconds;

            if (endAgo < 0) endAgo = 0;
            if (startAgo <= endAgo) startAgo = endAgo + 5.0;

            slot.AudioBytes = _mediaService.GetAudioSegment(startAgo, endAgo);
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

        private void HandleAudioError(string msg)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                StopBuffer();
                SendStatus($"⚠️ ERROR: {msg}");
                OnBufferStoppedUnexpectedly?.Invoke();
            });
        }
    }
}