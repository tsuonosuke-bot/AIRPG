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

日常開発では `Debug` を使い、テストプロジェクトだけを直接実行します。
これにより、テストに不要なブラウザ版のビルドと、Git管理されている
`bin/Release` 配下の更新を避けられます。

変更に対応するテスト名が分かる場合は、対象テストの成功後に同じビルド成果物で
全テストを実行します。

```powershell
.\tools\test-fast.cmd -Filter "FullyQualifiedName~RecruitScreenRendersEachCandidateOnlyOnce"
```

対象テストだけを繰り返す場合は `-SkipFull`、最初から全テストを実行する場合は
引数なしで起動します。`.cmd` は、このPCのPowerShell実行ポリシーを変更せず、
この1回のプロセスに限って `test-fast.ps1` を実行します。

```powershell
.\tools\test-fast.cmd -Filter "FullyQualifiedName~RecruitScreenRendersEachCandidateOnlyOnce" -SkipFull
.\tools\test-fast.cmd
```

NuGet依存関係を明示的に復元し直す場合は `-Restore` を指定します。

```powershell
.\tools\test-fast.cmd -Restore
```

配布前だけ、ソリューション全体を `Release` でビルドしてからテストします。
`bin/Release` は配布用としてGit管理されているため、生成差分も確認してください。

```powershell
dotnet build GuildSimulator.sln -c Release -m:1 -nr:false
.\tools\test-fast.cmd -Configuration Release
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

### 機能フラグ（凍結中の機能）

作り込んだ機能を消さずに止めておくためのスイッチを `GuildSimulator.Core/GameFeatures.cs` に置いています。
コードもマスタデータも残したまま、フラグ1つで on/off を切り替えます。

| フラグ | 既定値 | 状態 |
| --- | --- | --- |
| `GameFeatures.RelicsEnabled` | `false` | **遺物システムは凍結中** |

#### 遺物システムの凍結について

恒常的な強化（永久バフ）の入手経路を **施設に一本化** し、遺物と施設で二重に管理する
複雑さをなくすために止めています。凍結中の挙動は次の通りです。

- 遺物の効果はすべて無効（加算0・倍率1.0）。所持済みの遺物も効きません
- 宝箱・ボスドロップ・敵ドロップから遺物が出ません。
  遺物エントリを除外したうえで重みを取り直すので、**他の中身の出やすさの比率は変わりません**
- メインメニューから「遺物一覧」が消え、ヘルプの記述からも遺物が外れます
- `relics.json` と遺物を指す `relicId` はそのまま残しています（消すと復活できないため）
- セーブデータの `relicIds` は読み書きを続けるので、凍結前の所持記録は失われません

復活させるときは `GameFeatures.RelicsEnabled` を `true` に戻すだけです。
画面・ヘルプ・ドロップ・効果のすべてが同時に戻り、凍結中に進めたセーブデータでも
記録済みの遺物がそのまま効き始めます。凍結と復活の両方の挙動は
`GuildSimulator.Tests/RelicFreezeTests.cs` で固定しています。

なお、遺物が担っていた強化のうち **ユニットの能力補正・クエスト報酬倍率・維持費倍率** には
まだ対応する施設がありません（`facilities.json` にあるのは掲示枠・商店・休息回復・成長率・
雇入れ・負傷回復まで）。一本化を完了させるには、これらを施設側に用意する必要があります。
