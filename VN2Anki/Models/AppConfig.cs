namespace VN2Anki.Models
{
    public class AppConfig
    {
        public string AudioDevice { get; set; }
        public string VideoWindow { get; set; }
        public string Deck { get; set; }
        public string Model { get; set; }
        public string AudioField { get; set; }
        public string ImageField { get; set; }
        public string IdleTime { get; set; } = "30";
        public string MaxSlots { get; set; } = "50";
        public bool UseDynamicTimeout { get; set; } = true;
        public bool OpenSettingsOnStartup { get; set; } = false;
        public string Language { get; set; } = "en-US";
        public double MainWindowTop { get; set; } = double.NaN;
        public double MainWindowLeft { get; set; } = double.NaN;
        public int MaxImageWidth { get; set; } = 1280;
        public int AudioBitrate { get; set; } = 128;
        public string AnkiUrl { get; set; } = "http://127.0.0.1:8765";
        public int AnkiTimeout { get; set; } = 15;
    }
}