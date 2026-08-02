namespace GuildSimulator.Core.Models;

/// <summary>
/// 武器クラスの個性。basePv・damageDice・maxStatBonus・命中補正だけでは表しきれない
/// 「その得物にしかできないこと」をここにまとめる。
///
///   短剣 : critRange と extraAttacks（会心しやすく、手数で押す）、offHandBonus（左手で扱いやすい）
///   槍   : armorPierce（相手の装甲を無視して突く）
///   斧   : armorShred（当てるたびに相手の装甲そのものを削る）
///   剣   : どれも0（尖った長所がない代わりに短所もない）
///
/// 武器そのものの値（<see cref="MasterData.EquipmentMasterData.Traits"/>）と
/// スキル・遺物由来の補正（<see cref="StatBlock"/>）を足し合わせて実効値にする。
/// </summary>
public readonly record struct WeaponTraits(
    int armorPierce, int armorShred, int critRange, int extraAttacks, int offHandBonus = 0)
{
    public static readonly WeaponTraits None = default;

    /// <summary>スキル・遺物由来の補正を上乗せした実効値。負の値は切り上げて0にする。</summary>
    public WeaponTraits Combine(StatBlock bonus) => new(
        Math.Max(0, armorPierce + bonus.armorPierce),
        Math.Max(0, armorShred + bonus.armorShred),
        Math.Max(0, critRange + bonus.critRange),
        Math.Max(0, extraAttacks + bonus.extraAttacks),
        // 左手の発動率だけはスキル側（offHandChance）と足し合わせる先が違うので、ここでは持ち回るだけ。
        Math.Max(0, offHandBonus));
}
