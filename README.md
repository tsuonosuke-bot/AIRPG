# ギルドシミュレーター

ギルドを運営し、冒険者を雇い、クエストへ送り出すターン制シミュレーション。

コンソール版とブラウザ版の2つの遊び方があります。ゲームのルールや進行は同じで、
画面の描き方と入力方法だけが違います。

## Androidスマホで遊ぶ（ブラウザ版）

ブラウザ版はタップで操作できるWebアプリ（PWA）です。インストール作業は不要で、
Chromeでページを開くだけで遊べます。

1. AndroidのChromeで公開URLを開く
   （GitHub Pages有効化後は `https://<ユーザー名>.github.io/airpg/`）
2. 画面下のボタンをタップして操作する
3. アプリのように使いたい場合は、Chromeのメニューから
   **［ホーム画面に追加］** を選ぶ

ホーム画面に追加すると、アドレスバーのない全画面で起動し、
一度開いたあとは **オフラインでも遊べます**（機内モードや電波の弱い場所でも動きます）。

セーブデータはブラウザのlocalStorageに保存されます。つまり **その端末のそのブラウザにだけ**
残るので、別の端末には引き継がれません。ブラウザのサイトデータを消すとセーブも消えます。

### 公開の準備（リポジトリ所有者向け・初回のみ）

ブラウザ版は `main` への push で自動デプロイされますが、最初に一度だけ設定が必要です。

1. GitHubのリポジトリで **Settings → Pages** を開く
2. **Source** を **GitHub Actions** にする
3. `main` へ push する（または Actions から
   「ブラウザ版をGitHub Pagesへデプロイ」を手動実行する）

デプロイが終わると、Actionsの実行結果に公開URLが表示されます。

## PCで遊ぶ（コンソール版）

```bash
dotnet run --project GuildSimulator.Cli
```

数字やアルファベットのキーを入力し、Enterで決定します。
セーブデータは実行ファイルと同じ場所の `Saves/save1.json` に書き出されます。

## 開発

### 構成

| プロジェクト | 役割 |
| --- | --- |
| `GuildSimulator.Core` | 戦闘・クエスト・ギルド運営などのゲームロジック。画面には依存しない |
| `GuildSimulator.Game` | 画面ロジック（`Screens/`）、マスタデータ、セーブ処理、ゲームループ |
| `GuildSimulator.Cli` | コンソール版のホスト |
| `GuildSimulator.Web` | ブラウザ版（Blazor WebAssembly）のホスト |
| `GuildSimulator.Tests` | xUnitによるテスト |

`GuildSimulator.Game` の画面ロジックは `Presentation/IGameIo` を通じて入出力します。
コンソール版は `ConsoleGameIo`、ブラウザ版は `WebGameIo` がこれを実装しているため、
画面コードとゲームループは両方のホストで共有されます。

ブラウザ版では、ゲームループが入力待ちで `await` するたびに制御がブラウザへ戻り、
ボタンが押されると待機中のタスクが完了してループが再開します。

### ビルドとテスト

```bash
dotnet build GuildSimulator.sln
dotnet test GuildSimulator.sln
```

### ブラウザ版をローカルで動かす

```bash
dotnet run --project GuildSimulator.Web
```

表示されたURL（既定では <http://localhost:5000>）を開きます。
スマホでの見え方を確認するには、ブラウザの開発者ツールでモバイル表示に切り替えてください。

### マスタデータ

`GuildSimulator.Game/Data` 配下のJSONを編集します。詳細は [MASTER_DATA.md](MASTER_DATA.md) を参照してください。

```bash
dotnet run --project GuildSimulator.Cli -- --validate-master
```
