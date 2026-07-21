namespace GuildSimulator.Core.Models;

public struct StatMultiplier
{
    public float hp;
    public float san;
    public float pAtk;
    public float pDef;
    public float mAtk;
    public float mDef;
    public float hit;
    public float evade;
    public float heal;

    public static StatMultiplier One => new()
    { hp = 1f, san = 1f, pAtk = 1f, pDef = 1f, mAtk = 1f, mDef = 1f, hit = 1f, evade = 1f, heal = 1f };
}
