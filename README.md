# ギルドシミュレーター

ギルドを運営し、冒険者を雇い、クエストへ送り出すターン制シミュレーション。

ギルドに抱えられる冒険者は最初5人までで、宿舎系のギルド施設を建てるたびに1人ずつ増え、
最大9人まで在籍させられます。在籍上限に達している間は新しく雇うことができません。
編成上限より2人多く抱えられるのは、遠征でパーティが崩れても控えで立て直せるようにするためです。
枠を空けたいときは、冒険者一覧から解雇できます（費用はかかりませんが、育てた分は戻りません）。

パーティは最初3人まで編成でき、E・C・Aランクで解禁されるギルド施設を建てるたびに
4人、5人、最大6人へ拡張できます。人数上限以内なら、前衛・後衛の6マスへ自由に配置できます。

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
これにより、テストに不要なブラウザ版のビルドを避けられます。

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

### Balance Lab

実際の戦闘・クエスト進行ロジックをseed固定で繰り返し、勝率、撤退率、失敗率、残HP、所要ターン、Gold差などをJSON/CSVへ出力します。

```powershell
.\tools\balance-lab.cmd
.\tools\balance-lab.cmd --runs 10000 --seed 12345
.\tools\balance-lab.cmd --compare outputs\balance-lab\baseline.json
```

育成済みパーティは `partyLevel` / `partyRank` で全員の初期状態を指定できます。
メンバーごとの差や装備を指定するときは `partyIds` の代わりに `party` を使います。

```json
"partyLevel": 5,
"partyRank": 2,
"partyCapacityUpgrades": 1,
"party": [
  {
    "id": "adv_0001",
    "formationSlot": 1,
    "equipment": {
      "RightHand": "eq_sword_02",
      "Body": "eq_leather_01"
    }
  },
  { "id": "adv_0002", "formationSlot": 4, "level": 4, "rank": 2 }
]
```

`formationSlot` は1〜6で、1〜3が前衛、4〜6が後衛です。省略または0なら記述順に空き枠へ配置されます。
`quest` / `campaign` で4人以上を試す場合は、ゲーム内の3段階強化に合わせて
`partyCapacityUpgrades` を1〜3で明示します。生の戦闘比較である `battle` は従来どおり最大6人です。

`type: "campaign"` は同じパーティ・資金・経験値・負傷状態をクエスト間で持ち越します。
`autoRankUp` を有効にすると、昇格条件を満たした冒険者を各クエスト後に自動昇格させます。
結果JSONの `campaignSteps` には各クエストへの到達率、到達者ベースのクリア率、
開始・終了時の平均レベル／ランクが出力されます。

```powershell
.\tools\balance-lab.cmd --config GuildSimulator.Balance\scenarios\f-to-e-campaign.json
```

既定シナリオは `GuildSimulator.Balance/scenarios/default.json`、結果は
`outputs/balance-lab/balance-report.json` と同名CSVです。Excelマスタを再出力すると、結果が「バランスレポート」シートへ取り込まれます。

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
雇入れ・負傷回復・パーティ編成上限・在籍上限まで）。一本化を完了させるには、これらを施設側に用意する必要があります。
