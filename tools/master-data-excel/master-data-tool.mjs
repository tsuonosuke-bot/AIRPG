import fs from "node:fs/promises";
import path from "node:path";
import crypto from "node:crypto";
import { FileBlob, SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const command = process.argv[2] ?? "export";
const workbookSchemaVersion = 8;
const scriptDir = path.dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, "$1");
const repoRoot = path.resolve(scriptDir, "../..");
const dataDir = path.join(repoRoot, "GuildSimulator.Game", "Data");
// 編集中の実ブックを上書きせず検証できるよう、出力先だけ任意に差し替えられる。
const outputDir = process.env.AIRPG_MASTER_OUTPUT_DIR
  ? path.resolve(process.env.AIRPG_MASTER_OUTPUT_DIR)
  : path.join(repoRoot, "outputs", "master-data-editor");
const workbookPath = path.join(outputDir, "マスタデータ統合_編集用.xlsx");
const previewDir = path.join(outputDir, "previews");
const balanceReportPath = path.join(repoRoot, "outputs", "balance-lab", "balance-report.json");
const masterFiles = [
  "skills", "classes", "races", "equipment", "consumables", "relics", "facilities",
  "enemies", "choice_events", "enemy_units", "dungeons", "clues", "quests", "adventurers",
];
const statKeys = [
  "hp", "san", "av", "mav", "pv", "mpv", "dv", "toHit", "heal",
  // 武器クラスの個性。スキル・遺物・装備の補正として足せる。
  "armorPierce", "armorShred", "critRange", "extraAttacks",
  // 二刀流の発動率と盾の受け率。スキルから伸ばせる。
  "offHandChance", "blockChance",
  // 最新版で追加された戦闘・積載・ヘイト系の補正。
  "blockNegate", "carry", "threatWeight", "autoPenetrate", "critPv", "emergencyHeal",
];
// 武器そのものが持つ個性の列。武器クラスごとに固定で、Tierでは変えない。
const weaponTraitKeys = ["armorPierce", "armorShred", "critRange", "extraAttacks", "offHandBonus"];
// 両手武器と盾。盾の装甲は blockAv にだけ書く（bonus_av に書くと常時加算になる）。
const handKeys = ["isTwoHanded", "blockChance", "blockAv"];
// AV/DV/PVは1点が重いので倍率では触らない。mul列はこの3つだけを扱う。
const mulKeys = ["hp", "san", "heal"];
const expeditionKeys = [
  "goldPercent", "expPercent", "treasureChancePercent", "trapChancePercent",
  "enemyEncounterChancePercent", "healEventChancePercent", "restHealPercent",
  "enemyDropChancePercent", "rareDropChancePercent", "phasesPerTurnBonus",
];
const battleSkillKeys = [
  "protectAllyHpPercent", "protectChancePercent", "afflictedTargetPv",
  "cleanseOnHealChancePercent", "moraleOnHealPercent",
  "lowHpThresholdPercent", "lowHpPv", "counterChancePercent",
];
const rewardKeys = [
  "type", "relicId", "equipmentId", "skillId", "consumableId",
  "gold", "weight", "chance", "quantity", "unique", "minQuestRank", "maxQuestRank",
];

const readJson = async (name) =>
  JSON.parse(await fs.readFile(path.join(dataDir, `${name}.json`), "utf8"));

const columnName = (number) => {
  let n = number;
  let result = "";
  while (n > 0) {
    n -= 1;
    result = String.fromCharCode(65 + (n % 26)) + result;
    n = Math.floor(n / 26);
  }
  return result;
};
const columnNumber = (name) =>
  [...name].reduce((result, character) => result * 26 + character.charCodeAt(0) - 64, 0);

const clean = (value) => (value === undefined || value === null ? null : value);
const mapValue = (object, key) => clean(object?.[key]);
const flattenReward = (parentId, order, reward) => [
  parentId,
  order,
  ...rewardKeys.map((key) => mapValue(reward, key)),
];
const flattenStatuses = (ownerId, owner) => [
  ...(owner.battleStartStatuses ?? []).map((status, index) => [
    ownerId, "battleStart", index + 1, status.type, status.target,
    clean(status.chancePercent), clean(status.durationRounds), clean(status.potency),
  ]),
  ...(owner.onHitStatuses ?? []).map((status, index) => [
    ownerId, "onHit", index + 1, status.type, status.target,
    clean(status.chancePercent), clean(status.durationRounds), clean(status.potency),
  ]),
];

const colors = {
  navy: "#17324D",
  blue: "#245B82",
  paleBlue: "#EAF3F8",
  paleGold: "#FFF4D6",
  paleGreen: "#E9F6EE",
  paleRed: "#FDECEC",
  lightGray: "#D9E2E8",
  white: "#FFFFFF",
};
const bodyFont = "Yu Gothic";

const sheetDefinitions = {
  adventurers: {
    name: "冒険者",
    title: "冒険者マスタ",
    capacity: 120,
    unique: true,
    keys: [
      "id", "baseName", "defaultLevel", "defaultRank",
      "recruitGuildRank", "recruitWeight", "rarity",
      "vitality", "mental", "strength", "agility", "intelligence", "constitution", "appearance",
      "gender",
      "defaultClassId", "raceId", "defaultWeaponId", "defaultArmorId",
      "skillId1", "skillId2", "skillId3", "skillId4", "skillId5", "skillId6",
      "background", "personality", "motivation", "specialty", "fear", "creed", "selfIntroduction",
    ],
    labels: [
      "ID", "名前", "初期Lv", "初期ランク(1=F〜7=S)",
      "採用ギルドランク(1=F〜7=S)", "採用重み", "レアリティ",
      "生命力", "精神力", "筋力", "敏捷", "知力", "体格", "容姿",
      "性別",
      "初期職業", "種族", "初期武器", "初期防具",
      "スキル1", "スキル2", "スキル3", "スキル4", "スキル5", "スキル6",
      "経歴", "性格", "動機", "得意分野", "苦手・恐怖", "信条", "自己紹介",
    ],
  },
  classes: {
    name: "職業",
    title: "職業マスタ",
    capacity: 60,
    unique: true,
    keys: ["id", "className", "vitGrowth", "mentGrowth", "strGrowth", "intGrowth", "agiGrowth"],
    labels: ["ID", "職業名", "生命成長", "精神成長", "筋力成長", "知力成長", "敏捷成長"],
  },
  classSkills: {
    name: "職業スキル",
    title: "職業 スキル解禁明細",
    capacity: 300,
    unique: false,
    keys: ["classId", "order", "skillId", "requiredClearCount"],
    labels: ["職業ID", "順序", "スキルID", "必要適正クエストクリア数"],
  },
  races: {
    name: "種族",
    title: "種族マスタ",
    capacity: 60,
    unique: true,
    keys: ["id", "raceName", "vitGrowth", "mentGrowth", "strGrowth", "intGrowth", "agiGrowth", "allowedClassIds"],
    labels: ["ID", "種族名", "生命成長", "精神成長", "筋力成長", "知力成長", "敏捷成長", "就業可能職業ID（カンマ区切り）"],
  },
  equipment: {
    name: "装備",
    title: "装備マスタ",
    capacity: 120,
    unique: true,
    keys: [
      "id", "displayName", "rarity", "type", "weaponType", "armorType", "allowedSlots",
      "attackKind", "damageDice", "basePv", "maxStatBonus",
      ...weaponTraitKeys,
      ...handKeys,
      "healPower", "flatHeal", "price", "weight", "shopTier",
      ...statKeys.map((key) => `bonus_${key}`),
    ],
    labels: [
      "ID", "表示名", "レアリティ", "装備種別", "武器種", "防具種", "装備スロット",
      "攻撃種別", "ダメージダイス", "武器PV", "能力値上限",
      "装甲貫通", "装甲破壊", "会心域", "連撃", "左手ボーナス%",
      "両手武器", "受け率%", "受け成功時AV",
      "回復係数", "固定回復", "価格", "重量", "商店Tier",
      ...statKeys.map((key) => `補正 ${key}`),
    ],
  },
  equipmentStatuses: {
    name: "装備状態効果",
    title: "装備 状態効果明細",
    capacity: 360,
    unique: false,
    keys: ["equipmentId", "trigger", "order", "type", "target", "chancePercent", "durationRounds", "potency"],
    labels: ["装備ID", "発動契機", "順序", "状態種別", "対象", "確率%", "持続ラウンド", "強度"],
  },
  skills: {
    name: "スキル",
    title: "スキルマスタ",
    capacity: 240,
    unique: true,
    keys: [
      "id", "skillName", "family", "level", "scope", "frontOnly", "backOnly",
      "requireWeaponType", "requiredWeaponType", "requireArmorType", "requiredArmorType",
      "requireUnarmed", "requireTwoHanded", "requireShield", "requireOffHandWeapon",
      "requirePhysicalWeapon", "unarmedDamageDice",
      ...statKeys.map((key) => `add_${key}`),
      ...mulKeys.map((key) => `mul_${key}`),
      ...expeditionKeys.map((key) => `expedition_${key}`),
      ...battleSkillKeys.map((key) => `battle_${key}`),
    ],
    labels: [
      "ID", "スキル名", "系統", "段階", "範囲", "前衛限定", "後衛限定",
      "武器条件あり", "必要武器種", "防具条件あり", "必要防具種",
      "素手必須", "両手武器必須", "盾必須", "左手武器必須", "物理武器必須", "素手ダメージ",
      ...statKeys.map((key) => `加算 ${key}`),
      ...mulKeys.map((key) => `倍率 ${key}`),
      ...expeditionKeys.map((key) => `遠征 ${key}`),
      ...battleSkillKeys.map((key) => `戦闘条件 ${key}`),
    ],
  },
  skillStatuses: {
    name: "スキル状態効果",
    title: "スキル 状態効果明細",
    capacity: 720,
    unique: false,
    keys: ["skillId", "trigger", "order", "type", "target", "chancePercent", "durationRounds", "potency"],
    labels: ["スキルID", "発動契機", "順序", "状態種別", "対象", "確率%", "持続ラウンド", "強度"],
  },
  consumables: {
    name: "道具",
    title: "消費アイテムマスタ",
    capacity: 100,
    unique: true,
    keys: [
      "id", "displayName", "description", "rarity", "price", "effectType",
      "effectValue", "secondaryEffectValue",
    ],
    labels: ["ID", "表示名", "説明", "レアリティ", "価格", "効果種別", "効果値", "副効果値"],
  },
  clues: {
    name: "手掛かり",
    title: "物語の手掛かりマスタ",
    capacity: 120,
    unique: true,
    keys: ["id", "title", "description"],
    labels: ["ID", "名称", "説明"],
  },
  relics: {
    name: "レリック",
    title: "レリックマスタ",
    capacity: 120,
    unique: true,
    keys: [
      "id", "relicName", "description", "effectType", "rate",
      ...statKeys.map((key) => `add_${key}`),
      ...mulKeys.map((key) => `mul_${key}`),
    ],
    labels: [
      "ID", "名称", "説明", "効果種別", "倍率",
      ...statKeys.map((key) => `加算 ${key}`),
      ...mulKeys.map((key) => `倍率 ${key}`),
    ],
  },
  facilities: {
    name: "施設",
    title: "ギルド施設マスタ",
    capacity: 80,
    unique: true,
    keys: [
      "id", "displayName", "description", "buildCostGold", "upkeepGoldPerTurn",
      "requiredGuildRank", "questBoardBonus", "partySlotBonus", "rosterSlotBonus", "shopLevelBonus", "restHealBonusPercent",
      "growthRateBonusPercent", "noviceQuestBoardBonus", "recruitMinBonus", "injuryRecoveryBonus",
      "fatalityReductionPercent", "scarPreventionPercent",
    ],
    labels: [
      "ID", "施設名", "説明", "建設費", "毎ターン維持費", "必要ギルドランク",
      "掲示板枠加算", "パーティ編成上限加算", "在籍上限加算", "商店Lv加算", "休息回復%", "成長率%", "新人向けF依頼枠", "最低採用候補加算",
      "負傷回復加算", "死亡率軽減%", "傷痕予防%",
    ],
  },
  enemies: {
    name: "敵",
    title: "敵マスタ",
    capacity: 180,
    unique: true,
    keys: [
      "id", "baseName", "description", "exp", "threat", "vitality", "mental", "strength", "agility",
      "intelligence", "constitution", "defaultWeaponId", "defaultArmorId", "defaultOffHandId",
      "defaultShieldId", "naturalDamageDice", "naturalPv", "naturalAv", "naturalMav",
      "naturalAttackKind", "skillIds",
    ],
    labels: [
      "ID", "名称", "生態・外見", "経験値", "脅威ランク", "生命力", "精神力", "筋力", "敏捷", "知力", "体格",
      "右手装備", "防具", "左手武器", "盾", "素のダメージ", "素のPV", "素のAV", "素のMAV",
      "素の攻撃種別（Physical/Magic）", "スキルID（カンマ区切り）",
    ],
  },
  enemyDrops: {
    name: "敵ドロップ",
    title: "敵 ドロップ明細",
    capacity: 400,
    unique: false,
    keys: ["enemyId", "order", ...rewardKeys],
    labels: [
      "敵ID", "順序", "報酬種別", "レリックID", "装備ID", "スキルID", "道具ID",
      "Gold", "重み", "確率", "数量", "ユニーク", "最小依頼ランク", "最大依頼ランク",
    ],
  },
  choiceEvents: {
    name: "選択イベント",
    title: "選択イベントマスタ",
    capacity: 120,
    unique: true,
    keys: ["id", "title", "description", "weight"],
    labels: ["ID", "名称", "説明", "重み"],
  },
  choiceOptions: {
    name: "選択肢",
    title: "選択イベント 選択肢明細",
    capacity: 360,
    unique: false,
    keys: [
      "eventId", "order", "text", "resultText", "effectType", "value", "targetId", "targetsOneMember",
      "grantedClueId", "storyBranchId", "storyOutcomeText",
    ],
    labels: [
      "イベントID", "順序", "選択肢", "結果文", "効果種別", "値", "対象ID", "対象を1人選ぶ",
      "獲得手掛かりID", "物語分岐ID", "物語の余波",
    ],
  },
  choiceOutcomes: {
    name: "選択結果",
    title: "選択肢 重み付き結果明細",
    capacity: 600,
    unique: false,
    keys: ["eventId", "optionOrder", "order", "weight", "effectType", "value", "targetId", "resultText"],
    labels: ["イベントID", "選択肢順序", "結果順序", "重み", "効果種別", "値", "対象ID", "結果文"],
  },
  enemyUnits: {
    name: "敵ユニット",
    title: "敵ユニットマスタ",
    capacity: 180,
    unique: true,
    keys: ["id", "unitName", "formationId1", "formationId2", "formationId3", "formationId4", "formationId5", "formationId6"],
    labels: ["ID", "名称", "配置1", "配置2", "配置3", "配置4", "配置5", "配置6"],
  },
  quests: {
    name: "クエスト",
    title: "クエストマスタ",
    capacity: 120,
    unique: true,
    keys: [
      "id", "questName", "clientName", "description", "isStoryQuest", "storyArcId", "storyArcTitle",
      "requiredQuestIds", "requiredClueIds", "grantedClueIds", "storyBranchId",
      "rank", "totalPhases", "phasesPerTurn",
      "rewardGold", "rewardGuildPoints", "rewardExp",
      "isEmergencyQuest", "rankUpOnClear", "requiredGuildPoints",
      "dungeonId", "bossEnemyId", "bossPhase", "bossDropsAreGuaranteed",
      "gatherItemName", "gatherTargetCount", "gatherMinPerEvent", "gatherMaxPerEvent",
      "gatherChance", "gatherGoldPerItem",
    ],
    labels: [
      "ID", "クエスト名", "依頼人", "依頼文", "物語クエスト", "物語章ID", "物語章名",
      "必要クエストID（カンマ区切り）", "必要手掛かりID（カンマ区切り）",
      "獲得手掛かりID（カンマ区切り）", "分岐ID",
      "ランク(1=F〜7=S)", "総エリア", "ターン毎エリア",
      "報酬Gold", "Guildポイント", "経験値",
      "緊急クエスト", "クリア時RankUp", "必要Guildポイント",
      "ダンジョン", "ボス敵", "ボスエリア", "ボス報酬確定",
      "採取物名", "採取目標", "採取最小", "採取最大", "採取確率", "採取単価",
    ],
  },
  questRewards: {
    name: "クエスト報酬",
    title: "クエスト ボス報酬明細",
    capacity: 240,
    unique: false,
    keys: ["questId", "order", ...rewardKeys],
    labels: [
      "クエストID", "順序", "報酬種別", "レリックID", "装備ID", "スキルID",
      "道具ID", "Gold", "重み", "確率", "数量", "ユニーク", "最小依頼ランク", "最大依頼ランク",
    ],
  },
  questEvents: {
    name: "クエスト固定イベント",
    title: "クエスト 固定イベント明細",
    capacity: 240,
    unique: false,
    keys: ["questId", "order", "phase", "type", "choiceEventId"],
    labels: ["クエストID", "順序", "エリア", "イベント種別", "選択イベントID"],
  },
  dungeons: {
    name: "ダンジョン",
    title: "ダンジョンマスタ",
    capacity: 100,
    unique: true,
    keys: ["id", "dungeonName", "turnEndEventChance"],
    labels: ["ID", "ダンジョン名", "終了イベント確率"],
  },
  dungeonEvents: {
    name: "ダンジョンイベント",
    title: "ダンジョン イベント重み明細",
    capacity: 240,
    unique: false,
    keys: ["dungeonId", "order", "eventType", "weight"],
    labels: ["ダンジョンID", "順序", "イベント種別", "重み"],
  },
  dungeonEncounters: {
    name: "ダンジョン遭遇",
    title: "ダンジョン 遭遇テーブル明細",
    capacity: 300,
    unique: false,
    keys: ["dungeonId", "order", "unitId", "weight", "minPhase", "maxPhase"],
    labels: ["ダンジョンID", "順序", "敵ユニットID", "重み", "最小エリア", "最大エリア"],
  },
  dungeonRewards: {
    name: "ダンジョン報酬",
    title: "ダンジョン 宝箱明細",
    capacity: 360,
    unique: false,
    keys: ["dungeonId", "order", ...rewardKeys],
    labels: [
      "ダンジョンID", "順序", "報酬種別", "レリックID", "装備ID",
      "スキルID", "道具ID", "Gold", "重み", "確率", "数量", "ユニーク", "最小依頼ランク", "最大依頼ランク",
    ],
  },
  dungeonTurnEvents: {
    name: "ダンジョン終了イベント",
    title: "ダンジョン ターン終了イベント明細",
    capacity: 240,
    unique: false,
    keys: ["dungeonId", "order", "eventId"],
    labels: ["ダンジョンID", "順序", "選択イベントID"],
  },
};

const makeRows = (data) => {
  const adventurers = data.adventurers.map((a) => [
    a.id, a.baseName, a.defaultLevel, a.defaultRank,
    clean(a.recruitGuildRank), clean(a.recruitWeight), clean(a.rarity),
    a.vitality, a.mental, a.strength, a.agility, a.intelligence, a.constitution, a.appearance,
    clean(a.gender),
    clean(a.defaultClassId), clean(a.raceId), clean(a.defaultWeaponId), clean(a.defaultArmorId),
    ...Array.from({ length: 6 }, (_, index) => clean(a.skillIds?.[index])),
    clean(a.background), clean(a.personality), clean(a.motivation), clean(a.specialty),
    clean(a.fear), clean(a.creed), clean(a.selfIntroduction),
  ]);
  for (const a of data.adventurers) {
    if ((a.skillIds?.length ?? 0) > 6) throw new Error(`${a.id}: スキル数が6件を超えています。`);
  }

  const classes = data.classes.map((c) => [
    c.id, c.className, c.vitGrowth, c.mentGrowth, c.strGrowth, c.intGrowth, c.agiGrowth,
  ]);
  const classSkills = data.classes.flatMap((c) =>
    (c.classSkills ?? []).map((entry, index) => [
      c.id, index + 1, entry.skillId, entry.requiredClearCount,
    ]));

  const races = data.races.map((r) => [
    r.id, r.raceName, r.vitGrowth, r.mentGrowth, r.strGrowth, r.intGrowth, r.agiGrowth,
    clean(r.allowedClassIds?.join(", ")),
  ]);

  const equipment = data.equipment.map((e) => [
    e.id, e.displayName, clean(e.rarity), e.type, e.weaponType, e.armorType,
    clean(e.allowedSlots?.length ? e.allowedSlots.join(",") : null),
    clean(e.attackKind), clean(e.damageDice), clean(e.basePv), clean(e.maxStatBonus),
    ...weaponTraitKeys.map((key) => clean(e[key])),
    Boolean(e.isTwoHanded), clean(e.blockChance), clean(e.blockAv),
    clean(e.healPower), clean(e.flatHeal),
    e.price, e.weight, clean(e.shopTier),
    ...statKeys.map((key) => mapValue(e.bonus, key)),
  ]);
  const equipmentStatuses = data.equipment.flatMap((e) => flattenStatuses(e.id, e));

  const skills = data.skills.map((s) => [
    s.id, s.skillName, clean(s.family), clean(s.level), s.scope, Boolean(s.frontOnly), Boolean(s.backOnly),
    Boolean(s.requireWeaponType), clean(s.requiredWeaponType),
    Boolean(s.requireArmorType), clean(s.requiredArmorType),
    Boolean(s.requireUnarmed), Boolean(s.requireTwoHanded), Boolean(s.requireShield),
    Boolean(s.requireOffHandWeapon), Boolean(s.requirePhysicalWeapon), clean(s.unarmedDamageDice),
    ...statKeys.map((key) => mapValue(s.add, key)),
    ...mulKeys.map((key) => mapValue(s.mul, key)),
    ...expeditionKeys.map((key) => mapValue(s.expedition, key)),
    ...battleSkillKeys.map((key) => mapValue(s.battle, key)),
  ]);
  const skillStatuses = data.skills.flatMap((s) => flattenStatuses(s.id, s));

  const consumables = data.consumables.map((c) => [
    c.id, c.displayName, clean(c.description), clean(c.rarity),
    c.price, c.effectType, c.effectValue, clean(c.secondaryEffectValue),
  ]);

  const clues = data.clues.map((clue) => [
    clue.id, clue.title, clean(clue.description),
  ]);

  const relics = data.relics.map((r) => [
    r.id, r.relicName, clean(r.description), r.effectType, r.rate,
    ...statKeys.map((key) => mapValue(r.add, key)),
    ...mulKeys.map((key) => mapValue(r.mul, key)),
  ]);

  const facilities = data.facilities.map((f) => [
    f.id, f.displayName, clean(f.description), f.buildCostGold, f.upkeepGoldPerTurn,
    f.requiredGuildRank, f.questBoardBonus, clean(f.partySlotBonus), clean(f.rosterSlotBonus),
    f.shopLevelBonus, f.restHealBonusPercent,
    f.growthRateBonusPercent, clean(f.noviceQuestBoardBonus), clean(f.recruitMinBonus), clean(f.injuryRecoveryBonus),
    clean(f.fatalityReductionPercent), clean(f.scarPreventionPercent),
  ]);

  const enemies = data.enemies.map((e) => [
    e.id, e.baseName, clean(e.description), e.exp, e.threat, e.vitality, e.mental, e.strength, e.agility,
    e.intelligence, e.constitution, clean(e.defaultWeaponId), clean(e.defaultArmorId),
    clean(e.defaultOffHandId), clean(e.defaultShieldId), clean(e.naturalDamageDice),
    clean(e.naturalPv), clean(e.naturalAv), clean(e.naturalMav), clean(e.naturalAttackKind),
    clean(e.skillIds?.join(", ")),
  ]);
  const enemyDrops = data.enemies.flatMap((e) =>
    (e.dropTable ?? []).map((reward, index) => flattenReward(e.id, index + 1, reward)));

  const choiceEvents = data.choice_events.map((event) => [
    event.id, event.title, clean(event.description), event.weight,
  ]);
  const choiceOptions = data.choice_events.flatMap((event) =>
    (event.options ?? []).map((option, index) => [
      event.id, index + 1, option.text, clean(option.resultText), option.effectType,
      option.value, clean(option.targetId), Boolean(option.targetsOneMember),
      clean(option.grantedClueId), clean(option.storyBranchId), clean(option.storyOutcomeText),
    ]));
  const choiceOutcomes = data.choice_events.flatMap((event) =>
    (event.options ?? []).flatMap((option, optionIndex) =>
      (option.outcomes ?? []).map((outcome, outcomeIndex) => [
        event.id, optionIndex + 1, outcomeIndex + 1, outcome.weight, outcome.effectType,
        outcome.value, clean(outcome.targetId), clean(outcome.resultText),
      ])));

  const enemyUnits = data.enemy_units.map((unit) => [
    unit.id, unit.unitName,
    ...Array.from({ length: 6 }, (_, index) => clean(unit.formationIds?.[index])),
  ]);

  const quests = data.quests.map((q) => [
    q.id, q.questName, clean(q.clientName), clean(q.description), clean(q.isStoryQuest),
    clean(q.storyArcId), clean(q.storyArcTitle),
    clean(q.requiredQuestIds?.join(", ")), clean(q.requiredClueIds?.join(", ")),
    clean(q.grantedClueIds?.join(", ")), clean(q.storyBranchId),
    q.rank, q.totalPhases, q.phasesPerTurn,
    q.rewardGold, q.rewardGuildPoints, q.rewardExp,
    clean(q.isEmergencyQuest), clean(q.rankUpOnClear), clean(q.requiredGuildPoints),
    clean(q.dungeonId), clean(q.bossEnemyId), clean(q.bossPhase), clean(q.bossDropsAreGuaranteed),
    clean(q.gatherItemName), clean(q.gatherTargetCount), clean(q.gatherMinPerEvent), clean(q.gatherMaxPerEvent),
    clean(q.gatherChance), clean(q.gatherGoldPerItem),
  ]);
  const questRewards = data.quests.flatMap((q) =>
    (q.bossDrops ?? []).map((reward, index) => [q.id, index + 1, ...rewardKeys.map((key) => mapValue(reward, key))]));
  const questEvents = data.quests.flatMap((q) =>
    (q.fixedEvents ?? []).map((event, index) => [
      q.id, index + 1, event.phase, event.type, clean(event.choiceEventId),
    ]));

  const dungeons = data.dungeons.map((d) => [
    d.id, d.dungeonName, clean(d.turnEndEventChance),
  ]);
  const dungeonEvents = data.dungeons.flatMap((d) =>
    Object.entries(d.eventTable ?? {}).map(([eventType, weight], index) => [d.id, index + 1, eventType, weight]));
  const dungeonEncounters = data.dungeons.flatMap((d) =>
    (d.encounterTable ?? []).map((entry, index) => [
      d.id, index + 1, entry.unitId, entry.weight, entry.minPhase, entry.maxPhase,
    ]));
  const dungeonRewards = data.dungeons.flatMap((d) =>
    (d.treasureTable ?? []).map((reward, index) => flattenReward(d.id, index + 1, reward)));
  const dungeonTurnEvents = data.dungeons.flatMap((d) =>
    (d.turnEndEventIds ?? []).map((eventId, index) => [d.id, index + 1, eventId]));

  return {
    adventurers,
    classes,
    classSkills,
    races,
    equipment,
    equipmentStatuses,
    skills,
    skillStatuses,
    consumables,
    clues,
    relics,
    facilities,
    enemies,
    enemyDrops,
    choiceEvents,
    choiceOptions,
    choiceOutcomes,
    enemyUnits,
    quests,
    questRewards,
    questEvents,
    dungeons,
    dungeonEvents,
    dungeonEncounters,
    dungeonRewards,
    dungeonTurnEvents,
  };
};

const writeDataSheet = (workbook, definition, rows, tableName) => {
  const sheet = workbook.worksheets.add(definition.name);
  const keys = [...definition.keys, "入力チェック"];
  const labels = [...definition.labels, "自動判定"];
  const lastColumn = columnName(keys.length);
  const lastDataRow = definition.capacity + 4;
  sheet.showGridLines = false;
  sheet.getRange(`A1:${lastColumn}1`).merge();
  sheet.getRange("A1").values = [[definition.title]];
  sheet.getRange(`A1:${lastColumn}1`).format = {
    fill: colors.navy,
    font: { bold: true, color: colors.white, size: 15, name: bodyFont },
    rowHeight: 29,
  };
  sheet.getRange("A2").values = [["登録件数"]];
  sheet.getRange("B2").formulas = [[`=COUNTA(A5:A${lastDataRow})`]];
  sheet.getRange("A2:B2").format = {
    fill: colors.paleGold,
    font: { bold: true, color: colors.navy, name: bodyFont },
    borders: { preset: "outside", style: "thin", color: "#C7A95A" },
  };
  if (keys.length >= 5) {
    sheet.getRange(`D2:${lastColumn}2`).merge();
    sheet.getRange("D2").values = [[
      definition.unique
        ? "IDが空の行はJSON変換時に無視します。IDは重複不可です。"
        : "親IDが空の行はJSON変換時に無視します。順序列で配列順を指定します。",
    ]];
    sheet.getRange(`D2:${lastColumn}2`).format = {
      fill: colors.paleBlue,
      font: { color: colors.navy, italic: true, name: bodyFont },
    };
  }
  sheet.getRange(`A3:${lastColumn}3`).values = [labels];
  sheet.getRange(`A3:${lastColumn}3`).format = {
    fill: "#D8E6EF",
    font: { bold: true, color: colors.navy, size: 9, name: bodyFont },
    horizontalAlignment: "center",
    wrapText: true,
    rowHeight: 30,
  };
  sheet.getRange(`A4:${lastColumn}4`).values = [keys];
  sheet.getRange(`A4:${lastColumn}4`).format = {
    fill: colors.blue,
    font: { bold: true, color: colors.white, size: 9, name: bodyFont },
    horizontalAlignment: "center",
    wrapText: true,
    rowHeight: 32,
  };

  const paddedRows = Array.from({ length: definition.capacity }, (_, index) =>
    rows[index] ? [...rows[index], null] : Array(keys.length).fill(null));
  sheet.getRange(`A5:${lastColumn}${lastDataRow}`).values = paddedRows;
  const checkColumn = columnName(keys.length);
  sheet.getRange(`${checkColumn}5`).formulas = [[
    definition.unique
      ? `=IF(A5="","",IF(COUNTIF($A$5:$A$${lastDataRow},A5)>1,"ID重複","OK"))`
      : '=IF(A5="","","OK")',
  ]];
  sheet.getRange(`${checkColumn}5:${checkColumn}${lastDataRow}`).fillDown();
  const table = sheet.tables.add(`A4:${lastColumn}${lastDataRow}`, true, tableName);
  table.style = "TableStyleMedium2";
  table.showBandedRows = true;
  table.showFilterButton = true;

  sheet.getRange(`A5:${lastColumn}${lastDataRow}`).format.font = { name: bodyFont, size: 10 };
  sheet.getRange(`${checkColumn}5:${checkColumn}${lastDataRow}`).format.fill = colors.paleGreen;
  sheet.getRange(`${checkColumn}5:${checkColumn}${lastDataRow}`).conditionalFormats.add(
    "containsText",
    { text: "重複", format: { fill: colors.paleRed, font: { color: "#A61B1B", bold: true } } },
  );
  sheet.getRange(`A5:A${lastDataRow}`).conditionalFormats.addCustom(
    definition.unique
      ? `=AND(A5<>"",COUNTIF($A$5:$A$${lastDataRow},A5)>1)`
      : '=FALSE',
    { fill: colors.paleRed, font: { color: "#A61B1B", bold: true } },
  );

  for (let i = 1; i <= keys.length; i += 1) {
    const key = keys[i - 1];
    const column = columnName(i);
    let width = Math.max(9, Math.min(21, Math.max(key.length + 2, labels[i - 1].length + 2)));
    if (key === "description") width = 42;
    if (["background", "personality", "motivation", "specialty", "fear", "creed", "selfIntroduction"].includes(key)) {
      width = 38;
    }
    if (["text", "resultText", "storyOutcomeText"].includes(key)) width = 34;
    if (key.endsWith("Id") || key === "id" || key.startsWith("skillId")) width = 22;
    if (key.startsWith("formationId")) width = 24;
    if (["allowedClassIds", "skillIds", "requiredQuestIds", "requiredClueIds", "grantedClueIds"].includes(key)) {
      width = 40;
    }
    if (key === "入力チェック") width = 14;
    sheet.getRange(`${column}:${column}`).format.columnWidth = width;
  }
  const wrappedKeys = new Set([
    "description", "background", "personality", "motivation", "specialty", "fear", "creed",
    "selfIntroduction", "text", "resultText", "storyOutcomeText", "allowedClassIds", "skillIds", "requiredQuestIds",
    "requiredClueIds", "grantedClueIds",
  ]);
  const populatedLastRow = rows.length + 4;
  const wrappedColumns = definition.keys
    .map((key, index) => (wrappedKeys.has(key) ? columnName(index + 1) : null))
    .filter(Boolean);
  if (rows.length > 0 && wrappedColumns.length > 0) {
    for (const column of wrappedColumns) {
      sheet.getRange(`${column}5:${column}${populatedLastRow}`).format.wrapText = true;
    }
    sheet.getRange(`A5:${lastColumn}${populatedLastRow}`).format.rowHeight = 42;
  }
  sheet.freezePanes.freezeRows(4);
  sheet.freezePanes.freezeColumns(Math.min(2, keys.length));
  return sheet;
};

const addValidation = (sheet, definition, key, values) => {
  const index = definition.keys.indexOf(key);
  if (index < 0) return;
  const column = columnName(index + 1);
  sheet.getRange(`${column}5:${column}${definition.capacity + 4}`).dataValidation = {
    rule: { type: "list", values },
  };
};

const addMetadata = (workbook, data) => {
  const sheet = workbook.worksheets.add("_meta");
  sheet.showGridLines = false;
  const fingerprint = crypto
    .createHash("sha256")
    .update(JSON.stringify(stable(data)))
    .digest("hex");
  sheet.getRange("A1:D1").merge();
  sheet.getRange("A1").values = [["AIRPG マスタデータ ブック管理情報"]];
  sheet.getRange("A1:D1").format = {
    fill: colors.navy,
    font: { bold: true, color: colors.white, size: 14, name: bodyFont },
    rowHeight: 28,
  };
  sheet.getRange("A3:B7").values = [
    ["項目", "値"],
    ["schemaVersion", workbookSchemaVersion],
    ["generatedAtUtc", `UTC ${new Date().toISOString()}`],
    ["masterFingerprintSha256", fingerprint],
    ["source", "GuildSimulator.Game/Data/*.json"],
  ];
  sheet.getRange("A3:B3").format = {
    fill: colors.blue,
    font: { bold: true, color: colors.white, name: bodyFont },
  };
  sheet.getRange("A4:A7").format = {
    fill: colors.paleBlue,
    font: { bold: true, color: colors.navy, name: bodyFont },
  };
  sheet.getRange("A3:B7").format.borders = {
    insideHorizontal: { style: "thin", color: colors.lightGray },
    outside: { style: "thin", color: "#9FB4C3" },
  };
  sheet.getRange("A:A").format.columnWidth = 28;
  sheet.getRange("B:B").format.columnWidth = 72;
  sheet.getRange("B4:B7").format.wrapText = true;
  return sheet;
};

const readBalanceReport = async () => {
  try {
    const json = await fs.readFile(balanceReportPath, "utf8");
    return JSON.parse(json.replace(/^\uFEFF/, ""));
  } catch (error) {
    if (error?.code === "ENOENT") return null;
    throw error;
  }
};

const addBalanceReport = (workbook, report) => {
  const sheet = workbook.worksheets.add("バランスレポート");
  sheet.showGridLines = false;
  sheet.getRange("A1:Q1").merge();
  sheet.getRange("A1").values = [["Balance Lab シミュレーション結果"]];
  sheet.getRange("A1:Q1").format = {
    fill: colors.navy,
    font: { bold: true, color: colors.white, size: 16, name: bodyFont },
    rowHeight: 30,
  };
  sheet.getRange("A3:B5").values = [
    ["レポート生成日時", report ? `UTC ${report.generatedAtUtc}` : "未生成"],
    ["合格目安: 最低クリア率", 0.6],
    ["警戒目安: 最大失敗率", 0.2],
  ];
  sheet.getRange("A3:A5").format = {
    fill: colors.paleBlue,
    font: { bold: true, color: colors.navy, name: bodyFont },
  };
  sheet.getRange("B4:B5").format.numberFormat = "0.0%";
  sheet.getRange("A3:B5").format.borders = {
    insideHorizontal: { style: "thin", color: colors.lightGray },
    outside: { style: "thin", color: "#9FB4C3" },
  };

  const headers = [
    "シナリオID", "名称", "種別", "試行数", "クリア率", "撤退率", "失敗率", "破産率",
    "平均ラウンド", "平均ターン", "残HP率", "平均Gold差", "平均延長", "平均宝箱",
    "基準差 クリアpt", "基準差 HPpt", "判定",
  ];
  sheet.getRange("A7:Q7").values = [headers];
  sheet.getRange("A7:Q7").format = {
    fill: colors.blue,
    font: { bold: true, color: colors.white, name: bodyFont },
    wrapText: true,
    rowHeight: 34,
  };

  const scenarios = report?.scenarios ?? [];
  if (scenarios.length === 0) {
    sheet.getRange("A8:Q9").merge();
    sheet.getRange("A8").values = [[
      "outputs/balance-lab/balance-report.json がありません。Balance Labを実行してからexportしてください。",
    ]];
    sheet.getRange("A8:Q9").format = {
      fill: colors.paleGold,
      font: { color: colors.navy, name: bodyFont },
      wrapText: true,
      rowHeight: 36,
    };
  } else {
    const rows = scenarios.map((x) => [
      x.id, x.name, x.type, x.runs,
      (x.clearRatePercent ?? 0) / 100,
      (x.retreatRatePercent ?? 0) / 100,
      (x.failureRatePercent ?? 0) / 100,
      (x.bankruptcyRatePercent ?? 0) / 100,
      x.meanRounds ?? 0, x.meanTurns ?? 0,
      (x.meanRemainingHpPercent ?? 0) / 100,
      x.meanGoldDelta ?? 0, x.meanGatherExtensions ?? 0, x.meanChests ?? 0,
      x.baselineDelta?.clearRatePoints ?? null,
      x.baselineDelta?.meanRemainingHpPoints ?? null,
      null,
    ]);
    const lastRow = rows.length + 7;
    sheet.getRange(`A8:Q${lastRow}`).values = rows;
    sheet.getRange("Q8").formulas = [["=IF(E8<$B$4,\"要調整\",IF(G8>$B$5,\"要調整\",\"OK\"))"]];
    sheet.getRange(`Q8:Q${lastRow}`).fillDown();
    sheet.getRange(`D8:D${lastRow}`).format.numberFormat = "#,##0";
    sheet.getRange(`E8:H${lastRow}`).format.numberFormat = "0.0%";
    sheet.getRange(`I8:P${lastRow}`).format.numberFormat = "0.0";
    sheet.getRange(`K8:K${lastRow}`).format.numberFormat = "0.0%";
    sheet.getRange(`Q8:Q${lastRow}`).conditionalFormats.add(
      "containsText",
      { text: "要調整", format: { fill: colors.paleRed, font: { color: "#A61B1B", bold: true } } },
    );
    sheet.getRange(`Q8:Q${lastRow}`).conditionalFormats.add(
      "containsText",
      { text: "OK", format: { fill: colors.paleGreen, font: { color: "#237A43", bold: true } } },
    );
    const table = sheet.tables.add(`A7:Q${lastRow}`, true, "BalanceLabResults");
    table.style = "TableStyleMedium2";
    table.showBandedRows = true;

    sheet.getRange("S7:V7").values = [["名称", "クリア率", "撤退率", "失敗率"]];
    sheet.getRange("S8:V8").formulas = [["=B8", "=E8", "=F8", "=G8"]];
    sheet.getRange(`S8:V${lastRow}`).fillDown();
    const chart = sheet.charts.add("bar", sheet.getRange(`S7:V${lastRow}`));
    chart.title = "シナリオ別 成功・撤退・失敗率";
    chart.hasLegend = true;
    chart.xAxis = { axisType: "textAxis" };
    chart.yAxis = { numberFormatCode: "0%", min: 0, max: 1 };
    chart.setPosition("S2", "AC18");
  }

  sheet.getRange("A:A").format.columnWidth = 24;
  sheet.getRange("B:B").format.columnWidth = 28;
  sheet.getRange("C:Q").format.columnWidth = 14;
  sheet.getRange("A7:Q40").format.font = { name: bodyFont, size: 10 };
  sheet.freezePanes.freezeRows(7);
  return sheet;
};

const addGuide = (workbook) => {
  const sheet = workbook.worksheets.add("入力ガイド");
  sheet.showGridLines = false;
  sheet.getRange("A1:H1").merge();
  sheet.getRange("A1").values = [["マスタデータ統合 編集ガイド"]];
  sheet.getRange("A1:H1").format = {
    fill: colors.navy,
    font: { bold: true, color: colors.white, size: 16, name: bodyFont },
    rowHeight: 30,
  };
  const guideRows = [
    ["区分", "編集シート", "説明"],
    ["主要", "冒険者", "基本値、職業・種族・装備、初期スキル、人物プロフィール"],
    ["主要", "職業", "職業名と能力成長率"],
    ["明細", "職業スキル", "classIdとorderで職業ごとのスキル解禁順を構成"],
    ["主要", "種族", "能力成長率と就業可能な職業ID"],
    ["主要", "装備", "武器・防具、係数、価格、重量、bonus各種"],
    ["明細", "装備状態効果", "equipmentId・発動契機・orderで戦闘開始時／命中時の状態効果を構成"],
    ["主要", "スキル", "段階、装備条件、add/mul、遠征効果、戦闘条件効果"],
    ["明細", "スキル状態効果", "skillId・発動契機・orderで戦闘開始時／命中時の状態効果を構成"],
    ["主要", "道具", "消費アイテムの説明、価格、効果"],
    ["主要", "レリック", "効果種別、倍率、add/mul各種"],
    ["主要", "施設", "建設費、維持費、ギルド機能への加算"],
    ["主要", "敵", "能力値、装備、自然攻撃、スキル"],
    ["明細", "敵ドロップ", "enemyIdとorderで敵のdropTableを構成"],
    ["主要", "敵ユニット", "最大6枠の敵編成"],
    ["主要", "選択イベント", "イベント名、説明、重み"],
    ["明細", "選択肢", "eventIdとorderで選択肢を構成"],
    ["明細", "選択結果", "eventId・optionOrder・orderで重み付き結果を構成"],
    ["主要", "手掛かり", "物語クエストで発見し、後続クエストの解禁に使う調査情報"],
    ["主要", "クエスト", "依頼文、物語条件、報酬など。ボス報酬と固定イベントは下記明細シート"],
    ["明細", "クエスト報酬", "questIdとorderでボス報酬配列を構成"],
    ["明細", "クエスト固定イベント", "questIdとorderでfixedEvents配列を構成"],
    ["主要", "ダンジョン", "ダンジョン本体"],
    ["明細", "ダンジョンイベント", "eventTableのイベント種別と重み"],
    ["明細", "ダンジョン遭遇", "encounterTableの敵ユニットと出現範囲"],
    ["明細", "ダンジョン報酬", "treasureTable（宝箱）の中身と重み"],
    ["明細", "ダンジョン終了イベント", "turnEndEventIdsの順序付き一覧"],
    ["参照", "参照マスター", "各編集シートのIDと名称を横断確認する一覧"],
    ["分析", "バランスレポート", "Balance Labの試行結果、基準差、要調整シナリオを表示"],
    ["管理", "_meta", "ブックのschemaVersion、生成日時、マスタ指紋を記録"],
    ["共通", "入力チェック", "ID重複の簡易表示。JSON保存時にはツールが全参照を再検証"],
  ];
  const lastGuideRow = guideRows.length + 2;
  sheet.getRange(`A3:C${lastGuideRow}`).values = guideRows;
  sheet.getRange("A3:C3").format = {
    fill: colors.blue,
    font: { bold: true, color: colors.white, name: bodyFont },
  };
  sheet.getRange(`A4:A${lastGuideRow}`).format = {
    fill: colors.paleBlue,
    font: { bold: true, color: colors.navy, name: bodyFont },
  };
  sheet.getRange(`A3:C${lastGuideRow}`).format.borders = {
    insideHorizontal: { style: "thin", color: colors.lightGray },
    outside: { style: "thin", color: "#9FB4C3" },
  };
  sheet.getRange("A:A").format.columnWidth = 12;
  sheet.getRange("B:B").format.columnWidth = 28;
  sheet.getRange("C:C").format.columnWidth = 68;
  sheet.getRange(`C3:C${lastGuideRow}`).format.wrapText = true;
  sheet.getRange(`A3:C${lastGuideRow}`).format.font = { name: bodyFont, size: 10 };
  sheet.getRange(`A3:C${lastGuideRow}`).format.autofitRows();
  sheet.freezePanes.freezeRows(3);
  return lastGuideRow;
};

const addReferences = (workbook, refs) => {
  const sheet = workbook.worksheets.add("参照マスター");
  sheet.showGridLines = false;
  const blocks = [
    ["A", "職業", ["ID", "名称"], refs.classes.map((x) => [x.id, x.className])],
    ["D", "種族", ["ID", "名称"], refs.races.map((x) => [x.id, x.raceName])],
    ["G", "敵", ["ID", "名称"], refs.enemies.map((x) => [x.id, x.baseName])],
    ["J", "敵ユニット", ["ID", "名称"], refs.enemyUnits.map((x) => [x.id, x.unitName])],
    ["M", "レリック", ["ID", "名称"], refs.relics.map((x) => [x.id, x.relicName])],
    ["P", "選択イベント", ["ID", "名称"], refs.choiceEvents.map((x) => [x.id, x.title])],
    ["S", "スキル", ["ID", "名称"], refs.skills.map((x) => [x.id, x.skillName])],
    ["V", "装備", ["ID", "名称"], refs.equipment.map((x) => [x.id, x.displayName])],
    ["Y", "道具", ["ID", "名称"], refs.consumables.map((x) => [x.id, x.displayName])],
    ["AB", "施設", ["ID", "名称"], refs.facilities.map((x) => [x.id, x.displayName])],
    ["AE", "ダンジョン", ["ID", "名称"], refs.dungeons.map((x) => [x.id, x.dungeonName])],
    ["AH", "手掛かり", ["ID", "名称"], refs.clues.map((x) => [x.id, x.title])],
  ];
  sheet.getRange("A1:AJ1").merge();
  sheet.getRange("A1").values = [["ID参照一覧（編集は各マスタシートで行います）"]];
  sheet.getRange("A1:AJ1").format = {
    fill: colors.navy,
    font: { bold: true, color: colors.white, size: 14, name: bodyFont },
  };
  for (const [start, title, headers, rows] of blocks) {
    const startIndex = columnNumber(start);
    const end = columnName(startIndex + headers.length - 1);
    sheet.getRange(`${start}2:${end}2`).merge();
    sheet.getRange(`${start}2`).values = [[title]];
    sheet.getRange(`${start}2:${end}2`).format = {
      fill: colors.blue,
      font: { bold: true, color: colors.white, name: bodyFont },
    };
    sheet.getRange(`${start}3:${end}3`).values = [headers];
    sheet.getRange(`${start}3:${end}3`).format = {
      fill: colors.paleBlue,
      font: { bold: true, color: colors.navy, name: bodyFont },
    };
    if (rows.length > 0) sheet.getRange(`${start}4:${end}${rows.length + 3}`).values = rows;
    sheet.getRange(`${start}:${end}`).format.columnWidth = 20;
  }
  sheet.freezePanes.freezeRows(3);
};

const exportWorkbook = async (providedData = null, exitWhenDone = true, renderPreviews = true) => {
  const entries = providedData == null
    ? await Promise.all(masterFiles.map(async (name) => [name, await readJson(name)]))
    : null;
  const data = providedData ?? Object.fromEntries(entries);
  const refs = {
    classes: data.classes,
    races: data.races,
    enemies: data.enemies,
    enemyUnits: data.enemy_units,
    relics: data.relics,
    choiceEvents: data.choice_events,
    skills: data.skills,
    equipment: data.equipment,
    consumables: data.consumables,
    facilities: data.facilities,
    dungeons: data.dungeons,
    clues: data.clues,
  };
  const rowsBySheet = makeRows(data);
  const workbook = Workbook.create();
  workbook.comments.setSelf({ displayName: "User" });
  addMetadata(workbook, data);
  const guideLastRow = addGuide(workbook);
  const sheets = {};
  let tableIndex = 1;
  for (const [key, definition] of Object.entries(sheetDefinitions)) {
    sheets[key] = writeDataSheet(workbook, definition, rowsBySheet[key], `MasterTable${tableIndex}`);
    tableIndex += 1;
  }
  addReferences(workbook, refs);
  const balanceReport = await readBalanceReport();
  addBalanceReport(workbook, balanceReport);

  const boolFields = {
    skills: [
      "frontOnly", "backOnly", "requireWeaponType", "requireArmorType", "requireUnarmed",
      "requireTwoHanded", "requireShield", "requireOffHandWeapon", "requirePhysicalWeapon",
    ],
    equipment: ["isTwoHanded"],
    quests: ["isStoryQuest", "isEmergencyQuest", "bossDropsAreGuaranteed"],
    choiceOptions: ["targetsOneMember"],
    enemyDrops: ["unique"],
    questRewards: ["unique"],
    dungeonRewards: ["unique"],
  };
  for (const [key, fields] of Object.entries(boolFields)) {
    for (const field of fields) addValidation(sheets[key], sheetDefinitions[key], field, ["TRUE", "FALSE"]);
  }
  const rarities = ["Common", "Uncommon", "Rare", "Unique", "Legend"];
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "rarity", rarities);
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "gender", ["Unspecified", "Male", "Female"]);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "rarity", rarities);
  addValidation(sheets.consumables, sheetDefinitions.consumables, "rarity", rarities);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "type", [0, 1, 2, 3]);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "weaponType", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "armorType", [0, 1, 2, 3]);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "attackKind", ["Physical", "Magic", "Heal"]);
  // 認定ランクは F(1) 〜 S(7) の7段階。3種のランクすべてが同じ物差しに乗っている。
  const ranks = [1, 2, 3, 4, 5, 6, 7];
  addValidation(sheets.quests, sheetDefinitions.quests, "rank", ranks);
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "defaultRank", ranks);
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "recruitGuildRank", ranks);
  addValidation(sheets.facilities, sheetDefinitions.facilities, "requiredGuildRank", ranks);
  addValidation(sheets.enemies, sheetDefinitions.enemies, "threat", ranks);
  addValidation(sheets.skills, sheetDefinitions.skills, "scope", [0, 1]);
  addValidation(sheets.skills, sheetDefinitions.skills, "requiredWeaponType", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
  addValidation(sheets.skills, sheetDefinitions.skills, "requiredArmorType", [0, 1, 2, 3]);
  addValidation(sheets.consumables, sheetDefinitions.consumables, "effectType", [
    "MaxHpPercent", "MoralePercent", "GoldRewardPercent", "ExpRewardPercent", "TrapDamageReductionPercent",
    "RestHealPercent", "TreasureFromNothingPercent", "TargetPv", "TargetMpv",
    "GuaranteedNonEmptyChest", "BattleHorn", "EmergencyRetreatPercent",
  ]);
  addValidation(sheets.relics, sheetDefinitions.relics, "effectType", [0, 1, 2, 3, 4]);
  addValidation(sheets.enemyDrops, sheetDefinitions.enemyDrops, "type", [0, 1, 2, 3, 4]);
  addValidation(sheets.questRewards, sheetDefinitions.questRewards, "type", [0, 1, 2, 3, 4]);
  addValidation(sheets.questEvents, sheetDefinitions.questEvents, "type", [0, 1, 2, 3, 4, 5, 6, 7]);
  addValidation(sheets.dungeonEvents, sheetDefinitions.dungeonEvents, "eventType", [
    "EnemyEncounter", "Heal", "Trap", "Treasure", "Nothing",
  ]);
  addValidation(sheets.dungeonRewards, sheetDefinitions.dungeonRewards, "type", [0, 1, 2, 3, 4]);
  const choiceEffectTypes = [
    "None", "Morale", "HealPercent", "DamagePercent", "Experience", "Gold", "Equipment",
    "Consumable", "Treasure", "AdventurerStatUp", "AdventurerStatDown", "AdventurerSkill",
    "AdventurerDamage", "Purchase",
  ];
  addValidation(sheets.choiceOptions, sheetDefinitions.choiceOptions, "effectType", choiceEffectTypes);
  addValidation(sheets.choiceOutcomes, sheetDefinitions.choiceOutcomes, "effectType", choiceEffectTypes);
  const statusTriggers = ["battleStart", "onHit"];
  const statusTypes = ["Poisoned", "Bleeding", "Burning", "Stunned", "Regenerating", "Empowered", "Guarded"];
  const statusTargets = ["Self", "Allies", "Enemy"];
  for (const key of ["equipmentStatuses", "skillStatuses"]) {
    addValidation(sheets[key], sheetDefinitions[key], "trigger", statusTriggers);
    addValidation(sheets[key], sheetDefinitions[key], "type", statusTypes);
    addValidation(sheets[key], sheetDefinitions[key], "target", statusTargets);
  }

  await fs.mkdir(outputDir, { recursive: true });
  await fs.mkdir(previewDir, { recursive: true });
  const overview = await workbook.inspect({
    kind: "workbook,sheet,table",
    maxChars: 2500,
    tableMaxRows: 2,
    tableMaxCols: 6,
  });
  console.log(overview.ndjson);
  const errors = await workbook.inspect({
    kind: "match",
    searchTerm: "#REF!|#DIV/0!|#VALUE!|#NAME\\?|#N/A",
    options: { useRegex: true, maxResults: 200 },
    summary: "formula error scan",
  });
  console.log(errors.ndjson);

  const previewRanges = [
    ["_meta", "A1:D8"],
    ["入力ガイド", `A1:H${guideLastRow}`],
    ...Object.entries(sheetDefinitions).map(([key, definition]) => {
      const lastColumn = columnName(definition.keys.length + 1);
      const lastRow = Math.min(19, Math.max(8, rowsBySheet[key].length + 4));
      return [definition.name, `A1:${lastColumn}${lastRow}`];
    }),
    ["参照マスター", "A1:AJ30"],
    ["バランスレポート", "A1:AC20"],
  ];
  if (renderPreviews) {
    for (const [sheetName, range] of previewRanges) {
      const preview = await workbook.render({
        sheetName,
        range,
        scale: 0.8,
        format: "png",
      });
      await fs.writeFile(
        path.join(previewDir, `${sheetName}.png`),
        new Uint8Array(await preview.arrayBuffer()),
      );
    }
  }
  const output = await SpreadsheetFile.exportXlsx(workbook);
  await output.save(workbookPath);
  const reopened = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
  const reopenCheck = await reopened.inspect({
    kind: "table",
    range: "冒険者!A1:F8",
    include: "values,formulas",
    tableMaxRows: 8,
    tableMaxCols: 6,
  });
  console.log(`REOPENED_OK=${reopenCheck.ndjson.includes("adv_0001")}`);
  console.log(`WORKBOOK=${workbookPath}`);
  await fs.writeFile(path.join(outputDir, "export.ok"), workbookPath, "utf8");
  if (exitWhenDone) process.exit(0);
};

const text = (value) => String(value ?? "").trim();
const isBlank = (value) => value === null || value === undefined || text(value) === "";
const optionalText = (value) => (isBlank(value) ? null : text(value));
const idList = (value) =>
  isBlank(value)
    ? []
    : text(value).split(/[,、;\n]/).map((entry) => entry.trim()).filter(Boolean);
const numberValue = (value, label, row, integer = false) => {
  if (isBlank(value)) throw new Error(`${label} (${row}行目) が空です。`);
  const result = typeof value === "number" ? value : Number(value);
  if (!Number.isFinite(result) || (integer && !Number.isInteger(result))) {
    throw new Error(`${label} (${row}行目) が数値ではありません。`);
  }
  return result;
};
const optionalNumber = (value, label, row, integer = false) =>
  isBlank(value) ? null : numberValue(value, label, row, integer);
const boolValue = (value, label, row) => {
  if (typeof value === "boolean") return value;
  const normalized = text(value).toLowerCase();
  if (normalized === "true" || normalized === "1") return true;
  if (normalized === "false" || normalized === "0") return false;
  throw new Error(`${label} (${row}行目) はTRUE/FALSEで入力してください。`);
};
const optionalBool = (value, label, row) => (isBlank(value) ? null : boolValue(value, label, row));

const readSheetRows = (workbook, definition, allowMissingHeaders = false) => {
  let sheet;
  try {
    sheet = workbook.worksheets.getItem(definition.name);
  } catch (error) {
    if (allowMissingHeaders) return [];
    throw error;
  }
  if (allowMissingHeaders) {
    const usedValues = sheet.getUsedRange().values;
    const actualHeaders = (usedValues[3] ?? []).map(text);
    const indexes = definition.keys.map((key) => actualHeaders.indexOf(key));
    if (indexes[0] < 0) {
      throw new Error(`${definition.name}: 移行に必要な先頭キー ${definition.keys[0]} がありません。`);
    }
    return usedValues
      .slice(4)
      .map((sourceValues, index) => {
        const values = indexes.map((sourceIndex) => sourceIndex < 0 ? null : sourceValues[sourceIndex]);
        return {
          row: index + 5,
          values,
          object: Object.fromEntries(definition.keys.map((key, keyIndex) => [key, values[keyIndex]])),
        };
      })
      .filter(({ values }) => !isBlank(values[0]));
  }
  const lastColumn = columnName(definition.keys.length);
  const actualHeaders = sheet.getRange(`A4:${lastColumn}4`).values[0].map(text);
  if (JSON.stringify(actualHeaders) !== JSON.stringify(definition.keys)) {
    throw new Error(`${definition.name}: 4行目のJSONキー見出しが変更されています。`);
  }
  return sheet
    .getRange(`A5:${lastColumn}${definition.capacity + 4}`)
    .values
    .map((values, index) => ({
      row: index + 5,
      values,
      object: Object.fromEntries(definition.keys.map((key, keyIndex) => [key, values[keyIndex]])),
    }))
    .filter(({ values }) => !isBlank(values[0]));
};

const optionalAssign = (object, key, value) => {
  if (value !== null) object[key] = value;
};
const buildStatObject = (source, prefix, row, multiplier = false) => {
  const result = {};
  for (const key of (multiplier ? mulKeys : statKeys)) {
    const value = optionalNumber(source[`${prefix}_${key}`], `${prefix}_${key}`, row, !multiplier);
    if (value !== null) result[key] = value;
  }
  return result;
};
const buildNumberObject = (source, prefix, keys, row, integer = true) => {
  const result = {};
  for (const key of keys) {
    const value = optionalNumber(source[`${prefix}_${key}`], `${prefix}_${key}`, row, integer);
    if (value !== null) result[key] = value;
  }
  return result;
};
const buildReward = (source, row) => {
  const reward = { type: numberValue(source.type, "type", row, true) };
  for (const key of ["relicId", "equipmentId", "skillId", "consumableId"]) {
    optionalAssign(reward, key, optionalText(source[key]));
  }
  for (const key of ["gold", "weight", "quantity", "minQuestRank", "maxQuestRank"]) {
    optionalAssign(reward, key, optionalNumber(source[key], key, row, true));
  }
  optionalAssign(reward, "chance", optionalNumber(source.chance, "chance", row, false));
  optionalAssign(reward, "unique", optionalBool(source.unique, "unique", row));
  return reward;
};

const stable = (value) => {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value)
      .filter((key) => !(key === "targetId" && value[key] === ""))
      .sort()
      .map((key) => [key, stable(value[key])]));
  }
  return value;
};

const firstDifference = (left, right, currentPath = "$") => {
  if (Object.is(left, right)) return null;
  if (Array.isArray(left) && Array.isArray(right)) {
    if (left.length !== right.length) return `${currentPath}.length: ${left.length} != ${right.length}`;
    for (let index = 0; index < left.length; index += 1) {
      const difference = firstDifference(left[index], right[index], `${currentPath}[${index}]`);
      if (difference) return difference;
    }
    return null;
  }
  if (left && right && typeof left === "object" && typeof right === "object") {
    const keys = [...new Set([...Object.keys(left), ...Object.keys(right)])].sort();
    for (const key of keys) {
      if (!(key in left)) return `${currentPath}.${key}: only reconstructed`;
      if (!(key in right)) return `${currentPath}.${key}: only original`;
      const difference = firstDifference(left[key], right[key], `${currentPath}.${key}`);
      if (difference) return difference;
    }
    return null;
  }
  return `${currentPath}: ${JSON.stringify(left)} != ${JSON.stringify(right)}`;
};

const importWorkbook = async (writeMode, allowMissingHeaders = false) => {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
  const rows = Object.fromEntries(
    Object.entries(sheetDefinitions).map(([key, definition]) => [
      key,
      readSheetRows(workbook, definition, allowMissingHeaders),
    ]),
  );
  let refs = {};
  const errors = [];
  const guarded = (callback) => {
    try {
      return callback();
    } catch (error) {
      errors.push(error.message);
      return null;
    }
  };
  const ensureUnique = (items, label) => {
    const ids = items.map((x) => x.id);
    const duplicates = [...new Set(ids.filter((id, index) => ids.indexOf(id) !== index))];
    if (duplicates.length > 0) errors.push(`${label}: ID重複 ${duplicates.join(", ")}`);
  };
  const assertRef = (set, value, label, row, optional = false) => {
    if (isBlank(value) && optional) return;
    const id = text(value);
    if (!set.has(id)) errors.push(`${label} (${row}行目): 不正なID「${id}」`);
  };
  const orderRows = (items, parentKey) =>
    [...items].sort((a, b) =>
      text(a.object[parentKey]).localeCompare(text(b.object[parentKey])) ||
      numberValue(a.object.order, "order", a.row, true) - numberValue(b.object.order, "order", b.row, true));

  const buildStatusGroups = (entries, parentKey) => {
    const groups = new Map();
    for (const entry of orderRows(entries, parentKey)) {
      guarded(() => {
        const parentId = text(entry.object[parentKey]);
        const trigger = text(entry.object.trigger);
        if (trigger !== "battleStart" && trigger !== "onHit")
          throw new Error(`trigger (${entry.row}行目) は battleStart/onHit で入力してください。`);
        const status = {
          type: text(entry.object.type),
          target: text(entry.object.target),
        };
        optionalAssign(status, "chancePercent",
          optionalNumber(entry.object.chancePercent, "chancePercent", entry.row, true));
        optionalAssign(status, "durationRounds",
          optionalNumber(entry.object.durationRounds, "durationRounds", entry.row, true));
        optionalAssign(status, "potency",
          optionalNumber(entry.object.potency, "potency", entry.row, true));
        if (!groups.has(parentId)) groups.set(parentId, { battleStartStatuses: [], onHitStatuses: [] });
        groups.get(parentId)[trigger === "battleStart" ? "battleStartStatuses" : "onHitStatuses"].push(status);
      });
    }
    return groups;
  };

  const equipmentStatusGroups = buildStatusGroups(rows.equipmentStatuses, "equipmentId");
  const skillStatusGroups = buildStatusGroups(rows.skillStatuses, "skillId");

  const equipment = rows.equipment.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      displayName: text(x.displayName),
    };
    optionalAssign(item, "rarity", optionalText(x.rarity));
    item.type = numberValue(x.type, "type", row, true);
    item.weaponType = numberValue(x.weaponType, "weaponType", row, true);
    item.armorType = numberValue(x.armorType, "armorType", row, true);
    const allowedSlots = idList(x.allowedSlots);
    if (allowedSlots.length > 0) item.allowedSlots = allowedSlots;
    optionalAssign(item, "attackKind", optionalText(x.attackKind));
    optionalAssign(item, "damageDice", optionalText(x.damageDice));
    optionalAssign(item, "basePv", optionalNumber(x.basePv, "basePv", row, true));
    optionalAssign(item, "maxStatBonus", optionalNumber(x.maxStatBonus, "maxStatBonus", row, true));
    for (const key of weaponTraitKeys) {
      optionalAssign(item, key, optionalNumber(x[key], key, row, true));
    }
    if (optionalBool(x.isTwoHanded, "isTwoHanded", row) === true) item.isTwoHanded = true;
    optionalAssign(item, "blockChance", optionalNumber(x.blockChance, "blockChance", row, true));
    optionalAssign(item, "blockAv", optionalNumber(x.blockAv, "blockAv", row, true));
    optionalAssign(item, "healPower", optionalNumber(x.healPower, "healPower", row));
    optionalAssign(item, "flatHeal", optionalNumber(x.flatHeal, "flatHeal", row, true));
    item.price = numberValue(x.price, "price", row, true);
    item.weight = numberValue(x.weight, "weight", row, true);
    optionalAssign(item, "shopTier", optionalNumber(x.shopTier, "shopTier", row, true));
    const bonus = buildStatObject(x, "bonus", row);
    if (Object.keys(bonus).length > 0) item.bonus = bonus;
    const statuses = equipmentStatusGroups.get(item.id);
    if (statuses?.battleStartStatuses.length > 0) item.battleStartStatuses = statuses.battleStartStatuses;
    if (statuses?.onHitStatuses.length > 0) item.onHitStatuses = statuses.onHitStatuses;
    return item;
  })).filter(Boolean);
  ensureUnique(equipment, "装備");
  const equipmentIds = new Set(equipment.map((x) => x.id));
  const weaponIds = new Set(equipment.filter((x) => x.type === 0).map((x) => x.id));
  const armorIds = new Set(equipment.filter((x) => x.type === 1).map((x) => x.id));

  const skills = rows.skills.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      skillName: text(x.skillName),
      scope: numberValue(x.scope, "scope", row, true),
    };
    for (const key of ["frontOnly", "backOnly", "requireWeaponType", "requireArmorType"]) {
      if (boolValue(x[key], key, row)) item[key] = true;
    }
    optionalAssign(item, "family", optionalText(x.family));
    optionalAssign(item, "level", optionalNumber(x.level, "level", row, true));
    optionalAssign(item, "requiredWeaponType", optionalNumber(x.requiredWeaponType, "requiredWeaponType", row, true));
    optionalAssign(item, "requiredArmorType", optionalNumber(x.requiredArmorType, "requiredArmorType", row, true));
    for (const key of [
      "requireUnarmed", "requireTwoHanded", "requireShield", "requireOffHandWeapon", "requirePhysicalWeapon",
    ]) {
      const value = boolValue(x[key], key, row);
      if (value) item[key] = true;
    }
    optionalAssign(item, "unarmedDamageDice", optionalText(x.unarmedDamageDice));
    item.add = buildStatObject(x, "add", row);
    item.mul = buildStatObject(x, "mul", row, true);
    const expedition = buildNumberObject(x, "expedition", expeditionKeys, row, true);
    if (Object.keys(expedition).length > 0) item.expedition = expedition;
    const battle = buildNumberObject(x, "battle", battleSkillKeys, row, true);
    if (Object.keys(battle).length > 0) item.battle = battle;
    const statuses = skillStatusGroups.get(item.id);
    if (statuses?.battleStartStatuses.length > 0) item.battleStartStatuses = statuses.battleStartStatuses;
    if (statuses?.onHitStatuses.length > 0) item.onHitStatuses = statuses.onHitStatuses;
    return item;
  })).filter(Boolean);
  ensureUnique(skills, "スキル");
  const skillIds = new Set(skills.map((x) => x.id));
  for (const entry of rows.equipmentStatuses)
    assertRef(equipmentIds, entry.object.equipmentId, "装備状態効果.equipmentId", entry.row);
  for (const entry of rows.skillStatuses)
    assertRef(skillIds, entry.object.skillId, "スキル状態効果.skillId", entry.row);

  const consumables = rows.consumables.map(({ object: x, row }) => guarded(() => {
    const item = { id: text(x.id), displayName: text(x.displayName) };
    optionalAssign(item, "description", optionalText(x.description));
    optionalAssign(item, "rarity", optionalText(x.rarity));
    item.price = numberValue(x.price, "price", row, true);
    item.effectType = text(x.effectType);
    item.effectValue = numberValue(x.effectValue, "effectValue", row, true);
    optionalAssign(item, "secondaryEffectValue",
      optionalNumber(x.secondaryEffectValue, "secondaryEffectValue", row, true));
    return item;
  })).filter(Boolean);
  ensureUnique(consumables, "道具");
  const consumableIds = new Set(consumables.map((x) => x.id));

  const relics = rows.relics.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      relicName: text(x.relicName),
      effectType: numberValue(x.effectType, "effectType", row, true),
    };
    optionalAssign(item, "description", optionalText(x.description));
    optionalAssign(item, "rate", optionalNumber(x.rate, "rate", row));
    const add = buildStatObject(x, "add", row);
    const mul = buildStatObject(x, "mul", row, true);
    if (Object.keys(add).length > 0) item.add = add;
    if (Object.keys(mul).length > 0) item.mul = mul;
    return item;
  })).filter(Boolean);
  ensureUnique(relics, "レリック");
  const relicIds = new Set(relics.map((x) => x.id));

  const classSkillGroups = new Map();
  for (const entry of orderRows(rows.classSkills, "classId")) {
    guarded(() => {
      const classId = text(entry.object.classId);
      const skillId = text(entry.object.skillId);
      assertRef(skillIds, skillId, "職業スキル.skillId", entry.row);
      if (!classSkillGroups.has(classId)) classSkillGroups.set(classId, []);
      classSkillGroups.get(classId).push({
        skillId,
        requiredClearCount: numberValue(
          entry.object.requiredClearCount,
          "requiredClearCount",
          entry.row,
          true,
        ),
      });
    });
  }
  const classes = rows.classes.map(({ object: x, row }) => guarded(() => {
    const id = text(x.id);
    return {
      id,
      className: text(x.className),
      vitGrowth: numberValue(x.vitGrowth, "vitGrowth", row),
      mentGrowth: numberValue(x.mentGrowth, "mentGrowth", row),
      strGrowth: numberValue(x.strGrowth, "strGrowth", row),
      intGrowth: numberValue(x.intGrowth, "intGrowth", row),
      agiGrowth: numberValue(x.agiGrowth, "agiGrowth", row),
      classSkills: classSkillGroups.get(id) ?? [],
    };
  })).filter(Boolean);
  ensureUnique(classes, "職業");
  const classIds = new Set(classes.map((x) => x.id));
  for (const entry of rows.classSkills) {
    assertRef(classIds, entry.object.classId, "職業スキル.classId", entry.row);
  }

  const races = rows.races.map(({ object: x, row }) => guarded(() => {
    const allowedClassIds = idList(x.allowedClassIds);
    for (const classId of allowedClassIds) {
      assertRef(classIds, classId, "種族.allowedClassIds", row);
    }
    return {
      id: text(x.id),
      raceName: text(x.raceName),
      vitGrowth: numberValue(x.vitGrowth, "vitGrowth", row),
      mentGrowth: numberValue(x.mentGrowth, "mentGrowth", row),
      strGrowth: numberValue(x.strGrowth, "strGrowth", row),
      intGrowth: numberValue(x.intGrowth, "intGrowth", row),
      agiGrowth: numberValue(x.agiGrowth, "agiGrowth", row),
      allowedClassIds,
    };
  })).filter(Boolean);
  ensureUnique(races, "種族");
  const raceIds = new Set(races.map((x) => x.id));

  const facilities = rows.facilities.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      displayName: text(x.displayName),
      buildCostGold: numberValue(x.buildCostGold, "buildCostGold", row, true),
      upkeepGoldPerTurn: numberValue(x.upkeepGoldPerTurn, "upkeepGoldPerTurn", row, true),
      requiredGuildRank: numberValue(x.requiredGuildRank, "requiredGuildRank", row, true),
      questBoardBonus: numberValue(x.questBoardBonus, "questBoardBonus", row, true),
      shopLevelBonus: numberValue(x.shopLevelBonus, "shopLevelBonus", row, true),
      restHealBonusPercent: numberValue(x.restHealBonusPercent, "restHealBonusPercent", row, true),
      growthRateBonusPercent: numberValue(x.growthRateBonusPercent, "growthRateBonusPercent", row, true),
    };
    optionalAssign(item, "description", optionalText(x.description));
    for (const key of [
      "partySlotBonus", "rosterSlotBonus", "noviceQuestBoardBonus", "recruitMinBonus", "injuryRecoveryBonus", "fatalityReductionPercent", "scarPreventionPercent",
    ]) {
      optionalAssign(item, key, optionalNumber(x[key], key, row, true));
    }
    return item;
  })).filter(Boolean);
  ensureUnique(facilities, "施設");

  const shieldIds = new Set(equipment.filter((x) => x.type === 3).map((x) => x.id));
  const enemies = rows.enemies.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      baseName: text(x.baseName),
      exp: numberValue(x.exp, "exp", row, true),
      threat: numberValue(x.threat, "threat", row, true),
      vitality: numberValue(x.vitality, "vitality", row, true),
      mental: numberValue(x.mental, "mental", row, true),
      strength: numberValue(x.strength, "strength", row, true),
      agility: numberValue(x.agility, "agility", row, true),
      intelligence: numberValue(x.intelligence, "intelligence", row, true),
      constitution: numberValue(x.constitution, "constitution", row, true),
    };
    optionalAssign(item, "description", optionalText(x.description));
    item.defaultWeaponId = text(x.defaultWeaponId);
    item.defaultArmorId = text(x.defaultArmorId);
    for (const key of ["defaultOffHandId", "defaultShieldId"]) optionalAssign(item, key, optionalText(x[key]));
    optionalAssign(item, "naturalDamageDice", optionalText(x.naturalDamageDice));
    for (const key of ["naturalPv", "naturalAv", "naturalMav"]) {
      optionalAssign(item, key, optionalNumber(x[key], key, row, true));
    }
    optionalAssign(item, "naturalAttackKind", optionalText(x.naturalAttackKind));
    item.skillIds = idList(x.skillIds);
    if (item.defaultWeaponId) assertRef(weaponIds, item.defaultWeaponId, "敵.defaultWeaponId", row);
    if (item.defaultArmorId) assertRef(armorIds, item.defaultArmorId, "敵.defaultArmorId", row);
    if (item.defaultOffHandId) assertRef(weaponIds, item.defaultOffHandId, "敵.defaultOffHandId", row);
    if (item.defaultShieldId) assertRef(shieldIds, item.defaultShieldId, "敵.defaultShieldId", row);
    for (const skillId of item.skillIds) assertRef(skillIds, skillId, "敵.skillIds", row);
    return item;
  })).filter(Boolean);
  ensureUnique(enemies, "敵");
  const enemyIds = new Set(enemies.map((x) => x.id));

  const outcomeGroups = new Map();
  const sortedOutcomes = [...rows.choiceOutcomes].sort((a, b) =>
    text(a.object.eventId).localeCompare(text(b.object.eventId)) ||
    numberValue(a.object.optionOrder, "optionOrder", a.row, true) -
      numberValue(b.object.optionOrder, "optionOrder", b.row, true) ||
    numberValue(a.object.order, "order", a.row, true) - numberValue(b.object.order, "order", b.row, true));
  for (const entry of sortedOutcomes) {
    guarded(() => {
      const eventId = text(entry.object.eventId);
      const optionOrder = numberValue(entry.object.optionOrder, "optionOrder", entry.row, true);
      const key = `${eventId}\u0000${optionOrder}`;
      const effectType = typeof entry.object.effectType === "number"
        ? numberValue(entry.object.effectType, "effectType", entry.row, true)
        : text(entry.object.effectType);
      const outcome = {
        weight: numberValue(entry.object.weight, "weight", entry.row, true),
        effectType,
      };
      optionalAssign(outcome, "value", optionalNumber(entry.object.value, "value", entry.row, true));
      const resultText = optionalText(entry.object.resultText);
      if (resultText !== null) outcome.resultText = resultText;
      optionalAssign(outcome, "targetId", optionalText(entry.object.targetId));
      if (!outcomeGroups.has(key)) outcomeGroups.set(key, []);
      outcomeGroups.get(key).push(outcome);
    });
  }
  const optionGroups = new Map();
  for (const entry of orderRows(rows.choiceOptions, "eventId")) {
    guarded(() => {
      const eventId = text(entry.object.eventId);
      const optionOrder = numberValue(entry.object.order, "order", entry.row, true);
      const effectType = typeof entry.object.effectType === "number"
        ? numberValue(entry.object.effectType, "effectType", entry.row, true)
        : text(entry.object.effectType);
      const option = {
        text: text(entry.object.text),
        resultText: text(entry.object.resultText),
        effectType,
      };
      optionalAssign(option, "value", optionalNumber(entry.object.value, "value", entry.row, true));
      optionalAssign(option, "targetId", optionalText(entry.object.targetId));
      optionalAssign(option, "grantedClueId", optionalText(entry.object.grantedClueId));
      optionalAssign(option, "storyBranchId", optionalText(entry.object.storyBranchId));
      optionalAssign(option, "storyOutcomeText", optionalText(entry.object.storyOutcomeText));
      if (boolValue(entry.object.targetsOneMember, "targetsOneMember", entry.row)) {
        option.targetsOneMember = true;
      }
      const outcomes = outcomeGroups.get(`${eventId}\u0000${optionOrder}`) ?? [];
      if (outcomes.length > 0) option.outcomes = outcomes;
      if (!optionGroups.has(eventId)) optionGroups.set(eventId, []);
      optionGroups.get(eventId).push(option);
    });
  }
  const choiceEvents = rows.choiceEvents.map(({ object: x, row }) => guarded(() => {
    const id = text(x.id);
    const item = {
      id,
      title: text(x.title),
      weight: numberValue(x.weight, "weight", row, true),
      options: optionGroups.get(id) ?? [],
    };
    optionalAssign(item, "description", optionalText(x.description));
    return item;
  })).filter(Boolean);
  ensureUnique(choiceEvents, "選択イベント");
  const choiceEventIds = new Set(choiceEvents.map((x) => x.id));
  for (const entry of rows.choiceOptions) {
    assertRef(choiceEventIds, entry.object.eventId, "選択肢.eventId", entry.row);
  }
  for (const entry of rows.choiceOutcomes) {
    assertRef(choiceEventIds, entry.object.eventId, "選択結果.eventId", entry.row);
  }

  const enemyUnits = rows.enemyUnits.map(({ object: x, row }) => guarded(() => {
    const formationIds = [
      x.formationId1, x.formationId2, x.formationId3, x.formationId4, x.formationId5, x.formationId6,
    ].map(optionalText);
    for (const enemyId of formationIds.filter(Boolean)) {
      assertRef(enemyIds, enemyId, "敵ユニット.formationId", row);
    }
    return { id: text(x.id), unitName: text(x.unitName), formationIds };
  })).filter(Boolean);
  ensureUnique(enemyUnits, "敵ユニット");
  const enemyUnitIds = new Set(enemyUnits.map((x) => x.id));

  refs = {
    classes: classIds,
    races: raceIds,
    enemies: enemyIds,
    enemyUnits: enemyUnitIds,
    relics: relicIds,
    choiceEvents: choiceEventIds,
  };

  const validateChoiceTarget = (effectType, targetId, row, label) => {
    const normalized = text(effectType);
    if (normalized === "Purchase" || normalized === "13") {
      if (isBlank(targetId)) {
        errors.push(`${label}.targetId (${row}行目): Purchaseには商品IDが必要です`);
        return;
      }
      const id = text(targetId);
      const matches = Number(equipmentIds.has(id)) + Number(consumableIds.has(id));
      if (matches !== 1) {
        errors.push(`${label}.targetId (${row}行目): Purchaseの商品ID「${id}」は装備または道具のどちらか1件にしてください`);
      }
      return;
    }
    if (isBlank(targetId)) return;
    if (normalized === "Equipment" || normalized === "6") {
      assertRef(equipmentIds, targetId, `${label}.targetId`, row);
    } else if (normalized === "Consumable" || normalized === "7") {
      assertRef(consumableIds, targetId, `${label}.targetId`, row);
    } else if (normalized === "AdventurerSkill" || normalized === "11") {
      assertRef(skillIds, targetId, `${label}.targetId`, row);
    }
  };
  for (const entry of rows.choiceOptions) {
    validateChoiceTarget(entry.object.effectType, entry.object.targetId, entry.row, "選択肢");
    const normalized = text(entry.object.effectType);
    const purchasePrice = Number(entry.object.value);
    if ((normalized === "Purchase" || normalized === "13")
        && (!Number.isFinite(purchasePrice) || purchasePrice <= 0)) {
      errors.push(`選択肢.value (${entry.row}行目): Purchaseの提示価格は1以上にしてください`);
    }
  }
  for (const entry of rows.choiceOutcomes) {
    validateChoiceTarget(entry.object.effectType, entry.object.targetId, entry.row, "選択結果");
    const normalized = text(entry.object.effectType);
    if (normalized === "Purchase" || normalized === "13") {
      errors.push(`選択結果.effectType (${entry.row}行目): Purchaseはoutcomesに入れられません`);
    }
  }
  for (const choiceEvent of choiceEvents) {
    const hasPurchase = choiceEvent.options.some((option) =>
      text(option.effectType) === "Purchase" || text(option.effectType) === "13");
    const hasFreeAlternative = choiceEvent.options.some((option) =>
      text(option.effectType) !== "Purchase" && text(option.effectType) !== "13");
    if (hasPurchase && !hasFreeAlternative) {
      errors.push(`選択イベント「${choiceEvent.id}」: Purchase以外の選択肢も1つ必要です`);
    }
  }

  const dungeons = rows.dungeons.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      dungeonName: text(x.dungeonName),
    };
    optionalAssign(item, "turnEndEventChance", optionalNumber(x.turnEndEventChance, "turnEndEventChance", row));
    return item;
  })).filter(Boolean);
  ensureUnique(dungeons, "ダンジョン");
  const dungeonIds = new Set(dungeons.map((x) => x.id));

  const rewardRefCheck = (reward, row, label) => {
    if (reward.relicId) assertRef(refs.relics, reward.relicId, `${label}.relicId`, row);
    if (reward.equipmentId) assertRef(equipmentIds, reward.equipmentId, `${label}.equipmentId`, row);
    if (reward.skillId) assertRef(skillIds, reward.skillId, `${label}.skillId`, row);
    if (reward.consumableId) assertRef(consumableIds, reward.consumableId, `${label}.consumableId`, row);
  };

  const enemyDropGroups = new Map();
  for (const entry of orderRows(rows.enemyDrops, "enemyId")) {
    guarded(() => {
      const enemyId = text(entry.object.enemyId);
      const reward = buildReward(entry.object, entry.row);
      rewardRefCheck(reward, entry.row, "敵ドロップ");
      if (!enemyDropGroups.has(enemyId)) enemyDropGroups.set(enemyId, []);
      enemyDropGroups.get(enemyId).push(reward);
    });
  }
  for (const entry of rows.enemyDrops) {
    assertRef(enemyIds, entry.object.enemyId, "敵ドロップ.enemyId", entry.row);
  }
  for (const enemy of enemies) {
    const dropTable = enemyDropGroups.get(enemy.id) ?? [];
    if (dropTable.length > 0) enemy.dropTable = dropTable;
  }

  const questRewardGroups = new Map();
  for (const entry of orderRows(rows.questRewards, "questId")) {
    guarded(() => {
      const questId = text(entry.object.questId);
      const reward = buildReward(entry.object, entry.row);
      rewardRefCheck(reward, entry.row, "クエスト報酬");
      if (!questRewardGroups.has(questId)) questRewardGroups.set(questId, []);
      questRewardGroups.get(questId).push(reward);
    });
  }
  const questEventGroups = new Map();
  for (const entry of orderRows(rows.questEvents, "questId")) {
    guarded(() => {
      const questId = text(entry.object.questId);
      const event = {
        phase: numberValue(entry.object.phase, "phase", entry.row, true),
        type: numberValue(entry.object.type, "type", entry.row, true),
      };
      optionalAssign(event, "choiceEventId", optionalText(entry.object.choiceEventId));
      if (!questEventGroups.has(questId)) questEventGroups.set(questId, []);
      questEventGroups.get(questId).push(event);
    });
  }

  const clues = rows.clues.map(({ object: x }) => ({
    id: text(x.id),
    title: text(x.title),
    ...(optionalText(x.description) ? { description: optionalText(x.description) } : {}),
  }));
  ensureUnique(clues, "手掛かり");
  const clueIds = new Set(clues.map((x) => x.id));

  const quests = rows.quests.map(({ object: x, row }) => guarded(() => {
    const id = text(x.id);
    const item = {
      id,
      questName: text(x.questName),
      rank: numberValue(x.rank, "rank", row, true),
      totalPhases: numberValue(x.totalPhases, "totalPhases", row, true),
      phasesPerTurn: numberValue(x.phasesPerTurn, "phasesPerTurn", row, true),
      rewardGold: numberValue(x.rewardGold, "rewardGold", row, true),
      rewardGuildPoints: numberValue(x.rewardGuildPoints, "rewardGuildPoints", row, true),
      rewardExp: numberValue(x.rewardExp, "rewardExp", row, true),
    };
    optionalAssign(item, "clientName", optionalText(x.clientName));
    optionalAssign(item, "description", optionalText(x.description));
    optionalAssign(item, "isStoryQuest", optionalBool(x.isStoryQuest, "isStoryQuest", row));
    optionalAssign(item, "storyArcId", optionalText(x.storyArcId));
    optionalAssign(item, "storyArcTitle", optionalText(x.storyArcTitle));
    const requiredQuestIds = idList(x.requiredQuestIds);
    const requiredClueIds = idList(x.requiredClueIds);
    const grantedClueIds = idList(x.grantedClueIds);
    if (requiredQuestIds.length > 0) item.requiredQuestIds = requiredQuestIds;
    if (requiredClueIds.length > 0) item.requiredClueIds = requiredClueIds;
    if (grantedClueIds.length > 0) item.grantedClueIds = grantedClueIds;
    optionalAssign(item, "storyBranchId", optionalText(x.storyBranchId));
    optionalAssign(item, "isEmergencyQuest", optionalBool(x.isEmergencyQuest, "isEmergencyQuest", row));
    optionalAssign(item, "rankUpOnClear", optionalNumber(x.rankUpOnClear, "rankUpOnClear", row, true));
    optionalAssign(item, "requiredGuildPoints", optionalNumber(x.requiredGuildPoints, "requiredGuildPoints", row, true));
    item.dungeonId = text(x.dungeonId);
    item.bossEnemyId = text(x.bossEnemyId);
    item.bossPhase = numberValue(x.bossPhase, "bossPhase", row, true);
    optionalAssign(item, "bossDropsAreGuaranteed", optionalBool(x.bossDropsAreGuaranteed, "bossDropsAreGuaranteed", row));
    optionalAssign(item, "gatherItemName", optionalText(x.gatherItemName));
    optionalAssign(item, "gatherTargetCount", optionalNumber(x.gatherTargetCount, "gatherTargetCount", row, true));
    optionalAssign(item, "gatherMinPerEvent", optionalNumber(x.gatherMinPerEvent, "gatherMinPerEvent", row, true));
    optionalAssign(item, "gatherMaxPerEvent", optionalNumber(x.gatherMaxPerEvent, "gatherMaxPerEvent", row, true));
    optionalAssign(item, "gatherChance", optionalNumber(x.gatherChance, "gatherChance", row));
    optionalAssign(item, "gatherGoldPerItem", optionalNumber(x.gatherGoldPerItem, "gatherGoldPerItem", row, true));
    item.bossDrops = questRewardGroups.get(id) ?? [];
    item.fixedEvents = questEventGroups.get(id) ?? [];
    if (item.dungeonId) assertRef(dungeonIds, item.dungeonId, "クエスト.dungeonId", row);
    if (item.bossEnemyId) assertRef(refs.enemyUnits, item.bossEnemyId, "クエスト.bossEnemyId", row);
    return item;
  })).filter(Boolean);
  ensureUnique(quests, "クエスト");
  const questIds = new Set(quests.map((x) => x.id));
  for (const quest of quests) {
    for (const requiredQuestId of quest.requiredQuestIds ?? []) {
      if (!questIds.has(requiredQuestId)) {
        errors.push(`${quest.id}: 不正なrequiredQuestId「${requiredQuestId}」`);
      }
    }
    for (const clueId of [...(quest.requiredClueIds ?? []), ...(quest.grantedClueIds ?? [])]) {
      if (!clueIds.has(clueId)) errors.push(`${quest.id}: 不正なclueId「${clueId}」`);
    }
  }
  for (const entry of rows.choiceOptions) {
    if (!isBlank(entry.object.grantedClueId))
      assertRef(clueIds, entry.object.grantedClueId, "選択肢.grantedClueId", entry.row);
  }
  for (const entry of rows.questRewards) assertRef(questIds, entry.object.questId, "クエスト報酬.questId", entry.row);
  for (const entry of rows.questEvents) {
    assertRef(questIds, entry.object.questId, "クエスト固定イベント.questId", entry.row);
    if (text(entry.object.type) === "7" || text(entry.object.type) === "ForceChoice")
      assertRef(choiceEventIds, entry.object.choiceEventId, "クエスト固定イベント.choiceEventId", entry.row);
  }

  const eventGroups = new Map();
  for (const entry of orderRows(rows.dungeonEvents, "dungeonId")) {
    guarded(() => {
      const dungeonId = text(entry.object.dungeonId);
      if (!eventGroups.has(dungeonId)) eventGroups.set(dungeonId, {});
      eventGroups.get(dungeonId)[text(entry.object.eventType)] =
        numberValue(entry.object.weight, "weight", entry.row, true);
    });
  }
  const encounterGroups = new Map();
  for (const entry of orderRows(rows.dungeonEncounters, "dungeonId")) {
    guarded(() => {
      const dungeonId = text(entry.object.dungeonId);
      assertRef(refs.enemyUnits, entry.object.unitId, "ダンジョン遭遇.unitId", entry.row);
      const encounter = {
        unitId: text(entry.object.unitId),
        weight: numberValue(entry.object.weight, "weight", entry.row, true),
        minPhase: numberValue(entry.object.minPhase, "minPhase", entry.row, true),
        maxPhase: numberValue(entry.object.maxPhase, "maxPhase", entry.row, true),
      };
      if (!encounterGroups.has(dungeonId)) encounterGroups.set(dungeonId, []);
      encounterGroups.get(dungeonId).push(encounter);
    });
  }
  const dungeonRewardGroups = new Map();
  for (const entry of orderRows(rows.dungeonRewards, "dungeonId")) {
    guarded(() => {
      const dungeonId = text(entry.object.dungeonId);
      const reward = buildReward(entry.object, entry.row);
      rewardRefCheck(reward, entry.row, "ダンジョン報酬");
      if (!dungeonRewardGroups.has(dungeonId)) dungeonRewardGroups.set(dungeonId, []);
      dungeonRewardGroups.get(dungeonId).push(reward);
    });
  }
  const turnEventGroups = new Map();
  for (const entry of orderRows(rows.dungeonTurnEvents, "dungeonId")) {
    guarded(() => {
      const dungeonId = text(entry.object.dungeonId);
      assertRef(refs.choiceEvents, entry.object.eventId, "ダンジョン終了イベント.eventId", entry.row);
      if (!turnEventGroups.has(dungeonId)) turnEventGroups.set(dungeonId, []);
      turnEventGroups.get(dungeonId).push(text(entry.object.eventId));
    });
  }
  for (const [rowKey, label] of [
    ["dungeonEvents", "ダンジョンイベント"],
    ["dungeonEncounters", "ダンジョン遭遇"],
    ["dungeonRewards", "ダンジョン報酬"],
    ["dungeonTurnEvents", "ダンジョン終了イベント"],
  ]) {
    for (const entry of rows[rowKey]) assertRef(dungeonIds, entry.object.dungeonId, `${label}.dungeonId`, entry.row);
  }
  for (const dungeon of dungeons) {
    dungeon.eventTable = eventGroups.get(dungeon.id) ?? {};
    dungeon.encounterTable = encounterGroups.get(dungeon.id) ?? [];
    dungeon.treasureTable = dungeonRewardGroups.get(dungeon.id) ?? [];
    dungeon.turnEndEventIds = turnEventGroups.get(dungeon.id) ?? [];
  }

  const adventurers = rows.adventurers.map(({ object: x, row }) => guarded(() => {
    const item = {
      id: text(x.id),
      baseName: text(x.baseName),
      defaultLevel: numberValue(x.defaultLevel, "defaultLevel", row, true),
      defaultRank: numberValue(x.defaultRank, "defaultRank", row, true),
    };
    optionalAssign(item, "recruitGuildRank", optionalNumber(x.recruitGuildRank, "recruitGuildRank", row, true));
    optionalAssign(item, "recruitWeight", optionalNumber(x.recruitWeight, "recruitWeight", row, true));
    item.vitality = numberValue(x.vitality, "vitality", row, true);
    item.mental = numberValue(x.mental, "mental", row, true);
    item.strength = numberValue(x.strength, "strength", row, true);
    item.agility = numberValue(x.agility, "agility", row, true);
    item.intelligence = numberValue(x.intelligence, "intelligence", row, true);
    item.constitution = numberValue(x.constitution, "constitution", row, true);
    item.appearance = numberValue(x.appearance, "appearance", row, true);
    optionalAssign(item, "gender", optionalText(x.gender));
    item.defaultClassId = text(x.defaultClassId);
    item.raceId = text(x.raceId);
    item.defaultWeaponId = text(x.defaultWeaponId);
    item.defaultArmorId = text(x.defaultArmorId);
    item.skillIds = [x.skillId1, x.skillId2, x.skillId3, x.skillId4, x.skillId5, x.skillId6]
      .map(optionalText).filter(Boolean);
    optionalAssign(item, "rarity", optionalText(x.rarity));
    for (const key of [
      "background", "personality", "motivation", "specialty",
      "fear", "creed", "selfIntroduction",
    ]) {
      optionalAssign(item, key, optionalText(x[key]));
    }
    assertRef(refs.classes, item.defaultClassId, "冒険者.defaultClassId", row);
    assertRef(refs.races, item.raceId, "冒険者.raceId", row);
    if (item.defaultWeaponId) assertRef(weaponIds, item.defaultWeaponId, "冒険者.defaultWeaponId", row);
    if (item.defaultArmorId) assertRef(armorIds, item.defaultArmorId, "冒険者.defaultArmorId", row);
    for (const id of item.skillIds) assertRef(skillIds, id, "冒険者.skillId", row);
    return item;
  })).filter(Boolean);
  ensureUnique(adventurers, "冒険者");

  const reconstructed = {
    skills,
    classes,
    races,
    equipment,
    consumables,
    relics,
    facilities,
    enemies,
    choice_events: choiceEvents,
    enemy_units: enemyUnits,
    dungeons,
    clues,
    quests,
    adventurers,
  };
  if (errors.length > 0) {
    console.error(`VALIDATION_ERRORS=${errors.length}`);
    errors.forEach((error) => console.error(`- ${error}`));
    process.exit(2);
  }

  let allMatch = true;
  const changedNames = new Set();
  for (const name of masterFiles) {
    const original = await readJson(name);
    const match = JSON.stringify(stable(original)) === JSON.stringify(stable(reconstructed[name]));
    console.log(`${name}: rows=${reconstructed[name].length} roundtrip=${match ? "MATCH" : "CHANGED"}`);
    if (!match) console.log(`FIRST_DIFF=${firstDifference(original, reconstructed[name])}`);
    if (!match) {
      allMatch = false;
      changedNames.add(name);
    }
  }
  console.log(`ROUNDTRIP_MATCH=${allMatch}`);
  console.log(`DIFF_FILES=${[...changedNames].join(",") || "(none)"}`);

  if (writeMode) {
    const stamp = new Date().toISOString().replace(/[-:]/g, "").replace(/\..+/, "").replace("T", "_");
    const backupDir = path.join(outputDir, "backups", stamp);
    await fs.mkdir(backupDir, { recursive: true });
    for (const name of masterFiles) {
      const target = path.join(dataDir, `${name}.json`);
      await fs.copyFile(target, path.join(backupDir, `${name}.json`));
      if (!changedNames.has(name)) continue;
      const json = `${JSON.stringify(reconstructed[name], null, 2).replace(/\n/g, "\r\n")}\r\n`;
      await fs.writeFile(target, json, "utf8");
    }
    console.log(`SAVED=${[...changedNames].map((name) => `${name}.json`).join(",") || "(none)"}`);
    console.log(`BACKUP_DIR=${backupDir}`);
  }
  return reconstructed;
};

const mergeMissingObjectFields = (current, migrated) => {
  if (Array.isArray(migrated)) return migrated;
  if (!migrated || typeof migrated !== "object") return migrated;
  const result = current && typeof current === "object" && !Array.isArray(current)
    ? { ...current }
    : {};
  for (const [key, value] of Object.entries(migrated)) {
    result[key] = value && typeof value === "object" && !Array.isArray(value)
      ? mergeMissingObjectFields(current?.[key], value)
      : value;
  }
  return result;
};

const mergeMigratedMasterData = (current, migrated) => Object.fromEntries(
  masterFiles.map((name) => {
    const currentItems = current[name] ?? [];
    const migratedItems = migrated[name] ?? [];
    const currentById = new Map(currentItems.map((item) => [item.id, item]));
    const migratedIds = new Set(migratedItems.map((item) => item.id));
    const merged = migratedItems.map((item) =>
      mergeMissingObjectFields(currentById.get(item.id), item));
    for (const item of currentItems) {
      if (!migratedIds.has(item.id)) merged.push(item);
    }
    return [name, merged];
  }),
);

const migrateWorkbook = async () => {
  const migrated = await importWorkbook(false, true);
  const currentEntries = await Promise.all(masterFiles.map(async (name) => [name, await readJson(name)]));
  const current = Object.fromEntries(currentEntries);
  const merged = mergeMigratedMasterData(current, migrated);
  const stamp = new Date().toISOString().replace(/[-:]/g, "").replace(/\..+/, "").replace("T", "_");
  const backupDir = path.join(outputDir, "workbook-backups", stamp);
  await fs.mkdir(backupDir, { recursive: true });
  await fs.copyFile(workbookPath, path.join(backupDir, path.basename(workbookPath)));
  await exportWorkbook(merged, false, false);
  console.log(`MIGRATED_SCHEMA_VERSION=${workbookSchemaVersion}`);
  console.log(`WORKBOOK_BACKUP_DIR=${backupDir}`);
  process.exit(0);
};

if (command === "export") {
  await exportWorkbook();
} else if (command === "check") {
  await importWorkbook(false);
} else if (command === "diff") {
  await importWorkbook(false);
} else if (command === "import") {
  await importWorkbook(true);
} else if (command === "migrate") {
  await migrateWorkbook();
} else {
  throw new Error("Usage: master-data-tool.mjs export|check|diff|import|migrate");
}

process.exit(0);
