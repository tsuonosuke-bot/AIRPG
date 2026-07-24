# マスタデータ編集

ゲームを起動せず、`GuildSimulator.Cli/Data` 配下のJSONを直接編集できます。

- 冒険者: `adventurers.json`
- 装備とレアリティ: `equipment.json`
- 敵とレアドロップ: `enemies.json`
- 消費アイテム: `consumables.json`
- ターン末選択イベント: `choice_events.json`
- ダンジョンへのイベント割当: `dungeons.json`

レアリティには `Common`、`Uncommon`、`Rare`、`Unique`、`Legend` を指定します。
省略時は装備がCommon、冒険者は採用ウェイトから既定値が設定されます。

編集後はリポジトリのルートで次を実行すると、JSON構文と主要なID参照を検証できます。

```powershell
dotnet run --project GuildSimulator.Cli -- --validate-master
```

検証が成功すると、読み込んだ冒険者、装備、敵、消費アイテム、選択イベントの件数が表示されます。
