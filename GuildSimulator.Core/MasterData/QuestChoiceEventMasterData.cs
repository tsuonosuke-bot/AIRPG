using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.MasterData;

public class QuestChoiceEventMasterData
{
    public string id = "";
    public string title = "";
    public string description = "";
    public int weight = 10;
    public List<QuestChoiceOptionData> options = new();
}

public class QuestChoiceOptionData
{
    public string text = "";
    public string resultText = "";
    public QuestChoiceEffectType effectType;
    public int value;
    public string targetId = "";
    public EquipmentMasterData? Equipment { get; set; }
    public ConsumableMasterData? Consumable { get; set; }
    public SkillMasterData? Skill { get; set; }

    /// <summary>この選択で発見する物語上の手掛かり。通常の効果と同時に適用できる。</summary>
    public string grantedClueId = "";
    public StoryClueMasterData? GrantedClue { get; set; }

    /// <summary>この選択で確定する物語分岐。分岐はセーブされ、以後の世界効果に使われる。</summary>
    public string storyBranchId = "";

    /// <summary>調査記録に残す、分岐後の世界の変化。</summary>
    public string storyOutcomeText = "";

    /// <summary>
    /// 実行前にプレイヤーがパーティから1人選ぶ。
    /// 「誰に賭けるか」を決めさせてから結果を振るのが、この手のイベントの肝。
    /// </summary>
    public bool targetsOneMember;

    /// <summary>
    /// 結果の抽選表。空なら <see cref="effectType"/> がそのまま起きる（従来の挙動）。
    /// ここに複数入れると、選んだ後で何が起きるかが運になる。
    /// </summary>
    public List<QuestChoiceOutcome> outcomes = new();

    /// <summary>この選択肢で実際に起きうる結果。抽選表が空なら選択肢自身を1件として返す。</summary>
    public IReadOnlyList<QuestChoiceOutcome> Outcomes => outcomes.Count > 0
        ? outcomes
        : new[]
        {
            new QuestChoiceOutcome
            {
                weight = 1, effectType = effectType, value = value,
                targetId = targetId, resultText = "",
                Equipment = Equipment, Consumable = Consumable, Skill = Skill,
            },
        };

    /// <summary>結果が1通りに決まっているか。ギャンブル性のある選択肢かどうかの目印。</summary>
    public bool IsGamble => outcomes.Count > 1;
}

/// <summary>選択肢を選んだあとに抽選される結果の1件。</summary>
public class QuestChoiceOutcome
{
    public int weight = 1;
    public QuestChoiceEffectType effectType;
    public int value;

    /// <summary>効果ごとの対象指定。能力名（Strength など）やスキルIDが入る。</summary>
    public string targetId = "";

    /// <summary>この結果になったときに見せる文。空なら選択肢のresultTextだけが出る。</summary>
    public string resultText = "";

    public EquipmentMasterData? Equipment { get; set; }
    public ConsumableMasterData? Consumable { get; set; }
    public SkillMasterData? Skill { get; set; }
}
