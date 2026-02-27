using System.Text.Json.Serialization;

namespace VN2Anki.Models
{
    public class AppConfig
    {
        public GeneralConfig General { get; set; } = new GeneralConfig();
        public MediaConfig Media { get; set; } = new MediaConfig();
        public AnkiConfig Anki { get; set; } = new AnkiConfig();
        public SessionConfig Session { get; set; } = new SessionConfig();
        public HookConfig Hook { get; set; } = new HookConfig();
        public OverlayConfig Overlay { get; set; } = new OverlayConfig();
    }

    public class GeneralConfig
    {
        public string Language { get; set; } = "en-US";
        public bool OpenSettingsOnStartup { get; set; } = false;
        public double MainWindowTop { get; set; } = double.NaN;
        public double MainWindowLeft { get; set; } = double.NaN;
    }

    public class MediaConfig
    {
        public string AudioDevice { get; set; }
        public string VideoWindow { get; set; }
        public int MaxImageWidth { get; set; } = 1280;
        public int AudioBitrate { get; set; } = 128;
    }

    public class AnkiConfig
    {
        public string Deck { get; set; }
        public string Model { get; set; }
        public string AudioField { get; set; }
        public string ImageField { get; set; }
        public string Url { get; set; } = "http://127.0.0.1:8765";
        public int TimeoutSeconds { get; set; } = 15;
    }

    public class SessionConfig
    {
        public string IdleTime { get; set; } = "20";
        public string MaxSlots { get; set; } = "30";
        public bool UseDynamicTimeout { get; set; } = true;
    }

    public class HookConfig
    {
        // 0 = Clipboard, 1 = WebSocket (Luna), 2 = Textractor
        public int ActiveHookType { get; set; } = 0;
        public string WebSocketUrl { get; set; } = "ws://localhost:2333"; 
    }

    public class OverlayConfig
    {
        public bool IsEnabled { get; set; } = false;
        public string BgColor { get; set; } = "#E5111111";
        public string FontColor { get; set; } = "#FFFFFF";
        public string OverlayBgColor { get; set; } = "#E5111111";
        public int FontSize { get; set; } = 32;
        public string PassThroughModifier { get; set; } = "Ctrl";
        public List<string> CustomExtensions { get; set; } = new List<string>();
        // states
        public bool IsTextAtTop { get; set; } = false;
        public bool IsTransparent { get; set; } = true;
        public bool IsPassThrough { get; set; } = false;
    }
}