using CommunityToolkit.Mvvm.Messaging.Messages;
using VN2Anki.Models;

namespace VN2Anki.Messages
{
    // Mensagem enviada quando o texto de status lá embaixo na UI muda
    public class StatusMessage : ValueChangedMessage<string>
    {
        public StatusMessage(string value) : base(value) { }
    }

    // Mensagem enviada quando um novo slot é capturado
    public class SlotCapturedMessage : ValueChangedMessage<MiningSlot>
    {
        public SlotCapturedMessage(MiningSlot value) : base(value) { }
    }
}