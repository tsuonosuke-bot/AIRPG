using GuildSimulator.Core.MasterData;

namespace GuildSimulator.Core.Systems.Guild;

public static class RecruitmentSystem
{
    public static int RequiredGuildRankForLevel(int level) => level switch
    {
        <= 1 => 1,
        <= 3 => 2,
        <= 7 => 3,
        <= 11 => 4,
        _ => 5,
    };

    public static int DefaultWeightForGuildRank(int recruitGuildRank) => recruitGuildRank switch
    {
        <= 1 => 100,
        2 => 60,
        3 => 40,
        4 => 20,
        _ => 10,
    };

    public static List<AdventurerMasterData> DrawCandidates(
        IEnumerable<AdventurerMasterData> pool,
        GuildManager guild,
        int count,
        Func<int, int, int>? range = null)
    {
        if (count <= 0) return new();
        range ??= GameRandom.Range;

        var available = pool
            .Where(m => !guild.adventurers.Any(a => a.master == m)
                && m.recruitGuildRank <= guild.GuildRank
                && m.recruitWeight > 0)
            .ToList();
        var picked = new List<AdventurerMasterData>();

        while (picked.Count < count && available.Count > 0)
        {
            int totalWeight = available.Sum(m => m.recruitWeight);
            int roll = range(0, totalWeight);
            int accumulated = 0;
            int pickedIndex = 0;

            for (int i = 0; i < available.Count; i++)
            {
                accumulated += available[i].recruitWeight;
                if (roll < accumulated)
                {
                    pickedIndex = i;
                    break;
                }
            }

            picked.Add(available[pickedIndex]);
            available.RemoveAt(pickedIndex);
        }

        return picked;
    }
}
