using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Battle;

public sealed class CombatStatusInstance
{
    public CombatStatusType Type { get; init; }
    public int Potency { get; set; }
    public int ExpiresAfterRound { get; set; }
    public string SourceName { get; set; } = "";
}

/// <summary>戦闘ごとに生成され、戦闘終了と同時に破棄される状態効果の管理器。</summary>
public sealed class CombatStatusTracker
{
    readonly Dictionary<IUnitMember, Dictionary<CombatStatusType, CombatStatusInstance>> states = new();
    static readonly CombatStatusType[] HarmfulStatuses =
    {
        CombatStatusType.Stunned,
        CombatStatusType.Burning,
        CombatStatusType.Poisoned,
        CombatStatusType.Bleeding,
    };

    public bool Apply(
        IUnitMember target,
        CombatStatusApplicationData application,
        string sourceName,
        int currentRound,
        List<string> logs,
        int phase)
    {
        if (!target.IsAlive) return false;
        int chance = Math.Clamp(application.chancePercent, 0, 100);
        if (chance <= 0 || (chance < 100 && GameRandom.Range(1, 101) > chance)) return false;

        int duration = Math.Max(1, application.durationRounds);
        int expires = currentRound + duration - 1;
        if (!states.TryGetValue(target, out var byType))
        {
            byType = new Dictionary<CombatStatusType, CombatStatusInstance>();
            states[target] = byType;
        }

        bool refreshed = byType.TryGetValue(application.type, out var state);
        if (!refreshed)
        {
            state = new CombatStatusInstance { Type = application.type };
            byType[application.type] = state;
        }
        state!.Potency = Math.Max(state.Potency, Math.Max(0, application.potency));
        state.ExpiresAfterRound = Math.Max(state.ExpiresAfterRound, expires);
        state.SourceName = sourceName;

        string action = refreshed ? "延長" : "付与";
        logs.Add($"  エリア {phase}: {target.Name} に{DisplayName(application.type)}を{action}"
            + $"（{duration}ラウンド、{sourceName}）");
        return true;
    }

    public IReadOnlyCollection<CombatStatusInstance> GetActive(IUnitMember member) =>
        states.TryGetValue(member, out var byType)
            ? byType.Values.ToList()
            : Array.Empty<CombatStatusInstance>();

    /// <summary>処刑人が狙う、継続ダメージを受けている対象か。</summary>
    public bool HasDamagingAilment(IUnitMember member) =>
        states.TryGetValue(member, out var byType)
        && (byType.ContainsKey(CombatStatusType.Poisoned)
            || byType.ContainsKey(CombatStatusType.Bleeding)
            || byType.ContainsKey(CombatStatusType.Burning));

    /// <summary>浄化できる有害状態が1つでもあるか。</summary>
    public bool HasHarmfulStatus(IUnitMember member) =>
        states.TryGetValue(member, out var byType)
        && HarmfulStatuses.Any(byType.ContainsKey);

    /// <summary>
    /// 有害状態を1つ解除する。行動不能を最優先し、次にpotencyの大きい継続ダメージを選ぶ。
    /// 攻勢・守勢・再生は有益な状態なので解除しない。
    /// </summary>
    public bool CleanseOneHarmful(IUnitMember member, string sourceName, List<string> logs, int phase)
    {
        if (!states.TryGetValue(member, out var byType)) return false;
        var removed = byType.Values
            .Where(state => HarmfulStatuses.Contains(state.Type))
            .OrderByDescending(state => state.Type == CombatStatusType.Stunned)
            .ThenByDescending(state => state.Potency)
            .FirstOrDefault();
        if (removed == null) return false;

        byType.Remove(removed.Type);
        if (byType.Count == 0) states.Remove(member);
        logs.Add($"  エリア {phase}: {member.Name} の{DisplayName(removed.Type)}を浄化した（{sourceName}）");
        return true;
    }

    public StatBlock ApplyStatModifiers(IUnitMember member, StatBlock stats)
    {
        if (!states.TryGetValue(member, out var byType)) return stats;
        foreach (var state in byType.Values)
        {
            int potency = Math.Max(1, state.Potency);
            switch (state.Type)
            {
                case CombatStatusType.Burning:
                    stats.av -= Math.Max(1, potency / 2);
                    stats.mav -= Math.Max(1, potency / 2);
                    break;
                case CombatStatusType.Empowered:
                    stats.pv += potency;
                    stats.mpv += potency;
                    stats.toHit += potency;
                    break;
                case CombatStatusType.Guarded:
                    stats.av += potency;
                    stats.mav += potency;
                    stats.dv += potency;
                    break;
            }
        }
        return stats;
    }

    /// <summary>ラウンド冒頭の継続回復・継続ダメージを処理し、戦闘不能者数を返す。</summary>
    public int ProcessRoundStart(
        IEnumerable<IUnitMember?> side,
        int round,
        List<string> logs,
        int phase)
    {
        int downed = 0;
        foreach (var member in side.Where(m => m != null && m.IsAlive).Select(m => m!))
        {
            if (!states.TryGetValue(member, out var byType)) continue;

            if (byType.TryGetValue(CombatStatusType.Regenerating, out var regen))
            {
                int amount = Math.Max(1,
                    (int)Math.Ceiling(Math.Max(1, member.CombatHpMax) * regen.Potency / 100f));
                int before = member.CombatHp;
                member.CombatHp = Math.Min(member.CombatHpMax, member.CombatHp + amount);
                int healed = member.CombatHp - before;
                if (healed > 0)
                    logs.Add($"  エリア {phase}: {member.Name} の再生 +{healed}（{member.CombatHp}/{member.CombatHpMax}）");
            }

            foreach (var type in new[] { CombatStatusType.Poisoned, CombatStatusType.Bleeding, CombatStatusType.Burning })
            {
                if (!member.IsAlive || !byType.TryGetValue(type, out var dot)) continue;
                int damage = Math.Max(1,
                    (int)Math.Ceiling(Math.Max(1, member.CombatHpMax) * dot.Potency / 100f));
                member.CombatHp = Math.Max(0, member.CombatHp - damage);
                logs.Add($"  エリア {phase}: {member.Name} は{DisplayName(type)}で{damage}ダメージ"
                    + $"（{member.CombatHp}/{member.CombatHpMax}）");
                if (member.CombatHp <= 0)
                {
                    SetCombatDown(member, severity: 1, logs, phase);
                    downed++;
                }
            }
        }
        return downed;
    }

    public bool TryConsumeStun(IUnitMember member, List<string> logs, int phase)
    {
        if (!states.TryGetValue(member, out var byType)
            || !byType.Remove(CombatStatusType.Stunned))
            return false;

        logs.Add($"  エリア {phase}: {member.Name} は{DisplayName(CombatStatusType.Stunned)}して行動できない");
        if (byType.Count == 0) states.Remove(member);
        return true;
    }

    public void EndRound(int round)
    {
        foreach (var member in states.Keys.ToList())
        {
            var byType = states[member];
            foreach (var type in byType
                         .Where(kv => kv.Value.ExpiresAfterRound <= round)
                         .Select(kv => kv.Key)
                         .ToList())
                byType.Remove(type);
            if (byType.Count == 0) states.Remove(member);
        }
    }

    static void SetCombatDown(IUnitMember member, int severity, List<string> logs, int phase)
    {
        member.CombatHp = 0;
        if (member is AdventurerData adventurer)
        {
            adventurer.RegisterKnockout(severity);
            logs.Add($"  エリア {phase}: {member.Name} は戦闘不能！ 帰還後に生死・負傷を判定する");
        }
        else
        {
            member.IsAlive = false;
            logs.Add($"  エリア {phase}: {member.Name} 撃破！");
        }
    }

    public static string DisplayName(CombatStatusType type) => type switch
    {
        CombatStatusType.Poisoned => "毒",
        CombatStatusType.Bleeding => "出血",
        CombatStatusType.Burning => "火傷",
        CombatStatusType.Stunned => "凍結",
        CombatStatusType.Regenerating => "再生",
        CombatStatusType.Empowered => "攻勢",
        CombatStatusType.Guarded => "守勢",
        _ => type.ToString(),
    };
}

/// <summary>既存の武器種に最初から与える状態効果。マスタ個別設定がなくても戦闘へ登場する。</summary>
public static class CombatStatusDefaults
{
    public static CombatStatusApplicationData? BattleStart(EquipmentMasterData? weapon) => weapon?.weaponType switch
    {
        WeaponType.Earth => New(CombatStatusType.Guarded, CombatStatusTarget.Self, 100, 3, 1),
        WeaponType.Wind => New(CombatStatusType.Empowered, CombatStatusTarget.Self, 100, 3, 1),
        _ => null,
    };

    public static CombatStatusApplicationData? OnHit(EquipmentMasterData? weapon) => weapon?.weaponType switch
    {
        WeaponType.Fire => New(CombatStatusType.Burning, CombatStatusTarget.Enemy, 30, 3, 2),
        WeaponType.Dark => New(CombatStatusType.Poisoned, CombatStatusTarget.Enemy, 30, 3, 3),
        WeaponType.Water => New(CombatStatusType.Stunned, CombatStatusTarget.Enemy, 20, 2, 0),
        _ => null,
    };

    public static CombatStatusApplicationData? OnHeal(EquipmentMasterData? weapon) =>
        weapon?.weaponType == WeaponType.Light
            ? New(CombatStatusType.Regenerating, CombatStatusTarget.Enemy, 100, 3, 4)
            : null;

    static CombatStatusApplicationData New(
        CombatStatusType type,
        CombatStatusTarget target,
        int chance,
        int duration,
        int potency) => new()
    {
        type = type,
        target = target,
        chancePercent = chance,
        durationRounds = duration,
        potency = potency,
    };
}
