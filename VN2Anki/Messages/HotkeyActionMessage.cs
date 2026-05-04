namespace VN2Anki.Messages
{
    public class HotkeyActionMessage
    {
        public string ActionName { get; }
        public HotkeyActionMessage(string actionName)
        {
            ActionName = actionName;
        }
    }
}
