using GuildSimulator.Core;
using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;
using GuildSimulator.Core.Systems;
using GuildSimulator.Core.Systems.Battle;
using GuildSimulator.Core.Systems.Guild;
using GuildSimulator.Game.Data;
using GuildSimulator.Game.Presentation;

namespace GuildSimulator.Game.Screens;

public static class HelpScreen
{
    // 遺物システムの凍結中は説明文からも遺物を伏せる。復活させれば記述もそのまま戻る。
    static string SkillsAndRelics => GameFeatures.RelicsEnabled ? "スキル・遺物" : "スキル";
    static string AdventurerSectionTitle =>
        GameFeatures.RelicsEnabled ? "冒険者・装備・遺物" : "冒険者・装備";

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
                new MenuOption("9", AdventurerSectionTitle),
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
        Ui.WriteLine(GameFeatures.RelicsEnabled
            ? "  5) 得た資金で雇用・装備・遺物を強化し、また次のクエストへ"
            : "  5) 得た資金で雇用・装備・施設を強化し、また次のクエストへ");
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
        Ui.WriteLine($"  ・維持費          : ギルド基本{GuildManager.GuildBaseUpkeepGoldPerTurn}G＋所属冒険者のレベル合計×{GuildManager.UpkeepGoldPerLevel}G＋建設済み施設に応じた毎ターンの固定支出。");
        Ui.WriteLine($"                      冒険者の賃金は認定ランクが1つ上がるごとに{GuildManager.UpkeepGoldPerRank}Gずつ増える（希少さは関係しない）。");
        Ui.WriteLine("                      遺物の効果でこの合計にさらに倍率がかかることがある（実際に引かれる額が実効維持費）。");
        Ui.WriteLine("  ・ギルドポイント  : クエストクリアで得る昇格試験の解禁ポイント。撤退では入らない。");
        Ui.WriteLine("  ・ギルドランク    : 昇格試験（緊急クエスト）に正規クリアすると上がる。");
        Ui.WriteLine("                      ランクが上がると受注できるクエストの幅が広がる。");
        Ui.WriteLine($"  ・ランク表記      : {string.Join(" → ", RankLadder())} の{Rank.Max}段階。{Rank.Label(Rank.Min)}が最も低く、{Rank.Label(Rank.Max)}が最高。");
        Ui.WriteLine("                      冒険者・クエスト・ギルドのランクはすべてこの同じ物差しで比べる。");
        Ui.WriteLine($"                      掲示されるのは「クエストランク ≦ ギルドランク」のものだけ。");
        Ui.WriteLine("  ・施設            : ゴールドで建設するギルドの恒常強化。建設後は維持費が増える代わりに、");
        Ui.WriteLine("                      クエスト掲示枠・商店品揃え・休息回復量・成長率・求人候補の最低人数・");
        Ui.WriteLine("                      負傷回復（回復量/死亡率/傷痕発生率の軽減）のいずれかを高め続ける。");
        Ui.WriteLine("                      転職指南所は冒険者のクラスチェンジを解禁する（詳細は「職業とマスタリー」）。");
        await Ui.PauseAsync();
    }

    static async Task ShowQuestAsync()
    {
        Ui.BeginScreen();
        Ui.Header("クエスト");
        Ui.WriteLine("  ・難易度          : クエストボードで受注前に確認できる。戦闘率・罠率・敵の脅威度の目安。");
        Ui.WriteLine($"  ・クエストランク  : {Rank.Label(Rank.Min)}〜{Rank.Label(Rank.Max)}。ギルドランク以下のものだけが掲示される。");
        Ui.WriteLine("                      冒険者ランクと突き合わせて「適正ランク」かどうかも決まり、");
        Ui.WriteLine("                      適正ランクを正規クリアするとクラス習熟度が増える（ヘルプ8を参照）。");
        Ui.WriteLine("  ・掲示期限        : 受注されなかったクエストは掲示から一定ターンで掲示板から消える。");
        Ui.WriteLine("                      残りターン数はクエストボードの各クエストに表示される。");
        Ui.WriteLine("  ・緊急クエスト    : 通常枠とは別枠に掲示される特別なクエスト。昇格試験もこれに含まれる。");
        Ui.WriteLine("  ・昇格試験        : 必要ギルドポイントを満たすと出現する一度きりのクエスト。");
        Ui.WriteLine("                      クリアするとギルドランクが上がる（撤退・全滅ではランクは上がらない）。");
        Ui.WriteLine("  ・編成相対評価    : 人数・平均認定ランク・最大脅威・負傷状態から、現在の編成を相対評価する目安。");
        Ui.WriteLine("                      装備や敵との相性、乱数は含まないため、勝敗を保証するものではない。");
        Ui.WriteLine("  ・遠征方針        : 出発時に「生還優先」か「依頼達成優先」を選ぶ。");
        Ui.WriteLine($"                      生還優先はパーティHP{BattleResolver.SurvivalPartyHpPercent}%以下、または誰かが{BattleResolver.SurvivalMemberHpPercent}%以下で撤退する。");
        Ui.WriteLine("                      依頼達成優先は行動可能な限り続行するため、戦闘不能・死亡のリスクが高い。");
        Ui.WriteLine("  ・物語クエスト    : 調査で得た手掛かりによって次の依頼が掲示される一度きりのクエスト。");
        Ui.WriteLine("                      発見した内容はメインメニューの「J. 調査記録」で確認できる。");
        Ui.WriteLine("  ・撤退            : 士気が尽きるとパーティは自動的に撤退する。基本報酬は無しになるが、");
        Ui.WriteLine("                      道中で得た戦利品（宝箱など）はそのまま持ち帰れる。");
        Ui.WriteLine("  ・壊滅            : 全員が戦闘不能になった場合。報酬・戦利品はすべて失われる。");
        Ui.WriteLine("                      帰還処理で各自の死亡または負傷が確定し、医療院は死亡率を下げる。");
        Ui.WriteLine("  ・選択イベント    : ターン内の最終エリア後に発生することがある。");
        Ui.WriteLine("                      未解決の選択がある間は次のターンへ進めない。");
        Ui.WriteLine("  ・報酬の見方      : クエストボードの基本報酬は確定分のみ。宝箱・敵ドロップ・選択イベントの");
        Ui.WriteLine("                      副収入は含まれておらず、結果によって上乗せされる。");
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
        Ui.WriteLine("  ・状態異常        : 毒・出血・火傷はラウンド冒頭に継続ダメージ、凍結は次の行動を失う。");
        Ui.WriteLine("                      火傷は継続ダメージに加えてAV/mAVも下げる。");
        Ui.WriteLine("                      獣の牙=出血、炎=火傷、闇=毒、水=凍結の付与機会を持つ。戦闘終了時に解除される。");
        Ui.WriteLine("  ・一時バフ        : 土の武器は守勢（AV/mAV/DV）、風は攻勢（PV/mPV/命中）、光の治療は再生を与える。");
        Ui.WriteLine("                      バフも戦闘終了時に解除され、同じ効果は重複せず強い値と長い残り時間で更新される。");
        Ui.WriteLine("  ・士気            : パーティ全体の粘り強さ。出発時の最大値は編成の精神力(SAN)合計。");
        Ui.WriteLine("                      0になるとその場で撤退する（全滅の手前で止まる安全弁）。");
        Ui.WriteLine("  ・士気の減りかた  : ①そのラウンドで実際に減ったHPの割合に応じて（回復で押し返した分は減らない）");
        Ui.WriteLine($"                      ②仲間が1人倒れるごとに{MoraleState.AllyDownFlat}");
        Ui.WriteLine($"                      ③格上との遭遇時に、敵の脅威度と味方の平均ランクの差1段につき{MoraleState.ThreatGapFlat}（最大{MoraleState.ThreatGapFlatCap}）。");
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
        Ui.WriteLine($"     ・命中補正 ＝ 敏捷modifier ＋ 装備の命中補正 ＋ {SkillsAndRelics} － 過積載");
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
        Ui.WriteLine($"  能力値そのものが判定に使われることはない。戦闘に入る前に、能力値・装備・{SkillsAndRelics}が");
        Ui.WriteLine("  「HP / 士気 / 命中 / DV / PV / AV / 回復力」の7つの数値へ変換され、判定はその数値だけを見る。");
        Ui.WriteLine($"  多くは modifier を通す。modifier ＝ (能力値 - {QudCombat.MODIFIER_BASELINE}) ÷ {QudCombat.MODIFIER_STEP} の切り捨て。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 能力値がどこに効くか");
        Ui.WriteLine("     VIT 体力 : HP＝(VIT×10＋SIZ×5)÷2 / 積載上限");
        Ui.WriteLine("     SIZ 体格 : AV＝SIZのmodifier / HP / 積載上限");
        Ui.WriteLine("     MEN 精神 : 士気＝MEN×10（パーティ合計が最大値）/ mAV＝MENのmodifier / 回復力");
        Ui.WriteLine($"     AGI 敏捷 : 命中補正＝AGIのmodifier / DV＝{QudCombat.BASE_DV}＋AGIのmodifier");
        Ui.WriteLine("     STR 筋力 : 物理PV＝STRのmodifier（武器の上限まで）/ 積載上限");
        Ui.WriteLine("     INT 知力 : 魔法PV＝INTのmodifier（武器の上限まで）/ 回復力");
        Ui.WriteLine($"     APP 容姿 : APP{AppearanceSystem.HighAppearanceThreshold}以上でクエストGPと戦闘中の士気回復にボーナス。対人遭遇の判定にも使う");
        Ui.WriteLine("                APPが極端に高い、または低い隊員は、戦場で少し目立って狙われやすくなる。");
        Ui.WriteLine("     ・敏捷は命中とDVの両方に効くので、1点の価値がもっとも広い。");
        Ui.WriteLine($"     ・レベルアップで伸びるのは1レベルにつき{AdventurerData.StatPointsPerLevel}能力だけ。");
        Ui.WriteLine("       VIT・MEN・STR・AGI・INTのどれが伸びるかは種族と職業の重みで抽選され、選べない。");
        Ui.WriteLine("       得意な能力ほど当たりやすいが、不得手な能力も稀に伸びる。");
        Ui.WriteLine("       同じ職業・同じレベルでも育ち方が食い違うので、代わりの利かない一人になっていく。");
        Ui.WriteLine("     ・SIZとAPPは伸びない。体格と容姿は雇用したときの素質で決まる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 命中補正の内訳");
        Ui.WriteLine("     命中補正 ＝ AGIのmodifier");
        Ui.WriteLine("              ＋ 装備の命中補正の合計（短剣は+3、剣は+1、槍は-2、斧は-4）");
        Ui.WriteLine($"              ＋ {SkillsAndRelics}の命中補正");
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
        Ui.WriteLine($"        ＋ {SkillsAndRelics} － 過積載ペナルティ（最大-{overweightDv}）");
        Ui.WriteLine($"     ・前衛が健在な間、後衛はさらに+{BattleResolver.REAR_COVER_DV_BONUS}される。");
        Ui.WriteLine("     ・板金鎧は-4、鎖帷子は-2のように、硬い鎧ほどDVを下げてAVを上げる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ PV・AVの内訳");
        Ui.WriteLine("     PV ＝ 武器の基礎PV ＋ min(STR/INTのmodifier, 武器ごとの上限) ＋ 装備・スキルのPV補正");
        Ui.WriteLine($"     ・素手は基礎PV{AdventurerData.UNARMED_PV}・ダメージ{AdventurerData.UNARMED_DAMAGE_DICE}。乗せるmodifierに上限はなく、膂力はそのまま拳に乗る。");
        Ui.WriteLine("       ただし基礎PVもダメージダイスも小さいので、武器はやはり持たせたほうがよい。");
        Ui.WriteLine("     ・上限の目安は 短剣・風杖が+5、剣・弓・水杖が+6、槍・土杖が+7、斧・火杖・闇杖が+8。");
        Ui.WriteLine("       回復用の光杖は0で、力も知恵も上乗せできない。");
        Ui.WriteLine($"     AV ＝ SIZのmodifier ＋ 防具のAV補正 ＋ {SkillsAndRelics}（mAVはMENのmodifierから同様に）");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 積載と過積載");
        Ui.WriteLine("     積載上限 ＝ SIZ ＋ (STR＋VIT)÷2 ＋ スキルによる積載補正。装備の重さの合計がこれを超えると過積載になる。");
        Ui.WriteLine($"     ・上限を1超えるごとに過積載率が{AdventurerData.OVERWEIGHT_RATE_PER_POINT * 100:0}%増える（最大100%）。");
        Ui.WriteLine($"     ・過積載率に比例して DV最大-{overweightDv}、命中最大-{overweightToHit}。AVは担いでいるぶんそのまま効くので削られない。");
        Ui.WriteLine("     ・重い鎧を着せるなら、SIZとSTRの高い者に。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 補正の重ねかた");
        Ui.WriteLine("     ・装備      : 装備している全スロットの補正を合計する。");
        Ui.WriteLine("     ・スキル    : 前衛限定・後衛限定・特定の武器/防具限定といった条件を満たすときだけ乗る。");
        Ui.WriteLine("                   パーティ全体に効くスキルは、生存している全員にそれぞれ加算される。");
        if (GameFeatures.RelicsEnabled)
            Ui.WriteLine("     ・遺物      : 冒険者側のみ、全員に加算される。");
        Ui.WriteLine("     ・倍率がかかるのはHP・士気・回復力だけ。命中/DV/PV/AVは1点の重みが大きいため加算でしか動かない。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 敵の数値");
        Ui.WriteLine("     敵もまったく同じ式で命中・DV・PV・AVを組み立てる。");
        Ui.WriteLine("     ・敵にレベルはない。能力値はマスタに書かれた値がそのまま使われる。");
        Ui.WriteLine("       強さの段階は倍率ではなく別々の個体で表す");
        Ui.WriteLine("       （はぐれゴブリン → ゴブリン → ゴブリン兵士 → ゴブリン隊長）。");
        Ui.WriteLine($"     ・敵の脅威度は冒険者と同じ{Rank.Label(Rank.Min)}〜{Rank.Label(Rank.Max)}。能力値には影響せず、");
        Ui.WriteLine("       遭遇時の士気の削られ方と、クエストボードの難易度表示に使われる。");
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
        Ui.WriteLine("  ■ 両手武器（大剣・大斧・長槍）");
        Ui.WriteLine("     左手が塞がるので、盾も二刀流も使えない。その代わり同じ武器種の片手版より");
        Ui.WriteLine("     基礎PV・ダメージダイス・能力値上限がどれも1段上で、マスタリーはそのまま効く。");
        Ui.WriteLine("     重量も倍近いので、担げるだけの筋力と体格が要る。");
        Ui.WriteLine("     弓と魔法は最初から両手武器。射手と魔道士は盾を構えられない。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 二刀流（左手に武器を持つ）");
        Ui.WriteLine($"     左手の武器は毎手番かならず振れるわけではなく、確率で追撃が入る（基本{QudCombat.OFF_HAND_BASE_CHANCE}%）。");
        Ui.WriteLine("     短剣は取り回しがよく、左手に持つと発動率が上がる。「二刀流」スキルでさらに伸びる。");
        Ui.WriteLine("     ・左手の武器は命中やPVといった数値補正を供給しない。攻撃にだけ使う。");
        Ui.WriteLine("       同じ武器を2本持って補正を二重取りすることはできない。");
        Ui.WriteLine("     ・重さは両方ぶんかかるので、手数と引き換えに積載を圧迫する。");
        Ui.WriteLine("     ・左手の追撃は右手の連撃とは別枠で、PVの減衰を受けない。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 盾（左手に構える）");
        Ui.WriteLine("     盾の装甲は常時は効かない。攻撃を受けるたびに受け判定を行い、");
        Ui.WriteLine("     成功した攻撃にかぎってその一撃だけAVが上乗せされる。");
        Ui.WriteLine("     ・小盾は受け率が低く軽い。大盾は受け率も装甲も高いが、重くDVを削る。");
        Ui.WriteLine("     ・受けで得た装甲は斧の装甲破壊では剥がせない（着ている鎧とは別物のため）。");
        Ui.WriteLine("     ・魔法攻撃はmAVと突き合わせるので、盾では受けられない。");
        Ui.WriteLine("     ・「盾術」スキルで受け率が上がる。");
        ShowShieldTable(db);
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

    /// <summary>取り扱いのある盾を実データから並べる。</summary>
    static void ShowShieldTable(GameMasterData db)
    {
        var shields = db.equipment.Values
            .Where(e => e.IsShield)
            .OrderBy(e => e.shopTier).ThenBy(e => e.blockChance)
            .ToList();
        if (shields.Count == 0) return;

        Ui.WriteLine();
        Ui.Dim($"     {Ui.PadWide("盾", 18)}{Ui.PadWide("受け率", 10)}{Ui.PadWide("受け成功時AV", 16)}{Ui.PadWide("重量", 8)}回避");
        foreach (var s in shields)
            Ui.WriteLine($"     {Ui.PadWide(s.displayName, 18)}"
                + Ui.PadWide($"{s.blockChance}%", 10)
                + Ui.PadWide($"+{s.blockAv}", 16)
                + Ui.PadWide($"{s.weight}", 8)
                + (s.bonus.dv != 0 ? $"{s.bonus.dv:+#;-#;0}" : "±0"));
    }

    static async Task ShowMasteryAsync(GameMasterData db)
    {
        Ui.BeginScreen();
        Ui.Header("職業とマスタリー");
        Ui.WriteLine("  ■ マスタリーとは");
        Ui.WriteLine("     職業スキルのうち「○○マスタリー」は、その武器種・防具種を身に着けているときだけ効く。");
        Ui.WriteLine("     持ち替えると効果は消えるので、職業と得物は揃えたほうがよい。");
        Ui.WriteLine("     武器マスタリーはLv1〜Lv5、防具マスタリーもLv1〜Lv5まである。");
        Ui.WriteLine("     低いLvほど横並びで、Lv3・Lv5でその得物にしかできないことが顔を出す。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ Lv表記のスキルは重ねがけにならない");
        Ui.WriteLine("     同じ系統（○○ Lv1〜Lv5）は、覚えているうち「いちばん上のLv」だけが効く。");
        Ui.WriteLine("     Lv3を覚えるとLv1・Lv2は押しのけられるので、一覧の数値はそのまま最終値として読める。");
        Ui.WriteLine("     下位のLvが消えるわけではないので、系統が違えば何本でも並行して伸ばせる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 装備の条件");
        Ui.WriteLine("     武器種・防具種のほかに、次の構えを条件にするスキルもある。");
        Ui.WriteLine("       ・[素手]     右手に何も握っていないとき（格闘術）");
        Ui.WriteLine("       ・[両手武器] 両手で構える物理武器のとき（両手武器術。杖は含まない）");
        Ui.WriteLine("       ・[盾]       左手に盾を構えているとき（盾術）");
        Ui.WriteLine("       ・[左手武器] 左手に武器を握っているとき（二刀流）");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 戦闘の外に効くスキル");
        Ui.WriteLine("     報酬・道中イベント・休息・敵ドロップを動かすスキルは、連れて行った全員ぶんが合算される。");
        Ui.WriteLine("     隊列も生死も関係なく、その遠征に誰を出したかだけで決まる。");
        Ui.WriteLine();
        Ui.WriteLine("  ■ 習得条件 ── クラス習熟度");
        Ui.WriteLine("     職業スキルは経験値やレベルではなく、その職業の「クラス習熟度」で開く。");
        Ui.WriteLine("     各スキルには必要な習熟度が決まっていて、0のものは職業に就いた瞬間に手に入る。");
        Ui.WriteLine();
        Ui.WriteLine("     習熟度が入るのは、次をすべて満たしたときだけ。");
        Ui.WriteLine("       ・そのクエストに参加していること");
        Ui.WriteLine("       ・クエストを正規クリアすること（撤退・全滅では増えない）");
        Ui.WriteLine("       ・帰還時に生存していること");
        Ui.WriteLine("       ・クエストが、その冒険者にとって適正ランクであること");
        Ui.WriteLine($"     1回の獲得量は {AdventurerData.BaseMasteryPerClear}＋INT（上限{AdventurerData.BaseMasteryPerClear + AdventurerData.MaxIntMasteryBonus}）。INTが低くても{AdventurerData.BaseMasteryPerClear}を下回らない。");
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
        string ccFacilityName = db.facilities.TryGetValue(AdventurerScreen.ClassChangeFacilityId, out var ccFacility)
            ? ccFacility.displayName : AdventurerScreen.ClassChangeFacilityId;
        Ui.WriteLine($"     「{ccFacilityName}」を建設すると解禁される。それまでは冒険者一覧にクラスチェンジの選択肢が出ない。");
        Ui.WriteLine($"     解禁後は冒険者一覧から「レベル×{AdventurerScreen.ClassChangeCostPerLevel}G」で変更できる。就ける職業は種族によって決まる。");
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
                parts.AddRange(EquipmentText.ExpeditionParts(skill.expedition));
                parts.AddRange(EquipmentText.BattleParts(skill.battle));
                if (!string.IsNullOrWhiteSpace(skill.unarmedDamageDice))
                    parts.Add($"素手{skill.unarmedDamageDice}");
                string effect = parts.Count > 0 ? string.Join(" ", parts) : "効果なし";
                string where = skill.frontOnly ? "[前衛]" : skill.backOnly ? "[後衛]" : "";
                string scope = skill.scope == SkillScope.UnitAura ? "[隊全体]" : "";
                Ui.WriteLine($"        習熟度{entry.requiredClearCount,5} "
                    + Ui.PadWide(skill.skillName, 22)
                    + Ui.PadWide(effect, 29) + $"{GearRequirementText(skill)}{where}{scope}");
            }
        }
    }

    /// <summary>そのスキルが効くための「構え」。条件がなければ空文字。</summary>
    static string GearRequirementText(SkillMasterData skill)
    {
        var parts = new List<string>();
        if (skill.requireWeaponType)
            parts.Add($"{EquipmentText.WeaponClassName(skill.requiredWeaponType)}装備時");
        if (skill.requireArmorType) parts.Add($"{ArmorName(skill.requiredArmorType)}装備時");
        if (skill.requireUnarmed) parts.Add("素手");
        if (skill.requireTwoHanded) parts.Add(skill.requirePhysicalWeapon ? "両手近接武器" : "両手武器");
        else if (skill.requirePhysicalWeapon) parts.Add("物理武器");
        if (skill.requireShield) parts.Add("盾");
        if (skill.requireOffHandWeapon) parts.Add("左手に武器");
        return parts.Count == 0 ? "" : $"[{string.Join("・", parts)}]";
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
        Ui.Header(AdventurerSectionTitle);
        Ui.WriteLine("  ・冒険者          : 雇用して編成に加える。クエストで経験値を得てレベルアップする。");
        Ui.WriteLine($"  ・冒険者ランク    : {Rank.Label(Rank.Min)}〜{Rank.Label(Rank.Max)}。「格上クエストの正規クリア数」と");
        Ui.WriteLine("                      「累積の適正クエスト正規クリア数」の両方を満たすと1つ上がる。");
        Ui.WriteLine("                      同ランク以下のクエストは格上には数えないが、適正帯なら累積には載る。");
        Ui.WriteLine("                      昇格条件（格上／累積適正）:");
        for (int r = Rank.Min; r < Rank.Max; r++)
        {
            var probe = new AdventurerData(new AdventurerMasterData
            {
                id = "help_probe", baseName = "", defaultRank = r,
            });
            var req = probe.NextRankRequirement!.Value;
            Ui.WriteLine($"                        {Rank.Label(r)}→{Rank.Label(r + 1)}: "
                + $"格上{req.higherRankClears} ／ 累積{req.suitableTotalClears}");
        }
        Ui.WriteLine($"                      {Rank.Label(Rank.Max)}が上限。レベルとは別の物差し。");
        Ui.WriteLine("                      死亡した冒険者は蘇生できない。");
        Ui.WriteLine("  ・負傷            : 戦闘不能から生還すると裂傷・骨折・深い傷・心的外傷などが残ることがある。");
        Ui.WriteLine($"                      帰還時死亡率は重症度により{AdventurerData.MinorTraumaFatalityPercent}%/{AdventurerData.MajorTraumaFatalityPercent}%/{AdventurerData.CriticalTraumaFatalityPercent}%、壊滅時はさらに+{AdventurerData.PartyWipeFatalityBonusPercent}%。");
        Ui.WriteLine("                      負傷中でも出発できるが能力が下がる。出発させずターンを進めると1Tぶん休養する。");
        Ui.WriteLine("  ・医療院          : 休養時の回復を早め、帰還時死亡率と完治時に後遺症が残る確率を下げる。");
        Ui.WriteLine("  ・傷痕・後遺症    : 重傷の完治時に残る恒久効果。能力補正と固有称号を持ち、セーブにも記録される。");
        Ui.WriteLine("                      人物詳細では経歴・性格・動機・得意分野と、直近の遠征履歴を確認できる。");
        Ui.WriteLine("  ・装備            : 商店で購入・売却し、冒険者一覧画面で着せ替えできる。");
        Ui.WriteLine("  ・レアリティ      : コモン、アンコモン、レア、ユニーク、レジェンドの順に希少。");
        Ui.WriteLine("  ・消費アイテム    : 出発前に最大2個選び、出発時に消費してクエスト中だけ効果を得る。");
        Ui.WriteLine("  ・商店            : 品ぞろえと在庫は5ターンごと（Turn 1、6、11…）に更新される。");
        if (GameFeatures.RelicsEnabled)
        {
            Ui.WriteLine("  ・遺物            : ギルド全体に常時効果を及ぼす特別なアイテム。クエストの選択報酬や");
            Ui.WriteLine("                      道中の宝箱で入手できる。所持しているだけで効果を発揮する。");
        }
        else
        {
            Ui.WriteLine("  ・恒常的な強化    : ギルド全体に常時掛かる強化は「施設」に一本化されている。");
        }
        await Ui.PauseAsync();
    }
}
