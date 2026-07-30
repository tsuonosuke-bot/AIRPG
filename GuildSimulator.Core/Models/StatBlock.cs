namespace GuildSimulator.Core.Models;

/// <summary>
/// 戦闘ステータス。Caves of Qud に倣い、攻防は「装甲値(AV)・回避値(DV)・貫通値(PV)」の3本立てで表す。
/// ここに載るのは装備・スキル・レリック由来の<b>補正</b>で、能力値由来の素の値は各ユニットが持つ。
/// </summary>
public struct StatBlock
{
    public int hp;
    public int san;

    /// <summary>物理装甲値(AV)。貫通ロールがこれを超えないとダメージが通らない。</summary>
    public int av;

    /// <summary>魔法装甲値(mAV)。魔法攻撃はこちらと突き合わせる。</summary>
    public int mav;

    /// <summary>物理貫通値(PV)への加算。</summary>
    public int pv;

    /// <summary>魔法貫通値(mPV)への加算。</summary>
    public int mpv;

    /// <summary>回避値(DV)。1d20の命中判定でこの値を超えられなければ攻撃は外れる。</summary>
    public int dv;

    /// <summary>命中判定(1d20)への加算。</summary>
    public int toHit;

    public int heal;

    /// <summary>攻撃時に無視する相手の装甲値。武器クラスの個性（槍の貫通力）とスキルの合計。</summary>
    public int armorPierce;

    /// <summary>貫通に成功した攻撃1回につき、相手のAVを恒久的に削る量（斧の装甲破壊）。</summary>
    public int armorShred;

    /// <summary>会心になる1d20の出目の幅。0なら20のみ、1なら19〜20（短剣の会心しやすさ）。</summary>
    public int critRange;

    /// <summary>1手番あたりの追撃回数（短剣の連続攻撃）。追撃はPVが下がっていく。</summary>
    public int extraAttacks;

    public static StatBlock operator +(StatBlock a, StatBlock b) => new()
    {
        hp = a.hp + b.hp,
        san = a.san + b.san,
        av = a.av + b.av,
        mav = a.mav + b.mav,
        pv = a.pv + b.pv,
        mpv = a.mpv + b.mpv,
        dv = a.dv + b.dv,
        toHit = a.toHit + b.toHit,
        heal = a.heal + b.heal,
        armorPierce = a.armorPierce + b.armorPierce,
        armorShred = a.armorShred + b.armorShred,
        critRange = a.critRange + b.critRange,
        extraAttacks = a.extraAttacks + b.extraAttacks,
    };
}
