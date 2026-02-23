using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace VN2Anki.Services
{
    public class AudioDeviceItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DataFlow Flow { get; set; }
        public string DisplayName => Flow == DataFlow.Render ? $"🔊 {Name}" : $"🎤 {Name} (Input)";
    }

    public class AudioEngine
    {
        private WasapiCapture _captureDevice;
        private byte[] _circularBuffer;
        private int _writePosition = 0;
        private int _bufferLength;
        private WaveFormat _waveFormat;
        private readonly object _bufferLock = new object();
        private bool _isRecording = false;

        // event to notify errors from silent audio dc
        public event Action<string> OnRecordingError;
        private bool _isManualStop = false; // <-- Nova flag

        public int DurationSeconds { get; }

        public AudioEngine(int durationSeconds = 120)
        {
            DurationSeconds = durationSeconds;
        }

        public List<AudioDeviceItem> GetDevices()
        {
            var devices = new List<AudioDeviceItem>();

            using (var enumerator = new MMDeviceEnumerator())
            {
                // 1º: Outputs (Render) - Fones, Caixas de som e Cabos Virtuais (Prioridade máxima)
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    devices.Add(new AudioDeviceItem { Id = endpoint.ID, Name = endpoint.FriendlyName, Flow = endpoint.DataFlow });

                // 2º: Inputs (Capture) - Microfones (Vão para o fim da lista)
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    devices.Add(new AudioDeviceItem { Id = endpoint.ID, Name = endpoint.FriendlyName, Flow = endpoint.DataFlow });
            }

            return devices;
        }

        public void Start(string deviceId)
        {
            if (_isRecording) Stop();

            _isManualStop = false;

            try
            {
                using (var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator())
                {
                    var device = enumerator.GetDevice(deviceId);

                    if (device.DataFlow == NAudio.CoreAudioApi.DataFlow.Render)
                        _captureDevice = new NAudio.Wave.WasapiLoopbackCapture(device);
                    else
                        _captureDevice = new NAudio.CoreAudioApi.WasapiCapture(device);
                }

                _waveFormat = _captureDevice.WaveFormat;
                int newBufferLength = _waveFormat.AverageBytesPerSecond * DurationSeconds;

                if (_circularBuffer == null || _circularBuffer.Length != newBufferLength)
                {
                    _circularBuffer = new byte[newBufferLength];
                }

                _bufferLength = newBufferLength;
                _writePosition = 0;

                _captureDevice.DataAvailable += OnDataAvailable;

                _captureDevice.RecordingStopped += (s, e) =>
                {
                    _isRecording = false;

                    if (!_isManualStop)
                    {
                        string errorMsg = e.Exception != null ? e.Exception.Message : "Disconnected Device.";
                        OnRecordingError?.Invoke(errorMsg);
                    }
                };

                _captureDevice.StartRecording();
                _isRecording = true;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[AudioEngine] Error: {ex.Message}"); }
        }

        public void Stop()
        {
            _isManualStop = true; 

            if (_captureDevice != null)
            {
                _captureDevice.StopRecording();
                _captureDevice.DataAvailable -= OnDataAvailable;
                _captureDevice.Dispose();
                _captureDevice = null;
            }
            _isRecording = false;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0) return;
            lock (_bufferLock)
            {
                int bytesToCopy = e.BytesRecorded;
                int sourceOffset = 0;
                while (bytesToCopy > 0)
                {
                    int spaceLeftAtEnd = _bufferLength - _writePosition;
                    int chunk = Math.Min(bytesToCopy, spaceLeftAtEnd);
                    Array.Copy(e.Buffer, sourceOffset, _circularBuffer, _writePosition, chunk);
                    _writePosition += chunk;
                    if (_writePosition >= _bufferLength) _writePosition = 0;
                    sourceOffset += chunk;
                    bytesToCopy -= chunk;
                }
            }
        }

        public byte[] ExportSegment(double startSecondsAgo, double endSecondsAgo = 0)
        {
            if (!_isRecording || _circularBuffer == null) return null;
            lock (_bufferLock)
            {
                int bytesPerSec = _waveFormat.AverageBytesPerSecond;
                int startOffsetBytes = (int)(startSecondsAgo * bytesPerSec);
                int endOffsetBytes = (int)(endSecondsAgo * bytesPerSec);

                startOffsetBytes -= startOffsetBytes % _waveFormat.BlockAlign;
                endOffsetBytes -= endOffsetBytes % _waveFormat.BlockAlign;

                int lengthBytes = startOffsetBytes - endOffsetBytes;
                if (lengthBytes <= 0 || lengthBytes > _bufferLength) return null;

                // calculates index positions in the circular buffer, accounting for wrap-around
                int endIdx = (_writePosition - endOffsetBytes + _bufferLength) % _bufferLength;
                int startIdx = (_writePosition - startOffsetBytes + _bufferLength) % _bufferLength;

                using (var ms = new MemoryStream())
                {
                    using (var writer = new WaveFileWriter(ms, _waveFormat))
                    {
                        // direct write from circular buffer to WAV stream, handling the case where the audio data wraps around the end of the buffer
                        if (startIdx < endIdx)
                        {
                            writer.Write(_circularBuffer, startIdx, lengthBytes);
                        }
                        else
                        {
                            int firstPartLength = _bufferLength - startIdx;
                            writer.Write(_circularBuffer, startIdx, firstPartLength);
                            writer.Write(_circularBuffer, 0, endIdx);
                        }
                    }
                    return ms.ToArray();
                }
            }
        }
    }
}