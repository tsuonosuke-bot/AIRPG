# マスタデータ編集

ゲームを起動せず、`GuildSimulator.Game/Data` 配下のJSONを直接編集できます。

- 冒険者: `adventurers.json`
- 装備とレアリティ: `equipment.json`
- 敵とレアドロップ: `enemies.json`
- 消費アイテム: `consumables.json`
- ターン末選択イベント: `choice_events.json`
- 物語の手掛かり: `clues.json`
- ダンジョンへのイベント割当: `dungeons.json`
- ギルド施設: `facilities.json`

冒険者には戦闘能力に加えて、`background`、`personality`、`motivation`、
`specialty`、`fear`、`creed`、`selfIntroduction` を設定できます。

物語クエストでは `isStoryQuest` を `true` にし、次のIDリストで進行順を定義します。

- `requiredQuestIds`: 掲示に必要な完了済みクエスト
- `requiredClueIds`: 掲示に必要な発見済み手掛かり
- `grantedClueIds`: 正規クリア時に発見する手掛かり

## ギルド施設 (`facilities.json`)

建設するとゴールドを消費し、以後は `upkeepGoldPerTurn` が毎ターンの維持費に加算される。
効果は建設した施設すべての合計値として常時適用される（`requiredGuildRank` はギルドランクによる建設条件）。

- `questBoardBonus`: クエスト掲示板の通常枠を増やす数
- `shopLevelBonus`: 商店の品揃えレベルを増やす数（`equipment.json` の `shopTier` 以下の装備が商店に並ぶようになる。基準の商店レベルは1）
- `restHealBonusPercent`: 遠征中の休息回復量を増やす割合（%）
- `growthRateBonusPercent`: 冒険者のレベルアップ成長率を増やす割合（%）。1%単位で調整する想定

レアリティには `Common`、`Uncommon`、`Rare`、`Unique`、`Legend` を指定します。
省略時は装備がCommon、冒険者は採用ウェイトから既定値が設定されます。

編集後はリポジトリのルートで次を実行すると、JSON構文と主要なID参照を検証できます。

```powershell
dotnet run --project GuildSimulator.Cli -- --validate-master
```

検証が成功すると、読み込んだ冒険者、装備、敵、消費アイテム、選択イベントの件数が表示されます。
