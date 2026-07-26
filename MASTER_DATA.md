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

## 戦闘ダメージ

命中した攻撃のダメージは「武器ダイスを基礎値とし、能力で増幅し、相手の防御を引く」の3層で決まります。

```
増幅率   = clamp(1 + min(攻撃力, 武器の増幅上限) × 0.06 + レベル差 × 0.01, 0.1, 4.0)
素の威力 = floor(武器ダメージダイス × 増幅率)
ダメージ = max(素の威力 - 防御力, 素の威力 × 0.15, 1)
```

- 攻撃力・防御力は、武器の `magicCoeff` が0より大きければ魔攻/魔防、そうでなければ物攻/物防を使います
- 決定的成功（D100が命中率の1/5以下）では武器ダイスをもう1セット振って基礎値に加えます
- 防御は減算ですが、硬い相手に1ダメージしか通らない消耗戦を避けるため、
  素の威力の15%は必ず通ります
- 敵は1レベルごとに能力値が30%伸びるため、終盤の敵の攻撃力は冒険者の域（8〜25程度）を
  大きく超えます。増幅率に4.0の天井があるのは、そこで能力値が武器を追い越さないようにするためです

関係するマスタの項目は次のとおりです。

- `equipment.json` の `damageDice`: 攻撃時に振るダメージダイス（`1d6`、`2d4+1` など）。
  未設定の武器は素手扱いの `1d4` になります
- `equipment.json` の `maxAtkBonus`: 能力で増幅できる上限（攻撃力の単位）。**0なら無制限**。
  短剣や投石のような軽い得物ほど小さくし、斧や大剣は0にして青天井に伸ばします
- `enemies.json` の `naturalDamageDice`: 武器を持たない敵の牙・爪・体当たりのダイス。
  ダメージの基礎値は武器ダイスなので、`defaultWeaponId` が空の敵はここで打撃力を決めます

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
