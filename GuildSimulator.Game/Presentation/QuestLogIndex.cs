using System.Text.RegularExpressions;

namespace GuildSimulator.Game.Presentation;

/// <summary>1回の戦闘に属する詳細ログ。</summary>
internal sealed record QuestBattleLog(
    int Turn,
    int Phase,
    string Title,
    string Opponent,
    string Result,
    int Rounds,
    int StartLogIndex,
    IReadOnlyList<string> Lines);

/// <summary>平坦なクエストログを、戦闘とそれ以外へ分けた一時索引。</summary>
internal sealed record QuestLogIndex(
    IReadOnlyList<string> ExpeditionLogs,
    IReadOnlyList<QuestBattleLog> Battles);

/// <summary>
/// 保存済みの生ログから閲覧用の階層を組み立てる。
/// 索引自体は保存しないため、旧セーブのログもそのまま分類できる。
/// </summary>
internal static class QuestLogIndexer
{
    static readonly Regex BattleStart = new(
        @"^\[Turn (?<turn>\d+)\] (?:エリア|Phase) (?<phase>\d+): 戦闘開始 冒険者 vs (?<opponent>.+)$",
        RegexOptions.CultureInvariant);

    static readonly Regex BattleResult = new(
        @"^\[Turn (?<turn>\d+)\] (?:エリア|Phase) (?<phase>\d+)(?:/\d+)?: "
        + @"(?<title>(?:敵遭遇|ボス遭遇)：.+?) - (?<result>.+)$",
        RegexOptions.CultureInvariant);

    static readonly Regex RoundHeading = new(
        @"^\s*── ラウンド (?<round>\d+) ──\s*$",
        RegexOptions.CultureInvariant);

    public static QuestLogIndex Build(IReadOnlyList<string> logs)
    {
        var expedition = new List<string>();
        var battles = new List<QuestBattleLog>();

        int index = 0;
        while (index < logs.Count)
        {
            if (!TryParseBattleStart(logs[index], out int turn, out int phase, out string opponent))
            {
                expedition.Add(logs[index]);
                index++;
                continue;
            }

            int start = index;
            int endExclusive = index + 1;
            string title = "戦闘";
            string result = "";

            while (endExclusive < logs.Count)
            {
                string line = logs[endExclusive];
                if (TryParseBattleResult(line, turn, phase, out string parsedTitle, out string parsedResult))
                {
                    title = parsedTitle;
                    result = parsedResult;
                    endExclusive++;
                    break;
                }

                // 正常なログでは同じ戦闘の結果行で閉じる。欠損した旧ログでも、
                // 次のトップレベル記録を別戦闘へ誤って取り込まないようここで打ち切る。
                if (line.StartsWith("[", StringComparison.Ordinal))
                    break;

                endExclusive++;
            }

            var lines = logs.Skip(start).Take(endExclusive - start).ToList();
            int rounds = lines.Select(ParseRound).DefaultIfEmpty(0).Max();
            battles.Add(new QuestBattleLog(
                turn,
                phase,
                title,
                opponent,
                result,
                rounds,
                start,
                lines));
            index = endExclusive;
        }

        return new QuestLogIndex(expedition, battles);
    }

    static bool TryParseBattleStart(
        string line,
        out int turn,
        out int phase,
        out string opponent)
    {
        var match = BattleStart.Match(line);
        turn = 0;
        phase = 0;
        opponent = "";
        if (!match.Success) return false;

        turn = int.Parse(match.Groups["turn"].Value);
        phase = int.Parse(match.Groups["phase"].Value);
        opponent = match.Groups["opponent"].Value;
        return true;
    }

    static bool TryParseBattleResult(
        string line,
        int expectedTurn,
        int expectedPhase,
        out string title,
        out string result)
    {
        var match = BattleResult.Match(line);
        title = "";
        result = "";
        if (!match.Success
            || int.Parse(match.Groups["turn"].Value) != expectedTurn
            || int.Parse(match.Groups["phase"].Value) != expectedPhase)
            return false;

        title = match.Groups["title"].Value;
        result = match.Groups["result"].Value;
        return true;
    }

    static int ParseRound(string line)
    {
        var match = RoundHeading.Match(line);
        return match.Success ? int.Parse(match.Groups["round"].Value) : 0;
    }
}
