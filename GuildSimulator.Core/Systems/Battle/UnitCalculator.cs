using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Core.Systems.Battle;

public static class UnitCalculator
{
    // isAllySide: レリックのUnitバフは冒険者側にのみ適用する。敵側の計算では常にfalseを渡すこと。
    public static UnitStats Calc(IUnitMember?[] members, bool isAllySide)
    {
        UnitStats u = default;
        if (members == null) return u;

        StatBlock auraAdd = default;
        StatMultiplier auraMul = StatMultiplier.One;

        for (int i = 0; i < members.Length; i++)
        {
            var m = members[i];
            if (m == null || !m.IsAlive) continue;
            bool isFront = i < 3;
            var s = m.GetFinalCombatStats();
            ApplySkills(m, isFront, ref s, ref auraAdd, ref auraMul);
            u.hp += s.hp; u.san += s.san;
            u.av += s.av; u.mav += s.mav;
            u.pv += s.pv; u.mpv += s.mpv;
            u.dv += s.dv; u.toHit += s.toHit;
            u.heal += s.heal;
        }

        u.hp += auraAdd.hp; u.san += auraAdd.san;
        u.av += auraAdd.av; u.mav += auraAdd.mav;
        u.pv += auraAdd.pv; u.mpv += auraAdd.mpv;
        u.dv += auraAdd.dv; u.toHit += auraAdd.toHit;
        u.heal += auraAdd.heal;
        ApplyMulToStats(ref u, auraMul);

        if (isAllySide)
        {
            RelicSystem.GetUnitModifiers(out var relicAdd, out var relicMul);
            u.hp += relicAdd.hp; u.san += relicAdd.san;
            u.av += relicAdd.av; u.mav += relicAdd.mav;
            u.pv += relicAdd.pv; u.mpv += relicAdd.mpv;
            u.dv += relicAdd.dv; u.toHit += relicAdd.toHit;
            u.heal += relicAdd.heal;
            ApplyMulToStats(ref u, relicMul);
        }

        u.hp = Math.Max(1, u.hp);
        return u;
    }

    // 個人単位の最終ステータス。UnitAuraスキルとRelic効果は「合計に1回」ではなく生存メンバー全員に適用する。
    // isAllySide: レリックのUnitバフは冒険者側にのみ適用する。敵側の計算では常にfalseを渡すこと。
    public static (IUnitMember member, StatBlock stats)[] CalcPerMember(IUnitMember?[] members, bool isAllySide)
    {
        if (members == null) return Array.Empty<(IUnitMember, StatBlock)>();

        StatBlock auraAdd = default;
        StatMultiplier auraMul = StatMultiplier.One;
        var perMemberBase = new List<(IUnitMember member, bool isFront, StatBlock stats)>();

        for (int i = 0; i < members.Length; i++)
        {
            var m = members[i];
            if (m == null || !m.IsAlive) continue;
            bool isFront = i < 3;
            var s = m.GetFinalCombatStats();
            ApplySkills(m, isFront, ref s, ref auraAdd, ref auraMul);
            perMemberBase.Add((m, isFront, s));
        }

        StatBlock relicAdd = default;
        StatMultiplier relicMul = StatMultiplier.One;
        if (isAllySide) RelicSystem.GetUnitModifiers(out relicAdd, out relicMul);

        var result = new (IUnitMember member, StatBlock stats)[perMemberBase.Count];
        for (int i = 0; i < perMemberBase.Count; i++)
        {
            var (m, _, s) = perMemberBase[i];
            s += auraAdd;
            ApplyMulToBlock(ref s, auraMul);
            s += relicAdd;
            ApplyMulToBlock(ref s, relicMul);
            s.hp = Math.Max(1, s.hp);
            result[i] = (m, s);
        }
        return result;
    }

    public static int CountAlive(IUnitMember?[] members)
        => members?.Count(m => m != null && m.IsAlive) ?? 0;

    public static int AvgLevel(IUnitMember?[] members)
    {
        var alive = members?.Where(m => m != null && m.IsAlive).ToArray();
        if (alive == null || alive.Length == 0) return 1;
        return (int)Math.Round(alive.Average(m => (double)m!.Level));
    }

    static void ApplySkills(IUnitMember m, bool isFront, ref StatBlock s,
        ref StatBlock auraAdd, ref StatMultiplier auraMul)
    {
        foreach (var sk in m.Skills)
        {
            if (!IsActive(sk, m, isFront)) continue;
            var mul = FixMul(sk.mul);
            if (sk.scope == SkillScope.UnitAura)
            {
                auraAdd += sk.add;
                auraMul = Multiply(auraMul, mul);
            }
            else
            {
                s += sk.add;
                ApplyMulToBlock(ref s, mul);
            }
        }
    }

    static bool IsActive(SkillMasterData sk, IUnitMember m, bool isFront)
    {
        if (sk.frontOnly && !isFront) return false;
        if (sk.backOnly && isFront) return false;
        if (sk.requireWeaponType && (m.Weapon == null || m.Weapon.weaponType != sk.requiredWeaponType)) return false;
        if (sk.requireArmorType && (m.Armor == null || m.Armor.armorType != sk.requiredArmorType)) return false;
        return true;
    }

    static StatMultiplier FixMul(StatMultiplier m)
    {
        if (m.hp == 0f) m.hp = 1f;
        if (m.san == 0f) m.san = 1f;
        if (m.heal == 0f) m.heal = 1f;
        return m;
    }

    static StatMultiplier Multiply(StatMultiplier a, StatMultiplier b)
    {
        a.hp *= b.hp; a.san *= b.san; a.heal *= b.heal;
        return a;
    }

    // 倍率がかかるのはHP・SAN・回復量だけ。AV/DV/PVは1点の重みが大きいので加算でしか動かさない。
    static void ApplyMulToBlock(ref StatBlock s, StatMultiplier m)
    {
        s.hp = (int)Math.Floor(s.hp * m.hp);
        s.san = (int)Math.Floor(s.san * m.san);
        s.heal = (int)Math.Floor(s.heal * m.heal);
    }

    static void ApplyMulToStats(ref UnitStats u, StatMultiplier m)
    {
        u.hp = (int)Math.Floor(u.hp * m.hp);
        u.san = (int)Math.Floor(u.san * m.san);
        u.heal = (int)Math.Floor(u.heal * m.heal);
    }
}
