import fs from "node:fs";
import path from "node:path";

const scriptDir = path.dirname(new URL(import.meta.url).pathname).replace(/^\/([A-Za-z]:)/, "$1");
const repoRoot = path.resolve(scriptDir, "..");
const dataDir = path.join(repoRoot, "GuildSimulator.Game", "Data");

const masterFiles = [
  "skills.json", "classes.json", "races.json", "equipment.json", "consumables.json",
  "relics.json", "facilities.json", "enemies.json", "choice_events.json", "enemy_units.json",
  "dungeons.json", "clues.json", "quests.json", "adventurers.json",
];

const requested = process.argv.slice(2);
const targets = requested.length === 0
  ? masterFiles
  : requested.map((name) => (name.endsWith(".json") ? name : `${name}.json`));

for (const fileName of targets) {
  console.log(`=== ${fileName} ===`);
  if (!masterFiles.includes(fileName)) {
    console.error(`  不明なマスターファイル名です: ${fileName}`);
    continue;
  }
  const filePath = path.join(dataDir, fileName);
  try {
    const raw = fs.readFileSync(filePath, "utf8");
    const data = JSON.parse(raw);
    console.log(JSON.stringify(data, null, 2));
  } catch (err) {
    console.error(`  読み込みに失敗しました: ${err.message}`);
  }
  console.log("");
}
