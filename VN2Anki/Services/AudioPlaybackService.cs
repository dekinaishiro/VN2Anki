using NAudio.Wave;
using System;
using System.IO;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class AudioPlaybackService : IAudioPlaybackService, IDisposable
    {
        private WaveOutEvent _waveOut;
        private Mp3FileReader _waveReader;
        private MemoryStream _audioStream;

        public void PlayAudio(byte[] audioBytes)
        {
            if (audioBytes == null || audioBytes.Length == 0) return;

            StopAudio();

            try
            {
                _audioStream = new MemoryStream(audioBytes);
                _waveReader = new Mp3FileReader(_audioStream);
                _waveOut = new WaveOutEvent();

                _waveOut.Init(_waveReader);

                _waveOut.PlaybackStopped += (s, e) => StopAudio();

                _waveOut.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Playback Error: {ex.Message}");
            }
        }

        public void StopAudio()
        {
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

        public void Dispose()
        {
            StopAudio();
        }
    }
}