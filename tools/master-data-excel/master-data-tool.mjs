import fs from "node:fs/promises";
import path from "node:path";
import { FileBlob, SpreadsheetFile, Workbook } from "@oai/artifact-tool";

const command = process.argv[2] ?? "export";
const scriptDir = path.dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, "$1");
const repoRoot = path.resolve(scriptDir, "../..");
const dataDir = path.join(repoRoot, "GuildSimulator.Game", "Data");
const outputDir = path.join(repoRoot, "outputs", "master-data-editor");
const workbookPath = path.join(outputDir, "マスタデータ統合_編集用.xlsx");
const previewDir = path.join(outputDir, "previews");
const masterFiles = ["adventurers", "equipment", "skills", "consumables", "clues", "quests", "dungeons"];
const statKeys = [
  "hp", "san", "av", "mav", "pv", "mpv", "dv", "toHit", "heal",
  // 武器クラスの個性。スキル・遺物・装備の補正として足せる。
  "armorPierce", "armorShred", "critRange", "extraAttacks",
  // 二刀流の発動率と盾の受け率。スキルから伸ばせる。
  "offHandChance", "blockChance",
];
// 武器そのものが持つ個性の列。武器クラスごとに固定で、Tierでは変えない。
const weaponTraitKeys = ["armorPierce", "armorShred", "critRange", "extraAttacks", "offHandBonus"];
// 両手武器と盾。盾の装甲は blockAv にだけ書く（bonus_av に書くと常時加算になる）。
const handKeys = ["isTwoHanded", "blockChance", "blockAv"];
// AV/DV/PVは1点が重いので倍率では触らない。mul列はこの3つだけを扱う。
const mulKeys = ["hp", "san", "heal"];
const rewardKeys = [
  "type", "relicId", "equipmentId", "skillId", "consumableId",
  "gold", "weight", "chance", "quantity", "unique",
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

const clean = (value) => (value === undefined || value === null ? null : value);
const mapValue = (object, key) => clean(object?.[key]);
const flattenReward = (parentId, order, reward) => [
  parentId,
  order,
  ...rewardKeys.map((key) => mapValue(reward, key)),
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
      "id", "baseName", "upkeepGold", "defaultLevel", "defaultRank",
      "recruitGuildRank", "recruitWeight", "rarity",
      "vitality", "mental", "strength", "agility", "intelligence", "constitution", "appearance",
      "defaultClassId", "raceId", "defaultWeaponId", "defaultArmorId",
      "skillId1", "skillId2", "skillId3", "skillId4", "skillId5", "skillId6",
      "background", "personality", "motivation", "specialty", "fear", "creed", "selfIntroduction",
    ],
    labels: [
      "ID", "名前", "維持費", "初期Lv", "初期ランク(1=F〜7=S)",
      "採用ギルドランク(1=F〜7=S)", "採用重み", "レアリティ",
      "生命力", "精神力", "筋力", "敏捷", "知力", "体格", "容姿",
      "初期職業", "種族", "初期武器", "初期防具",
      "スキル1", "スキル2", "スキル3", "スキル4", "スキル5", "スキル6",
      "経歴", "性格", "動機", "得意分野", "苦手・恐怖", "信条", "自己紹介",
    ],
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
  skills: {
    name: "スキル",
    title: "スキルマスタ",
    capacity: 140,
    unique: true,
    keys: [
      "id", "skillName", "scope", "frontOnly", "backOnly",
      "requireWeaponType", "requiredWeaponType", "requireArmorType", "requiredArmorType",
      ...statKeys.map((key) => `add_${key}`),
      ...mulKeys.map((key) => `mul_${key}`),
    ],
    labels: [
      "ID", "スキル名", "範囲", "前衛限定", "後衛限定",
      "武器条件あり", "必要武器種", "防具条件あり", "必要防具種",
      ...statKeys.map((key) => `加算 ${key}`),
      ...mulKeys.map((key) => `倍率 ${key}`),
    ],
  },
  consumables: {
    name: "道具",
    title: "消費アイテムマスタ",
    capacity: 100,
    unique: true,
    keys: ["id", "displayName", "description", "rarity", "price", "effectType", "effectValue"],
    labels: ["ID", "表示名", "説明", "レアリティ", "価格", "効果種別", "効果値"],
  },
  clues: {
    name: "手掛かり",
    title: "物語の手掛かりマスタ",
    capacity: 120,
    unique: true,
    keys: ["id", "title", "description"],
    labels: ["ID", "名称", "説明"],
  },
  quests: {
    name: "クエスト",
    title: "クエストマスタ",
    capacity: 120,
    unique: true,
    keys: [
      "id", "questName", "clientName", "description", "isStoryQuest",
      "requiredQuestIds", "requiredClueIds", "grantedClueIds", "storyBranchId",
      "rank", "totalPhases", "phasesPerTurn",
      "rewardGold", "rewardGuildPoints", "rewardExp",
      "isEmergencyQuest", "rankUpOnClear", "requiredGuildPoints",
      "dungeonId", "bossEnemyId", "bossPhase", "bossDropsAreGuaranteed",
      "gatherItemName", "gatherTargetCount", "gatherMinPerEvent", "gatherMaxPerEvent",
      "gatherChance", "gatherGoldPerItem",
    ],
    labels: [
      "ID", "クエスト名", "依頼人", "依頼文", "物語クエスト",
      "必要クエストID（カンマ区切り）", "必要手掛かりID（カンマ区切り）",
      "獲得手掛かりID（カンマ区切り）", "分岐ID",
      "ランク(1=F〜7=S)", "総フェーズ", "ターン毎フェーズ",
      "報酬Gold", "Guildポイント", "経験値",
      "緊急クエスト", "クリア時RankUp", "必要Guildポイント",
      "ダンジョン", "ボス敵", "ボスフェーズ", "ボス報酬確定",
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
      "道具ID", "Gold", "重み", "確率", "数量", "ユニーク",
    ],
  },
  questEvents: {
    name: "クエスト固定イベント",
    title: "クエスト 固定イベント明細",
    capacity: 240,
    unique: false,
    keys: ["questId", "order", "phase", "type"],
    labels: ["クエストID", "順序", "フェーズ", "イベント種別"],
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
    labels: ["ダンジョンID", "順序", "敵ユニットID", "重み", "最小フェーズ", "最大フェーズ"],
  },
  dungeonRewards: {
    name: "ダンジョン報酬",
    title: "ダンジョン 宝箱明細",
    capacity: 360,
    unique: false,
    keys: ["dungeonId", "order", ...rewardKeys],
    labels: [
      "ダンジョンID", "順序", "報酬種別", "レリックID", "装備ID",
      "スキルID", "道具ID", "Gold", "重み", "確率", "数量", "ユニーク",
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
    a.id, a.baseName, a.upkeepGold, a.defaultLevel, a.defaultRank,
    clean(a.recruitGuildRank), clean(a.recruitWeight), clean(a.rarity),
    a.vitality, a.mental, a.strength, a.agility, a.intelligence, a.constitution, a.appearance,
    clean(a.defaultClassId), clean(a.raceId), clean(a.defaultWeaponId), clean(a.defaultArmorId),
    ...Array.from({ length: 6 }, (_, index) => clean(a.skillIds?.[index])),
    clean(a.background), clean(a.personality), clean(a.motivation), clean(a.specialty),
    clean(a.fear), clean(a.creed), clean(a.selfIntroduction),
  ]);
  for (const a of data.adventurers) {
    if ((a.skillIds?.length ?? 0) > 6) throw new Error(`${a.id}: スキル数が6件を超えています。`);
  }

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

  const skills = data.skills.map((s) => [
    s.id, s.skillName, s.scope, s.frontOnly, s.backOnly,
    s.requireWeaponType, clean(s.requiredWeaponType),
    s.requireArmorType, clean(s.requiredArmorType),
    ...statKeys.map((key) => mapValue(s.add, key)),
    ...mulKeys.map((key) => mapValue(s.mul, key)),
  ]);

  const consumables = data.consumables.map((c) => [
    c.id, c.displayName, clean(c.description), clean(c.rarity),
    c.price, c.effectType, c.effectValue,
  ]);

  const clues = data.clues.map((clue) => [
    clue.id, clue.title, clean(clue.description),
  ]);

  const quests = data.quests.map((q) => [
    q.id, q.questName, clean(q.clientName), clean(q.description), clean(q.isStoryQuest),
    clean(q.requiredQuestIds?.join(", ")), clean(q.requiredClueIds?.join(", ")),
    clean(q.grantedClueIds?.join(", ")), clean(q.storyBranchId),
    q.rank, q.totalPhases, q.phasesPerTurn,
    q.rewardGold, q.rewardGuildPoints, q.rewardExp,
    clean(q.isEmergencyQuest), clean(q.rankUpOnClear), clean(q.requiredGuildPoints),
    clean(q.dungeonId), clean(q.bossEnemyId), q.bossPhase, clean(q.bossDropsAreGuaranteed),
    clean(q.gatherItemName), q.gatherTargetCount, q.gatherMinPerEvent, q.gatherMaxPerEvent,
    q.gatherChance, q.gatherGoldPerItem,
  ]);
  const questRewards = data.quests.flatMap((q) =>
    (q.bossDrops ?? []).map((reward, index) => [q.id, index + 1, ...rewardKeys.map((key) => mapValue(reward, key))]));
  const questEvents = data.quests.flatMap((q) =>
    (q.fixedEvents ?? []).map((event, index) => [q.id, index + 1, event.phase, event.type]));

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
    equipment,
    skills,
    consumables,
    clues,
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
    if (key.endsWith("Id") || key === "id" || key.startsWith("skillId")) width = 20;
    if (key === "入力チェック") width = 14;
    sheet.getRange(`${column}:${column}`).format.columnWidth = width;
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
  sheet.getRange("A3:C18").values = [
    ["区分", "編集シート", "説明"],
    ["主要", "冒険者", "基本値、職業・種族・装備、初期スキル、人物プロフィール"],
    ["主要", "装備", "武器・防具、係数、価格、重量、bonus各種"],
    ["主要", "スキル", "装備条件、対象範囲、add/mul各種"],
    ["主要", "道具", "消費アイテムの説明、価格、効果"],
    ["主要", "手掛かり", "物語クエストで発見し、後続クエストの解禁に使う調査情報"],
    ["主要", "クエスト", "依頼文、物語条件、報酬など。ボス報酬と固定イベントは下記明細シート"],
    ["明細", "クエスト報酬", "questIdとorderでボス報酬配列を構成"],
    ["明細", "クエスト固定イベント", "questIdとorderでfixedEvents配列を構成"],
    ["主要", "ダンジョン", "ダンジョン本体"],
    ["明細", "ダンジョンイベント", "eventTableのイベント種別と重み"],
    ["明細", "ダンジョン遭遇", "encounterTableの敵ユニットと出現範囲"],
    ["明細", "ダンジョン報酬", "treasureTable（宝箱）の中身と重み"],
    ["明細", "ダンジョン終了イベント", "turnEndEventIdsの順序付き一覧"],
    ["参照", "参照マスター", "職業・種族・敵・レリックなど、編集対象外IDの参照用"],
    ["共通", "入力チェック", "ID重複の簡易表示。JSON保存時にはツールが全参照を再検証"],
  ];
  sheet.getRange("A3:C3").format = {
    fill: colors.blue,
    font: { bold: true, color: colors.white, name: bodyFont },
  };
  sheet.getRange("A4:A18").format = {
    fill: colors.paleBlue,
    font: { bold: true, color: colors.navy, name: bodyFont },
  };
  sheet.getRange("A3:C18").format.borders = {
    insideHorizontal: { style: "thin", color: colors.lightGray },
    outside: { style: "thin", color: "#9FB4C3" },
  };
  sheet.getRange("A:A").format.columnWidth = 12;
  sheet.getRange("B:B").format.columnWidth = 28;
  sheet.getRange("C:C").format.columnWidth = 68;
  sheet.getRange("C3:C18").format.wrapText = true;
  sheet.getRange("A3:C18").format.font = { name: bodyFont, size: 10 };
  sheet.getRange("A3:C18").format.autofitRows();
  sheet.freezePanes.freezeRows(3);
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
  ];
  sheet.getRange("A1:R1").merge();
  sheet.getRange("A1").values = [["参照専用マスタ（このExcelからはJSONへ書き戻しません）"]];
  sheet.getRange("A1:R1").format = {
    fill: colors.navy,
    font: { bold: true, color: colors.white, size: 14, name: bodyFont },
  };
  for (const [start, title, headers, rows] of blocks) {
    const startIndex = start.charCodeAt(0) - 64;
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

const exportWorkbook = async () => {
  const entries = await Promise.all(masterFiles.map(async (name) => [name, await readJson(name)]));
  const data = Object.fromEntries(entries);
  const refs = {
    classes: await readJson("classes"),
    races: await readJson("races"),
    enemies: await readJson("enemies"),
    enemyUnits: await readJson("enemy_units"),
    relics: await readJson("relics"),
    choiceEvents: await readJson("choice_events"),
  };
  const rowsBySheet = makeRows(data);
  const workbook = Workbook.create();
  workbook.comments.setSelf({ displayName: "User" });
  addGuide(workbook);
  const sheets = {};
  let tableIndex = 1;
  for (const [key, definition] of Object.entries(sheetDefinitions)) {
    sheets[key] = writeDataSheet(workbook, definition, rowsBySheet[key], `MasterTable${tableIndex}`);
    tableIndex += 1;
  }
  addReferences(workbook, refs);

  const boolFields = {
    skills: ["frontOnly", "backOnly", "requireWeaponType", "requireArmorType"],
    equipment: ["isTwoHanded"],
    quests: ["isStoryQuest", "isEmergencyQuest", "bossDropsAreGuaranteed"],
    questRewards: ["unique"],
    dungeonRewards: ["unique"],
  };
  for (const [key, fields] of Object.entries(boolFields)) {
    for (const field of fields) addValidation(sheets[key], sheetDefinitions[key], field, ["TRUE", "FALSE"]);
  }
  const rarities = ["Common", "Uncommon", "Rare", "Unique", "Legend"];
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "rarity", rarities);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "rarity", rarities);
  addValidation(sheets.consumables, sheetDefinitions.consumables, "rarity", rarities);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "type", [0, 1, 2, 3]);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "weaponType", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
  addValidation(sheets.equipment, sheetDefinitions.equipment, "armorType", [0, 1, 2, 3]);
  // 認定ランクは F(1) 〜 S(7) の7段階。3種のランクすべてが同じ物差しに乗っている。
  const ranks = [1, 2, 3, 4, 5, 6, 7];
  addValidation(sheets.quests, sheetDefinitions.quests, "rank", ranks);
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "defaultRank", ranks);
  addValidation(sheets.adventurers, sheetDefinitions.adventurers, "recruitGuildRank", ranks);
  addValidation(sheets.skills, sheetDefinitions.skills, "scope", [0, 1]);
  addValidation(sheets.skills, sheetDefinitions.skills, "requiredWeaponType", [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
  addValidation(sheets.skills, sheetDefinitions.skills, "requiredArmorType", [0, 1, 2, 3]);
  addValidation(sheets.consumables, sheetDefinitions.consumables, "effectType", [
    "MaxHpPercent", "MoralePercent", "GoldRewardPercent", "ExpRewardPercent", "TrapDamageReductionPercent",
  ]);
  addValidation(sheets.questRewards, sheetDefinitions.questRewards, "type", [0, 1, 2, 3, 4]);
  addValidation(sheets.questEvents, sheetDefinitions.questEvents, "type", [0, 1, 2, 3, 4, 5, 6]);
  addValidation(sheets.dungeonEvents, sheetDefinitions.dungeonEvents, "eventType", [
    "EnemyEncounter", "Heal", "Trap", "Treasure", "Nothing",
  ]);
  addValidation(sheets.dungeonRewards, sheetDefinitions.dungeonRewards, "type", [0, 1, 2, 3, 4]);

  await fs.mkdir(outputDir, { recursive: true });
  await fs.mkdir(previewDir, { recursive: true });
  const overview = await workbook.inspect({
    kind: "workbook,sheet,table",
    maxChars: 9000,
    tableMaxRows: 5,
    tableMaxCols: 8,
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
    ["入力ガイド", "A1:H17"],
    ...Object.entries(sheetDefinitions).map(([key, definition]) => {
      const lastColumn = columnName(definition.keys.length + 1);
      const lastRow = Math.min(19, Math.max(8, rowsBySheet[key].length + 4));
      return [definition.name, `A1:${lastColumn}${lastRow}`];
    }),
    ["参照マスター", "A1:R19"],
  ];
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
  process.exit(0);
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

const readSheetRows = (workbook, definition) => {
  const sheet = workbook.worksheets.getItem(definition.name);
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
const buildReward = (source, row) => {
  const reward = { type: numberValue(source.type, "type", row, true) };
  for (const key of ["relicId", "equipmentId", "skillId", "consumableId"]) {
    optionalAssign(reward, key, optionalText(source[key]));
  }
  for (const key of ["gold", "weight", "quantity"]) {
    optionalAssign(reward, key, optionalNumber(source[key], key, row, true));
  }
  optionalAssign(reward, "chance", optionalNumber(source.chance, "chance", row, false));
  optionalAssign(reward, "unique", optionalBool(source.unique, "unique", row));
  return reward;
};

const stable = (value) => {
  if (Array.isArray(value)) return value.map(stable);
  if (value && typeof value === "object") {
    return Object.fromEntries(Object.keys(value).sort().map((key) => [key, stable(value[key])]));
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

const importWorkbook = async (writeMode) => {
  const workbook = await SpreadsheetFile.importXlsx(await FileBlob.load(workbookPath));
  const rows = Object.fromEntries(
    Object.entries(sheetDefinitions).map(([key, definition]) => [key, readSheetRows(workbook, definition)]),
  );
  const refs = {
    classes: new Set((await readJson("classes")).map((x) => x.id)),
    races: new Set((await readJson("races")).map((x) => x.id)),
    enemies: new Set((await readJson("enemies")).map((x) => x.id)),
    enemyUnits: new Set((await readJson("enemy_units")).map((x) => x.id)),
    relics: new Set((await readJson("relics")).map((x) => x.id)),
    choiceEvents: new Set((await readJson("choice_events")).map((x) => x.id)),
  };
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
    item.bonus = buildStatObject(x, "bonus", row);
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
      frontOnly: boolValue(x.frontOnly, "frontOnly", row),
      backOnly: boolValue(x.backOnly, "backOnly", row),
      requireWeaponType: boolValue(x.requireWeaponType, "requireWeaponType", row),
    };
    optionalAssign(item, "requiredWeaponType", optionalNumber(x.requiredWeaponType, "requiredWeaponType", row, true));
    item.requireArmorType = boolValue(x.requireArmorType, "requireArmorType", row);
    optionalAssign(item, "requiredArmorType", optionalNumber(x.requiredArmorType, "requiredArmorType", row, true));
    item.add = buildStatObject(x, "add", row);
    item.mul = buildStatObject(x, "mul", row, true);
    return item;
  })).filter(Boolean);
  ensureUnique(skills, "スキル");
  const skillIds = new Set(skills.map((x) => x.id));

  const consumables = rows.consumables.map(({ object: x, row }) => guarded(() => {
    const item = { id: text(x.id), displayName: text(x.displayName) };
    optionalAssign(item, "description", optionalText(x.description));
    optionalAssign(item, "rarity", optionalText(x.rarity));
    item.price = numberValue(x.price, "price", row, true);
    item.effectType = text(x.effectType);
    item.effectValue = numberValue(x.effectValue, "effectValue", row, true);
    return item;
  })).filter(Boolean);
  ensureUnique(consumables, "道具");
  const consumableIds = new Set(consumables.map((x) => x.id));

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
  for (const entry of rows.questRewards) assertRef(questIds, entry.object.questId, "クエスト報酬.questId", entry.row);
  for (const entry of rows.questEvents) assertRef(questIds, entry.object.questId, "クエスト固定イベント.questId", entry.row);

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
      upkeepGold: numberValue(x.upkeepGold, "upkeepGold", row, true),
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
    assertRef(weaponIds, item.defaultWeaponId, "冒険者.defaultWeaponId", row);
    assertRef(armorIds, item.defaultArmorId, "冒険者.defaultArmorId", row);
    for (const id of item.skillIds) assertRef(skillIds, id, "冒険者.skillId", row);
    return item;
  })).filter(Boolean);
  ensureUnique(adventurers, "冒険者");

  const reconstructed = { adventurers, equipment, skills, consumables, clues, quests, dungeons };
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
};

if (command === "export") {
  await exportWorkbook();
} else if (command === "check") {
  await importWorkbook(false);
} else if (command === "import") {
  await importWorkbook(true);
} else {
  throw new Error("Usage: master-data-tool.mjs export|check|import");
}

process.exit(0);
