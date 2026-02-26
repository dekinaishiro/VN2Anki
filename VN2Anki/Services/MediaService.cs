using System;
using System.IO;

namespace VN2Anki.Services
{
    public class MediaService
    {
        private readonly AudioEngine _audio;
        private readonly VideoEngine _video;

        public MediaService(AudioEngine audio, VideoEngine video)
        {
            _audio = audio;
            _video = video;
        }

        public byte[] CaptureScreenshot(string processName, int maxWidth)
        {
            return string.IsNullOrEmpty(processName) ? null : _video.CaptureWindow(processName, maxWidth);
        }

        public byte[] GetAudioSegment(double startSecondsAgo, double endSecondsAgo)
        {
            return _audio.ExportSegment(startSecondsAgo, endSecondsAgo) ?? Array.Empty<byte>();
        }

        public byte[] ConvertWavToMp3(byte[] wavBytes, int bitrate)
        {
            try
            {
                using var retMs = new MemoryStream();
                using var wavMs = new MemoryStream(wavBytes);
                using var rdr = new NAudio.Wave.WaveFileReader(wavMs);
                using var wtr = new NAudio.Lame.LameMP3FileWriter(retMs, rdr.WaveFormat, bitrate);

                rdr.CopyTo(wtr);
                wtr.Flush();
                return retMs.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MP3 Encode Error] {ex.Message}");
                return wavBytes;
            }
        }
    }
}