# マスタデータ統合 Excel ツール

`GuildSimulator.Game/Data` の次のJSONを、1つのExcelへ書き出し・読み戻しします。

- `adventurers.json`
- `equipment.json`
- `skills.json`
- `consumables.json`
- `clues.json`
- `quests.json`
- `dungeons.json`

クエストやダンジョンの入れ子配列は、親IDと順序を持つ明細シートへ展開します。

## 実行

```powershell
# JSONからExcelを生成
.\tools\master-data-excel\Run-MasterDataTool.cmd export

# 編集済みExcelを検証（JSONは変更しない）
.\tools\master-data-excel\Run-MasterDataTool.cmd check

# 検証後、6個のJSONへ保存
.\tools\master-data-excel\Run-MasterDataTool.cmd import
```

PowerShellスクリプトの実行が許可されている環境では、同じ引数で
`Run-MasterDataTool.ps1` も利用できます。

Excelは `outputs/master-data-editor/マスタデータ統合_編集用.xlsx` に作成されます。

`import` は、保存前の7ファイルを `outputs/master-data-editor/backups/<timestamp>/` に退避します。
ID重複、必須数値、親子関係、職業・種族・装備・スキル・敵・敵ユニット・レリック・選択イベントの参照を検証し、問題があればJSONを書き換えません。

## 注意

- 主要シートはIDが空の行を無視します。
- 明細シートは親IDが空の行を無視します。
- 配列順は `order` 列で指定します。
- 冒険者の初期スキルは最大6個です。
- `参照マスター` シートは参照専用で、JSONへ書き戻しません。
- このツールはCodexの表計算ランタイムに含まれる `@oai/artifact-tool` を使用します。
