using GuildSimulator.Core.GameData;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class HelpScreen
{
    public static async Task ShowAsync()
    {
        while (true)
        {
            Ui.BeginScreen();
            Ui.Header("ヘルプ・用語集");
            string choice = await Ui.SelectAsync("選択", new[]
            {
                new MenuOption("1", "基本の流れ"),
                new MenuOption("2", "ギルド運営", "資金・維持費・ギルドポイント・ランク"),
                new MenuOption("3", "クエスト", "難易度・緊急クエスト・昇格試験・撤退"),
                new MenuOption("4", "戦闘", "行動順・狙われ方・士気・隊列"),
                new MenuOption("5", "ダメージ計算", "命中・貫通・損傷の計算式"),
                new MenuOption("6", "能力値と戦闘数値", "能力値・装備が命中/DV/PV/AVに変わる過程"),
                new MenuOption("7", "冒険者・装備・遺物"),
                new MenuOption("0", "戻る", Style: TextStyle.Dim),
            });
            switch (choice)
            {
                case "1": await ShowBasicsAsync(); break;
                case "2": await ShowGuildAsync(); break;
                case "3": await ShowQuestAsync(); break;
                case "4": await ShowBattleAsync(); break;
                case "5": await ShowDamageAsync(); break;
                case "6": await ShowStatsAsync(); break;
                case "7": await ShowAdventurerAsync(); break;
                default: return;
            }
        }
    }

    static async Task ShowBasicsAsync()
    {
        Ui.BeginScreen();
        Ui.Header("基本の流れ");
        Ui.WriteLine("  1) クエストボードから受注するクエストを選ぶ");
        Ui.WriteLine("  2) 冒険者を編成して送り出す（前衛/後衛の配置あり）");
        Ui.WriteLine("  3) ターンを進めると自動で戦闘・探索が進行する");
        Ui.WriteLine("  4) クエストが完了すると報酬（資金・経験値・選択報酬）を受け取る");
        Ui.WriteLine("  5) 得た資金で雇用・装備・遺物を強化し、また次のクエストへ");
        Ui.WriteLine();
        Ui.WriteLine($"  毎ターン、ギルド基本維持費{GuildManager.GuildBaseUpkeepGoldPerTurn}Gと冒険者の賃金が資金から引かれるため、");
        Ui.WriteLine("  資金が尽きる（0以下になる）とゲームオーバーになる。");
        await Ui.PauseAsync();
    }

    static async Task ShowGuildAsync()
    {
        Ui.BeginScreen();
        Ui.Header("ギルド運営");
        Ui.WriteLine("  ・資金（Gold）    : クエスト報酬や目標数を超えた採取物の買取で得る。毎ターン維持費が引かれる。");
        Ui.WriteLine($"  ・維持費          : ギルド基本{GuildManager.GuildBaseUpkeepGoldPerTurn}G＋所属冒険者のレベル合計＋建設済み施設に応じた毎ターンの固定支出。");
        Ui.WriteLine("  ・ギルドポイント  : クエストクリアで得る昇格試験の解禁ポイント。撤退では入らない。");
        Ui.WriteLine("  ・ギルドランク    : 昇格試験（緊急クエスト）に正規クリアすると上がる。");
        Ui.WriteLine("                      ランクが上がると受注できるクエストの幅が広がる。");
        Ui.WriteLine("  ・施設            : ゴールドで建設するギルドの恒常強化。建設後は維持費が増える代わりに、");
        Ui.WriteLine("                      クエスト掲示枠・商店品揃え・休息回復量・成長率のいずれかを高め続ける。");
        await Ui.PauseAsync();
    }

    static async Task ShowQuestAsync()
    {
        Ui.BeginScreen();
        Ui.Header("クエスト");
        Ui.WriteLine("  ・難易度（★）    : クエストボードで受注前に確認できる。戦闘率・罠率・敵レベル帯の目安。");
        Ui.WriteLine("  ・緊急クエスト    : 通常枠とは別枠に掲示される特別なクエスト。昇格試験もこれに含まれる。");
        Ui.WriteLine("  ・昇格試験        : 必要ギルドポイントを満たすと出現する一度きりのクエスト。");
        Ui.WriteLine("                      クリアするとギルドランクが上がる（撤退・全滅ではランクは上がらない）。");
        Ui.WriteLine("  ・遠征方針        : 出発時に「生還優先」か「依頼達成優先」を選ぶ。");
        Ui.WriteLine("                      生還優先では損耗が危険域に入る前に撤退し、依頼達成優先では任務を続行する。");
        Ui.WriteLine("  ・物語クエスト    : 調査で得た手掛かりによって次の依頼が掲示される一度きりのクエスト。");
        Ui.WriteLine("                      発見した内容はメインメニューの「J. 調査記録」で確認できる。");
        Ui.WriteLine("  ・撤退            : 士気が尽きるとパーティは自動的に撤退する。基本報酬は無しになるが、");
        Ui.WriteLine("                      道中で得た戦利品（宝箱など）はそのまま持ち帰れる。");
        Ui.WriteLine("  ・全滅            : 全員が戦闘不能になった場合。報酬・戦利品はすべて失われる。");
        Ui.WriteLine("  ・選択イベント    : ターン内の最終フェーズ後に発生することがある。");
        Ui.WriteLine("                      未解決の選択がある間は次のターンへ進めない。");
        await Ui.PauseAsync();
    }

    static async Task ShowBattleAsync()
    {
        Ui.BeginScreen();
        Ui.Header("戦闘");
        Ui.WriteLine("  ・戦闘の流れ      : 両陣営の生存者が1回ずつ行動すると1ラウンド。どちらかが全滅するか、");
        Ui.WriteLine("                      士気が尽きて撤退するまで自動で繰り返される。");
        Ui.WriteLine("  ・行動順          : 命中補正の高い順に動く（同値ならランダム）。");
        Ui.WriteLine("  ・攻撃の解決      : 「命中判定 → 貫通判定 → 損傷判定」の3段階。");
        Ui.WriteLine("                      具体的な式はヘルプの「ダメージ計算」を参照。");
        Ui.WriteLine("  ・前衛/後衛       : 前衛は1〜3番目、後衛は4番目以降の枠。");
        Ui.WriteLine($"                      前衛が1人でも健在な間は攻撃の{BattleResolver.FRONT_TARGET_CHANCE * 100:0}%が前衛に向かい、");
        Ui.WriteLine($"                      後衛はDVが+{BattleResolver.REAR_COVER_DV_BONUS}される。前衛が全滅すると後衛が直接狙われる。");
        Ui.WriteLine($"                      後衛から近接武器で殴ると命中が-{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}される（弓・魔法は不利なし）。");
        Ui.WriteLine("  ・狙われやすさ    : 同じ列の中では装甲と回避の合計（AV+mAV+DV）が低い者ほど狙われやすい。");
        Ui.WriteLine("                      硬い前衛を立てるほど、後ろの柔らかい仲間を守れる。");
        Ui.WriteLine("  ・回復            : 治療用の武器を持つ者は、味方のHPが7割を切ると攻撃の代わりに手当てをする。");
        Ui.WriteLine($"                      回復量は回復値×{BattleResolver.HEAL_SCALE:0.#}（1d20の出目20ならさらに×{BattleResolver.HEAL_CRIT_SCALE:0.#}、出目1は失敗）。");
        Ui.WriteLine("  ・士気            : パーティ全体の粘り強さ。出発時の最大値は編成の精神力(SAN)合計。");
        Ui.WriteLine("                      0になるとその場で撤退する（全滅の手前で止まる安全弁）。");
        Ui.WriteLine("  ・士気の減りかた  : ①そのラウンドで実際に減ったHPの割合に応じて（回復で押し返した分は減らない）");
        Ui.WriteLine($"                      ②仲間が1人倒れるごとに{MoraleState.AllyDownFlat} ③格上との遭遇時にレベル差1につき{MoraleState.LevelGapFlat}（最大{MoraleState.LevelGapFlatCap}）。");
        Ui.WriteLine($"                      戦闘に勝つと最大値の{MoraleState.VictoryRecoverRate * 100:0}%、休息では{MoraleState.RestRecoverRate * 100:0}%回復する。");
        await Ui.PauseAsync();
    }

    static async Task ShowDamageAsync()
    {
        string penDie = $"1d{QudCombat.PENETRATION_DIE}{QudCombat.PENETRATION_OFFSET:+#;-#;+0}";

        Ui.BeginScreen();
        Ui.Header("ダメージ計算");
        Ui.WriteLine("  攻撃1回は「命中判定 → 貫通判定 → 損傷判定」の順に解決する。");
        Ui.WriteLine("  能力値は直接ダメージに乗らない。ダメージの大きさを決めるのは「何回貫通したか」だけ。");
        Ui.WriteLine();
        Ui.WriteLine($"  ① 命中判定   1d{QudCombat.HIT_DIE} ＋ 命中補正 ＞ 相手のDV（回避値）なら命中");
        Ui.WriteLine("     ・命中補正 ＝ 敏捷modifier ＋ 装備の命中補正 ＋ スキル・遺物 － 過積載");
        Ui.WriteLine($"                （後衛から近接武器で殴る場合はさらに-{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}）");
        Ui.WriteLine($"     ・DVは{QudCombat.BASE_DV}を基準に敏捷と装備で増減する。重い鎧はDVを下げる。");
        Ui.WriteLine($"     ・素の出目{QudCombat.CRITICAL_ROLL}は会心。DVに関わらず必ず命中する。");
        Ui.WriteLine($"     ・素の出目{QudCombat.FUMBLE_ROLL}は補正がいくら高くても必ず外れる。");
        Ui.WriteLine("     ・内訳の詳しい作り方はヘルプの「能力値と戦闘数値」を参照。");
        Ui.WriteLine();
        Ui.WriteLine($"  ② 貫通判定   ({penDie}) ＋ PV（貫通値） ＞ 相手のAV（装甲値）を{QudCombat.PENETRATION_ROLLS_PER_SET}回で1セット");
        Ui.WriteLine("     ・1回でも上回れば1貫通。");
        Ui.WriteLine($"     ・{QudCombat.PENETRATION_ROLLS_PER_SET}回とも上回ったらPVを-{QudCombat.PENETRATION_PV_DECAY}して次のセットへ進み、貫通を積み増す。");
        Ui.WriteLine("       セットごとにPVが減るので、どれだけ相手が柔らかくても貫通回数は有限で止まる。");
        Ui.WriteLine("     ・1回も上回らなければ装甲に弾かれてダメージ0。最低保証ダメージはない。");
        Ui.WriteLine($"     ・貫通ダイスは出目{QudCombat.PENETRATION_DIE}が出るたびに振り足して加算する（上振れは青天井）。");
        Ui.WriteLine("       AVが高くても、薄い確率で抜ける目は常に残る。");
        Ui.WriteLine();
        Ui.WriteLine("  ③ 損傷判定   武器のダメージダイスを「貫通回数」ぶん振って合計する");
        Ui.WriteLine("     ・例）1d6の武器で3回貫通 → 1d6を3回振って合計（3〜18ダメージ）。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ PV（貫通値）の作り方");
        Ui.WriteLine("     PV ＝ 武器の基礎PV ＋ min(能力値modifier, 武器ごとの上限) ＋ 装備・スキルのPV補正");
        Ui.WriteLine($"     ・能力値modifier ＝ (能力値 - {QudCombat.MODIFIER_BASELINE}) ÷ {QudCombat.MODIFIER_STEP} の切り捨て。");
        Ui.WriteLine($"       {QudCombat.MODIFIER_BASELINE}で±0、{QudCombat.MODIFIER_BASELINE + QudCombat.MODIFIER_STEP}で+1、{QudCombat.MODIFIER_BASELINE - QudCombat.MODIFIER_STEP}で-1。");
        Ui.WriteLine("     ・武器ごとに乗せられる上限がある。短剣や投石は腕力を乗せきれず頭打ちになり、");
        Ui.WriteLine("       斧や大剣は上限がなく青天井に伸びる。");
        Ui.WriteLine("       力自慢には重い得物を、器用な者には軽い得物を持たせるとよい。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 物理と魔法");
        Ui.WriteLine("     どちらで殴るかは装備した武器で決まる（能力値の大小では決まらない）。");
        Ui.WriteLine("     ・物理武器 : 筋力のmodifierをPVに乗せ、相手のAV（装甲値）と突き合わせる。");
        Ui.WriteLine("     ・魔法武器 : 知力のmodifierをPVに乗せ、相手のmAV（魔法装甲値）と突き合わせる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 会心（1d20の素の出目20）");
        Ui.WriteLine($"     命中が確定したうえで、PVが+{QudCombat.CRITICAL_PV_BONUS}される。");
        Ui.WriteLine("     さらに1回も抜けなかった場合でも、最低1貫通ぶんのダメージは通る。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 戦闘ログの読み方");
        Ui.WriteLine("     「命中！（1d20=14+3=17 > DV6、物理 PV8 vs AV5） 2回貫通 1d6×2 ダメージ=7」は、");
        Ui.WriteLine("     1d20で14を出し命中補正+3を足した17が相手のDV6を上回って命中、");
        Ui.WriteLine("     PV8とAV5の貫通判定で2回抜け、1d6を2回振って合計7を与えた、という意味。");
        await Ui.PauseAsync();
    }

    static async Task ShowStatsAsync()
    {
        int overweightToHit = (int)AdventurerData.OVERWEIGHT_TO_HIT_PENALTY;
        int overweightDv = (int)AdventurerData.OVERWEIGHT_DV_PENALTY;

        Ui.BeginScreen();
        Ui.Header("能力値と戦闘数値");
        Ui.WriteLine("  能力値そのものが判定に使われることはない。戦闘に入る前に、能力値・装備・スキル・遺物が");
        Ui.WriteLine("  「HP / 士気 / 命中 / DV / PV / AV / 回復力」の7つの数値へ変換され、判定はその数値だけを見る。");
        Ui.WriteLine($"  多くは modifier を通す。modifier ＝ (能力値 - {QudCombat.MODIFIER_BASELINE}) ÷ {QudCombat.MODIFIER_STEP} の切り捨て。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 能力値がどこに効くか");
        Ui.WriteLine("     VIT 体力 : HP＝(VIT×10＋CON×5)÷2 / 積載上限");
        Ui.WriteLine("     CON 耐久 : AV＝CONのmodifier / HP / 積載上限");
        Ui.WriteLine("     MEN 精神 : 士気＝MEN×10（パーティ合計が最大値）/ mAV＝MENのmodifier / 回復力");
        Ui.WriteLine($"     AGI 敏捷 : 命中補正＝AGIのmodifier / DV＝{QudCombat.BASE_DV}＋AGIのmodifier");
        Ui.WriteLine("     STR 筋力 : 物理PV＝STRのmodifier（武器の上限まで）/ 積載上限");
        Ui.WriteLine("     INT 知力 : 魔法PV＝INTのmodifier（武器の上限まで）/ 回復力");
        Ui.WriteLine("     ・敏捷は命中とDVの両方に効くので、1点の価値がもっとも広い。");
        Ui.WriteLine("     ・レベルアップで伸びるのはVIT・MEN・STR・AGI・INTの5つ。CONは伸びない。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 命中補正の内訳");
        Ui.WriteLine("     命中補正 ＝ AGIのmodifier");
        Ui.WriteLine("              ＋ 装備の命中補正の合計（短剣・投石・風杖は+2、槍・斧は-2〜-4）");
        Ui.WriteLine("              ＋ スキル・遺物の命中補正");
        Ui.WriteLine($"              － 過積載ペナルティ（最大-{overweightToHit}）");
        Ui.WriteLine($"              －（後衛から近接武器で殴る場合のみ）{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}");
        Ui.WriteLine("     この合計を1d20に足し、相手のDVと比べる。行動順もこの値の高い順に決まる。");
        Ui.WriteLine("     例）AGI12（modifier+2）が短剣（命中+2）を持つと命中補正は+4。");
        Ui.WriteLine($"        DV{QudCombat.BASE_DV}の相手には1d20で3以上を出せば命中する（90%）。");
        Ui.WriteLine("        同じ相手でも斧（命中-4）に持ち替えると命中補正は-2、命中率は60%まで落ちる。");
        Ui.WriteLine("        当てにくい武器はそのぶん基礎PVとダメージダイスが大きい、という取引になっている。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 回避DVの内訳");
        Ui.WriteLine($"     DV ＝ {QudCombat.BASE_DV}（全員共通の下駄）＋ AGIのmodifier ＋ 装備のDV補正");
        Ui.WriteLine($"        ＋ スキル・遺物 － 過積載ペナルティ（最大-{overweightDv}）");
        Ui.WriteLine($"     ・前衛が健在な間、後衛はさらに+{BattleResolver.REAR_COVER_DV_BONUS}される。");
        Ui.WriteLine("     ・板金鎧は-4、鎖帷子は-2のように、硬い鎧ほどDVを下げてAVを上げる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ PV・AVの内訳");
        Ui.WriteLine("     PV ＝ 武器の基礎PV ＋ min(STR/INTのmodifier, 武器ごとの上限) ＋ 装備・スキルのPV補正");
        Ui.WriteLine($"     ・素手は基礎PV{AdventurerData.UNARMED_PV}・ダメージ{AdventurerData.UNARMED_DAMAGE_DICE}。乗せるmodifierに上限はなく、膂力はそのまま拳に乗る。");
        Ui.WriteLine("       ただし基礎PVもダメージダイスも小さいので、武器はやはり持たせたほうがよい。");
        Ui.WriteLine("     ・上限の目安は 短剣・投石・風杖が+5、剣・弓・水杖が+6、槍・土杖が+7、斧・火杖・闇杖が+8。");
        Ui.WriteLine("       回復用の光杖は0で、力も知恵も上乗せできない。");
        Ui.WriteLine("     AV ＝ CONのmodifier ＋ 防具のAV補正 ＋ スキル・遺物（mAVはMENのmodifierから同様に）");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 積載と過積載");
        Ui.WriteLine("     積載上限 ＝ CON ＋ (STR＋VIT)÷2。装備の重さの合計がこれを超えると過積載になる。");
        Ui.WriteLine($"     ・上限を1超えるごとに過積載率が{AdventurerData.OVERWEIGHT_RATE_PER_POINT * 100:0}%増える（最大100%）。");
        Ui.WriteLine($"     ・過積載率に比例して DV最大-{overweightDv}、命中最大-{overweightToHit}。AVは担いでいるぶんそのまま効くので削られない。");
        Ui.WriteLine("     ・重い鎧を着せるなら、CONとSTRの高い者に。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 補正の重ねかた");
        Ui.WriteLine("     ・装備      : 装備している全スロットの補正を合計する。");
        Ui.WriteLine("     ・スキル    : 前衛限定・後衛限定・特定の武器/防具限定といった条件を満たすときだけ乗る。");
        Ui.WriteLine("                   パーティ全体に効くスキルは、生存している全員にそれぞれ加算される。");
        Ui.WriteLine("     ・遺物      : 冒険者側のみ、全員に加算される。");
        Ui.WriteLine("     ・倍率がかかるのはHP・士気・回復力だけ。命中/DV/PV/AVは1点の重みが大きいため加算でしか動かない。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 敵の数値");
        Ui.WriteLine("     敵もまったく同じ式で命中・DV・PV・AVを組み立てる。");
        Ui.WriteLine($"     ・敵の能力値はレベル1を基準に、レベルが1上がるごとに+{EnemyData.GROWTH_PER_LEVEL * 100:0}%（線形）。");
        Ui.WriteLine("     ・獣は防具を着ていなくても、甲殻や毛皮のぶんのAV・mAVを持つ。");
        await Ui.PauseAsync();
    }

    static async Task ShowAdventurerAsync()
    {
        Ui.BeginScreen();
        Ui.Header("冒険者・装備・遺物");
        Ui.WriteLine("  ・冒険者          : 雇用して編成に加える。クエストで経験値を得てレベルアップする。");
        Ui.WriteLine("                      死亡した冒険者は蘇生できない。");
        Ui.WriteLine("                      人物詳細では経歴・性格・動機・得意分野と、直近の遠征履歴を確認できる。");
        Ui.WriteLine("  ・装備            : 商店で購入・売却し、冒険者一覧画面で着せ替えできる。");
        Ui.WriteLine("  ・レアリティ      : コモン、アンコモン、レア、ユニーク、レジェンドの順に希少。");
        Ui.WriteLine("  ・消費アイテム    : 出発前に最大2個選び、出発時に消費してクエスト中だけ効果を得る。");
        Ui.WriteLine("  ・商店            : 品ぞろえと在庫は5ターンごと（Turn 1、6、11…）に更新される。");
        Ui.WriteLine("  ・遺物            : ギルド全体に常時効果を及ぼす特別なアイテム。クエストの選択報酬や");
        Ui.WriteLine("                      道中の宝箱で入手できる。所持しているだけで効果を発揮する。");
        await Ui.PauseAsync();
    }
}
