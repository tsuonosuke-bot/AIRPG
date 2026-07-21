using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class SkillMasterData
{
    public string id = "";
    public string skillName = "";
    public SkillScope scope = SkillScope.Self;
    public bool frontOnly;
    public bool backOnly;
    public bool requireWeaponType;
    public WeaponType requiredWeaponType;
    public bool requireArmorType;
    public ArmorType requiredArmorType;
    public StatBlock add;
    public StatMultiplier mul = StatMultiplier.One;
}
