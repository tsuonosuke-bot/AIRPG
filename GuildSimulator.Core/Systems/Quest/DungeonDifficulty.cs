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
        public int enemyThreatMin;
        public int enemyThreatMax;
        public bool hasBoss;
        public int bossThreat;
        public float expectedFights; // 見込み戦闘回数（ボス除く）

        /// <summary>出現する敵の脅威度の帯（F〜S）。</summary>
        public string EnemyThreatRange =>
            enemyThreatMax <= 0 ? "―"
            : enemyThreatMin == enemyThreatMax ? Rank.Label(enemyThreatMin)
            : $"{Rank.Label(enemyThreatMin)}〜{Rank.Label(enemyThreatMax)}";

        public string BossThreatLabel => Rank.Label(bossThreat);
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

        // --- 敵の脅威度帯 ---
        // 倍率でのスケーリングは廃止したので、出現しうる敵ユニットの脅威度をそのまま見る。
        if (d != null && d.encounterTable.Count > 0)
        {
            var units = d.encounterTable.Where(e => e.Unit != null).Select(e => e.Unit!).ToList();
            if (units.Count > 0)
            {
                r.enemyThreatMin = Rank.Clamp(units.Min(u => u.Threat));
                r.enemyThreatMax = Rank.Clamp(units.Max(u => u.Threat));
            }
        }

        // --- ボス ---
        r.hasBoss = q.BossEnemy != null;
        if (r.hasBoss) r.bossThreat = Rank.Clamp(q.BossEnemy!.Threat);

        // --- 総合スコア ---
        // クエストランクを土台に、戦闘頻度・罠・敵の脅威度・ボスで上積みする。
        // 段階数に縛られる星評価はやめ、連続値のスコアとラベルで表す。
        // 脅威度はF〜Sの7段階しかなく、旧来の敵レベル（青天井）より粗いので係数を上げてある。
        r.score =
            q.rank * 4.0
          + r.combatChance * 100 * 0.35
          + r.trapChance * 100 * 0.20
          + r.enemyThreatMax * 3.0
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
