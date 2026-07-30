using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class HelpScreen
{
    /// <summary>
    /// 武器とマスタリーの説明はマスタデータから組み立てる。
    /// JSONを触ったときにヘルプの記述だけが取り残されるのを防ぐため、数値はここに書き写さない。
    /// </summary>
    public static async Task ShowAsync(GameMasterData db)
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
                new MenuOption("7", "武器の種類", "剣・短剣・槍・斧・弓・魔法の得手不得手"),
                new MenuOption("8", "職業とマスタリー", "マスタリーの効果と習得条件"),
                new MenuOption("9", "冒険者・装備・遺物"),
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
                case "7": await ShowWeaponClassesAsync(db); break;
                case "8": await ShowMasteryAsync(db); break;
                case "9": await ShowAdventurerAsync(); break;
                default: return;
            }
        }
    }

    /// <summary>F→E→…→S の並び。段階数を変えても説明が追随するよう、Rank から組み立てる。</summary>
    static IEnumerable<string> RankLadder()
    {
        for (int r = Rank.Min; r <= Rank.Max; r++) yield return Rank.Label(r);
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
        Ui.WriteLine($"  ・ランク表記      : {string.Join(" → ", RankLadder())} の{Rank.Max}段階。{Rank.Label(Rank.Min)}が最も低く、{Rank.Label(Rank.Max)}が最高。");
        Ui.WriteLine("                      冒険者・クエスト・ギルドのランクはすべてこの同じ物差しで比べる。");
        Ui.WriteLine($"                      掲示されるのは「クエストランク ≦ ギルドランク」のものだけ。");
        Ui.WriteLine("  ・施設            : ゴールドで建設するギルドの恒常強化。建設後は維持費が増える代わりに、");
        Ui.WriteLine("                      クエスト掲示枠・商店品揃え・休息回復量・成長率のいずれかを高め続ける。");
        await Ui.PauseAsync();
    }

    static async Task ShowQuestAsync()
    {
        Ui.BeginScreen();
        Ui.Header("クエスト");
        Ui.WriteLine("  ・難易度（★）    : クエストボードで受注前に確認できる。戦闘率・罠率・敵レベル帯の目安。");
        Ui.WriteLine($"  ・クエストランク  : {Rank.Label(Rank.Min)}〜{Rank.Label(Rank.Max)}。ギルドランク以下のものだけが掲示される。");
        Ui.WriteLine("                      冒険者ランクと突き合わせて「適正ランク」かどうかも決まり、");
        Ui.WriteLine("                      適正ランクを正規クリアするとクラス習熟度が増える（ヘルプ8を参照）。");
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
        Ui.WriteLine("     ・武器ごとに乗せられる上限がある。短剣は腕力を乗せきれず+5で頭打ちになり、");
        Ui.WriteLine("       斧は+8まで受け止める。力自慢には重い得物を、器用な者には軽い得物を持たせるとよい。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 物理と魔法");
        Ui.WriteLine("     どちらで殴るかは装備した武器で決まる（能力値の大小では決まらない）。");
        Ui.WriteLine("     ・物理武器 : 筋力のmodifierをPVに乗せ、相手のAV（装甲値）と突き合わせる。");
        Ui.WriteLine("     ・魔法武器 : 知力のmodifierをPVに乗せ、相手のmAV（魔法装甲値）と突き合わせる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 会心（1d20の素の出目20）");
        Ui.WriteLine($"     命中が確定したうえで、PVが+{QudCombat.CRITICAL_PV_BONUS}される。");
        Ui.WriteLine("     さらに1回も抜けなかった場合でも、最低1貫通ぶんのダメージは通る。");
        Ui.WriteLine("     短剣のように会心域を持つ武器は、出目18〜20でも会心になる（出目1は決して会心にならない）。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 武器の種類ごとの上乗せ");
        Ui.WriteLine("     ・装甲貫通（槍）: 貫通判定の前に、相手のAVをその値だけ差し引く。");
        Ui.WriteLine("     ・装甲破壊（斧）: 貫通した攻撃1回につき、相手のAVを恒久的に削る。");
        Ui.WriteLine($"     ・連撃（短剣）  : 1手番に続けて振るう。追撃はPVが-{QudCombat.FOLLOW_UP_PV_PENALTY}ずつ下がっていく。");
        Ui.WriteLine("     詳しくはヘルプの「武器の種類」を参照。");
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
        Ui.WriteLine("              ＋ 装備の命中補正の合計（短剣は+3、剣は+1、槍は-2、斧は-4）");
        Ui.WriteLine("              ＋ スキル・遺物の命中補正");
        Ui.WriteLine($"              － 過積載ペナルティ（最大-{overweightToHit}）");
        Ui.WriteLine($"              －（後衛から近接武器で殴る場合のみ）{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}");
        Ui.WriteLine("     この合計を1d20に足し、相手のDVと比べる。行動順もこの値の高い順に決まる。");
        Ui.WriteLine("     例）AGI12（modifier+2）が短剣（命中+3）を持つと命中補正は+5。");
        Ui.WriteLine($"        DV{QudCombat.BASE_DV}の相手には1d20で2以上を出せば命中する（95%）。");
        Ui.WriteLine("        同じ相手でも斧（命中-4）に持ち替えると命中補正は-2、命中率は60%まで落ちる。");
        Ui.WriteLine("        当てにくい武器はそのぶん一撃が重い、という取引になっている。");
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
        Ui.WriteLine("     ・上限の目安は 短剣・風杖が+5、剣・弓・水杖が+6、槍・土杖が+7、斧・火杖・闇杖が+8。");
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

    static async Task ShowWeaponClassesAsync(GameMasterData db)
    {
        Ui.BeginScreen();
        Ui.Header("武器の種類");
        Ui.WriteLine("  同じ段階の武器なら、どれを選んでも総合的な強さはほぼ揃えてある。");
        Ui.WriteLine("  違うのは「どんな相手に強いか」で、噛み合う相手に当てるほど働く。");
        Ui.WriteLine("  得意不得意を分けるのは、ほぼ相手のAV（装甲値）の高さである。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 剣 ── 何でもこなす、軽くて扱いやすい");
        Ui.WriteLine("     特殊な効果を持たない代わりに、命中補正が正で重量も軽い。");
        Ui.WriteLine("     尖った長所がない代わりに穴もないので、相手を選ばず、序盤ほど頼りになる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 短剣 ── 手数と急所。当てやすいが一撃は軽い");
        Ui.WriteLine("     ・連撃      : 1手番に続けて斬りかかる。");
        Ui.WriteLine($"                   ただし追撃はPVが-{QudCombat.FOLLOW_UP_PV_PENALTY}ずつ下がるので、手数ほどには火力が伸びない。");
        Ui.WriteLine("     ・会心域    : 出目20だけでなく、19、18…でも会心になる（他の武器は20のみ）。");
        Ui.WriteLine("     ・命中補正は全武器で最高、重量は最軽量。基礎PVとダメージダイスは最も小さい。");
        Ui.WriteLine("     装甲の薄い相手を数で押し潰すのが仕事。硬い相手には一撃が軽すぎて弾かれる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 槍 ── 装甲を無視して突く。重くて当てにくい");
        Ui.WriteLine("     ・装甲貫通  : 命中したとき、相手のAVをその値だけ無かったことにして貫通判定を行う。");
        Ui.WriteLine("                   PVを上げるのとは違い、硬い相手にだけ効き、素肌の相手には何も起きない。");
        Ui.WriteLine("                   毎回の攻撃に必ず乗るが、効果はその一撃かぎりで後には残らない。");
        Ui.WriteLine("     ・命中は負、重量は重め。基礎PVは高いがダメージダイスは小さい。");
        Ui.WriteLine("     鎧を着た相手に強い。初撃から効くので、短い戦闘でも取りこぼしがない。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 斧 ── 装甲そのものを砕く。当たらないが一撃が重い");
        Ui.WriteLine("     ・装甲破壊  : 貫通した攻撃1回につき、相手のAVをその値だけ恒久的に削る。");
        Ui.WriteLine($"                   削れた装甲はその戦闘のあいだ戻らず、1体につき合計-{QudCombat.MAX_ARMOR_SHRED}まで積み上がる。");
        Ui.WriteLine("                   削るのは物理AVだけで、魔法装甲(mAV)は割れない。");
        Ui.WriteLine("     ・命中は全武器で最も低く、重量は最重量。ダメージダイスは全武器で最大。");
        Ui.WriteLine("     ・能力値上限が最も高く、筋力を伸ばすほど伸びしろが残る。");
        Ui.WriteLine("     削った装甲は味方全員の攻撃にも効く。斧使いが前を崩し、仲間が叩き込むと噛み合う。");
        Ui.WriteLine("     ただし貫通できなければ削れない。初撃は素のAVと戦うので、立ち上がりは槍より遅い。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 弓 ── 後衛から撃てる");
        Ui.WriteLine($"     後衛から撃っても命中-{BattleResolver.REAR_MELEE_TO_HIT_PENALTY}を受けない。前衛に守られたまま攻撃に参加できる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 魔法（火・風・水・土・闇） ── AVではなくmAVと戦う");
        Ui.WriteLine("     PVに乗るのは筋力ではなく知力。相手の魔法装甲(mAV)と突き合わせるので、");
        Ui.WriteLine("     鎧で固めた相手ほど通りやすい。弓と同じく後衛から撃っても不利にならない。");
        Ui.WriteLine("     装甲貫通も装甲破壊もmAVには効かない。光の杖だけは攻撃ではなく手当てをする。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 近接4種の数値（同じ商店Tierで比べたもの）");
        ShowMeleeComparisonTable(db);
        Ui.WriteLine();
        Ui.WriteLine("  マスタリーの効果と習得条件はヘルプの「職業とマスタリー」を参照。");
        await Ui.PauseAsync();
    }

    /// <summary>近接4種を実データから並べる。数値はここに書き写さず equipment.json をそのまま読む。</summary>
    static void ShowMeleeComparisonTable(GameMasterData db)
    {
        var melee = new[] { WeaponType.Sword, WeaponType.Dagger, WeaponType.Spear, WeaponType.Axe };
        Ui.Dim($"     {Ui.PadWide("武器", 8)}{Ui.PadWide("命中", 8)}{Ui.PadWide("能力値上限", 12)}{Ui.PadWide("重量", 8)}個性");
        foreach (var type in melee)
        {
            // 同じ武器種なら個性・能力値上限・命中補正は全Tier共通なので、代表を1本選べば足りる。
            var sample = db.equipment.Values
                .Where(e => e.type == EquipmentType.Weapon && e.weaponType == type)
                .OrderBy(e => e.shopTier)
                .FirstOrDefault();
            if (sample == null) continue;

            var traits = EquipmentText.TraitParts(sample.Traits);
            Ui.WriteLine($"     {Ui.PadWide(EquipmentText.WeaponClassName(type), 8)}"
                + Ui.PadWide($"{sample.bonus.toHit:+#;-#;+0}", 8)
                + Ui.PadWide($"{sample.maxStatBonus:+#;-#;+0}", 12)
                + Ui.PadWide($"{sample.weight}", 8)
                + (traits.Count > 0 ? string.Join(" ", traits) : "なし"));
        }
        Ui.Dim("     基礎PVとダメージダイスはTierで上がる。商店や持ち物の画面で個別に確認できる。");
    }

    static async Task ShowMasteryAsync(GameMasterData db)
    {
        Ui.BeginScreen();
        Ui.Header("職業とマスタリー");
        Ui.WriteLine("  ■ マスタリーとは");
        Ui.WriteLine("     職業スキルのうち「○○マスタリー」は、その武器種を握っているときだけ効く。");
        Ui.WriteLine("     持ち替えると効果は消えるので、職業と得物は揃えたほうがよい。");
        Ui.WriteLine("     基礎はどれもPV+2で横並びだが、「・極」まで育つと得物ごとの持ち味が伸びる。");
        Ui.WriteLine("     基礎と「・極」は入れ替わりではなく両方が乗る（重ねがけになる）。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 習得条件 ── クラス習熟度");
        Ui.WriteLine("     職業スキルは経験値やレベルではなく「その職業での正規クリア回数（クラス習熟度）」で開く。");
        Ui.WriteLine("     一覧の各スキルには必要な習熟度が決まっていて、0のものは職業に就いた瞬間に手に入る。");
        Ui.WriteLine();
        Ui.WriteLine("     習熟度が1つ増えるのは、次をすべて満たしたときだけ。");
        Ui.WriteLine("       ・そのクエストに参加していること");
        Ui.WriteLine("       ・クエストを正規クリアすること（撤退・全滅では増えない）");
        Ui.WriteLine("       ・帰還時に生存していること");
        Ui.WriteLine("       ・クエストが、その冒険者にとって適正ランクであること");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 適正ランクとは");
        Ui.WriteLine($"     自分と同じランクから、{Rank.SuitableRangeAbove}つ上までのクエスト。");
        Ui.WriteLine($"     例）ランク{Rank.Label(Rank.Min + 2)}の冒険者なら {Rank.SuitableRangeLabel(Rank.Min + 2)} のクエストが適正。");
        Ui.WriteLine("     ・格下では学ぶものがない。ランクが上がった冒険者は、易しい依頼では伸びなくなる。");
        Ui.WriteLine("     ・格上すぎても連れ回されているだけで身につかない。強い仲間への丸投げでは育たない。");
        Ui.WriteLine("     クエストボードには、そのクエストで習熟度が入る冒険者ランクと、");
        Ui.WriteLine("     待機中の何人が該当するかが出ている。冒険者詳細には自分の適正帯が出ている。");
        Ui.WriteLine();
        Ui.WriteLine("     ・習熟度は職業ごとに別々に数える。今就いている職業のぶんだけが増える。");
        Ui.WriteLine("     ・一度覚えたスキルは永久に残る。職業を変えても失われない。");
        Ui.WriteLine("     ・習熟度も職業ごとに保存される。元の職業に戻れば、続きから数え直しになる。");
        Ui.WriteLine("       別の職業のマスタリーを覚えてから戻れば、複数の得物を使い分けられる。");
        Ui.WriteLine("     ・現在の習熟度は冒険者一覧の人物詳細で確認できる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ クラスチェンジ");
        Ui.WriteLine($"     冒険者一覧から「レベル×{AdventurerScreen.ClassChangeCostPerLevel}G」で変更できる。就ける職業は種族によって決まる。");
        Ui.WriteLine("     変更した時点で、必要習熟度0のスキルと、すでに満たしている条件のスキルが手に入る。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 職業とスキル一覧（左端は解禁に必要な習熟度）");
        ShowClassSkillTable(db);
        await Ui.PauseAsync();
    }

    /// <summary>職業ごとの習得表。classes.json / skills.json をそのまま読むので、調整しても説明がずれない。</summary>
    static void ShowClassSkillTable(GameMasterData db)
    {
        foreach (var cls in db.classes.Values)
        {
            Ui.WriteLine($"     ● {cls.className}");
            foreach (var entry in cls.classSkills.Where(e => e.Skill != null)
                         .OrderBy(e => e.requiredClearCount))
            {
                var skill = entry.Skill!;
                var parts = EquipmentText.BonusParts(skill.add);
                parts.AddRange(MultiplierParts(skill.mul));
                string effect = parts.Count > 0 ? string.Join(" ", parts) : "効果なし";
                string need = skill.requireWeaponType
                    ? $"[{EquipmentText.WeaponClassName(skill.requiredWeaponType)}装備時]"
                    : skill.requireArmorType ? $"[{ArmorName(skill.requiredArmorType)}装備時]" : "";
                string where = skill.frontOnly ? "[前衛]" : skill.backOnly ? "[後衛]" : "";
                string scope = skill.scope == SkillScope.UnitAura ? "[隊全体]" : "";
                Ui.WriteLine($"        習熟度{entry.requiredClearCount,2} "
                    + Ui.PadWide(skill.skillName, 22)
                    + Ui.PadWide(effect, 29) + $"{need}{where}{scope}");
            }
        }
    }

    static List<string> MultiplierParts(StatMultiplier m)
    {
        var parts = new List<string>();
        if (Math.Abs(m.hp - 1f) > 0.001f) parts.Add($"HPx{m.hp:0.##}");
        if (Math.Abs(m.san - 1f) > 0.001f) parts.Add($"士気x{m.san:0.##}");
        if (Math.Abs(m.heal - 1f) > 0.001f) parts.Add($"回復力x{m.heal:0.##}");
        return parts;
    }

    static string ArmorName(ArmorType type) => type switch
    {
        ArmorType.Cloth => "布防具",
        ArmorType.LightArmor => "軽鎧",
        ArmorType.Plate => "重鎧",
        _ => "防具",
    };

    static async Task ShowAdventurerAsync()
    {
        Ui.BeginScreen();
        Ui.Header("冒険者・装備・遺物");
        Ui.WriteLine("  ・冒険者          : 雇用して編成に加える。クエストで経験値を得てレベルアップする。");
        Ui.WriteLine($"  ・冒険者ランク    : {Rank.Label(Rank.Min)}〜{Rank.Label(Rank.Max)}。自分のランク以上のクエストを正規クリアするとランクポイントが貯まり、");
        Ui.WriteLine($"                      一定量で1つ上がる。{Rank.Label(Rank.Max)}が上限。レベルとは別の物差し。");
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
