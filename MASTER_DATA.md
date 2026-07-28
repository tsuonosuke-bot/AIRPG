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

戦闘の解決は **Caves of Qud** の方式です。攻撃は「命中判定」と「貫通判定」の二段構えで、
ダメージの大きさはダメージダイスそのものではなく **何回貫通したか** で決まります。

```
1. 命中判定
   1d20 + 命中補正 > 相手のDV  なら命中
   DV = 6 + 敏捷modifier + 装備・スキル由来のDV補正

2. 貫通判定
   PV = 武器の基礎PV + min(筋力modifier, 武器ごとの上限) + 装備・スキル由来のPV補正
        （魔法は 知力modifier を使う）
   AV = 体格modifier + 装備・スキル由来のAV補正
        （魔法は mAV。精神modifier + mAV補正）

   (1d10-2) + PV > AV  の試行を3回で1セット
     ├ 1回も上回らない → 装甲に弾かれた（ダメージ0）
     ├ 1〜2回上回った  → 1貫通してそこで終了
     └ 3回とも上回った → 1貫通し、PVを2下げて次のセットへ（以降くり返し）

3. ダメージ
   貫通回数ぶんだけ武器のダメージダイスを振って合計する
```

- **能力値は直接ダメージに乗りません。** 筋力（魔法は知力）はPVに、敏捷はDVと命中に、
  体格（魔法は精神）はAVに変わります。ダメージを増やすのは貫通回数と武器ダイスだけです
- 能力値modifierは `(能力値 - 8) / 2` の切り捨てです。Qudの `(能力値-16)/2` を、
  本作の能力値レンジ（Lv1で4〜14）に合わせて基準点を8に縮尺したものです
- 貫通ダイスは `1d10-2`（-1〜8）で、**最大の出目が出るたびに振り足して加算します**（爆発ダイス）。
  上振れが青天井なので、AVが高くても薄い可能性で貫通の目が残ります
- 貫通は1セットごとにPVが2ずつ減っていくため、どれだけ格上でも必ず有限回で止まります
- 物理か魔法かは能力値の大小ではなく武器で決まります。武器の `attackKind` が `Magic` なら魔法です
- 会心（1d20の**素の出目**が20）はDVに関わらず命中し、PVが+1され、
  1回も抜けなかった場合でも最低1貫通は通ります
- 貫通できなければダメージは0です。最低保証はありません。AVへの投資がそのまま被弾の遮断になります

関係するマスタの項目は次のとおりです。

- `equipment.json` の `attackKind`: `Physical` / `Magic` / `Heal`。武器が何を撃つかを決めます
- `equipment.json` の `damageDice`: 貫通1回につき振るダメージダイス（`1d6`、`2d4+1` など）。
  未設定の武器は素手扱いの `1d2` になります
- `equipment.json` の `basePv`: 武器そのものの貫通値。素材の等級がそのままPVの差になります（標準は4）
- `equipment.json` の `maxStatBonus`: PVに上乗せできる能力値modifier（筋力／知力）の上限。
  軽い得物ほど小さく、重い得物ほど大きくして「短剣に腕力を乗せきれない」を表現します。
  上限は武器クラスごとの固定値とし、Tierでは変えません（Tier差は `basePv` とダイスが表現します）。
  最軽量（短剣・投石・風）5／標準（剣・弓・水）6／長柄（槍・地）7／重量級（斧・火・闇）8。
  クラス間を1点刻みにすると、主能力がどれだけ伸びてもクラス間の差は最大3点（＝PV3点）で頭打ちになります。
  重い得物を無制限にすると主能力が伸びるほど差が青天井に開くため、重量級にも上限を置いています。
  なお上限を超えた主能力は**行き場がありません**。能力値がダメージに直接乗らない方式なので、
  伸ばしすぎた筋力を受け止める先（旧方式のダメージ・ボーナス）は存在しません。
  回復魔法は貫通判定を通らないため、光属性の `maxStatBonus` と `damageDice` は使われません
- `equipment.json` 防具の `bonus`: `av` / `mav` / `dv` で表します。重い鎧ほどAVが高く、DVを削ります
- `equipment.json` の `armorType`: `Cloth`(0) / `LightArmor`(1) / `Plate`(2) / `Null`(3)。
  スキルの `requiredArmorType` はこれと突き合わせます。**列挙順がJSONの数値そのものなので並べ替え禁止**です
- `equipment.json` の `allowedSlots`: 装備できるスロット（`RightHand` / `LeftHand` / `Head` / `Body` / `Accessory`）。
  **未指定の防具はすべて `Body` 扱いになる**ため、頭防具には `["Head"]` の明示が必須です
- `enemies.json` の `naturalDamageDice`: 武器を持たない敵の牙・爪・体当たりのダイス
- `enemies.json` の `naturalPv`: 素手の敵の牙・爪そのもののPV。武器持ちの敵は武器の `basePv` が優先されます
- `enemies.json` の `naturalAv` / `naturalMav`: 甲殻・毛皮など、防具を着ていなくても持っている装甲。
  防具の上にも常に加算されます（鱗の上に鎧を着ている、の表現）

なお **AV / DV / PV は1点の重みが大きい小さな整数** なので、スキルや遺物の効果はすべて
加算（`add`）で表します。倍率（`mul`）が効くのは HP・士気(san)・回復量(heal) だけです。

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
