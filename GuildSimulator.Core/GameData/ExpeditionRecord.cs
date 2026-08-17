namespace GuildSimulator.Core.GameData;

/// <summary>
/// 遠征中の隊員の振る舞いを数える項目。特性（<c>traits.json</c>）の解禁条件はここを参照する。
///
/// 数え方の原則は「<b>結果ではなく、身の置き方を数える</b>」こと。倒した数のような戦果だけを数えると
/// 強い隊員がさらに強くなるだけになるので、削られながら立っていた時間や、弾かれ続けた回数のような
/// 「うまくいかなかった経験」も同じ重みで数える。
///
/// セーブデータに数値で載るため、<b>既存の値を振り直してはいけない</b>。項目を足すときは末尾に付ける。
/// </summary>
public enum ExpeditionRecordType
{
    // ---- リスク記録 ----
    // 実際に命を危険へ晒したことの証。欠点を持たない「純粋強化」の特性は、
    // このいずれかを解禁条件に含めなければならない（MasterValidator が検査する）。

    /// <summary>HPが4分の1以下の状態でラウンドを迎えた回数。</summary>
    NearDeathRounds = 0,

    /// <summary>戦闘不能になった回数。</summary>
    TimesDowned = 1,

    /// <summary>同じ戦闘で味方が目の前で戦闘不能になった回数。</summary>
    AlliesFellBeside = 2,

    /// <summary>全滅した遠征に加わっていた回数。</summary>
    QuestsFailed = 3,

    /// <summary>同じ遠征で仲間を死なせた人数の累計（戦闘不能ではなく、実際の死）。</summary>
    ComradesLost = 4,

    /// <summary>自分だけが生きて帰った回数。</summary>
    SoleSurvivor = 5,

    // ---- 通常記録 ----
    // 日々の戦い方の蓄積。ここだけを条件にできるのは、欠点を併せ持つ「諸刃」の特性だけ。

    /// <summary>HPが半分以下の状態でラウンドを迎えた回数。</summary>
    LowHpRounds = 10,

    /// <summary>とどめを刺した回数。</summary>
    Kills = 11,

    /// <summary>会心の一撃でとどめを刺した回数。</summary>
    CritKills = 12,

    /// <summary>味方を庇うことに成功した回数。</summary>
    ProtectedAlly = 13,

    /// <summary>盾で攻撃を受け止めた回数。</summary>
    ShieldBlocks = 14,

    /// <summary>自分の攻撃が1回も貫通せず、装甲に弾かれた回数。</summary>
    RepelledByArmor = 15,

    /// <summary>単独でクエストを正規クリアした回数。</summary>
    SoloClears = 16,

    /// <summary>ボス編成へ最後の一撃を与え、その遠征から生きて帰った回数。</summary>
    BossKills = 17,

    /// <summary>撤退して帰った回数。</summary>
    Retreats = 18,

    /// <summary>誰ひとり戦闘不能にならずにクリアした遠征の回数。</summary>
    FlawlessClears = 19,
}

public static class ExpeditionRecordTypes
{
    public static readonly IReadOnlyList<ExpeditionRecordType> All = new[]
    {
        ExpeditionRecordType.NearDeathRounds,
        ExpeditionRecordType.TimesDowned,
        ExpeditionRecordType.AlliesFellBeside,
        ExpeditionRecordType.QuestsFailed,
        ExpeditionRecordType.ComradesLost,
        ExpeditionRecordType.SoleSurvivor,
        ExpeditionRecordType.LowHpRounds,
        ExpeditionRecordType.Kills,
        ExpeditionRecordType.CritKills,
        ExpeditionRecordType.ProtectedAlly,
        ExpeditionRecordType.ShieldBlocks,
        ExpeditionRecordType.RepelledByArmor,
        ExpeditionRecordType.SoloClears,
        ExpeditionRecordType.BossKills,
        ExpeditionRecordType.Retreats,
        ExpeditionRecordType.FlawlessClears,
    };

    /// <summary>
    /// 命を危険へ晒したことを示す記録か。欠点のない特性はこの記録を条件に含めなければならない。
    /// 「素直な見返りが欲しければ、先に代価を払っていること」という設計判断そのもの。
    /// </summary>
    /// <remarks>
    /// 失敗と喪失もここに入る。全滅を潜ったことと仲間を失ったことは、
    /// 瀕死で立ち続けたことと同じかそれ以上に高くついた経験なので、
    /// 素直な見返りの代価として認める。
    /// </remarks>
    public static bool IsRisk(ExpeditionRecordType type) => type is
        ExpeditionRecordType.NearDeathRounds
        or ExpeditionRecordType.TimesDowned
        or ExpeditionRecordType.AlliesFellBeside
        or ExpeditionRecordType.QuestsFailed
        or ExpeditionRecordType.ComradesLost
        or ExpeditionRecordType.SoleSurvivor;

    public static string DisplayName(ExpeditionRecordType type) => type switch
    {
        ExpeditionRecordType.NearDeathRounds => "瀕死で戦ったラウンド",
        ExpeditionRecordType.TimesDowned => "戦闘不能になった回数",
        ExpeditionRecordType.AlliesFellBeside => "目の前で味方が倒れた回数",
        ExpeditionRecordType.LowHpRounds => "手傷を負って戦ったラウンド",
        ExpeditionRecordType.Kills => "とどめを刺した回数",
        ExpeditionRecordType.CritKills => "会心でとどめを刺した回数",
        ExpeditionRecordType.ProtectedAlly => "味方を庇った回数",
        ExpeditionRecordType.ShieldBlocks => "盾で受けた回数",
        ExpeditionRecordType.RepelledByArmor => "装甲に弾かれた回数",
        ExpeditionRecordType.QuestsFailed => "全滅した遠征の回数",
        ExpeditionRecordType.ComradesLost => "死なせた仲間の人数",
        ExpeditionRecordType.SoleSurvivor => "唯一の生還者になった回数",
        ExpeditionRecordType.SoloClears => "単独でやり遂げた依頼",
        ExpeditionRecordType.BossKills => "ボスにとどめを刺した回数",
        ExpeditionRecordType.Retreats => "撤退して帰った回数",
        ExpeditionRecordType.FlawlessClears => "誰も倒れずに終えた依頼",
        _ => type.ToString(),
    };

    /// <summary>特性が開花したときに読ませる、記録の言い換え。</summary>
    public static string Narrate(ExpeditionRecordType type, int count) => type switch
    {
        ExpeditionRecordType.NearDeathRounds => $"倒れる寸前のまま{count}ラウンド剣を握り続けた",
        ExpeditionRecordType.TimesDowned => $"{count}度倒れ、それでも戻ってきた",
        ExpeditionRecordType.AlliesFellBeside => $"隣で仲間が崩れ落ちるのを{count}度見た",
        ExpeditionRecordType.LowHpRounds => $"手傷を負ったまま{count}ラウンド戦い抜いた",
        ExpeditionRecordType.Kills => $"{count}体にとどめを刺してきた",
        ExpeditionRecordType.CritKills => $"急所を{count}度貫いた",
        ExpeditionRecordType.ProtectedAlly => $"{count}度、仲間の前に身体を割り込ませた",
        ExpeditionRecordType.ShieldBlocks => $"{count}度、盾で衝撃を受け止めた",
        ExpeditionRecordType.RepelledByArmor => $"{count}度、渾身の一撃を装甲に弾かれた",
        ExpeditionRecordType.QuestsFailed => $"{count}度、隊が全滅する場に居合わせた",
        ExpeditionRecordType.ComradesLost => $"{count}人の仲間を連れて帰れなかった",
        ExpeditionRecordType.SoleSurvivor => $"{count}度、ただ一人だけ帰ってきた",
        ExpeditionRecordType.SoloClears => $"{count}件の依頼を、たった一人でやり遂げた",
        ExpeditionRecordType.BossKills => $"{count}体の主を討ち取って生還した",
        ExpeditionRecordType.Retreats => $"{count}度、引き返すほうを選んだ",
        ExpeditionRecordType.FlawlessClears => $"{count}件の依頼を、誰ひとり倒れさせずに終えた",
        _ => $"{DisplayName(type)}: {count}",
    };
}

/// <summary>
/// 記録の集計。冒険者は生涯の累計をこの形で持ち、遠征ごとの増分も同じ型で数える。
/// </summary>
public sealed class ExpeditionRecord
{
    readonly Dictionary<ExpeditionRecordType, int> counts = new();

    public int this[ExpeditionRecordType type] =>
        counts.TryGetValue(type, out int value) ? value : 0;

    public bool IsEmpty => counts.Count == 0 || counts.Values.All(v => v == 0);

    public IReadOnlyDictionary<ExpeditionRecordType, int> Entries => counts;

    public void Add(ExpeditionRecordType type, int amount = 1)
    {
        if (amount == 0) return;
        counts[type] = this[type] + amount;
    }

    public void MergeFrom(ExpeditionRecord other)
    {
        foreach (var (type, amount) in other.counts)
            Add(type, amount);
    }

    public ExpeditionRecord Clone()
    {
        var copy = new ExpeditionRecord();
        copy.MergeFrom(this);
        return copy;
    }
}

/// <summary>
/// 1回の遠征のあいだ、隊員ごとの記録を集める。<see cref="AdventurerData"/> でない参加者
/// （敵や、戦闘シミュレーターの仮ユニット）は数えない。
///
/// <see cref="Systems.Battle.BattleResolver"/> へは任意引数で渡す。渡さなければ何も記録しないので、
/// 戦闘シミュレーターと Balance Lab は実在の冒険者の記録を汚さずに同じ戦闘ロジックを回せる。
/// </summary>
public sealed class ExpeditionRecorder
{
    readonly Dictionary<string, ExpeditionRecord> byAdventurerId = new();

    public IReadOnlyDictionary<string, ExpeditionRecord> Entries => byAdventurerId;

    public ExpeditionRecord For(string adventurerId)
    {
        if (!byAdventurerId.TryGetValue(adventurerId, out var record))
        {
            record = new ExpeditionRecord();
            byAdventurerId[adventurerId] = record;
        }
        return record;
    }

    public void Add(IUnitMember? member, ExpeditionRecordType type, int amount = 1)
    {
        if (member is not AdventurerData adventurer) return;
        For(adventurer.id).Add(type, amount);
    }

    /// <summary>参照専用の読み取り。<see cref="For"/> と違い、空のエントリを作らない。</summary>
    public int Count(string adventurerId, ExpeditionRecordType type) =>
        byAdventurerId.TryGetValue(adventurerId, out var record) ? record[type] : 0;

    /// <summary>味方側の全員に同じ記録を1つずつ加える（味方が倒れた瞬間の目撃など）。</summary>
    public void AddToAll(IEnumerable<IUnitMember?> members, ExpeditionRecordType type, int amount = 1)
    {
        foreach (var member in members)
            Add(member, type, amount);
    }
}
