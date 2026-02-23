using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace VN2Anki.Services
{
    public class SessionTracker : INotifyPropertyChanged
    {
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch;

        private int _validCharacterCount;
        private TimeSpan _elapsed;

        public SessionTracker()
        {
            _stopwatch = new Stopwatch();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => Elapsed = _stopwatch.Elapsed;
        }

        public TimeSpan Elapsed
        {
            get => _elapsed;
            private set
            {
                _elapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedCharsPerHour));
            }
        }

        public int ValidCharacterCount
        {
            get => _validCharacterCount;
            private set
            {
                _validCharacterCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpeedCharsPerHour));
            }
        }

        public double SpeedCharsPerMinute =>
            Elapsed.TotalMinutes > 0 ? Math.Round(ValidCharacterCount / Elapsed.TotalMinutes, 1) : 0;

        public double SpeedCharsPerHour =>
            Elapsed.TotalHours > 0 ? Math.Round(ValidCharacterCount / Elapsed.TotalHours, 0) : 0;

        public bool IsTracking => _stopwatch.IsRunning;

        public void Start()
        {
            _stopwatch.Start();
            _timer.Start();
            OnPropertyChanged(nameof(IsTracking));
        }

        public void Pause()
        {
            _stopwatch.Stop();
            _timer.Stop();
            OnPropertyChanged(nameof(IsTracking));
        }

        public void Reset()
        {
            _stopwatch.Reset();
            _timer.Stop();
            Elapsed = TimeSpan.Zero;
            ValidCharacterCount = 0;
            OnPropertyChanged(nameof(IsTracking));
        }

        public void AddCharacters(int count) => ValidCharacterCount += count;

        public void RemoveCharacters(int count) => ValidCharacterCount = Math.Max(0, ValidCharacterCount - count);

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}