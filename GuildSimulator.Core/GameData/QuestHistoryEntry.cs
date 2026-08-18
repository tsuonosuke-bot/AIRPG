using System.Collections.ObjectModel;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.GameData;

public enum QuestHistoryOutcome
{
    Success,
    Retreat,
    Failure,
}

/// <summary>
/// 完了したクエストの閲覧用スナップショット。
/// マスタや冒険者の生存期間に依存せず、当時の名前とログをそのまま保持する。
/// </summary>
public sealed class QuestHistoryEntry
{
    /// <summary>
    /// 30件保持してもJSONのメタデータ込みでlocalStorageを圧迫しすぎない、1件あたりの上限。
    /// ログ本文だけなら最大1,080,000文字となる。
    /// </summary>
    public const int MaxLogCharacters = 36_000;
    public const int MaxLogLines = 600;
    public const string OmissionMarker = "[... 古いログを省略しました ...]";
    public const string PartialLineMarker = "…";

    public string QuestId { get; }
    public string QuestName { get; }
    public int StartedTurn { get; }
    public int CompletedTurn { get; }
    public QuestHistoryOutcome Outcome { get; }
    public IReadOnlyList<string> Logs { get; }
    public int LogCharacterCount => CountCharacters(Logs);
    internal int CapturedQuestLogCount { get; }

    public QuestHistoryEntry(
        string questId,
        string questName,
        int startedTurn,
        int completedTurn,
        QuestHistoryOutcome outcome,
        IEnumerable<string>? logs)
        : this(
            questId,
            questName,
            startedTurn,
            completedTurn,
            outcome,
            logs,
            capturedQuestLogCount: int.MaxValue)
    {
    }

    QuestHistoryEntry(
        string questId,
        string questName,
        int startedTurn,
        int completedTurn,
        QuestHistoryOutcome outcome,
        IEnumerable<string>? logs,
        int capturedQuestLogCount)
    {
        QuestId = questId ?? "";
        QuestName = string.IsNullOrWhiteSpace(questName) ? "不明なクエスト" : questName;
        StartedTurn = Math.Max(1, startedTurn);
        CompletedTurn = Math.Max(StartedTurn, completedTurn);
        Outcome = outcome;
        Logs = BoundLogs(logs ?? Array.Empty<string>());
        CapturedQuestLogCount = capturedQuestLogCount;
    }

    public static QuestHistoryEntry Capture(QuestRun quest)
    {
        int completedTurn = quest.reportEvents.Count == 0
            ? quest.startedTurn
            : quest.reportEvents.Max(entry => entry.turn);
        var outcome = quest.completed
            ? QuestHistoryOutcome.Success
            : quest.retreated
                ? QuestHistoryOutcome.Retreat
                : QuestHistoryOutcome.Failure;

        return new QuestHistoryEntry(
            quest.def.id,
            quest.def.questName,
            quest.startedTurn,
            completedTurn,
            outcome,
            BuildSelfContainedLogs(quest),
            quest.logs.Count);
    }

    internal QuestHistoryEntry WithQuestLogUpdates(QuestRun quest)
    {
        int firstNewLog = Math.Clamp(CapturedQuestLogCount, 0, quest.logs.Count);
        if (firstNewLog >= quest.logs.Count) return this;

        return new QuestHistoryEntry(
            QuestId,
            QuestName,
            StartedTurn,
            CompletedTurn,
            Outcome,
            Logs.Concat(quest.logs.Skip(firstNewLog)),
            quest.logs.Count);
    }

    static IReadOnlyList<string> BoundLogs(IEnumerable<string> logs)
    {
        var lines = logs
            .SelectMany(SplitLogicalLines)
            .ToList();
        if (lines.Count <= MaxLogLines && CountCharacters(lines) <= MaxLogCharacters)
            return new ReadOnlyCollection<string>(lines);

        var suffix = SelectNewestLines(
            lines,
            MaxLogLines - 1,
            MaxLogCharacters - OmissionMarker.Length - 1);
        int battleStartIndex = FindTruncatedLatestBattleStart(lines, suffix);
        if (battleStartIndex < 0)
            return BuildBoundedResult(suffix, battleStart: null);

        // 戦闘途中からの末尾だけでは QuestLogIndexer が戦闘として認識できない。
        // 元の開始行を省略マーカー直後へ予約し、残りの枠で新しいアクションと結果を保持する。
        string battleStart = FitBattleStart(lines[battleStartIndex]);
        suffix = SelectNewestLines(
            lines,
            MaxLogLines - 2,
            MaxLogCharacters - OmissionMarker.Length - battleStart.Length - 2,
            minimumSourceIndexExclusive: battleStartIndex);
        return BuildBoundedResult(suffix, battleStart);
    }

    static BoundedSuffix SelectNewestLines(
        IReadOnlyList<string> lines,
        int lineLimit,
        int characterLimit,
        int minimumSourceIndexExclusive = -1)
    {
        var newestFirst = new List<BoundedLine>(Math.Max(0, lineLimit));
        int remainingCharacters = Math.Max(0, characterLimit);

        for (int index = lines.Count - 1;
             index > minimumSourceIndexExclusive
                && newestFirst.Count < lineLimit
                && remainingCharacters > 0;
             index--)
        {
            string line = lines[index];
            int separatorCost = newestFirst.Count == 0 ? 0 : 1;
            int available = remainingCharacters - separatorCost;
            if (available <= 0) break;

            if (line.Length <= available)
            {
                newestFirst.Add(new BoundedLine(line, index, IsPartial: false));
                remainingCharacters -= separatorCost + line.Length;
                continue;
            }

            string partial = available <= PartialLineMarker.Length
                ? PartialLineMarker[..available]
                : PartialLineMarker + line[^(available - PartialLineMarker.Length)..];
            newestFirst.Add(new BoundedLine(partial, index, IsPartial: true));
            remainingCharacters = 0;
        }

        newestFirst.Reverse();
        return new BoundedSuffix(newestFirst);
    }

    static IReadOnlyList<string> BuildBoundedResult(BoundedSuffix suffix, string? battleStart)
    {
        var bounded = new List<string>(suffix.Lines.Count + (battleStart == null ? 1 : 2))
        {
            OmissionMarker,
        };
        if (battleStart != null) bounded.Add(battleStart);
        bounded.AddRange(suffix.Lines.Select(line => line.Text));
        return new ReadOnlyCollection<string>(bounded);
    }

    static int FindTruncatedLatestBattleStart(
        IReadOnlyList<string> lines,
        BoundedSuffix suffix)
    {
        if (suffix.Lines.Count == 0) return -1;

        int battleStartIndex = -1;
        for (int index = lines.Count - 1; index >= 0; index--)
            if (IsBattleStart(lines[index]))
            {
                battleStartIndex = index;
                break;
            }
        if (battleStartIndex < 0) return -1;

        int battleEndIndex = lines.Count - 1;
        for (int index = battleStartIndex + 1; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith("[", StringComparison.Ordinal)) continue;
            battleEndIndex = IsBattleResult(lines[index]) ? index : index - 1;
            break;
        }

        var oldest = suffix.Lines[0];
        bool startWasCut = oldest.SourceIndex > battleStartIndex
            || (oldest.SourceIndex == battleStartIndex && oldest.IsPartial);
        bool suffixStillContainsBattle = oldest.SourceIndex <= battleEndIndex;
        return startWasCut && suffixStillContainsBattle ? battleStartIndex : -1;
    }

    static bool IsBattleStart(string line) =>
        line.StartsWith("[Turn ", StringComparison.Ordinal)
        && (line.Contains("] エリア ", StringComparison.Ordinal)
            || line.Contains("] Phase ", StringComparison.Ordinal))
        && line.Contains(": 戦闘開始 冒険者 vs ", StringComparison.Ordinal);

    static bool IsBattleResult(string line) =>
        line.StartsWith("[Turn ", StringComparison.Ordinal)
        && (line.Contains(": 敵遭遇：", StringComparison.Ordinal)
            || line.Contains(": ボス遭遇：", StringComparison.Ordinal));

    static string FitBattleStart(string battleStart)
    {
        int maxLength = MaxLogCharacters - OmissionMarker.Length - 3;
        return battleStart.Length <= maxLength ? battleStart : battleStart[..maxLength];
    }

    sealed record BoundedSuffix(List<BoundedLine> Lines);
    sealed record BoundedLine(string Text, int SourceIndex, bool IsPartial);

    static IEnumerable<string> SplitLogicalLines(string? log)
    {
        string normalized = (log ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        return normalized.Split('\n');
    }

    static int CountCharacters(IReadOnlyList<string> logs)
    {
        long count = Math.Max(0, logs.Count - 1);
        foreach (string log in logs)
            count += log.Length;
        return count >= int.MaxValue ? int.MaxValue : (int)count;
    }

    static List<string> BuildSelfContainedLogs(QuestRun quest)
    {
        var logs = new List<string>(quest.logs);
        int outcomeIndex = quest.reportEvents.FindLastIndex(report => report.kind is
            ExpeditionEventKind.Completion or ExpeditionEventKind.Retreat);

        // FinalizeQuest が帰還直前に追加する手掛かり発見は、完了/撤退イベントの直前に連続する。
        // 道中の古い発見まで末尾へ移してしまわないよう、その終端ブロックだけを補完する。
        int firstOutcomeSupplementIndex = outcomeIndex;
        while (firstOutcomeSupplementIndex > 0
            && quest.reportEvents[firstOutcomeSupplementIndex - 1].kind == ExpeditionEventKind.Discovery)
            firstOutcomeSupplementIndex--;

        var outcomeSupplements = new List<string>();
        if (outcomeIndex >= 0)
        {
            for (int index = firstOutcomeSupplementIndex; index <= outcomeIndex; index++)
            {
                var report = quest.reportEvents[index];
                if (report.kind != ExpeditionEventKind.Discovery && index != outcomeIndex) continue;
                if (IsRepresented(logs, report)) continue;
                outcomeSupplements.Add(FormatReportEvent(report));
            }
        }

        int returnLogIndex = logs.FindIndex(log =>
            log.StartsWith("[帰還処理]", StringComparison.Ordinal)
            || log.StartsWith("[特性選択]", StringComparison.Ordinal));
        if (returnLogIndex < 0) returnLogIndex = logs.Count;
        logs.InsertRange(returnLogIndex, outcomeSupplements);

        var injurySupplements = new List<string>();
        int firstInjuryIndex = Math.Max(0, outcomeIndex + 1);
        for (int index = firstInjuryIndex; index < quest.reportEvents.Count; index++)
        {
            var report = quest.reportEvents[index];
            if (report.kind != ExpeditionEventKind.Injury) continue;
            if (IsRepresented(logs, report)) continue;
            injurySupplements.Add(FormatReportEvent(report));
        }

        int traitLogIndex = logs.FindIndex(log =>
            log.StartsWith("[特性選択]", StringComparison.Ordinal));
        if (traitLogIndex < 0) traitLogIndex = logs.Count;
        logs.InsertRange(traitLogIndex, injurySupplements);
        return logs;
    }

    static bool IsRepresented(IReadOnlyList<string> logs, ExpeditionEventRecord report)
    {
        string detail = report.detail?.Trim() ?? "";
        if (detail.Length > 0)
            return logs.Any(log => log.Contains(detail, StringComparison.Ordinal));

        string title = report.title?.Trim() ?? "";
        return title.Length > 0
            && logs.Any(log => log.Contains(title, StringComparison.Ordinal));
    }

    static string FormatReportEvent(ExpeditionEventRecord report)
    {
        string phase = report.phase > 0 ? $" エリア {report.phase}" : "";
        string actor = string.IsNullOrWhiteSpace(report.actorName) ? "" : $"（{report.actorName}）";
        string detail = string.IsNullOrWhiteSpace(report.detail) ? "" : $" - {report.detail}";
        return $"[Turn {report.turn}]{phase}: [遠征報告/{ReportKindLabel(report.kind)}] "
            + $"{report.title}{actor}{detail}";
    }

    static string ReportKindLabel(ExpeditionEventKind kind) => kind switch
    {
        ExpeditionEventKind.Departure => "出発",
        ExpeditionEventKind.Progress => "進行",
        ExpeditionEventKind.Encounter => "遭遇",
        ExpeditionEventKind.Rest => "休息",
        ExpeditionEventKind.Trap => "罠",
        ExpeditionEventKind.Treasure => "宝箱",
        ExpeditionEventKind.Gather => "採取",
        ExpeditionEventKind.Decision => "選択",
        ExpeditionEventKind.Discovery => "発見",
        ExpeditionEventKind.Completion => "完了",
        ExpeditionEventKind.Retreat => "撤退",
        ExpeditionEventKind.Injury => "負傷",
        _ => "記録",
    };
}
