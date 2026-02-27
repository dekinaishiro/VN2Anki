using CommunityToolkit.Mvvm.Messaging.Messages;
using VN2Anki.Models;

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
}