# CLAUDE.md

ギルド運営×クエスト派遣のターン制シミュレーション（コンソール版 / ブラウザ版PWA）。
詳しいプレイ内容は [README.md](README.md)、マスタデータ仕様は [MASTER_DATA.md](MASTER_DATA.md) を参照。

## 構成

| プロジェクト | 役割 |
| --- | --- |
| `GuildSimulator.Core` | 戦闘・クエスト・ギルド運営などのゲームロジック。画面に依存しない |
| `GuildSimulator.Game` | 画面ロジック（`Screens/`）、マスタデータ（`Data/*.json`）、セーブ処理、ゲームループ |
| `GuildSimulator.Cli` | コンソール版のホスト |
| `GuildSimulator.Web` | ブラウザ版（Blazor WebAssembly）のホスト |
| `GuildSimulator.Balance` | Balance Lab。戦闘・クエストをseed固定で大量シミュレートし勝率などを集計 |
| `GuildSimulator.Tests` | xUnitテスト |
| `tools/master-data-excel` | マスタデータJSONをExcelで編集するツール |

`GuildSimulator.Game` の画面ロジックは `Presentation/IGameIo` を通じて入出力する。
コンソール版は `ConsoleGameIo`、ブラウザ版は `WebGameIo` がこれを実装しており、
画面コードとゲームループは両ホストで共有される。ブラウザ版はゲームループが入力待ちで
`await` するたびに制御がブラウザへ戻り、ボタン押下で待機中のタスクが完了して再開する。

## ビルド・テスト

日常開発は `Debug` でテストプロジェクトだけを実行する（ブラウザ版のビルドを避けるため）。

```powershell
.\tools\test-fast.cmd -Filter "FullyQualifiedName~対象テスト名"
.\tools\test-fast.cmd -Filter "FullyQualifiedName~対象テスト名" -SkipFull  # 対象テストだけ繰り返す
.\tools\test-fast.cmd                                                    # 全テスト
.\tools\test-fast.cmd -Restore                                           # NuGet復元をやり直す
```

配布前のみ `Release` でソリューション全体をビルドしてから確認する。

```bash
dotnet build GuildSimulator.sln -c Release -m:1 -nr:false
```

`bin/`・`obj/` は一切git管理しない（`.gitignore`）。配布物はビルドの都度生成する。

マスタデータの整合性チェック:

```bash
dotnet run --project GuildSimulator.Cli -- --validate-master
```

## マスタデータ

`GuildSimulator.Game/Data/*.json` を直接編集する。冒険者・スキル・職業・装備・敵・消費アイテム・
選択イベント・物語手掛かり・ダンジョン・施設・特性・遺物など。詳細フォーマットは
[MASTER_DATA.md](MASTER_DATA.md) に記載。編集後は上記の `--validate-master` を必ず通すこと。

## 機能フラグ（凍結中の機能）

`GuildSimulator.Core/GameFeatures.cs` に、実装とマスタデータを残したまま機能を丸ごと
止めるためのスイッチがある。

| フラグ | 既定値 | 状態 |
| --- | --- | --- |
| `GameFeatures.RelicsEnabled` | `false` | 遺物システムは凍結中（永久バフの入手経路を施設へ一本化するため） |

凍結中も `relics.json` や `relicId` 参照は消さない（消すと復活できない）。挙動の詳細は
`GuildSimulator.Tests/RelicFreezeTests.cs` と README.md の「機能フラグ」節を参照。

## Balance Lab

戦闘・クエストロジックをseed固定で繰り返し実行し、勝率・撤退率・失敗率・所要ターンなどを
JSON/CSVへ出力する。

```powershell
.\tools\balance-lab.cmd
.\tools\balance-lab.cmd --runs 10000 --seed 12345
.\tools\balance-lab.cmd --compare outputs\balance-lab\baseline.json
```

## CI (`.github/workflows/deploy-web.yml`)

`main` へのpushでのみ、テスト→マスタデータ検証→Balance Labスモークテスト→ブラウザ版ビルド→
GitHub Pagesデプロイが走る。**PR時点ではCIが実行されない**ため、マージ前に上記の
`test-fast.cmd` と `--validate-master` をローカルで通しておくこと。

## ブランチ運用

このリポジトリは機能追加のたびに `claude/<topic>-<id>` ブランチを切って作業する運用が多い。
マージ後のブランチはリポジトリ側の「Automatically delete head branches」設定に従って
自動整理される想定（未設定の場合は手動で削除する）。
