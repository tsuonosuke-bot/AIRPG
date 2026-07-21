namespace GuildSimulator.Core.Systems.Battle;

/// <summary>
/// パーティの士気。クエスト開始時に san（精神力）の合計で初期化され、被害を受けるたびに減る。
/// 0 になった時点でパーティは撤退する（＝全滅する前の安全弁）。休息と戦闘勝利で回復する。
/// HPと違って「粘り強さ」を表すため、mental の高い編成ほど不利な戦況に長く踏みとどまれる。
/// </summary>
public class MoraleState
{
    /// <summary>
    /// 損耗による士気の減りかた。最大HPの1割を失うと士気を「最大値の1割 × この係数」だけ失う。
    /// 士気の最大値に比例させているので、この係数だけで「HPが何割まで減ったら折れるか」が決まる
    /// （1/1.3 ≒ 77% を失った時点、つまりHP2割強で撤退）。編成が変わっても体感がぶれない。
    /// </summary>
    public const float HpLossDrainK = 1.3f;

    /// <summary>味方が1人倒れるごとに失う士気（固定値）。san が高い編成ほど動揺しにくい。</summary>
    public const int AllyDownFlat = 40;

    /// <summary>敵とのレベル差1につき遭遇時に失う士気（固定値）。</summary>
    public const int LevelGapFlat = 12;
    public const int LevelGapFlatCap = 60;

    /// <summary>戦闘に勝利したときに回復する割合。</summary>
    public const float VictoryRecoverRate = 0.2f;

    /// <summary>休息イベントで回復する割合。</summary>
    public const float RestRecoverRate = 0.5f;

    public int Max { get; }
    public int Current { get; private set; }

    public MoraleState(int max)
    {
        Max = Math.Max(1, max);
        Current = Max;
    }

    public float Rate => Math.Clamp((float)Current / Max, 0f, 1f);
    public bool IsBroken => Current <= 0;

    /// <summary>実際に減った量を返す。</summary>
    public int Drain(int amount)
    {
        if (amount <= 0) return 0;
        int before = Current;
        Current = Math.Max(0, Current - amount);
        return before - Current;
    }

    /// <summary>実際に回復した量を返す。</summary>
    public int Restore(int amount)
    {
        if (amount <= 0) return 0;
        int before = Current;
        Current = Math.Min(Max, Current + amount);
        return Current - before;
    }

    public int RestoreRate(float rate) => Restore((int)Math.Ceiling(Max * rate));

    /// <summary>
    /// 受けた損害で士気を削る。生の damage ではなくパーティ最大HPに対する割合で見るため、
    /// 治療で回復した分はきちんと帳消しにならず、かつ編成のHP規模に依存しない。
    /// </summary>
    public int DrainFromDamage(int damage, int partyMaxHp)
    {
        if (damage <= 0 || partyMaxHp <= 0) return 0;
        return Drain((int)Math.Round(Max * ((float)damage / partyMaxHp) * HpLossDrainK));
    }

    public int DrainAllyDown(int count = 1) => Drain(AllyDownFlat * Math.Max(0, count));

    public int DrainLevelGap(int gap)
        => gap <= 0 ? 0 : Drain(Math.Min(LevelGapFlatCap, gap * LevelGapFlat));
}
