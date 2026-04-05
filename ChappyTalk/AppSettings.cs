using System.IO;
using System.Text.Json;

namespace ChappyTalk
{
    public class AppSettings
    {
        public string SystemPrompt { get; set; } = "";
        public string OpenAiApiKey { get; set; } = "";
        public string AivisUrl { get; set; } = "http://127.0.0.1:10101";
        public double SpeedScale { get; set; } = 1.1;
        public double PitchScale { get; set; } = 0.0;
        public double IntonationScale { get; set; } = 1.2;
        public float SilenceThreshold { get; set; } = 0.02f;
        public int EchoGuardDelay { get; set; } = 300;
        public int MaxHistory { get; set; } = 3;
        public int SpeakerId { get; set; } = 606865152;
        public double FontSize { get; set; } = 18;
        public double UsdToJpy { get; set; } = 150.0;
        public int TotalPromptTokens { get; set; } = 0;
        public int TotalCompletionTokens { get; set; } = 0;
        public string SaveFolder { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        private static readonly string SettingsPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
                // 読み込み失敗時はデフォルト値を使用
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // 保存失敗は無視
            }
        }
    }
}
