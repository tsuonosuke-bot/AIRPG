using GuildSimulator.Game.Presentation;
using Xunit;

namespace GuildSimulator.Tests;

public class MainMenuBuilderTests
{
    [Fact]
    public void MainMenuPrioritizesTurnAndSeparatesExit()
    {
        var menu = MainMenuBuilder.BuildMain(
            currentTurn: 1,
            upkeepPerTurn: 10,
            projectedAfterUpkeep: 290,
            pendingDecisionCount: 0,
            relicsEnabled: false);

        Assert.Equal(
            new[] { "クエスト", "冒険者", "ギルド資産", "ターン操作", "その他", "セーブデータ", "システム" },
            menu.Select(option => option.Group).Distinct());

        var turn = Assert.Single(menu, option => option.Key == "9");
        Assert.Equal(MenuRole.Primary, turn.Role);
        Assert.Equal("ターン操作", turn.Group);

        var guildManagement = Assert.Single(menu, option => option.Key == "G");
        Assert.True(menu.IndexOf(turn) < menu.IndexOf(guildManagement));
        Assert.DoesNotContain(menu, option => option.Key is "8" or "B" or "J" or "M" or "T" or "H");

        var exit = Assert.Single(menu, option => option.Key == "0");
        Assert.Same(exit, menu[^1]);
        Assert.Equal("システム", exit.Group);
        Assert.Equal(MenuRole.Danger, exit.Role);
    }

    [Fact]
    public void NormalTurnShowsProjectionAndAccent()
    {
        var turn = MainMenuBuilder.BuildMain(3, 25, 175, 0, false)
            .Single(option => option.Key == "9");

        Assert.Equal("ターンを進める", turn.Label);
        Assert.Equal(TextStyle.Accent, turn.Style);
        Assert.Contains("Turn 3 → 4", turn.Detail);
        Assert.Contains("維持費 25G", turn.Detail);
        Assert.Contains("支払後 175G", turn.Detail);
    }

    [Fact]
    public void PendingDecisionsReplaceTurnActionWithResolutionAction()
    {
        var turn = MainMenuBuilder.BuildMain(3, 25, 175, 2, false)
            .Single(option => option.Key == "9");

        Assert.Equal("指示待ちを解決（2件）", turn.Label);
        Assert.Equal(TextStyle.Warn, turn.Style);
        Assert.Contains("ターン進行前に判断が必要", turn.Detail);
        Assert.Contains("解決後: Turn 3 → 4", turn.Detail);
    }

    [Fact]
    public void BankruptcyRiskWarnsBeforeTurnAdvance()
    {
        var turn = MainMenuBuilder.BuildMain(3, 25, 0, 0, false)
            .Single(option => option.Key == "9");

        Assert.Equal("ターンを進める", turn.Label);
        Assert.Equal(TextStyle.Warn, turn.Style);
        Assert.Contains("クエスト報酬がなければ破産", turn.Detail);
    }

    [Fact]
    public void RelicOptionFollowsFeatureStateWithoutChangingGroupOrder()
    {
        var withoutRelics = MainMenuBuilder.BuildMain(1, 10, 290, 0, false);
        var withRelics = MainMenuBuilder.BuildMain(1, 10, 290, 0, true);

        Assert.DoesNotContain(withoutRelics, option => option.Key == "7");
        Assert.Equal("ギルド資産", Assert.Single(withRelics, option => option.Key == "7").Group);
        Assert.Equal(
            withoutRelics.Select(option => option.Group).Distinct(),
            withRelics.Select(option => option.Group).Distinct());
    }

    [Fact]
    public void GuildManagementSubmenuContainsSixActionsAndBack()
    {
        var menu = MainMenuBuilder.BuildGuildManagement();

        Assert.Equal(new[] { "8", "B", "J", "M", "T", "H", "0" }, menu.Select(option => option.Key));
        Assert.Equal("メインメニューへ戻る", menu[^1].Label);
    }
}

static class MenuTestExtensions
{
    public static int IndexOf(this IReadOnlyList<MenuOption> menu, MenuOption option)
    {
        for (int index = 0; index < menu.Count; index++)
            if (ReferenceEquals(menu[index], option)) return index;
        return -1;
    }
}
