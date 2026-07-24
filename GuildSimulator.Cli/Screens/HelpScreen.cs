using GuildSimulator.Core.Systems.Guild;

namespace GuildSimulator.Cli.Screens;

public static class HelpScreen
{
    public static void Show()
    {
        while (true)
        {
            ConsoleHelper.Header("ヘルプ・用語集");
            Console.WriteLine("  1. 基本の流れ");
            Console.WriteLine("  2. ギルド運営（資金・維持費・ギルドポイント・ランク）");
            Console.WriteLine("  3. クエスト（難易度・緊急クエスト・昇格試験・撤退）");
            Console.WriteLine("  4. 戦闘（命中・回避・士気）");
            Console.WriteLine("  5. 冒険者・装備・遺物");
            Console.WriteLine("  0. 戻る");
            Console.Write("\n選択: ");
            switch (Console.ReadLine()?.Trim())
            {
                case "1": ShowBasics(); break;
                case "2": ShowGuild(); break;
                case "3": ShowQuest(); break;
                case "4": ShowBattle(); break;
                case "5": ShowAdventurer(); break;
                case "0": return;
            }
        }
    }

    static void ShowBasics()
    {
        ConsoleHelper.Header("基本の流れ");
        Console.WriteLine("  1) クエストボードから受注するクエストを選ぶ");
        Console.WriteLine("  2) 冒険者を編成して送り出す（前衛/後衛の配置あり）");
        Console.WriteLine("  3) ターンを進めると自動で戦闘・探索が進行する");
        Console.WriteLine("  4) クエストが完了すると報酬（資金・経験値・選択報酬）を受け取る");
        Console.WriteLine("  5) 得た資金で雇用・装備・遺物を強化し、また次のクエストへ");
        Console.WriteLine();
        Console.WriteLine($"  毎ターン、ギルド基本維持費{GuildManager.GuildBaseUpkeepGoldPerTurn}Gと冒険者の賃金が資金から引かれるため、");
        Console.WriteLine("  資金が尽きる（0以下になる）とゲームオーバーになる。");
        ConsoleHelper.PressAnyKey();
    }

    static void ShowGuild()
    {
        ConsoleHelper.Header("ギルド運営");
        Console.WriteLine("  ・資金（Gold）    : クエスト報酬や目標数を超えた採取物の買取で得る。毎ターン維持費が引かれる。");
        Console.WriteLine($"  ・維持費          : ギルド基本{GuildManager.GuildBaseUpkeepGoldPerTurn}G＋所属冒険者のレベル合計に応じた毎ターンの固定支出。");
        Console.WriteLine("  ・ギルドポイント  : クエストクリアで得る昇格試験の解禁ポイント。撤退では入らない。");
        Console.WriteLine("  ・ギルドランク    : 昇格試験（緊急クエスト）に正規クリアすると上がる。");
        Console.WriteLine("                      ランクが上がると受注できるクエストの幅が広がる。");
        ConsoleHelper.PressAnyKey();
    }

    static void ShowQuest()
    {
        ConsoleHelper.Header("クエスト");
        Console.WriteLine("  ・難易度（★）    : クエストボードで受注前に確認できる。戦闘率・罠率・敵レベル帯の目安。");
        Console.WriteLine("  ・緊急クエスト    : 通常枠とは別枠に掲示される特別なクエスト。昇格試験もこれに含まれる。");
        Console.WriteLine("  ・昇格試験        : 必要ギルドポイントを満たすと出現する一度きりのクエスト。");
        Console.WriteLine("                      クリアするとギルドランクが上がる（撤退・全滅ではランクは上がらない）。");
        Console.WriteLine("  ・撤退            : 士気が尽きるとパーティは自動的に撤退する。基本報酬は無しになるが、");
        Console.WriteLine("                      道中で得た戦利品（宝箱など）はそのまま持ち帰れる。");
        Console.WriteLine("  ・全滅            : 全員が戦闘不能になった場合。報酬・戦利品はすべて失われる。");
        ConsoleHelper.PressAnyKey();
    }

    static void ShowBattle()
    {
        ConsoleHelper.Header("戦闘");
        Console.WriteLine("  ・命中/回避       : 攻撃側の命中と防御側の回避の差でヒット判定を行う。");
        Console.WriteLine("                      命中すればダメージが発生し、回避されるとダメージは0になる。");
        Console.WriteLine("  ・士気            : パーティ全体の粘り強さ。被ダメージや仲間の戦闘不能で減っていく。");
        Console.WriteLine("                      0になるとその場でパーティは撤退する（全滅の手前で止まる安全弁）。");
        Console.WriteLine("  ・前衛/後衛       : 前衛は敵から狙われやすく、後衛は狙われにくい代わりに前衛が");
        Console.WriteLine("                      いなくなると狙われるようになる。");
        ConsoleHelper.PressAnyKey();
    }

    static void ShowAdventurer()
    {
        ConsoleHelper.Header("冒険者・装備・遺物");
        Console.WriteLine("  ・冒険者          : 雇用して編成に加える。クエストで経験値を得てレベルアップする。");
        Console.WriteLine("                      死亡した冒険者は蘇生できない。");
        Console.WriteLine("  ・装備            : 商店で購入・売却し、冒険者一覧画面で着せ替えできる。");
        Console.WriteLine("  ・遺物            : ギルド全体に常時効果を及ぼす特別なアイテム。クエストの選択報酬や");
        Console.WriteLine("                      道中の宝箱で入手できる。所持しているだけで効果を発揮する。");
        ConsoleHelper.PressAnyKey();
    }
}
