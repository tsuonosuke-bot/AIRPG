using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

/// <summary>
/// 戦闘開始時または命中時に与える状態効果。スキル・装備マスタから共通して利用する。
/// potency は継続ダメージ/回復なら最大HP比率、能力強化なら戦闘値への加算値として扱う。
/// </summary>
public class CombatStatusApplicationData
{
    public CombatStatusType type;
    public CombatStatusTarget target = CombatStatusTarget.Enemy;
    public int chancePercent = 100;
    public int durationRounds = 2;
    public int potency = 1;
}
