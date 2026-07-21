namespace GuildSimulator.Core.MasterData;

public class ClassMasterData
{
    public string id = "";
    public string className = "";
    public float vitGrowth;
    public float mentGrowth;
    public float strGrowth;
    public float intGrowth;
    public float agiGrowth;
    public List<ClassSkillEntry> classSkills = new();
}
