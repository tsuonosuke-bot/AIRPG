#!/usr/bin/env python3
"""冒険者の能力値を「素質 + レベル成長」に分解して組み立て・検算する。

手で足し算すると、体格・容姿を素質に数え忘れる／成長点の配分をレベルアップの
重みと違う比で配ってしまう、という2つの取り違えが必ず起きる。どちらも
--validate-master では検出されない（値としては正しいJSONなので）ため、
ここで数値を作り、ここで確かめる。

  python3 adventurer_stats.py audit                     名簿全体を帯・レアリティ別に実測
  python3 adventurer_stats.py check adv_0030            既存エントリを規則と突き合わせる
  python3 adventurer_stats.py plan --race Race_Human --class class_Healer \
      --level 8 --rarity Rare --con 6 --app 13 --base 9,15,6,10,11
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

# レベルアップの抽選重みに乗る下駄（AdventurerData.BaseGrowthWeight）。
BASE_GROWTH_WEIGHT = 0.2
# 1レベルにつき伸びる能力の数（AdventurerData.StatPointsPerLevel）。
STAT_POINTS_PER_LEVEL = 1

# 帯の標準レアリティの素質と、1段上のレアリティの素質（MASTER_DATA.md「素質とレアリティ上乗せ」）。
BAND_TALENT = 65
PREMIUM_TALENT = 70

# 成長する5能力。体格(SIZ)と容姿(APP)はレベルでは伸びないので、ここには入らない。
GROWN = ["vitality", "mental", "strength", "agility", "intelligence"]
GROWTH_KEY = {
    "vitality": "vitGrowth",
    "mental": "mentGrowth",
    "strength": "strGrowth",
    "agility": "agiGrowth",
    "intelligence": "intGrowth",
}
LABEL = {
    "vitality": "体力",
    "mental": "精神",
    "strength": "筋力",
    "agility": "敏捷",
    "intelligence": "知力",
}
RANK_LABEL = {1: "F", 2: "E", 3: "D", 4: "C", 5: "B", 6: "A", 7: "S"}
# ランク帯の物差しの冒険者レベル帯。
LEVEL_BAND = {1: (1, 5), 2: (6, 10), 3: (11, 16), 4: (17, 24),
              5: (25, 32), 6: (33, 40), 7: (41, 50)}
# RecruitmentSystem.DefaultWeightForGuildRank。
DEFAULT_WEIGHT = {1: 100, 2: 60, 3: 40, 4: 20}
# MasterLoader.DefaultAdventurerRarity（recruitWeight から既定レアリティを引く表）。
RARITY_WEIGHT_RANGE = {
    "Legend": (0, 10), "Unique": (11, 25), "Rare": (26, 45),
    "Uncommon": (46, 75), "Common": (76, 10_000),
}


def data_dir() -> Path:
    """リポジトリのどこから呼ばれても GuildSimulator.Game/Data を見つける。"""
    for base in [Path.cwd(), *Path(__file__).resolve().parents]:
        candidate = base / "GuildSimulator.Game" / "Data"
        if candidate.is_dir():
            return candidate
    sys.exit("GuildSimulator.Game/Data が見つかりません。リポジトリ内で実行してください。")


def load(name: str):
    return json.loads((data_dir() / f"{name}.json").read_text(encoding="utf-8"))


def talent(entry: dict) -> int:
    """Lv1換算の素質＝7能力の合計から、レベルで得た点を引いたもの。"""
    return seven(entry) - (entry["defaultLevel"] - 1) * STAT_POINTS_PER_LEVEL


def five(entry: dict) -> int:
    return sum(entry[k] for k in GROWN)


def seven(entry: dict) -> int:
    return five(entry) + entry["constitution"] + entry["appearance"]


def growth_weights(race: dict, cls: dict) -> dict[str, float]:
    """重み = 下駄 + 種族の成長率 + 職業の成長率（負の重みは0に切り上げ）。"""
    return {
        stat: max(0.0, BASE_GROWTH_WEIGHT + race[GROWTH_KEY[stat]] + cls[GROWTH_KEY[stat]])
        for stat in GROWN
    }


def distribute(points: int, weights: dict[str, float]) -> dict[str, int]:
    """成長点を重みの比で配る（最大剰余法）。

    レベルアップは1点ずつの抽選なので実際の結果はぶれるが、マスタに書く
    「その帯まで順当に育った姿」は期待値どおりに配るのが筋が通る。
    """
    total = sum(weights.values())
    if points <= 0 or total <= 0:
        return {stat: 0 for stat in GROWN}
    exact = {stat: points * w / total for stat, w in weights.items()}
    out = {stat: int(value) for stat, value in exact.items()}
    remainder = points - sum(out.values())
    for stat in sorted(GROWN, key=lambda s: exact[s] - int(exact[s]), reverse=True)[:remainder]:
        out[stat] += 1
    return out


def cmd_audit(_args) -> int:
    roster = load("adventurers")
    print(f"{'名前':<12}{'rank':>5}{'rarity':>10}{'Lv':>4}{'5能力':>7}{'体格':>5}{'容姿':>5}"
          f"{'7合計':>7}{'素質7':>7}")
    for entry in sorted(roster, key=lambda e: (e["defaultRank"], -talent(e), e["id"])):
        print(f"{entry['baseName']:<12}{RANK_LABEL[entry['defaultRank']]:>5}"
              f"{entry['rarity']:>10}{entry['defaultLevel']:>4}{five(entry):>7}"
              f"{entry['constitution']:>5}{entry['appearance']:>5}"
              f"{seven(entry):>7}{talent(entry):>7}")

    print("\n帯 × レアリティごとの素質（Lv1換算・7能力）")
    groups: dict[tuple[int, str], list[int]] = {}
    for entry in roster:
        groups.setdefault((entry["defaultRank"], entry["rarity"]), []).append(talent(entry))
    for (rank, rarity), values in sorted(groups.items()):
        print(f"  {RANK_LABEL[rank]}帯 {rarity:<9} {len(values):>2}人  "
              f"素質 {min(values)}〜{max(values)}")
    return 0


def check(entry: dict, races: dict, classes: dict) -> list[str]:
    problems = []
    rank = entry["defaultRank"]
    level = entry["defaultLevel"]

    if entry.get("recruitGuildRank") != rank:
        problems.append(
            f"recruitGuildRank({entry.get('recruitGuildRank')}) は defaultRank({rank}) と同じにする")
    if rank in LEVEL_BAND:
        low, high = LEVEL_BAND[rank]
        if not low <= level <= high:
            problems.append(
                f"defaultLevel {level} が {RANK_LABEL[rank]}帯のレベル帯 {low}〜{high} の外")

    weight = entry.get("recruitWeight")
    rarity = entry.get("rarity", "Common")
    if weight is not None and rarity in RARITY_WEIGHT_RANGE:
        low, high = RARITY_WEIGHT_RANGE[rarity]
        if not low <= weight <= high:
            problems.append(
                f"recruitWeight {weight} は {rarity} の帯 {low}〜{high} の外"
                "（MasterLoader.DefaultAdventurerRarity）")
    band_default = DEFAULT_WEIGHT.get(rank, 10)
    if weight is not None and weight > band_default:
        problems.append(
            f"recruitWeight {weight} が帯の既定 {band_default} 以上。"
            "レアなら必ず出にくくする")

    actual = talent(entry)
    premium = weight is not None and weight < band_default
    expected = PREMIUM_TALENT if premium else BAND_TALENT
    if actual > PREMIUM_TALENT:
        problems.append(f"素質 {actual} が上限 {PREMIUM_TALENT} を超えている")
    elif abs(actual - expected) > 2:
        problems.append(
            f"素質 {actual} が想定 {expected}（{'レアリティ上乗せあり' if premium else '帯の標準'}）から離れている")

    race = races.get(entry.get("raceId", ""))
    cls_id = entry.get("defaultClassId", "")
    if race and cls_id and cls_id not in race["allowedClassIds"]:
        problems.append(f"{race['raceName']} は {cls_id} に就けない（races.json の allowedClassIds）")

    cls = classes.get(cls_id)
    if race and cls:
        base = {stat: entry[stat] for stat in GROWN}
        grown = distribute((level - 1) * STAT_POINTS_PER_LEVEL, growth_weights(race, cls))
        for stat in GROWN:
            if base[stat] - grown[stat] < 1:
                problems.append(
                    f"{LABEL[stat]} {base[stat]} は成長分 +{grown[stat]} を引くと Lv1 で1未満になる")
    return problems


def cmd_check(args) -> int:
    races = {r["id"]: r for r in load("races")}
    classes = {c["id"]: c for c in load("classes")}
    roster = {e["id"]: e for e in load("adventurers")}
    targets = [roster[i] for i in args.ids] if args.ids else list(roster.values())

    bad = 0
    for entry in targets:
        problems = check(entry, races, classes)
        if problems:
            bad += 1
            print(f"✗ {entry['id']} {entry['baseName']}")
            for problem in problems:
                print(f"    - {problem}")
        elif args.ids:
            print(f"✓ {entry['id']} {entry['baseName']}  素質{talent(entry)} / "
                  f"Lv{entry['defaultLevel']} / 7合計{seven(entry)}")
    if bad == 0:
        print(f"{len(targets)}件すべて規則どおりです。")
    return 1 if bad else 0


def cmd_plan(args) -> int:
    races = {r["id"]: r for r in load("races")}
    classes = {c["id"]: c for c in load("classes")}
    race, cls = races.get(args.race), classes.get(args.klass)
    if race is None:
        sys.exit(f"不明な raceId '{args.race}'。候補: {', '.join(sorted(races))}")
    if cls is None:
        sys.exit(f"不明な classId '{args.klass}'。候補: {', '.join(sorted(classes))}")
    if args.klass not in race["allowedClassIds"]:
        sys.exit(f"{race['raceName']} は {cls['className']} に就けません。"
                 f"就ける職業: {', '.join(race['allowedClassIds'])}")

    premium = args.rarity not in ("Common", "Uncommon") or args.premium
    goal = PREMIUM_TALENT if premium else BAND_TALENT
    budget = goal - args.con - args.app
    if budget < len(GROWN):
        sys.exit(f"体格{args.con}＋容姿{args.app}で素質{goal}のうち{args.con + args.app}を使い切っています。")

    weights = growth_weights(race, cls)
    if args.base:
        base = dict(zip(GROWN, [int(v) for v in args.base.split(",")]))
        if len(base) != len(GROWN):
            sys.exit("--base は 体力,精神,筋力,敏捷,知力 の5つをカンマ区切りで渡してください。")
    else:
        # 目安。ここは「その人がどんな冒険者か」を決める場所なので、必ず手で寄せる。
        base = distribute(budget, weights)

    if sum(base.values()) != budget:
        sys.exit(f"Lv1の5能力の合計は {budget} にしてください"
                 f"（素質{goal} − 体格{args.con} − 容姿{args.app}）。いまは {sum(base.values())} です。")

    weight = args.weight if args.weight is not None else (
        30 if premium else DEFAULT_WEIGHT.get(args.rank, 10))
    low, high = RARITY_WEIGHT_RANGE.get(args.rarity, (0, 10_000))
    if not low <= weight <= high:
        sys.exit(f"recruitWeight {weight} は {args.rarity} の帯 {low}〜{high} の外です"
                 "（MasterLoader.DefaultAdventurerRarity）。"
                 "--validate-master はこれを素通りするので、ここで止めます。")
    band_default = DEFAULT_WEIGHT.get(args.rank, 10)
    if premium and weight >= band_default:
        sys.exit(f"帯より1段上のレアリティなら recruitWeight は帯の既定 {band_default} 未満に"
                 f"してください（いまは {weight}）。出にくさこそがレアリティの中身です。")

    grown = distribute((args.level - 1) * STAT_POINTS_PER_LEVEL, weights)
    final = {stat: base[stat] + grown[stat] for stat in GROWN}

    print(f"{race['raceName']} / {cls['className']} / Lv{args.level} / {args.rarity}")
    print(f"素質(7能力) {goal} ＝ 帯の基準{BAND_TALENT}"
          f"{f' + レアリティ上乗せ{PREMIUM_TALENT - BAND_TALENT}' if premium else ''}\n")
    print(f"{'':6}{'Lv1':>6}{'成長':>6}{'最終':>6}{'重み':>8}")
    for stat in GROWN:
        print(f"{LABEL[stat]:<6}{base[stat]:>6}{grown[stat]:>+6}{final[stat]:>6}{weights[stat]:>8.2f}")
    print(f"{'体格':<6}{args.con:>6}{'-':>6}{args.con:>6}")
    print(f"{'容姿':<6}{args.app:>6}{'-':>6}{args.app:>6}")
    print(f"\n7能力合計 {sum(final.values()) + args.con + args.app}"
          f"（素質{goal} + Lv{args.level}の+{args.level - 1}）")
    if not args.base:
        print("※ --base 未指定なので成長率どおりの機械的な配分です。"
              "その冒険者らしい形へ手で寄せてから --base で渡し直してください。")

    print("\n--- adventurers.json へ貼る雛形 ---")
    print(json.dumps({
        "id": args.id,
        "baseName": args.name,
        "defaultLevel": args.level,
        "defaultRank": args.rank,
        "recruitGuildRank": args.rank,
        "recruitWeight": weight,
        **final,
        "constitution": args.con,
        "appearance": args.app,
        "gender": args.gender,
        "defaultClassId": args.klass,
        "raceId": args.race,
        "defaultWeaponId": "",
        "defaultArmorId": "eq_cloth_01",
        "skillIds": [],
        "rarity": args.rarity,
        "background": "",
    }, ensure_ascii=False, indent=2))
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="cmd", required=True)

    sub.add_parser("audit", help="名簿全体を帯・レアリティ別に実測する").set_defaults(func=cmd_audit)

    p_check = sub.add_parser("check", help="既存エントリを規則と突き合わせる")
    p_check.add_argument("ids", nargs="*", help="省略すると名簿全体")
    p_check.set_defaults(func=cmd_check)

    p_plan = sub.add_parser("plan", help="素質とレベル成長から能力値を組み立てる")
    p_plan.add_argument("--race", required=True)
    p_plan.add_argument("--class", dest="klass", required=True)
    p_plan.add_argument("--level", type=int, required=True)
    p_plan.add_argument("--rank", type=int, default=0,
                        help="省略するとレベル帯から決める")
    p_plan.add_argument("--rarity", default="Uncommon")
    p_plan.add_argument("--premium", action="store_true",
                        help="Uncommon でも帯より1段上として素質70で組む")
    p_plan.add_argument("--con", type=int, required=True, help="体格(SIZ)")
    p_plan.add_argument("--app", type=int, required=True, help="容姿(APP)")
    p_plan.add_argument("--base", help="Lv1の 体力,精神,筋力,敏捷,知力")
    p_plan.add_argument("--weight", type=int, help="recruitWeight")
    p_plan.add_argument("--id", default="adv_XXXX")
    p_plan.add_argument("--name", default="")
    p_plan.add_argument("--gender", default="Unspecified")
    p_plan.set_defaults(func=cmd_plan)

    args = parser.parse_args()
    if getattr(args, "rank", None) == 0:
        args.rank = next((r for r, (lo, hi) in LEVEL_BAND.items() if lo <= args.level <= hi), 1)
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
