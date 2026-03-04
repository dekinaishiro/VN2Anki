using CommunityToolkit.Mvvm.Messaging.Messages;
using VN2Anki.Models;
using VN2Anki.Models.Entities;

namespace VN2Anki.Messages
{
    public class StatusMessage : ValueChangedMessage<string>
    {
        public StatusMessage(string value) : base(value) { }
    }

    public class SlotCapturedMessage : ValueChangedMessage<MiningSlot>
    {
        public SlotCapturedMessage(MiningSlot value) : base(value) { }
    }
    public class OverlayConfigUpdatedMessage
    {
        // Overlay config update trigger
    }
    public class FlashMessagePayload
    {
        public string Message { get; set; }
        public bool IsError { get; set; }
    }

    public class ShowFlashMessage : ValueChangedMessage<FlashMessagePayload>
    {
        public ShowFlashMessage(FlashMessagePayload value) : base(value) { }
    }

    public class PlayVnMessage
    {
        public VisualNovel VisualNovel { get; }
        public PlayVnMessage(VisualNovel vn)
        {
            VisualNovel = vn;
        }
    }
    public class SessionEndedMessage{ }
    public class SessionSavedMessage{ }

    public class TextCopiedMessage
    {
        public string Text { get; }
        public DateTime Timestamp { get; }
        public TextCopiedMessage(string text, DateTime timestamp)
        {
            Text = text;
            Timestamp = timestamp;
        }
    }

    public class AudioErrorMessage : ValueChangedMessage<string>
    {
        public AudioErrorMessage(string value) : base(value) { }
    }

    public class BufferStoppedMessage { }
}