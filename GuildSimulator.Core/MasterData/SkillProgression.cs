namespace GuildSimulator.Core.MasterData;

/// <summary>
/// 段階スキル（Lv1〜Lv5）の畳み込み。
///
/// 上の段階を覚えても下の段階は失われないので、そのまま全部を足すと
/// 「Lv1〜Lv5を全部持っている」状態が正しく効いてしまい、数値が二重三重に乗る。
/// 同じ <see cref="SkillMasterData.family"/> の中では最上位の1つだけを有効にして、
/// マスタに書いた各段階の値を「その段階での最終値」として読めるようにする。
/// </summary>
public static class SkillProgression
{
    /// <summary>
    /// 同系統は最上位だけを残した一覧。family が空のスキルはすべてそのまま残る。
    /// 並び順は入力の順を保つ（表示の並びがマスタの記述順から動かないように）。
    /// </summary>
    public static void CollapseInto(IEnumerable<SkillMasterData> source, List<SkillMasterData> destination)
    {
        destination.Clear();

        // family ごとの最上位を先に決めてから、元の並びで拾い直す。
        Dictionary<string, SkillMasterData>? best = null;
        foreach (var s in source)
        {
            if (s == null || string.IsNullOrEmpty(s.family)) continue;
            best ??= new Dictionary<string, SkillMasterData>();
            if (!best.TryGetValue(s.family, out var cur) || s.level > cur.level)
                best[s.family] = s;
        }

        foreach (var s in source)
        {
            if (s == null || destination.Contains(s)) continue;
            if (!string.IsNullOrEmpty(s.family) && best != null && best[s.family] != s) continue;
            destination.Add(s);
        }
    }

    public static IReadOnlyList<SkillMasterData> Collapse(IEnumerable<SkillMasterData> source)
    {
        var list = new List<SkillMasterData>();
        CollapseInto(source, list);
        return list;
    }
}
