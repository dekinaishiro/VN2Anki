using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VN2Anki.Models;
using System.IO;
using NAudio.Wave;

namespace VN2Anki
{
    public partial class MiningWindow : Window
    {
        private static MiningWindow _instance;
        private static Action<MiningSlot> _onMineAction;
        private static Action<MiningSlot> _onDeleteAction;

        private WaveOutEvent _waveOut;
        private WaveFileReader _waveReader;
        private MemoryStream _audioStream;

        public static void ShowWindow(ObservableCollection<MiningSlot> history, Action<MiningSlot> onMineAction, Action<MiningSlot> onDeleteAction)
        {
            _onMineAction = onMineAction;
            _onDeleteAction = onDeleteAction;

            if (_instance == null)
            {
                _instance = new MiningWindow(history);
                _instance.Show();
            }
            else
            {
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Activate();
            }
        }

        private MiningWindow(ObservableCollection<MiningSlot> history)
        {
            InitializeComponent();
            ListHistory.ItemsSource = history;
        }

        private void BtnMiner_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is MiningSlot slot) _onMineAction?.Invoke(slot);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is MiningSlot slot) _onDeleteAction?.Invoke(slot);
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAudio();

            _instance = null;
            base.OnClosed(e);
        }

        private void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is MiningSlot slot)
            {
                PlayAudio(slot.AudioBytes);
            }
        }

        private void PlayAudio(byte[] audioBytes)
        {
            // Para qualquer áudio que já esteja tocando
            StopAudio();

            if (audioBytes == null || audioBytes.Length == 0) return;

            try
            {
                _audioStream = new MemoryStream(audioBytes);
                _waveReader = new WaveFileReader(_audioStream);
                _waveOut = new WaveOutEvent();

                _waveOut.Init(_waveReader);

                // Garante a liberação de recursos assim que o áudio terminar naturalmente
                _waveOut.PlaybackStopped += (s, e) => StopAudio();

                _waveOut.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao reproduzir áudio: {ex.Message}");
                StopAudio();
            }
        }

        private void StopAudio()
        {
            // A ordem importa: primeiro para a reprodução, depois libera os leitores
            if (_waveOut != null)
            {
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }
            if (_waveReader != null)
            {
                _waveReader.Dispose();
                _waveReader = null;
            }
            if (_audioStream != null)
            {
                _audioStream.Dispose();
                _audioStream = null;
            }
        }


    }
}