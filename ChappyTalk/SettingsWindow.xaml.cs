using System.Windows;

namespace ChappyTalk
{
    public partial class SettingsWindow : Window
    {
        public AppSettings Result { get; private set; }
        private double _originalFontSize;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();

            // 現在の設定を画面に反映
            SystemPromptBox.Text = settings.SystemPrompt;
            ApiKeyBox.Password = settings.OpenAiApiKey;
            AivisUrlBox.Text = settings.AivisUrl;
            SaveFolderBox.Text = settings.SaveFolder;
            CheckOpeningWords.IsChecked = settings.UseOpening;
            TextBoxOpening.Text = settings.OpeningText;
            CheckEndingWords.IsChecked = settings.UseEnding;
            TextBoxEnding.Text = settings.EndingText;
            SpeedSlider.Value = settings.SpeedScale;
            PitchSlider.Value = settings.PitchScale;
            IntonationSlider.Value = settings.IntonationScale;
            SilenceSlider.Value = settings.SilenceThreshold;
            EchoSlider.Value = settings.EchoGuardDelay;
            HistorySlider.Value = settings.MaxHistory;
            FontSizeSlider.Value = settings.FontSize;
            _originalFontSize = settings.FontSize;
            UsdToJpyBox.Text = settings.UsdToJpy.ToString("F1");
            BudgetLimitBox.Text = settings.BudgetLimitJpy > 0 ? settings.BudgetLimitJpy.ToString("F0") : "0";
            AutoSaveLogCheck.IsChecked = settings.AutoSaveLog;
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

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (FontSizeLabel != null) FontSizeLabel.Text = ((int)FontSizeSlider.Value).ToString();

            // リアルタイムプレビュー: MainWindow の OutputText に反映
            if (Owner is MainWindow main)
            {
                main.OutputText.FontSize = (int)FontSizeSlider.Value;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Result = new AppSettings
            {
                SystemPrompt = SystemPromptBox.Text,
                OpenAiApiKey = ApiKeyBox.Password,
                AivisUrl = AivisUrlBox.Text,
                SaveFolder = SaveFolderBox.Text,
                UseOpening = CheckOpeningWords.IsChecked == true,
                OpeningText = TextBoxOpening.Text,
                UseEnding = CheckEndingWords.IsChecked == true,
                EndingText = TextBoxEnding.Text,
                SpeedScale = SpeedSlider.Value,
                PitchScale = PitchSlider.Value,
                IntonationScale = IntonationSlider.Value,
                SilenceThreshold = (float)SilenceSlider.Value,
                EchoGuardDelay = (int)EchoSlider.Value,
                MaxHistory = (int)HistorySlider.Value,
                FontSize = (int)FontSizeSlider.Value,
                UsdToJpy = double.TryParse(UsdToJpyBox.Text, out var rate) ? rate : 150.0,
                BudgetLimitJpy = double.TryParse(BudgetLimitBox.Text, out var budget) ? budget : 0,
                AutoSaveLog = AutoSaveLogCheck.IsChecked == true
            };

            DialogResult = true;
        }

        // =========================
        // 📁 保存フォルダー設定
        // =========================
        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "音声ファイルの保存先フォルダーを選択",
                InitialDirectory = SaveFolderBox.Text
            };

            if (dialog.ShowDialog() == true)
            {
                SaveFolderBox.Text = dialog.FolderName;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // 保存以外で閉じた場合（キャンセル・×ボタン・Esc）は元のフォントサイズに戻す
            if (DialogResult != true && Owner is MainWindow main)
            {
                main.OutputText.FontSize = _originalFontSize;
            }
        }
    }
}
