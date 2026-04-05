/* 今後の追加機能アイデア
•	会話履歴のクリアボタン
•	お気に入りキャラクター登録
•	音声速度の調整スライダー
•	会話のテキスト保存（履歴として）
•	システムプロンプトのカスタマイズ

    2026/4/3追加
    フォントサイズを変更できるようにした
    トークン数と料金の表示を追加した
    予算クレジットの追加と超過警告機能を追加した（設定で予算上限を0にすると無制限、上限を超えると警告表示デフォルトは500円に設定）
    ログの自動保存機能を追加（設定でオンオフ、保存先はマイドキュメントのChappyTalkLogsフォルダ、ファイル名はタイムスタンプ）

    2026/4/6追加
    ログの読み込み機能を追加（保存したログファイルを選択して内容を表示）
    ログの再生機能を追加（ログの内容を音声で再生、AIの発言は現在選択中のキャラクターの声で、自分の発言はUserVoiceComboBoxで選んだキャラクターの声で再生）
    ログの再生中に停止できるようにした
 */


using NAudio.Wave;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Windows;
using File = System.IO.File;

namespace ChappyTalk
{
    // 話者情報を保持するクラス
    public class SpeakerInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public partial class MainWindow : Window
    {
        // ===== 音声関連 =====
        WaveInEvent waveIn;
        WaveFileWriter writer;
        private WaveOutEvent outputDevice;
        private AudioFileReader audioFile;
        private TaskCompletionSource<bool> recordingStoppedTcs;

        // ===== 状態管理 =====
        bool isRecording = false;
        bool isSpeaking = false; // ← エコー防止
        bool isMuted = false; // マイクミュート状態

        float silenceThreshold = 0.02f;
        int silenceCount = 0;
        int silenceLimit = 5;

        // ===== API =====
        private string OPENAI_API_KEY = "";
        private string AIVIS_URL = "http://127.0.0.1:10101";
        private int SPEAKER = 606865152;

        // ===== 設定 =====
        private AppSettings appSettings;
        private double speedScale = 1.1;
        private double pitchScale = 0.0;
        private double intonationScale = 1.2;
        private int echoGuardDelay = 300;
        private int maxHistory = 3;
        private string systemPrompt = "";

        // ===== トークン使用量 =====
        private int sessionPromptTokens = 0;
        private int sessionCompletionTokens = 0;
        private int totalPromptTokens = 0;
        private int totalCompletionTokens = 0;
        private double usdToJpy = 150.0; // ドル円レート
        private double budgetLimitJpy = 0; // 予算上限（円）0=無制限
        private bool budgetWarningShown = false; // 警告済みフラグ
        private bool autoSaveLog = true; // ログ自動保存

        // 話者情報
        private List<SpeakerInfo> speakers = new List<SpeakerInfo>();

        // 音声保存フォルダー（デフォルトはマイドキュメント）
        private string saveFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        private CancellationTokenSource? _logPlaybackCts;

        // HTTPクライアントを最適化（接続プーリング、Keep-Alive有効化）
        private static readonly HttpClient http = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 10 // 並列リクエストを許可
        })
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        // 会話履歴
        private List<object> conversationHistory = new List<object>();

        private string baseQueryJson = null;

        public MainWindow()
        {
            InitializeComponent();
            LoadSettings();
            LoadSpeakers();
            _ = InitializeQuery();
            StartAutoRecording();
        }

        private void LoadSettings()
        {
            appSettings = AppSettings.Load();
            ApplySettings(appSettings);
        }

        private void ApplySettings(AppSettings s)
        {
            OPENAI_API_KEY = s.OpenAiApiKey;
            AIVIS_URL = s.AivisUrl;
            silenceThreshold = s.SilenceThreshold;
            speedScale = s.SpeedScale;
            pitchScale = s.PitchScale;
            intonationScale = s.IntonationScale;
            echoGuardDelay = s.EchoGuardDelay;
            maxHistory = s.MaxHistory;
            systemPrompt = s.SystemPrompt;
            saveFolder = s.SaveFolder;
            SPEAKER = s.SpeakerId;
            OutputText.FontSize = s.FontSize;
            usdToJpy = s.UsdToJpy;
            totalPromptTokens = s.TotalPromptTokens;
            totalCompletionTokens = s.TotalCompletionTokens;
            budgetLimitJpy = s.BudgetLimitJpy;
            budgetWarningShown = false;
            autoSaveLog = s.AutoSaveLog;
            UpdateTokenDisplay();
        }

        private async Task InitializeQuery()
        {
            try
            {
                var res = await http.PostAsync(
                    $"{AIVIS_URL}/audio_query?text=こんにちは&speaker={SPEAKER}",
                    null
                );

                baseQueryJson = await res.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputText.Text += $"⚠️ 初期化失敗: {ex.Message}\n";
                });
            }
        }

        // =========================
        // 🎭 話者一覧の取得
        // =========================
        private async void LoadSpeakers()
        {
            try
            {
                var res = await http.GetAsync($"{AIVIS_URL}/speakers");
                var json = await res.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(json);
                var speakersArray = doc.RootElement.EnumerateArray();

                foreach (var speaker in speakersArray)
                {
                    var name = speaker.GetProperty("name").GetString();
                    var styles = speaker.GetProperty("styles").EnumerateArray();

                    foreach (var style in styles)
                    {
                        var styleName = style.GetProperty("name").GetString();
                        var id = style.GetProperty("id").GetInt32();

                        speakers.Add(new SpeakerInfo
                        {
                            Id = id,
                            Name = $"{name} ({styleName})"
                        });
                    }
                }

                // ListBoxにバインド
                SpeakerListBox.ItemsSource = speakers;
                UserVoiceComboBox.ItemsSource = speakers;
                UserVoiceComboBox.SelectedIndex = 0;

                // 現在のSPEAKERと一致するものを選択
                var currentSpeaker = speakers.FirstOrDefault(s => s.Id == SPEAKER);
                if (currentSpeaker != null)
                {
                    SpeakerListBox.SelectedItem = currentSpeaker;
                }

                OutputText.Text = $"✅ {speakers.Count}個のキャラクターを読み込みました\n\n";
            }
            catch (Exception ex)
            {
                OutputText.Text = $"⚠️ 話者一覧の取得に失敗: {ex.Message}\n";
            }
        }

        // =============================
        // 🎭 キャラクター選択イベント
        // =============================
        private void SpeakerListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SpeakerListBox.SelectedItem is SpeakerInfo selectedSpeaker)
            {
                SPEAKER = selectedSpeaker.Id;
                appSettings.SpeakerId = SPEAKER;
                appSettings.Save();
                OutputText.Text += $"🎭 {selectedSpeaker.Name} に変更しました\n";
                OutputText.ScrollToEnd();
            }
        }

        // =========================
        // 🎤 ミュートボタン
        // =========================
        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            isMuted = !isMuted;

            if (isMuted)
            {
                // ミュートON
                MuteButton.Content = "🔇 マイクOFF";
                MuteButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightCoral);
                OutputText.Text += "🔇 マイクをミュートしました\n";
            }
            else
            {
                // ミュートOFF
                MuteButton.Content = "🎤 マイクON";
                MuteButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGreen);
                OutputText.Text += "🎤 マイクをオンにしました\n";
            }
            OutputText.ScrollToEnd();
        }

        // =========================
        // 📁 保存フォルダー設定
        // =========================
        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "音声ファイルの保存先フォルダーを選択",
                InitialDirectory = saveFolder
            };

            if (dialog.ShowDialog() == true)
            {
                saveFolder = dialog.FolderName;
                appSettings.SaveFolder = saveFolder;
                appSettings.Save();
                OutputText.Text += $"📁 保存先を設定: {saveFolder}\n";
                OutputText.ScrollToEnd();
            }
        }

        // =========================
        // ⚙️ 設定画面
        // =========================
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(appSettings)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                appSettings = settingsWindow.Result;
                appSettings.SaveFolder = saveFolder; // フォルダーは別管理なので維持
                appSettings.SpeakerId = SPEAKER; // キャラクターは別管理なので維持
                appSettings.TotalPromptTokens = totalPromptTokens; // 累積トークンを維持
                appSettings.TotalCompletionTokens = totalCompletionTokens;
                appSettings.Save();
                ApplySettings(appSettings);
                OutputText.Text += "⚙️ 設定を保存しました\n";
                OutputText.ScrollToEnd();
            }
        }

        // =========================
        // 📂 ログ読み込み
        // =========================
        private void LogLoadButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "ログファイルを開く",
                Filter = "テキストファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*",
                DefaultExt = ".txt"
            };

            if (dialog.ShowDialog() == true)
            {
                string content = System.IO.File.ReadAllText(dialog.FileName);
                OutputText.Text = content;
            }
        }
        
        // =========================
        // 📝 ログ保存
        // =========================
        private void LogSaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveLog(showMessage: true);
        }

        // =========================
        // ▶️ ログ再生
        // =========================
        private async void LogPlayButton_Click(object sender, RoutedEventArgs e)
        {
            isSpeaking = true;
            var lines = OutputText.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                isSpeaking = false;
                return;
            }
            // AIの声 → 現在選択中のキャラクター
            var aiSpeaker = SpeakerListBox.SelectedItem as SpeakerInfo;
            // 自分の声 → UserVoiceComboBoxで選んだキャラクター
            var userSpeaker = UserVoiceComboBox.SelectedItem as SpeakerInfo;

            if (aiSpeaker == null)
            {
                MessageBox.Show("AIのキャラクターを選択してください。", "確認",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                isSpeaking = false;
                return;
            }
            if (userSpeaker == null)
            {
                MessageBox.Show("自分の声の担当キャラクターを選択してください。", "確認",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                isSpeaking = false;
                return;
            }

            _logPlaybackCts = new CancellationTokenSource();
            LogPlayButton.IsEnabled = false;
            LogStopButton.IsEnabled = true;

            try
            {
                foreach (var line in lines)
                {
                    if (_logPlaybackCts.Token.IsCancellationRequested) break;

                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    string text;
                    int speakerId;

                    if (trimmed.Contains("🧑"))
                    {
                        int idx = trimmed.IndexOf("🧑");
                        text = trimmed[(idx + "🧑".Length)..].Trim();
                        speakerId = userSpeaker.Id;
                    }
                    else if (trimmed.Contains("🤖"))
                    {
                        int idx = trimmed.IndexOf("🤖");
                        text = trimmed[(idx + "🤖".Length)..].Trim();
                        speakerId = aiSpeaker.Id;
                    }
                    else
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(text)) continue;

                    await SpeakWithIdAsync(text, speakerId, _logPlaybackCts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                MessageBox.Show($"再生中にエラーが発生しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LogPlayButton.IsEnabled = true;
                LogStopButton.IsEnabled = false;
                _logPlaybackCts?.Dispose();
                _logPlaybackCts = null;
                isSpeaking = false;
            }
        }

        // ==========
        // ⏹ 停止
        // ==========
        private void LogStopButton_Click(object sender, RoutedEventArgs e)
        {
            _logPlaybackCts?.Cancel();
        }

        // ==============================
        // ウインドウを閉じる時のイベント
        // ==============================
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (autoSaveLog)
            {
                SaveLog(showMessage: false);
            }
        }

        // ==============
        // 📝 ログ保存
        // ============
        private void SaveLog(bool showMessage)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(OutputText.Text))
                {
                    if (showMessage)
                    {
                        OutputText.Text += "⚠️ 保存するログがありません\n";
                        OutputText.ScrollToEnd();
                    }
                    return;
                }

                if (!Directory.Exists(saveFolder))
                {
                    Directory.CreateDirectory(saveFolder);
                }

                string fileName = $"Log{DateTime.Now:yyyy-MM-dd_HH-mm}.txt";
                string filePath = Path.Combine(saveFolder, fileName);

                // 同名ファイルがある場合は連番を追加
                int counter = 1;
                while (File.Exists(filePath))
                {
                    filePath = Path.Combine(saveFolder, $"Log{DateTime.Now:yyyy-MM-dd_HH-mm}_{counter}.txt");
                    counter++;
                }

                File.WriteAllText(filePath, OutputText.Text, Encoding.UTF8);

                if (showMessage)
                {
                    OutputText.Text += $"📝 ログを保存しました: {Path.GetFileName(filePath)}\n";
                    OutputText.ScrollToEnd();
                }
            }
            catch
            {
                if (showMessage)
                {
                    OutputText.Text += "❌ ログ保存失敗\n";
                    OutputText.ScrollToEnd();
                }
            }
        }

        // =========================
        // 💾 選択部分を音声保存
        // =========================
        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // 選択テキストを取得
            string selectedText = OutputText.SelectedText;

            if (string.IsNullOrWhiteSpace(selectedText))
            {
                MessageBox.Show("保存したいテキストを選択してください", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 絵文字やマーカーを除去してファイル名用のテキストを作成
            string cleanText = System.Text.RegularExpressions.Regex.Replace(selectedText, @"[🧑🤖🎤🔇📋✅⚠️❌🎭💾📁]", "");
            cleanText = cleanText.Trim();

            if (string.IsNullOrWhiteSpace(cleanText))
            {
                MessageBox.Show("音声化できるテキストがありません", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // ファイル名として使えない文字を除去
            string fileName = cleanText;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c.ToString(), "");
            }

            // 長すぎる場合は切り詰め（最大50文字）
            if (fileName.Length > 50)
            {
                fileName = fileName.Substring(0, 50);
            }

            // 保存先フォルダーが存在しない場合は作成
            if (!Directory.Exists(saveFolder))
            {
                Directory.CreateDirectory(saveFolder);
            }

            // 同名ファイルがある場合は連番を追加
            string filePath = Path.Combine(saveFolder, fileName + ".wav");
            int counter = 1;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(saveFolder, $"{fileName}_{counter}.wav");
                counter++;
            }

            try
            {
                OutputText.Text += $"💾 音声生成中...\n";
                OutputText.ScrollToEnd();

                // 音声生成（cleanTextを使用）
                byte[] audioBytes = await CreateAudio(cleanText);

                if (audioBytes.Length == 0)
                {
                    MessageBox.Show("音声の生成に失敗しました", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // NAudioを使って正しいWAVファイルとして保存
                using (var ms = new MemoryStream(audioBytes))
                using (var reader = new WaveFileReader(ms))
                using (var writer = new WaveFileWriter(filePath, reader.WaveFormat))
                {
                    reader.CopyTo(writer);
                }

                OutputText.Text += $"✅ 保存しました: {Path.GetFileName(filePath)}\n";
                OutputText.ScrollToEnd();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                OutputText.Text += $"❌ 保存失敗: {ex.Message}\n";
                OutputText.ScrollToEnd();
            }
        }

        // =========================
        // 🎤 自動録音
        // =========================
        private void StartAutoRecording()
        {
            waveIn = new WaveInEvent();
            waveIn.WaveFormat = new WaveFormat(16000, 1);
            waveIn.BufferMilliseconds = 50; // 50msごとに検出（デフォルト100ms→倍速で反応）

            int speechLength = 0;
            DateTime recordStartTime = DateTime.MinValue;

            waveIn.DataAvailable += async (s, a) =>
            {
                if (isSpeaking || isMuted) return;

                float volume = 0;

                for (int i = 0; i < a.BytesRecorded; i += 2)
                {
                    short sample = (short)(a.Buffer[i] | (a.Buffer[i + 1] << 8));
                    volume = Math.Max(volume, Math.Abs(sample / 32768f));
                }

                // =========================
                // 🔊 音あり
                // =========================
                if (volume > silenceThreshold)
                {
                    silenceCount = 0;
                    speechLength++;

                    if (!isRecording)
                    {
                        isRecording = true;
                        speechLength = 0;

                        writer = new WaveFileWriter("mic.wav", waveIn.WaveFormat);
                        recordingStoppedTcs = new TaskCompletionSource<bool>();

                        recordStartTime = DateTime.Now;

                        Dispatcher.Invoke(() =>
                        {
                            OutputText.Text += "🎤 録音中...\n";
                            OutputText.ScrollToEnd();
                        });
                    }

                    writer?.Write(a.Buffer, 0, a.BytesRecorded);
                }
                // =========================
                // 🔇 無音
                // =========================
                else
                {
                    if (isRecording)
                    {
                        // 余韻（語尾切れ防止）
                        writer?.Write(a.Buffer, 0, a.BytesRecorded);

                        silenceCount++;

                        // 🔥 動的無音判定（50msバッファ基準）
                        int dynamicSilenceLimit;

                        if (speechLength < 30)
                            dynamicSilenceLimit = 14; // 短い発話 → 700ms待つ
                        else if (speechLength < 80)
                            dynamicSilenceLimit = 10; // 中程度 → 500ms
                        else
                            dynamicSilenceLimit = 7;  // 長い発話 → 350ms で早く切る

                        // 🔥 最低録音時間（誤爆防止）
                        var recordingTime = (DateTime.Now - recordStartTime).TotalMilliseconds;

                        if (silenceCount > dynamicSilenceLimit && recordingTime > 400)
                        {
                            isRecording = false;

                            waveIn.StopRecording();

                            await recordingStoppedTcs.Task;

                            await Dispatcher.InvokeAsync(async () =>
                            {
                                await ProcessVoice();

                                // 🔁 再スタート
                                StartAutoRecording();
                            });
                        }
                    }
                }
            };

            waveIn.RecordingStopped += (s, a) =>
            {
                writer?.Flush();
                writer?.Dispose();
                writer = null;

                waveIn?.Dispose();
                waveIn = null;

                recordingStoppedTcs?.SetResult(true);
            };

            waveIn.StartRecording();
        }
        // =========================
        // 🎤 → 🤖 → 🔊
        // =========================
        private async Task ProcessVoice()
        {
            string text = await SpeechToText();

            if (string.IsNullOrWhiteSpace(text))
            {
                // 録音したが無音だった場合、「🎤 録音中...」を削除
                if (OutputText.Text.EndsWith("🎤 録音中...\n"))
                {
                    OutputText.Text = OutputText.Text.Substring(0, OutputText.Text.Length - 9);
                }
                return;
            }

            // 「🎤 録音中...」を実際の発言に置き換え
            if (OutputText.Text.EndsWith("🎤 録音中...\n"))
            {
                OutputText.Text = OutputText.Text.Substring(0, OutputText.Text.Length - 9);
            }

            OutputText.Text += "🧑 " + text + "\n";
            OutputText.ScrollToEnd(); // 自動スクロール

            string reply = await AskGPT(text);
            OutputText.Text += "🤖 " + reply + "\n\n";
            OutputText.ScrollToEnd(); // 自動スクロール

            var (first, rest) = SplitFirstSentence(reply);
            // まず最初だけ喋る（即）
            await Speak(first);

            // 残りも順番に喋る（awaitでエコー防止）
            if (!string.IsNullOrWhiteSpace(rest))
            {
                await Speak(rest);
            }
        }

        private (string first, string rest) SplitFirstSentence(string text)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"^.*?[。！？]");

            if (match.Success)
            {
                var first = match.Value;
                var rest = text.Substring(first.Length);
                return (first, rest);
            }

            return (text, "");
        }

        // =========================
        // 🤖 ChatGPT
        // =========================
        private async Task<string> AskGPT(string input)
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", OPENAI_API_KEY);

            // ユーザーメッセージを履歴に追加
            conversationHistory.Add(new { role = "user", content = input });

            // 履歴が多すぎる場合は古いものを削除（料金節約）
            while (conversationHistory.Count > maxHistory * 2)
            {
                conversationHistory.RemoveAt(0);
            }

            // システムプロンプト + 会話履歴を組み立て
            var messages = new List<object>();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                messages.Add(new { role = "system", content = systemPrompt });
            }
            messages.AddRange(conversationHistory);

            var body = new
            {
                model = "gpt-4o-mini",
                messages = messages,
                max_tokens = 200
            };

            var content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            );

            var res = await http.PostAsync(
                "https://api.openai.com/v1/chat/completions",  // 正しいエンドポイント
                content
            );

            var json = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            var reply = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            // トークン使用量を取得・累積
            if (doc.RootElement.TryGetProperty("usage", out var usage))
            {
                int promptTokens = usage.GetProperty("prompt_tokens").GetInt32();
                int completionTokens = usage.GetProperty("completion_tokens").GetInt32();

                sessionPromptTokens += promptTokens;
                sessionCompletionTokens += completionTokens;
                totalPromptTokens += promptTokens;
                totalCompletionTokens += completionTokens;

                // 累積トークンを設定に保存
                appSettings.TotalPromptTokens = totalPromptTokens;
                appSettings.TotalCompletionTokens = totalCompletionTokens;
                appSettings.Save();

                UpdateTokenDisplay();
            }

            // AIの返答を履歴に追加
            conversationHistory.Add(new { role = "assistant", content = reply });

            return reply;
        }

        // =========================
        // 🎤 音声認識
        // =========================
        private async Task<string> SpeechToText()
        {
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", OPENAI_API_KEY);

            using var form = new MultipartFormDataContent();

            var file = new ByteArrayContent(File.ReadAllBytes("mic.wav"));
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

            form.Add(file, "file", "mic.wav");
            form.Add(new StringContent("gpt-4o-mini-transcribe"), "model");

            var res = await http.PostAsync(
                "https://api.openai.com/v1/audio/transcriptions",
                form
            );

            var json = await res.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("text", out var textElement))
                return textElement.GetString();

            return "";
        }

        // =============================
        // 💰 トークン使用量・料金表示
        // =============================
        private void UpdateTokenDisplay()
        {
            // gpt-4o-mini 料金: 入力 $0.15/1M, 出力 $0.60/1M
            double sessionCostUsd = sessionPromptTokens * 0.15 / 1_000_000 + sessionCompletionTokens * 0.60 / 1_000_000;
            double totalCostUsd = totalPromptTokens * 0.15 / 1_000_000 + totalCompletionTokens * 0.60 / 1_000_000;
            double sessionCostJpy = sessionCostUsd * usdToJpy;
            double totalCostJpy = totalCostUsd * usdToJpy;

            int sessionTokens = sessionPromptTokens + sessionCompletionTokens;
            int totalTokens = totalPromptTokens + totalCompletionTokens;

            SessionTokenStatus.Text = $"📊 今回: {sessionTokens:#,0} tokens　約 {sessionCostJpy:F2}円";

            // 予算上限の表示
            if (budgetLimitJpy > 0)
            {
                double remaining = budgetLimitJpy - totalCostJpy;
                string budgetText = remaining >= 0
                    ? $"　残り約 {remaining:F2}円"
                    : $"　⚠️ 予算超過 {Math.Abs(remaining):F2}円";

                TotalTokenStatus.Text = $"📈 累計: {totalTokens:#,0} tokens　約 {totalCostJpy:F2}円 / {budgetLimitJpy:F0}円{budgetText}";
                TotalTokenStatus.Foreground = remaining < 0
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red)
                    : remaining < budgetLimitJpy * 0.2
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.OrangeRed)
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));

                // 予算超過警告（1回だけ）
                if (remaining < 0 && !budgetWarningShown)
                {
                    budgetWarningShown = true;
                    OutputText.Text += $"⚠️ 予算上限（{budgetLimitJpy:F0}円）を超えました！設定で予算を見直すかトークンをクリアしてください\n";
                    OutputText.ScrollToEnd();
                }
            }
            else
            {
                TotalTokenStatus.Text = $"📈 累計: {totalTokens:#,0} tokens　約 {totalCostJpy:F2}円（${totalCostUsd:F4}）";
                TotalTokenStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x55, 0x55, 0x55));
            }
        }
        /*=========================
         💣 トークンリセット
         =========================
         予算管理のため、累計トークン数をリセットする機能を追加
         設定画面からもリセット可能に（予算管理のリスタート用）
         リセットするとセッションと累計の両方が0になる
         リセット前に確認ダイアログを表示して誤操作を防止
         */
        private void ClearTokens_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("累計トークン数をリセットしますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                totalPromptTokens = 0;
                totalCompletionTokens = 0;
                sessionPromptTokens = 0;
                sessionCompletionTokens = 0;

                appSettings.TotalPromptTokens = 0;
                appSettings.TotalCompletionTokens = 0;
                appSettings.Save();

                UpdateTokenDisplay();
            }
        }

        // =========================
        // 🔊 音声合成（AIVIS）
        // =========================
        private async Task Speak(string text)
        {
            isSpeaking = true;

            try
            {
                // 50文字を目安に分割（句読点優先で自然な区切りに）
                var lines = SplitText(text, 80);

                // 🚀 並列処理: 再生と音声生成を並行実行
                var audioGenerationTasks = new Queue<Task<byte[]>>();

                // 最初の3つを先に生成開始（先読みを増やして高速化）
                int prefetchCount = Math.Min(3, lines.Count);
                for (int i = 0; i < prefetchCount; i++)
                {
                    var line = lines[i];
                    audioGenerationTasks.Enqueue(CreateAudioAsync(line));
                }

                int currentIndex = prefetchCount;

                // 順番に再生しながら、次の音声を並行生成
                while (audioGenerationTasks.Count > 0)
                {
                    try
                    {
                        // 次の音声が完成するのを待つ
                        var audioTask = audioGenerationTasks.Dequeue();
                        var audio = await audioTask;

                        // 次の文の生成を開始（先読み）
                        if (currentIndex < lines.Count)
                        {
                            var nextLine = lines[currentIndex];
                            audioGenerationTasks.Enqueue(CreateAudioAsync(nextLine));
                            currentIndex++;
                        }

                        await PlaySoundFromMemory(audio);
                        await Task.Delay(100); 
                    }
                    catch (HttpRequestException ex)
                    {
                        // API接続エラーをログに表示
                        Dispatcher.Invoke(() =>
                        {
                            OutputText.Text += $"\n⚠️ 音声生成エラー: {ex.Message}";
                        });
                        // エラーが出ても残りのタスクをクリア
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    OutputText.Text += $"\n❌ エラー: {ex.Message}";
                });
            }
            finally
            {
                // Bluetoothスピーカーの遅延＋残響を考慮して待機
                await Task.Delay(echoGuardDelay);
                isSpeaking = false;
            }
        }

        private List<string> SplitText(string text, int maxLength)
        {
            var result = new List<string>();

            // 改行を適切な区切りに変換（改行コードを完全除去）
            text = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");

            // 連続する空白を1つにまとめる
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");

            // 前後の空白を削除
            text = text.Trim();

            // 句読点で分割（。！？）
            var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[。！？])");

            foreach (var sentence in sentences)
            {
                var cleanSentence = sentence.Trim();
                if (string.IsNullOrWhiteSpace(cleanSentence)) continue;

                // 長すぎる場合はさらに分割
                if (cleanSentence.Length <= maxLength)
                {
                    result.Add(cleanSentence);
                }
                else
                {
                    // 「、」でさらに分割
                    var parts = System.Text.RegularExpressions.Regex.Split(cleanSentence, @"(?<=[、])");

                    foreach (var part in parts)
                    {
                        var cleanPart = part.Trim();
                        if (string.IsNullOrWhiteSpace(cleanPart)) continue;

                        if (cleanPart.Length <= maxLength)
                        {
                            result.Add(cleanPart);
                        }
                        else
                        {
                            // 空白で分割を試みる（単語の途中を避ける）
                            var words = cleanPart.Split(' ');
                            var currentChunk = new StringBuilder();

                            foreach (var word in words)
                            {
                                if (currentChunk.Length + word.Length + 1 <= maxLength)
                                {
                                    if (currentChunk.Length > 0) currentChunk.Append(' ');
                                    currentChunk.Append(word);
                                }
                                else
                                {
                                    // 現在のチャンクを追加
                                    if (currentChunk.Length > 0)
                                    {
                                        result.Add(currentChunk.ToString());
                                        currentChunk.Clear();
                                    }

                                    // 単語自体がmaxLengthより長い場合のみ強制カット
                                    if (word.Length > maxLength)
                                    {
                                        for (int i = 0; i < word.Length; i += maxLength)
                                        {
                                            result.Add(word.Substring(i, Math.Min(maxLength, word.Length - i)));
                                        }
                                    }
                                    else
                                    {
                                        currentChunk.Append(word);
                                    }
                                }
                            }

                            // 最後のチャンクを追加
                            if (currentChunk.Length > 0)
                            {
                                result.Add(currentChunk.ToString());
                            }
                        }
                    }
                }
            }

            return result;
        }

        // 音声生成を非同期で実行（キャッシュも検討可能）
        private async Task<byte[]> CreateAudioAsync(string text)
        {
            return await CreateAudio(text);
        }

        private async Task<byte[]> CreateAudio(string text)
        {
            try
            {
                text = text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

                if (string.IsNullOrWhiteSpace(text))
                    return Array.Empty<byte>();

                // ✅ 毎回クエリ作る（でも最適化する）
                var queryRes = await http.PostAsync(
                    $"{AIVIS_URL}/audio_query?text={Uri.EscapeDataString(text)}&speaker={SPEAKER}",
                    null
                );

                queryRes.EnsureSuccessStatusCode();

                var queryJson = await queryRes.Content.ReadAsStringAsync();

                // ✅ ここでパラメータだけ書き換え
                using var doc = JsonDocument.Parse(queryJson);
                var root = doc.RootElement;

                using var ms = new MemoryStream();
                using var writer = new Utf8JsonWriter(ms);

                writer.WriteStartObject();

                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name == "speedScale")
                        writer.WriteNumber("speedScale", speedScale);

                    else if (prop.Name == "pitchScale")
                        writer.WriteNumber("pitchScale", pitchScale);

                    else if (prop.Name == "intonationScale")
                        writer.WriteNumber("intonationScale", intonationScale);

                    else
                        prop.WriteTo(writer);
                }

                writer.WriteEndObject();
                writer.Flush();

                var audioRes = await http.PostAsync(
                    $"{AIVIS_URL}/synthesis?speaker={SPEAKER}",
                    new StringContent(Encoding.UTF8.GetString(ms.ToArray()), Encoding.UTF8, "application/json")
                );

                audioRes.EnsureSuccessStatusCode();

                return await audioRes.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"音声生成エラー: {ex.Message}", ex);
            }
        }


        private async Task PlaySoundFromMemory(byte[] audioBytes)
        {
            var tcs = new TaskCompletionSource<bool>();

            // メモリストリームとリーダーを再生完了まで保持
            var ms = new MemoryStream(audioBytes);
            var reader = new WaveFileReader(ms);
            var output = new WaveOutEvent();

            // レイテンシを最小化（200msに短縮して高速応答）
            output.DesiredLatency = 200;

            output.PlaybackStopped += (s, e) =>
            {
                output?.Dispose();
                reader?.Dispose();
                ms?.Dispose();
                tcs.SetResult(true);
            };

            output.Init(reader);
            output.Play();

            // 再生完了まで待つ
            await tcs.Task;
        }
        private byte[] AddSilence(byte[] wavData, int milliseconds = 1050)
        {
            using var ms = new MemoryStream(wavData);
            using var reader = new WaveFileReader(ms);

            var silenceBytes = new byte[reader.WaveFormat.AverageBytesPerSecond * milliseconds / 1000];

            using var outStream = new MemoryStream();
            using (var writer = new WaveFileWriter(outStream, reader.WaveFormat))
            {
                // 前に無音追加
                writer.Write(silenceBytes, 0, silenceBytes.Length);
                // 元音声コピー
                reader.CopyTo(writer);
                // 後ろにも無音追加
                writer.Write(silenceBytes, 0, silenceBytes.Length);
            }
            return outStream.ToArray();
        }

        private async Task SpeakWithIdAsync(string text, int speakerId, CancellationToken ct)
        {
            using var http = new HttpClient();

            // 1. audio_query 取得
            var queryRes = await http.PostAsync(
                $"{AIVIS_URL}/audio_query?text={Uri.EscapeDataString(text)}&speaker={speakerId}", null, ct);
            queryRes.EnsureSuccessStatusCode();
            var queryJson = await queryRes.Content.ReadAsStringAsync(ct);

            // 2. synthesis で音声データ取得
            var synthContent = new StringContent(queryJson, System.Text.Encoding.UTF8, "application/json");
            var synthRes = await http.PostAsync(
                $"{AIVIS_URL}/synthesis?speaker={speakerId}", synthContent, ct);
            synthRes.EnsureSuccessStatusCode();
            var audioData = await synthRes.Content.ReadAsByteArrayAsync(ct);

            // 3. NAudio で再生（完了まで待機）
            ct.ThrowIfCancellationRequested();
            using var ms = new System.IO.MemoryStream(audioData);
            using var reader = new NAudio.Wave.WaveFileReader(ms);
            using var waveOut = new NAudio.Wave.WaveOutEvent();
            var tcs = new TaskCompletionSource<bool>();

            waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);
            waveOut.Init(reader);
            waveOut.Play();

            // キャンセル対応: キャンセルされたら再生停止
            using var reg = ct.Register(() =>
            {
                waveOut.Stop();
                tcs.TrySetCanceled();
            });
            await tcs.Task;
        }
    }
}