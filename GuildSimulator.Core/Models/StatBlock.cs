namespace GuildSimulator.Core.Models;

public struct StatBlock
{
    public int hp;
    public int san;
    public int pAtk;
    public int pDef;
    public int mAtk;
    public int mDef;
    public int hit;
    public int evade;
    public int heal;

    public static StatBlock operator +(StatBlock a, StatBlock b) => new()
    {
        hp = a.hp + b.hp,
        san = a.san + b.san,
        pAtk = a.pAtk + b.pAtk,
        pDef = a.pDef + b.pDef,
        mAtk = a.mAtk + b.mAtk,
        mDef = a.mDef + b.mDef,
        hit = a.hit + b.hit,
        evade = a.evade + b.evade,
        heal = a.heal + b.heal,
    };
}
