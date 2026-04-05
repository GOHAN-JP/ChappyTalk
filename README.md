# 🗣️ ChappyTalk

AIキャラクターとリアルタイム音声会話ができるWindowsデスクトップアプリです。

マイクに向かって話すだけで、AIが考えて、キャラクターの声で返事をしてくれます。

![WPF](https://img.shields.io/badge/WPF-.NET%2010-blue)
![License](https://img.shields.io/badge/license-MIT-green)

## ✨ 特徴

- 🎤 **自動音声認識** — 話し始めると自動で録音、黙ると自動で送信
- 🤖 **ChatGPT連携** — GPT-4o-miniが自然な返答を生成
- 🔊 **キャラクターボイス** — AIVIS Speech（VOICEVOX互換）で好きなキャラの声で再生
- 🎭 **キャラクター切替** — 100以上のキャラクターから選択可能
- ⚙️ **カスタマイズ** — 音声速度・ピッチ・抑揚・システムプロンプトなどを設定画面で調整
- 💾 **音声保存** — 会話テキストを選択して音声ファイル（WAV）として保存
- 🔇 **エコー防止** — Bluetoothスピーカーにも対応

## 📋 必要なもの

| 項目 | 説明 |
|------|------|
| **OS** | Windows 10/11 |
| **ランタイム** | [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **音声合成** | [AIVIS Speech](https://aivis-project.com/) （起動しておく） |
| **APIキー** | [OpenAI API Key](https://platform.openai.com/api-keys) （GPT-4o-mini + Whisper用） |

## 🚀 セットアップ

### 1. AIVIS Speechを起動

[AIVIS Speech](https://aivis-project.com/) をダウンロード・インストールして起動してください。
デフォルトの `http://127.0.0.1:10101` で接続します。

### 2. アプリをダウンロード

1. [Releases](https://github.com/GOHAN-JP/ChappyTalk/releases) ページを開く
2. 最新版の **ChappyTalk-vX.X.X-win-x64.zip** をダウンロード
3. ZIPを解凍して **ChappyTalk.exe** を実行

> 💡 開発者向け：ソースからビルドする場合は `dotnet run --project ChappyTalk` で起動できます

### 3. APIキーを設定

1. アプリ上部の **⚙️ 設定** ボタンをクリック
2. **OpenAI APIキー** に `sk-proj-...` のキーを入力
3. **💾 保存** をクリック

### 4. 話しかける！

マイクに向かって話すと、自動で認識 → ChatGPTが返答 → キャラクターの声で再生されます。

## ⚙️ 設定項目

| 項目 | 説明 |
|------|------|
| 💬 システムプロンプト | AIの性格・口調を指定（例：「関西弁で話して」） |
| ⏩ 音声速度 | 0.5〜2.0（デフォルト: 1.1） |
| 🎵 ピッチ | -0.15〜0.15 |
| 🎭 抑揚 | 0.0〜2.0（デフォルト: 1.2） |
| 🎤 無音検出感度 | 小さい値 = 高感度（静かな環境向け） |
| 🔇 エコー防止待機 | Bluetoothスピーカーは大きめに |
| 📝 会話履歴保持 | 多いほど文脈を覚えるが料金増 |

## 💰 料金の目安

- **GPT-4o-mini**: 入力 $0.15 / 100万トークン、出力 $0.60 / 100万トークン
- **Whisper**: $0.006 / 分
- 普通の会話なら **月数十円〜数百円** 程度

## 🏗️ 技術スタック

- **UI**: WPF (.NET 10)
- **音声入力**: NAudio（マイク録音）
- **音声認識**: OpenAI Whisper API (gpt-4o-mini-transcribe)
- **AI応答**: OpenAI ChatGPT API (gpt-4o-mini)
- **音声合成**: AIVIS Speech（VOICEVOX互換REST API）

## 📄 ライセンス

[MIT License](LICENSE)

---

Made with ❤️ and AI
