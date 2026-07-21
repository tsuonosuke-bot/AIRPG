namespace GuildSimulator.Core.MasterData;

public class ClassSkillEntry
{
    public string skillId = "";
    public int requiredClearCount;
    public SkillMasterData? Skill { get; set; }
}
