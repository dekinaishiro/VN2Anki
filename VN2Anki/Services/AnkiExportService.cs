using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Messages;
using VN2Anki.Models;

namespace VN2Anki.Services
{
    public class AnkiExportService
    {
        private readonly AnkiHandler _anki;
        private readonly MediaService _mediaService;

        public AnkiExportService(AnkiHandler anki, MediaService mediaService)
        {
            _anki = anki;
            _mediaService = mediaService;
        }

        private void SendStatus(string message)
        {
            WeakReferenceMessenger.Default.Send(new StatusMessage(message));
        }

        public async Task<(bool success, string message)> ExportSlotAsync(MiningSlot slot, AnkiConfig ankiConfig, MediaConfig mediaConfig)
        {
            if (string.IsNullOrEmpty(ankiConfig.Deck)) return (false, "Please select a Deck.");

            string uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8);
            string audioFilename = $"miner_{uniqueId}.mp3";
            string imageFilename = $"miner_{uniqueId}.jpg";

            bool hasAudio = slot.AudioBytes != null && slot.AudioBytes.Length > 0;
            bool hasImage = slot.ScreenshotBytes != null && slot.ScreenshotBytes.Length > 0;

            if (hasAudio)
            {
                await _anki.StoreMediaAsync(audioFilename, slot.AudioBytes);
            }
            if (hasImage)
            {
                await _anki.StoreMediaAsync(imageFilename, slot.ScreenshotBytes);
            }

            var result = await _anki.UpdateLastCardAsync(ankiConfig.Deck, ankiConfig.AudioField, ankiConfig.ImageField, hasAudio ? audioFilename : null, hasImage ? imageFilename : null);

            // force GC collection after heavy media processing
            //_ = Task.Run(async () =>
            //{
            //    await Task.Delay(1000);
            //    System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            //    GC.Collect(2, GCCollectionMode.Forced, false, true);
            //});

            return result;
        }
    }
}