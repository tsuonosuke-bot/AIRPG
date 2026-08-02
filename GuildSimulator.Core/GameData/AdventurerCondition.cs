using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

public class AdventurerInjury
{
    public InjuryType type;
    public int remainingRestTurns = 1;
    public int scarChancePercent;

    public string DisplayName => type switch
    {
        InjuryType.CutsAndBruises => "裂傷・打撲",
        InjuryType.Fracture => "骨折",
        InjuryType.DeepWound => "深い傷",
        InjuryType.Trauma => "心的外傷",
        _ => type.ToString(),
    };

    public string EffectDescription => type switch
    {
        InjuryType.CutsAndBruises => "最大HP-10%",
        InjuryType.Fracture => "PV/mPV/命中-1",
        InjuryType.DeepWound => "最大HP-20%、AV/mAV-1",
        InjuryType.Trauma => "士気-25%、命中-1",
        _ => "",
    };
}

public class AdventurerScar
{
    public ScarType type;

    public string DisplayName => type switch
    {
        ScarType.BattleScar => "歴戦の傷痕",
        ScarType.StiffJoint => "古傷",
        ScarType.Nightmares => "消えない悪夢",
        ScarType.Survivor => "死線帰り",
        _ => type.ToString(),
    };

    public string Title => type switch
    {
        ScarType.BattleScar => "傷を越えし者",
        ScarType.StiffJoint => "古傷を抱く者",
        ScarType.Nightmares => "夜を恐れぬ者",
        ScarType.Survivor => "死線帰り",
        _ => "生還者",
    };

    public string EffectDescription => type switch
    {
        ScarType.BattleScar => "士気+10%",
        ScarType.StiffJoint => "回避DV-1",
        ScarType.Nightmares => "士気-15%、命中+1",
        ScarType.Survivor => "HP+5%、応急処置+5%",
        _ => "",
    };
}

public sealed record TraumaResolution(bool Died, AdventurerInjury? Injury, AdventurerScar? Scar, string Message);

public sealed record RecoveryResolution(
    IReadOnlyList<AdventurerInjury> Healed,
    IReadOnlyList<AdventurerScar> NewScars);

public static class AdventurerConditionRules
{
    public static void ApplyModifiers(
        IEnumerable<AdventurerInjury> injuries,
        IEnumerable<AdventurerScar> scars,
        ref StatBlock stats)
    {
        float hpRate = 1f;
        float sanRate = 1f;

        foreach (var injury in injuries)
        {
            switch (injury.type)
            {
                case InjuryType.CutsAndBruises:
                    hpRate *= 0.90f;
                    break;
                case InjuryType.Fracture:
                    stats.pv -= 1;
                    stats.mpv -= 1;
                    stats.toHit -= 1;
                    break;
                case InjuryType.DeepWound:
                    hpRate *= 0.80f;
                    stats.av -= 1;
                    stats.mav -= 1;
                    break;
                case InjuryType.Trauma:
                    sanRate *= 0.75f;
                    stats.toHit -= 1;
                    break;
            }
        }

        foreach (var scar in scars)
        {
            switch (scar.type)
            {
                case ScarType.BattleScar:
                    sanRate *= 1.10f;
                    break;
                case ScarType.StiffJoint:
                    stats.dv -= 1;
                    break;
                case ScarType.Nightmares:
                    sanRate *= 0.85f;
                    stats.toHit += 1;
                    break;
                case ScarType.Survivor:
                    hpRate *= 1.05f;
                    stats.emergencyHeal += 5;
                    break;
            }
        }

        stats.hp = Math.Max(1, (int)Math.Floor(stats.hp * hpRate));
        stats.san = Math.Max(1, (int)Math.Floor(stats.san * sanRate));
    }
}
