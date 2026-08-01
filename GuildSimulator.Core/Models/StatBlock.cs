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

    /// <summary>左手の武器が攻撃に加わる確率への加算（%）。二刀流スキルがここを伸ばす。</summary>
    public int offHandChance;

    /// <summary>盾で受け止められる確率への加算（%）。盾術スキルがここを伸ばす。</summary>
    public int blockChance;

    /// <summary>
    /// 盾で受け止めたとき、相手のPVに関わらずダメージを丸ごと無効化する確率（%）。
    /// blockChance が「受けられるか」なら、こちらは「受けきれるか」。高位の盾術だけが持つ。
    /// </summary>
    public int blockNegate;

    /// <summary>
    /// 積載上限への加算。装備の重さを担ぐ余裕そのものを増やすので、
    /// 重い鎧を着ても過積載のDV・命中ペナルティを受けずに済むようになる。
    /// </summary>
    public int carry;

    /// <summary>
    /// 狙われやすさへの加算（%）。+なら囮として敵を引きつけ、-なら的にされにくくなる。
    /// 硬さ・回避による狙われにくさに掛け合わせる形で効く。
    /// </summary>
    public int threatWeight;

    /// <summary>装甲判定を無条件で1回通す確率（%）。硬い相手に手も足も出ない事故を減らす。</summary>
    public int autoPenetrate;

    /// <summary>会心したときに上乗せするPV。会心の「効き」そのものを重くする。</summary>
    public int critPv;

    /// <summary>
    /// 応急処置。HPが半分を切った瞬間に最大HPのこの割合（%）を回復する。
    /// 1回の戦闘につき1度しか発動しない。
    /// </summary>
    public int emergencyHeal;

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
        offHandChance = a.offHandChance + b.offHandChance,
        blockChance = a.blockChance + b.blockChance,
        blockNegate = a.blockNegate + b.blockNegate,
        carry = a.carry + b.carry,
        threatWeight = a.threatWeight + b.threatWeight,
        autoPenetrate = a.autoPenetrate + b.autoPenetrate,
        critPv = a.critPv + b.critPv,
        emergencyHeal = a.emergencyHeal + b.emergencyHeal,
    };
}
