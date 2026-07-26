using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class BurialScreen
{
    public static async Task ShowAsync(GuildManager guild)
    {
        Ui.BeginScreen();
        Ui.Header("埋葬記録");

        var records = guild.burialRecords;
        if (records.Count == 0)
        {
            Ui.Dim("  まだ埋葬された冒険者はいません");
            await Ui.PauseAsync();
            return;
        }

        Ui.WriteLine($"  埋葬者数: {records.Count}人");
        Ui.WriteLine();
        foreach (var r in records)
        {
            Ui.WriteLine($"  ◆ {r.name}  Lv{r.level}  {r.classAndRace}");
            Ui.Dim($"      埋葬: Turn {r.buriedTurn}  遠征{r.expeditionCount}回（成功{r.successCount}回）");
        }

        await Ui.PauseAsync();
    }
}
