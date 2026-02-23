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

        public int DurationSeconds { get; }

        public AudioEngine(int durationSeconds = 120)
        {
            DurationSeconds = durationSeconds;
        }

        public List<AudioDeviceItem> GetDevices()
        {
            var devices = new List<AudioDeviceItem>();

            // 'using' keyword here prevents leaking audio device Handles on each call to GetDevices
            using (var enumerator = new MMDeviceEnumerator())
            {
                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    devices.Add(new AudioDeviceItem { Id = endpoint.ID, Name = endpoint.FriendlyName, Flow = endpoint.DataFlow });

                foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    devices.Add(new AudioDeviceItem { Id = endpoint.ID, Name = endpoint.FriendlyName, Flow = endpoint.DataFlow });
            }

            return devices;
        }

        public void Start(string deviceId)
        {
            if (_isRecording) Stop();
            try
            {
                //
                // ('using' prevents leaking audio Handles on each Start click)
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var device = enumerator.GetDevice(deviceId);

                    if (device.DataFlow == DataFlow.Render)
                        _captureDevice = new WasapiLoopbackCapture(device);
                    else
                        _captureDevice = new WasapiCapture(device);
                }

                _waveFormat = _captureDevice.WaveFormat;
                int newBufferLength = _waveFormat.AverageBytesPerSecond * DurationSeconds;

                // (only creates the 21MB array if it doesn't exist or if the headphone changes)
                if (_circularBuffer == null || _circularBuffer.Length != newBufferLength)
                {
                    _circularBuffer = new byte[newBufferLength];
                }

                _bufferLength = newBufferLength;
                _writePosition = 0;

                _captureDevice.DataAvailable += OnDataAvailable;
                _captureDevice.RecordingStopped += (s, e) => _isRecording = false;

                _captureDevice.StartRecording();
                _isRecording = true;
            }
            catch (Exception ex) { Debug.WriteLine($"[AudioEngine] Erro: {ex.Message}"); }
        }

        public void Stop()
        {
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