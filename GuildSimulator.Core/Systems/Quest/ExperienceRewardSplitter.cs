namespace GuildSimulator.Core.Systems.Quest;

/// <summary>戦闘・クエスト報酬の経験値を参加人数で公平に分ける。</summary>
public static class ExperienceRewardSplitter
{
    /// <summary>
    /// 余りは先頭から1ずつ配る。全員への配分合計が元の経験値と一致し、
    /// 参加人数が増えても経験値総量が人数倍に膨らまないようにする。
    /// </summary>
    public static int ShareFor(int totalExperience, int participantCount, int participantIndex)
    {
        if (totalExperience <= 0 || participantCount <= 0
            || participantIndex < 0 || participantIndex >= participantCount)
            return 0;

        int share = totalExperience / participantCount;
        int remainder = totalExperience % participantCount;
        return share + (participantIndex < remainder ? 1 : 0);
    }
}
