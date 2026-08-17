namespace GuildSimulator.Core.GameData;

/// <summary>
/// 遠征そのものの結末を記録に落とす。
///
/// <see cref="Systems.Battle.BattleResolver"/> のフックが数えるのは戦闘ラウンド単位の出来事
/// （何度瀕死で立っていたか、何度弾かれたか）だが、こちらが数えるのは<b>1本の依頼が
/// どう終わったか</b>——単独でやり遂げたのか、全滅したのか、誰を連れて帰れなかったのか。
/// 記録の粒度が違うだけで、行き先は同じ <see cref="ExpeditionRecord"/> になる。
///
/// <para>
/// <b>必ず帰還時の死亡判定が終わったあとに呼ぶこと。</b> 戦闘不能は即死ではなく、
/// 死亡が確定するのは <c>ResolvePendingTrauma</c> を通ったあとなので、
/// それより前に数えると「死なせた仲間」が常に0になる。
/// </para>
/// </summary>
public static class ExpeditionOutcomeRecorder
{
    public static void Record(QuestRun run)
    {
        var members = run.EnumerateMembers().ToList();
        if (members.Count == 0) return;

        var fallen = members.Where(m => !m.isAlive).ToList();
        var survivors = members.Where(m => m.isAlive).ToList();

        // 「誰も倒れなかった」は戦闘記録から導く。帰還時には戦闘不能が負傷へ解決済みで、
        // その場の状態を見ても道中で倒れたかどうかは分からないため。
        bool nobodyFell = members.All(m =>
            run.recorder.Count(m.id, ExpeditionRecordType.TimesDowned) == 0);

        foreach (var member in members)
        {
            var record = run.recorder.For(member.id);

            if (run.failed)
                record.Add(ExpeditionRecordType.QuestsFailed);
            if (run.retreated)
                record.Add(ExpeditionRecordType.Retreats);

            // 死者本人は勘定に入れない。数えているのは「連れて帰れなかった仲間」。
            int lost = fallen.Count(other => other != member);
            if (member.isAlive && lost > 0)
                record.Add(ExpeditionRecordType.ComradesLost, lost);

            // 隊が自分ひとりきりだった遠征は「唯一の生還者」には数えない。失う仲間がいない。
            if (member.isAlive && survivors.Count == 1 && members.Count > 1 && lost > 0)
                record.Add(ExpeditionRecordType.SoleSurvivor);

            if (!run.completed || !member.isAlive) continue;

            if (members.Count == 1)
                record.Add(ExpeditionRecordType.SoloClears);
            // 主討伐は同行者全員の実績ではない。ボス編成の最後の1体へとどめを刺し、
            // かつ本人が依頼を完遂して生還した場合だけ数える。
            if (run.bossDefeated && member.id == run.bossFinisherAdventurerId)
                record.Add(ExpeditionRecordType.BossKills);
            if (nobodyFell)
                record.Add(ExpeditionRecordType.FlawlessClears);
        }
    }
}
