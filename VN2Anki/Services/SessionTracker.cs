using System;
using System.Diagnostics;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using Timer = System.Timers.Timer;

namespace VN2Anki.Services
{
    public partial class SessionTracker : ObservableObject
    {
        private readonly Timer _timer;
        private readonly Stopwatch _stopwatch;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SpeedCharsPerMinute))]
        [NotifyPropertyChangedFor(nameof(SpeedCharsPerHour))]
        private int _validCharacterCount;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SpeedCharsPerMinute))]
        [NotifyPropertyChangedFor(nameof(SpeedCharsPerHour))]
        private TimeSpan _elapsed;

        [ObservableProperty]
        private bool _isTracking;

        public SessionTracker()
        {
            _stopwatch = new Stopwatch();

           _timer = new Timer(1000);
            _timer.Elapsed += (s, e) => Elapsed = _stopwatch.Elapsed;
        }

        public double SpeedCharsPerMinute =>
            Elapsed.TotalMinutes > 0 ? Math.Round(ValidCharacterCount / Elapsed.TotalMinutes, 1) : 0;

        public double SpeedCharsPerHour =>
            Elapsed.TotalHours > 0 ? Math.Round(ValidCharacterCount / Elapsed.TotalHours, 0) : 0;

        public void Start()
        {
            _stopwatch.Start();
            _timer.Start();
            IsTracking = true;
        }

        public void Pause()
        {
            _stopwatch.Stop();
            _timer.Stop();
            IsTracking = false;
        }

        public void Reset()
        {
            _stopwatch.Reset();
            _timer.Stop();
            Elapsed = TimeSpan.Zero;
            ValidCharacterCount = 0;
            IsTracking = false;
        }

        public void AddCharacters(int count) => ValidCharacterCount += count;

        public void RemoveCharacters(int count) => ValidCharacterCount = Math.Max(0, ValidCharacterCount - count);
    }
}