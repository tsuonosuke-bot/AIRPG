# マスタデータ統合 Excel ツール

`GuildSimulator.Game/Data` でゲームが読み込む14個のマスタJSONを、1つのExcelへ書き出し・読み戻しします。

- `adventurers.json`
- `classes.json`
- `races.json`
- `equipment.json`
- `skills.json`
- `consumables.json`
- `relics.json`
- `facilities.json`
- `enemies.json`
- `enemy_units.json`
- `choice_events.json`
- `clues.json`
- `quests.json`
- `dungeons.json`

職業スキル、敵ドロップ、選択イベント、クエスト、ダンジョンの入れ子配列は、
親IDと順序を持つ明細シートへ展開します。

schemaVersion 8 では、選択肢の `grantedClueId` / `storyBranchId` / `storyOutcomeText`、
クエスト固定イベントの `choiceEventId`、物語章の `storyArcId` / `storyArcTitle`、
施設の `partySlotBonus` / `rosterSlotBonus` を編集・往復できます。
schemaVersion 7 のブックは列が1つ足りないので、`migrate` で作り直してください。

## 実行

```powershell
# JSONからExcelを生成
.\tools\master-data-excel\Run-MasterDataTool.cmd export

# 編集済みExcelを検証（JSONは変更しない）
.\tools\master-data-excel\Run-MasterDataTool.cmd check

# JSONとの差分を確認（JSONは変更しない）
.\tools\master-data-excel\Run-MasterDataTool.cmd diff

# 検証後、14個のJSONへ保存
.\tools\master-data-excel\Run-MasterDataTool.cmd import

# 旧スキーマのExcelを、編集値を保ったまま現行形式へ移行
.\tools\master-data-excel\Run-MasterDataTool.cmd migrate
```

PowerShellスクリプトの実行が許可されている環境では、同じ引数で
`Run-MasterDataTool.ps1` も利用できます。

Excelは `outputs/master-data-editor/マスタデータ統合_編集用.xlsx` に作成されます。

`import` は、保存前の14ファイルを `outputs/master-data-editor/backups/<timestamp>/` に退避します。
ID重複、必須数値、親子関係、職業・種族・装備・スキル・敵・敵ユニット・レリック・選択イベントの参照を検証し、問題があればJSONを書き換えません。

`migrate` は移行前のExcelを `outputs/master-data-editor/workbook-backups/<timestamp>/` に退避します。
旧ブックにある同名列の編集値を優先し、現行JSONにしかない新規列・新規IDを補完して、現行スキーマへ書き換えます。

ブックの `_meta` シートには `schemaVersion`、生成日時、元JSONのSHA-256指紋が入ります。
`outputs/balance-lab/balance-report.json` がある場合は、「バランスレポート」シートに試行結果、基準差、判定、比較グラフを出力します。

## 注意

- 主要シートはIDが空の行を無視します。
- 明細シートは親IDが空の行を無視します。
- 配列順は `order` 列で指定します。
- 冒険者の初期スキルは最大6個です。
- `参照マスター` シートは参照専用で、JSONへ書き戻しません。
- `_meta` と `バランスレポート` は管理・分析用で、JSONへ書き戻しません。
- このツールはCodexの表計算ランタイムに含まれる `@oai/artifact-tool` を使用します。
