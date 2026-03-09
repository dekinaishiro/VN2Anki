using NAudio.Wave;
using System;
using System.IO;

namespace VN2Anki.Services.Interfaces
{
    public interface IAudioPlaybackService
    {
        void PlayAudio(byte[] audioBytes);
        void StopAudio();
    }
}