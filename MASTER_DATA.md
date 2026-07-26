# マスタデータ編集

ゲームを起動せず、`GuildSimulator.Game/Data` 配下のJSONを直接編集できます。

- 冒険者: `adventurers.json`
- 装備とレアリティ: `equipment.json`
- 敵とレアドロップ: `enemies.json`
- 消費アイテム: `consumables.json`
- ターン末選択イベント: `choice_events.json`
- 物語の手掛かり: `clues.json`
- ダンジョンへのイベント割当: `dungeons.json`

冒険者には戦闘能力に加えて、`background`、`personality`、`motivation`、
`specialty`、`fear`、`creed`、`selfIntroduction` を設定できます。

物語クエストでは `isStoryQuest` を `true` にし、次のIDリストで進行順を定義します。

- `requiredQuestIds`: 掲示に必要な完了済みクエスト
- `requiredClueIds`: 掲示に必要な発見済み手掛かり
- `grantedClueIds`: 正規クリア時に発見する手掛かり

アイテムは宝箱で手に入ります。道中では未開封のまま持ち運び、**帰還後に開けて中身を抽選します**
（全滅すると開ける前に失います）。宝箱は2種類です。

- 道中の宝箱: 中身は `dungeons.json` の `treasureTable` から1件。`weight` で出やすさを決めます。
  このダンジョンで拾えるアイテムはすべてここに書きます。**2割の確率で空っぽ**になります
  （`QuestRewardService.EmptyChestRate`）。所持済みの遺物は抽選対象から外れます。
  入手経路は道中の宝箱イベントと、`choice_events.json` の選択肢に
  `"effectType": "Treasure"` を置いたもの（`value` 個ぶん渡す）です。
- ボスの宝箱: ボス撃破で手に入り、中身は `quests.json` の `bossDrops`。
  エントリごとに `chance`（0より大きく1以下）で抽選し、**空っぽ抽選は受けません**。
  物語進行に必要で落とすと詰むものは、クエスト側の `bossDropsAreGuaranteed` を `true` にすると
  `chance` も無視して全て確定します。

宝箱以外に、敵のレアドロップ（`enemies.json` の `dropTable`）と選択イベントの拾い物は
中身が分かっている戦利品としてそのまま持ち帰ります。

採取クエスト（`gatherTargetCount` > 0）の採取判定は、ダンジョンの `eventTable` とは
**別枠**です。フェーズごとに `gatherChance` で判定し、当たれば同じフェーズの戦闘や宝箱と
並行して採取が進みます（イベントを置き換えません）。目標数に届いた時点で判定は止まります。

レアリティには `Common`、`Uncommon`、`Rare`、`Unique`、`Legend` を指定します。
省略時は装備がCommon、冒険者は採用ウェイトから既定値が設定されます。

編集後はリポジトリのルートで次を実行すると、JSON構文と主要なID参照を検証できます。

```powershell
dotnet run --project GuildSimulator.Cli -- --validate-master
```

検証が成功すると、読み込んだ冒険者、装備、敵、消費アイテム、選択イベントの件数が表示されます。
