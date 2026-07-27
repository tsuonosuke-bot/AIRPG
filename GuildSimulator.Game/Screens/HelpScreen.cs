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
                new MenuOption("4", "戦闘", "命中・回避・士気"),
                new MenuOption("5", "冒険者・装備・遺物"),
                new MenuOption("0", "戻る", Style: TextStyle.Dim),
            });
            switch (choice)
            {
                case "1": await ShowBasicsAsync(); break;
                case "2": await ShowGuildAsync(); break;
                case "3": await ShowQuestAsync(); break;
                case "4": await ShowBattleAsync(); break;
                case "5": await ShowAdventurerAsync(); break;
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
        Ui.WriteLine("  ・命中/回避       : 攻撃側の命中と防御側の回避の差でヒット判定を行う。");
        Ui.WriteLine("                      命中すればダメージが発生し、回避されるとダメージは0になる。");
        Ui.WriteLine("  ・貫通判定        : 命中した後、攻撃側の貫通値(PV)と防御側の装甲値(AV)を突き合わせる。");
        Ui.WriteLine("                      PVは筋力と体格、AVは体格と装備の防御力から決まる。");
        Ui.WriteLine("                      3個のダイスの最良の出目にPV-AVを足した「余剰」が深いほど、");
        Ui.WriteLine("                      武器のダメージダイスを多く振る（貫通1回／ハード2回／イクストリーム3回）。");
        Ui.WriteLine("                      余剰が0以下なら装甲に弾かれ、ダメージは通らない。");
        Ui.WriteLine("  ・ダメージ・ボーナス: 筋力と体格の合計が高いほど、上乗せされるダイスが1段ずつ強くなる。");
        Ui.WriteLine("                      貫通は3回で頭打ちになるので、そこから先の伸びはこちらが受け持つ。");
        Ui.WriteLine("  ・貫通の上限      : 武器ごとに「筋力をどこまでPVに乗せられるか」の上限がある。");
        Ui.WriteLine("                      短剣や投石は腕力を乗せきれず頭打ちになり、斧や大剣は青天井に伸びる。");
        Ui.WriteLine("                      力自慢には重い得物を、器用な者には軽い得物を持たせるとよい。");
        Ui.WriteLine("  ・物理/魔法       : どちらで殴るかは装備した武器で決まる。杖なら知力と精神で貫通を判定し、");
        Ui.WriteLine("                      それ以外は筋力と体格で判定する。");
        Ui.WriteLine("  ・決定的成功      : 命中判定で特に良い出目を出すと必ず貫通し、さらに武器ダイスの最大値と");
        Ui.WriteLine("                      もう1回ぶんのロールが上乗せされる。");
        Ui.WriteLine("  ・士気            : パーティ全体の粘り強さ。被ダメージや仲間の戦闘不能で減っていく。");
        Ui.WriteLine("                      0になるとその場でパーティは撤退する（全滅の手前で止まる安全弁）。");
        Ui.WriteLine("  ・前衛/後衛       : 前衛は敵から狙われやすく、後衛は狙われにくい代わりに前衛が");
        Ui.WriteLine("                      いなくなると狙われるようになる。");
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
