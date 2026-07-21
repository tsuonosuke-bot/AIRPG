using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public static class RelicSystem
{
    static List<RelicMasterData>? _relics;

    public static void SetRelics(List<RelicMasterData> relics) => _relics = relics;

    public static void GetUnitModifiers(out StatBlock add, out StatMultiplier mul)
    {
        add = default;
        mul = StatMultiplier.One;
        if (_relics == null) return;
        foreach (var r in _relics)
        {
            if (r.effectType == RelicEffectType.Unit_AddFlat) add += r.add;
            else if (r.effectType == RelicEffectType.Unit_Multiply) mul = Mul(mul, FixMul(r.mul));
        }
    }

    public static float GetGoldRewardMultiplier()
    {
        float x = 1f;
        if (_relics == null) return x;
        foreach (var r in _relics)
            if (r.effectType == RelicEffectType.GoldReward_Multiply) x *= Math.Max(0f, r.rate);
        return x;
    }

    public static float GetUpkeepMultiplier()
    {
        float x = 1f;
        if (_relics == null) return x;
        foreach (var r in _relics)
            if (r.effectType == RelicEffectType.Upkeep_Multiply) x *= Math.Max(0f, r.rate);
        return x;
    }

    public static float GetRestHealMultiplier()
    {
        float x = 1f;
        if (_relics == null) return x;
        foreach (var r in _relics)
            if (r.effectType == RelicEffectType.RestHeal_Multiply) x *= Math.Max(0f, r.rate);
        return x;
    }

    static StatMultiplier FixMul(StatMultiplier m)
    {
        if (m.hp == 0f) m.hp = 1f; if (m.san == 0f) m.san = 1f;
        if (m.pAtk == 0f) m.pAtk = 1f; if (m.pDef == 0f) m.pDef = 1f;
        if (m.mAtk == 0f) m.mAtk = 1f; if (m.mDef == 0f) m.mDef = 1f;
        if (m.hit == 0f) m.hit = 1f; if (m.evade == 0f) m.evade = 1f;
        if (m.heal == 0f) m.heal = 1f;
        return m;
    }

    static StatMultiplier Mul(StatMultiplier a, StatMultiplier b)
    {
        a.hp *= b.hp; a.san *= b.san; a.pAtk *= b.pAtk; a.pDef *= b.pDef;
        a.mAtk *= b.mAtk; a.mDef *= b.mDef; a.hit *= b.hit; a.evade *= b.evade; a.heal *= b.heal;
        return a;
    }
}
