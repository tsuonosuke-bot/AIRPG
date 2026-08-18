using System.Text.RegularExpressions;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Game.Presentation;

/// <summary>
/// ターン進行直後の要約表示。詳細ログをそのまま並べず、現在値・増減・主な出来事へ分ける。
/// </summary>
internal static class TurnSummaryRenderer
{
    const string GrowthMarker = " / 成長: ";
    static readonly Regex EncounterVitals = new(
        @"（HP \d+/\d+ 士気 \d+/\d+）",
        RegexOptions.CultureInvariant);

    public static void Write(
        QuestRun quest,
        int beforePhase,
        int beforeHp,
        int beforeMorale,
        int beforeReportCount)
    {
        Ui.Write("  ◆ ", TextStyle.Accent);
        Ui.WriteLine(quest.def.questName, TextStyle.Accent);

        Ui.Write("    状態  ", TextStyle.Dim);
        Ui.WriteLine(StatusText(quest), StatusStyle(quest));

        WriteMetric(
            "進捗",
            $"エリア {quest.currentPhase}/{quest.PhaseLimit}",
            quest.currentPhase - beforePhase);
        WriteMetric(
            "HP",
            $"{quest.unitHpCurrent}/{quest.unitHpMax}",
            quest.unitHpCurrent - beforeHp);
        WriteMetric(
            "士気",
            $"{quest.morale.Current}/{quest.morale.Max}",
            quest.morale.Current - beforeMorale);

        Ui.WriteLine("    今ターンの出来事", TextStyle.Dim);
        WriteEvents(quest, beforeReportCount);
    }

    static void WriteMetric(string label, string currentValue, int change)
    {
        string paddedLabel = label == "HP" ? "HP    " : $"{label}  ";
        Ui.Write($"    {paddedLabel}", TextStyle.Dim);
        Ui.Write(currentValue);
        Ui.Write("（", TextStyle.Dim);
        Ui.Write(ChangeText(change), ChangeStyle(change));
        Ui.WriteLine("）", TextStyle.Dim);
    }

    static void WriteEvents(QuestRun quest, int beforeReportCount)
    {
        int start = Math.Clamp(beforeReportCount, 0, quest.reportEvents.Count);
        var events = quest.reportEvents.Skip(start).ToList();
        var notableEvents = events.Where(e => !IsQuietProgress(e)).ToList();
        int quietPhaseCount = events
            .Where(e => e.phase > 0)
            .GroupBy(e => e.phase)
            .Count(phaseEvents =>
                phaseEvents.Any(IsQuietProgress)
                && phaseEvents.All(IsQuietProgress));

        foreach (var e in notableEvents)
        {
            string detail = CompactDetail(e.detail, out string growth);
            TextStyle style = EventStyle(e);
            string marker = e.important ? "◆" : "・";

            Ui.Write($"      {marker} ", style);
            if (e.phase > 0)
                Ui.Write($"{e.phase}/{quest.PhaseLimit}  ", TextStyle.Dim);
            Ui.Write(e.title, style);
            if (detail.Length > 0)
            {
                Ui.Write(" → ", TextStyle.Dim);
                Ui.WriteLine(detail);
            }
            else
            {
                Ui.WriteLine();
            }

            if (growth.Length > 0)
                Ui.WriteLine($"          ★ 成長  {growth}", TextStyle.Warn);
        }

        if (quietPhaseCount > 0)
        {
            string prefix = notableEvents.Count > 0 ? "ほか" : "";
            Ui.Dim($"      ・{prefix}{quietPhaseCount}エリア：特記事項なし");
        }
        else if (notableEvents.Count == 0)
        {
            Ui.Dim("      ・特記事項なし");
        }
    }

    static bool IsQuietProgress(ExpeditionEventRecord e) =>
        e.kind == ExpeditionEventKind.Progress
        && e.title == "進行"
        && e.detail == "何も起きなかった";

    static string CompactDetail(string detail, out string growth)
    {
        int growthStart = detail.IndexOf(GrowthMarker, StringComparison.Ordinal);
        if (growthStart >= 0)
        {
            growth = detail[(growthStart + GrowthMarker.Length)..].Trim();
            detail = detail[..growthStart];
        }
        else
        {
            growth = "";
        }

        return EncounterVitals.Replace(detail, "").Trim();
    }

    static string StatusText(QuestRun quest) => quest.failed
        ? "全員戦闘不能・帰還処理待ち"
        : quest.retreated
            ? "撤退・帰還処理待ち"
            : quest.CanComplete
                ? "達成・報酬受取待ち"
                : quest.HasPendingChoice || quest.HasGatherDecision
                    ? "指示待ち"
                    : "進行中";

    static TextStyle StatusStyle(QuestRun quest) => quest.failed
        ? TextStyle.Error
        : quest.retreated || quest.HasPendingChoice || quest.HasGatherDecision
            ? TextStyle.Warn
            : quest.CanComplete
                ? TextStyle.Info
                : TextStyle.Normal;

    static TextStyle EventStyle(ExpeditionEventRecord e) => e.kind switch
    {
        ExpeditionEventKind.Trap or ExpeditionEventKind.Retreat or ExpeditionEventKind.Injury => TextStyle.Warn,
        ExpeditionEventKind.Treasure or ExpeditionEventKind.Discovery or ExpeditionEventKind.Completion => TextStyle.Info,
        ExpeditionEventKind.Decision => TextStyle.Warn,
        _ when e.important => TextStyle.Accent,
        _ => TextStyle.Normal,
    };

    static string ChangeText(int change) => change == 0
        ? "変化なし"
        : $"今ターン {(change > 0 ? "+" : "")}{change}";

    static TextStyle ChangeStyle(int change) => change switch
    {
        > 0 => TextStyle.Info,
        < 0 => TextStyle.Warn,
        _ => TextStyle.Dim,
    };
}
