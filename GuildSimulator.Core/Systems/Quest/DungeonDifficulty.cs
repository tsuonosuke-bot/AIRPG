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
        public double score;         // 難易度の総合スコア（下限なし、段階数に縛られない目安値）
        public string label = "";
        public float combatChance;   // 1フェーズあたりの敵遭遇率 0..1
        public float trapChance;     // 1フェーズあたりの罠率 0..1
        public int enemyLevelMin;
        public int enemyLevelMax;
        public bool hasBoss;
        public int bossLevel;
        public float expectedFights; // 見込み戦闘回数（ボス除く）

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
        // ボスも bossPhase の深さでスケーリングを受ける（QuestProgressor.AdvanceOnePhase と同じ計算式）。
        r.hasBoss = q.BossEnemy != null;
        if (r.hasBoss)
        {
            int bossScale = d != null ? (int)Math.Floor((q.bossPhase - 1) * d.enemyLevelPerPhase) : 0;
            r.bossLevel = Math.Max(1, q.BossEnemy!.baseLevel + Math.Max(0, bossScale));
        }

        // --- 総合スコア ---
        // クエストランクを土台に、戦闘頻度・罠・敵Lv・ボスで上積みする。
        // 段階数に縛られる星評価はやめ、連続値のスコアとラベルで表す。
        r.score =
            q.rank * 4.0
          + r.combatChance * 100 * 0.35
          + r.trapChance * 100 * 0.20
          + r.enemyLevelMax * 1.2
          + (r.hasBoss ? 8 : 0);

        r.label = r.score switch
        {
            < 18 => "楽勝",
            < 26 => "軽め",
            < 34 => "標準",
            < 42 => "危険",
            _ => "過酷",
        };
        return r;
    }
}
