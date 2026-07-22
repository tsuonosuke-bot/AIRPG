using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Quest;

/// <summary>
/// クエスト（＝ダンジョン構成）から戦闘の激しさを見積もり、受注前の判断材料を出す。
/// マスターデータのみから算出し、実プレイの乱数には依存しない目安値。
/// </summary>
public static class DungeonDifficulty
{
    public class Rating
    {
        public int stars;            // 1..5
        public string label = "";
        public float combatChance;   // 1フェーズあたりの敵遭遇率 0..1
        public float trapChance;     // 1フェーズあたりの罠率 0..1
        public int enemyLevelMin;
        public int enemyLevelMax;
        public bool hasBoss;
        public int bossLevel;
        public float expectedFights; // 見込み戦闘回数（ボス除く）

        public string StarBar => new string('★', stars) + new string('☆', 5 - stars);

        public string EnemyLevelRange =>
            enemyLevelMax <= 0 ? "―"
            : enemyLevelMin == enemyLevelMax ? $"Lv{enemyLevelMin}"
            : $"Lv{enemyLevelMin}〜{enemyLevelMax}";
    }

    public static Rating Evaluate(QuestMasterData q)
    {
        var r = new Rating();
        var d = q.Dungeon;

        // --- イベント構成から戦闘率・罠率 ---
        if (d != null && d.eventTable.Count > 0)
        {
            int total = 0;
            foreach (var w in d.eventTable.Values) if (w > 0) total += w;
            if (total > 0)
            {
                d.eventTable.TryGetValue(DungeonEventType.EnemyEncounter, out int enc);
                d.eventTable.TryGetValue(DungeonEventType.Trap, out int trap);
                r.combatChance = (float)Math.Max(0, enc) / total;
                r.trapChance = (float)Math.Max(0, trap) / total;
            }
        }
        r.expectedFights = r.combatChance * q.totalPhases;

        // --- 敵レベル帯（深部ほどスケールする分を上限に反映）---
        if (d != null && d.encounterTable.Count > 0)
        {
            var units = d.encounterTable.Where(e => e.Unit != null).Select(e => e.Unit!).ToList();
            if (units.Count > 0)
            {
                int baseMin = units.Min(u => u.baseLevel);
                int baseMax = units.Max(u => u.baseLevel);
                int scale = (int)Math.Floor((q.totalPhases - 1) * d.enemyLevelPerPhase);
                r.enemyLevelMin = Math.Max(1, baseMin);
                r.enemyLevelMax = Math.Max(r.enemyLevelMin, baseMax + Math.Max(0, scale));
            }
        }

        // --- ボス ---
        r.hasBoss = q.BossEnemy != null;
        r.bossLevel = q.BossEnemy?.baseLevel ?? 0;

        // --- 総合スコア → 星（1..5）---
        // クエストランクを土台に、戦闘頻度・罠・敵Lv・ボスで上積みする。
        double score =
            q.rank * 4.0
          + r.combatChance * 100 * 0.35
          + r.trapChance * 100 * 0.20
          + r.enemyLevelMax * 1.2
          + (r.hasBoss ? 8 : 0);

        r.stars = score switch
        {
            < 18 => 1,
            < 26 => 2,
            < 34 => 3,
            < 42 => 4,
            _ => 5,
        };
        r.label = r.stars switch
        {
            1 => "楽勝",
            2 => "軽め",
            3 => "標準",
            4 => "危険",
            _ => "過酷",
        };
        return r;
    }
}
