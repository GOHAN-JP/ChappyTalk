using System.Windows;

namespace ChappyTalk
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Result { get; private set; }

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();

            // 現在の設定を画面に反映
            SystemPromptBox.Text = settings.SystemPrompt;
            ApiKeyBox.Password = settings.OpenAiApiKey;
            AivisUrlBox.Text = settings.AivisUrl;
            SpeedSlider.Value = settings.SpeedScale;
            PitchSlider.Value = settings.PitchScale;
            IntonationSlider.Value = settings.IntonationScale;
            SilenceSlider.Value = settings.SilenceThreshold;
            EchoSlider.Value = settings.EchoGuardDelay;
            HistorySlider.Value = settings.MaxHistory;
        }

        private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SpeedLabel != null) SpeedLabel.Text = SpeedSlider.Value.ToString("F1");
        }

        private void PitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (PitchLabel != null) PitchLabel.Text = PitchSlider.Value.ToString("F2");
        }

        private void IntonationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (IntonationLabel != null) IntonationLabel.Text = IntonationSlider.Value.ToString("F1");
        }

        private void SilenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SilenceLabel != null) SilenceLabel.Text = SilenceSlider.Value.ToString("F3");
        }

        private void EchoSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (EchoLabel != null) EchoLabel.Text = ((int)EchoSlider.Value).ToString();
        }

        private void HistorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (HistoryLabel != null) HistoryLabel.Text = ((int)HistorySlider.Value).ToString();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Result = new AppSettings
            {
                SystemPrompt = SystemPromptBox.Text,
                OpenAiApiKey = ApiKeyBox.Password,
                AivisUrl = AivisUrlBox.Text,
                SpeedScale = SpeedSlider.Value,
                PitchScale = PitchSlider.Value,
                IntonationScale = IntonationSlider.Value,
                SilenceThreshold = (float)SilenceSlider.Value,
                EchoGuardDelay = (int)EchoSlider.Value,
                MaxHistory = (int)HistorySlider.Value
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
