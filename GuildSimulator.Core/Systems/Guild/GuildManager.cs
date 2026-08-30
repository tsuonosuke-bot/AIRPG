using GuildSimulator.Core.GameData;
using GuildSimulator.Core.MasterData;
using GuildSimulator.Core.Models;

namespace GuildSimulator.Core.Systems.Guild;

public class GuildManager
{
    public const int GuildBaseUpkeepGoldPerTurn = 10;
    public const int UpkeepGoldPerLevel = 1;
    public const int FormationSlotCount = 6;
    public const int FrontRowSlotCount = 3;
    public const int BasePartyCapacity = 3;
    public const int MaximumPartyCapacity = FormationSlotCount;
    public const int PartyCapacityUpgradeMaximum =
        MaximumPartyCapacity - BasePartyCapacity;

    /// <summary>施設を建てる前に在籍させられる冒険者の人数。</summary>
    public const int BaseRosterCapacity = 4;

    /// <summary>宿舎系の施設をすべて建てたときの在籍上限。</summary>
    public const int MaximumRosterCapacity = 8;

    public const int RosterCapacityUpgradeMaximum =
        MaximumRosterCapacity - BaseRosterCapacity;

    /// <summary>認定ランクが1つ上がるごとに増える賃金。レアリティは維持費に関係させない。</summary>
    public const int UpkeepGoldPerRank = 15;

    public List<AdventurerData> adventurers = new();
    public int Gold { get; private set; }
    public int GuildRank { get; private set; }
    public int GuildPoints { get; private set; }
    /// <summary>現在のギルドランクになってから獲得したGP。次の昇格試験の解禁に使う。</summary>
    public int GuildPointsThisRank { get; private set; }
    public List<RelicMasterData> relics = new();
    public List<FacilityMasterData> facilities = new();
    public List<string> economyLogs = new();
    readonly List<EquipmentStack> inventory = new();
    readonly List<ConsumableStack> consumables = new();
    public Dictionary<string, int> shopEquipmentStock = new();
    public Dictionary<string, int> shopConsumableStock = new();
    public int LastShopRefreshTurn { get; private set; }
    readonly HashSet<string> discoveredEnemyIds = new(StringComparer.Ordinal);

    /// <summary>実際の遠征で一度でも遭遇し、モンスター図鑑へ登録された敵ID。</summary>
    public IReadOnlyCollection<string> DiscoveredEnemyIds => discoveredEnemyIds;

    /// <summary>プレイヤーに見せるギルドランクの表記（F〜S）。</summary>
    public string GuildRankLabel => Rank.Label(GuildRank);

    public bool IsMaxGuildRank => Rank.IsMax(GuildRank);

    public GuildManager(int startGold = 50, int startRank = Rank.Min)
    {
        Gold = startGold;
        GuildRank = Rank.Clamp(startRank);
        RelicSystem.SetRelics(relics);
        FacilitySystem.SetFacilities(facilities);
        economyLogs.Add($"初期資金: {Gold}G");
    }

    public void AddGold(int amount, string reason)
    {
        Gold += amount;
        economyLogs.Add($"{reason}: {(amount >= 0 ? "+" : "")}{amount}G（所持 {Gold}G）");
    }

    public void SpendGold(int amount, string reason) => AddGold(-amount, reason);

    public void AddGuildPoints(int amount, string reason, bool countTowardRankProgress = true)
    {
        GuildPoints += amount;
        if (countTowardRankProgress) GuildPointsThisRank += amount;
        economyLogs.Add($"{reason}: ギルドポイント +{amount}（合計 {GuildPoints}）");
    }

    public void RankUp(int amount, string reason)
    {
        if (amount <= 0 || IsMaxGuildRank) return;
        GuildRank = Rank.Clamp(GuildRank + amount);
        GuildPointsThisRank = 0;
        economyLogs.Add($"{reason}: 認定ランク → {GuildRankLabel}");
    }

    public void AddAdventurer(AdventurerData adv)
    {
        adventurers.Add(adv);
        economyLogs.Add($"雇用: {adv.name}（維持費 {CalculateAdventurerUpkeep(adv.level, adv.rank)}G/Turn）");
    }

    public const int BurialCostBase = 30;
    public const int BurialCostPerLevel = 10;
    public List<BurialRecord> burialRecords = new();

    public static int CalculateBurialCost(int level) => BurialCostBase + Math.Max(1, level) * BurialCostPerLevel;

    public bool TryBuryAdventurer(AdventurerData adv, int currentTurn, out string reason)
    {
        reason = "";
        int cost = CalculateBurialCost(adv.level);
        if (Gold < cost) { reason = $"埋葬費が不足しています（必要: {cost}G  所持: {Gold}G）"; return false; }
        if (!adventurers.Remove(adv)) { reason = "対象が見つかりません"; return false; }

        SpendGold(cost, $"埋葬費: {adv.name}");
        burialRecords.Add(new BurialRecord(adv.name, adv.level, adv.ClassAndRace, currentTurn, adv.expeditionCount, adv.successfulExpeditionCount));
        return true;
    }

    /// <summary>
    /// 生存している冒険者を解雇して在籍枠を空ける。装備の返却は呼び出し側で先に済ませること。
    /// 費用は取らない。維持費が払えなくなった状況で解雇まで有料にすると、
    /// 人員を減らして立て直すという唯一の逃げ道まで塞いでしまう。
    /// </summary>
    public bool TryDismissAdventurer(AdventurerData adv, out string reason)
    {
        reason = "";
        if (!adv.isAlive) { reason = $"{adv.name} は死亡しています。解雇ではなく埋葬してください"; return false; }
        if (!adventurers.Remove(adv)) { reason = "対象が見つかりません"; return false; }

        economyLogs.Add(
            $"解雇: {adv.name}（維持費 -{CalculateAdventurerUpkeep(adv.level, adv.rank)}G/Turn）");
        return true;
    }

    public void RestoreBurialRecords(IEnumerable<BurialRecord> records)
    {
        burialRecords.Clear();
        burialRecords.AddRange(records);
    }

    /// <summary>セーブデータからの復元専用。経済ログは追加しない。</summary>
    public void RestoreEconomy(int gold, int guildRank, int guildPoints, int guildPointsThisRank = 0)
    {
        Gold = gold;
        GuildRank = Rank.Clamp(guildRank);
        GuildPoints = guildPoints;
        GuildPointsThisRank = Math.Max(0, guildPointsThisRank);
    }

    /// <summary>敵を図鑑へ登録する。初遭遇ならtrue、登録済みならfalse。</summary>
    public bool DiscoverEnemy(EnemyMasterData enemy) =>
        !string.IsNullOrWhiteSpace(enemy.id) && discoveredEnemyIds.Add(enemy.id);

    public bool HasDiscoveredEnemy(string enemyId) => discoveredEnemyIds.Contains(enemyId);

    /// <summary>セーブデータからの復元専用。現在のマスタに存在するIDだけを渡す。</summary>
    public void RestoreDiscoveredEnemies(IEnumerable<string> enemyIds)
    {
        discoveredEnemyIds.Clear();
        foreach (var id in enemyIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            discoveredEnemyIds.Add(id);
    }

    /// <summary>
    /// 冒険者1人ぶんの賃金。レベルぶんの単価に、認定ランクが上がるごとの加算を足す。
    /// ランクが上がるほど「安く使える駒」ではなくなる、というのがこのゲームの取り決め。
    /// </summary>
    public static int CalculateAdventurerUpkeep(int level, int rank = Rank.Min) =>
        Math.Max(1, level) * UpkeepGoldPerLevel
        + (Rank.Clamp(rank) - Rank.Min) * UpkeepGoldPerRank;

    public int AdventurerUpkeepPerTurn =>
        adventurers.Where(a => a != null && a.isAlive).Sum(a => CalculateAdventurerUpkeep(a.level, a.rank));

    public int FacilityUpkeepPerTurn => facilities.Sum(f => f.upkeepGoldPerTurn);
    public int PartyCapacityUpgradeCount => Math.Clamp(
        facilities.Sum(f => Math.Max(0, f.partySlotBonus)),
        0,
        PartyCapacityUpgradeMaximum);
    public int PartyCapacity => BasePartyCapacity + PartyCapacityUpgradeCount;

    /// <summary>
    /// 在籍上限を数えるときの人数。埋葬待ちの故人は席を埋めない。
    /// 死者が枠を塞ぐと、埋葬費が払えないだけで雇入れまで止まってしまう。
    /// </summary>
    public int RosterCount => adventurers.Count(a => a != null && a.isAlive);

    public int RosterCapacityUpgradeCount => Math.Clamp(
        facilities.Sum(f => Math.Max(0, f.rosterSlotBonus)),
        0,
        RosterCapacityUpgradeMaximum);

    /// <summary>いま在籍させられる冒険者の上限。施設を建てるたびに増える。</summary>
    public int RosterCapacity => BaseRosterCapacity + RosterCapacityUpgradeCount;

    public bool IsRosterFull => RosterCount >= RosterCapacity;

    /// <summary>
    /// いま1人雇えるか。上限に達しているときは、増やす手段（施設建設）まで含めて理由を返す。
    /// </summary>
    public bool CanHireAdventurer(out string reason)
    {
        reason = "";
        if (!IsRosterFull) return true;
        reason = $"在籍上限に達しています（{RosterCount}/{RosterCapacity}人）"
            + (RosterCapacity < MaximumRosterCapacity
                ? "。ギルド施設で宿舎を建設するか、冒険者を解雇すると空きます"
                : "。宿舎はすべて建設済みなので、冒険者を解雇して席を空けてください");
        return false;
    }

    /// <summary>そのランクで対象施設をすべて建てた場合の編成人数上限。</summary>
    public static int PartyCapacityCeilingForRank(int rank) => Math.Clamp(
        BasePartyCapacity
        + (rank >= 2 ? 1 : 0)
        + (rank >= 4 ? 1 : 0)
        + (rank >= 6 ? 1 : 0),
        BasePartyCapacity,
        MaximumPartyCapacity);

    public int BaseUpkeepPerTurn =>
        GuildBaseUpkeepGoldPerTurn + AdventurerUpkeepPerTurn + FacilityUpkeepPerTurn;

    public int EffectiveUpkeepPerTurn => CalculateEffectiveUpkeep(BaseUpkeepPerTurn);

    public static int CalculateEffectiveUpkeep(int baseUpkeep) =>
        Math.Max(0, (int)Math.Floor(Math.Max(0, baseUpkeep) * RelicSystem.GetUpkeepMultiplier()));

    /// <summary>
    /// 報酬などの収入がない前提で、破産せずに支払える維持費の回数。
    /// Gold が0以下になった支払いターンにゲームオーバーとなるため、残金1Gを安全ラインとする。
    /// </summary>
    public static int SafeUpkeepTurns(int gold, int effectiveUpkeep)
    {
        if (effectiveUpkeep <= 0) return int.MaxValue;
        return Math.Max(0, (gold - 1) / effectiveUpkeep);
    }

    /// <summary>
    /// 現在のギルド維持費が所要ターン中ずっと続く前提で、基本報酬から差し引いた予想収支。
    /// ランダム報酬・採取超過・レベルアップによる維持費変動は含めない。
    /// </summary>
    public int EstimateNetAfterUpkeep(int rewardGold, int turns) =>
        rewardGold - EffectiveUpkeepPerTurn * Math.Max(0, turns);

    public int PayUpkeepForAll(int currentTurn)
    {
        int baseTotal = BaseUpkeepPerTurn;
        int effectiveTotal = CalculateEffectiveUpkeep(baseTotal);
        if (effectiveTotal > 0)
            SpendGold(effectiveTotal,
                $"[Turn {currentTurn}] 維持費支払い（ギルド基本 {GuildBaseUpkeepGoldPerTurn}G + 冒険者 {adventurers.Count}人 + 施設 {facilities.Count}件）");
        return effectiveTotal;
    }

    /// <summary>
    /// クエストへ出ていない冒険者を1ターン休養させる。負傷中でも出発はできるため、
    /// 休ませるか戦力として使うかは編成によってプレイヤーが選ぶ。
    /// </summary>
    public IReadOnlyList<string> AdvanceRecovery(
        int currentTurn,
        Func<AdventurerData, bool> canRest)
    {
        var messages = new List<string>();
        int recovery = 1 + Math.Max(0, FacilitySystem.GetInjuryRecoveryBonus());
        int scarPrevention = FacilitySystem.GetScarPreventionPercent();

        foreach (var adventurer in adventurers.Where(a => a.isAlive && a.injuries.Count > 0 && canRest(a)))
        {
            var result = adventurer.AdvanceRecovery(recovery, scarPrevention);
            foreach (var injury in result.Healed)
                messages.Add($"[Turn {currentTurn}] {adventurer.name}: {injury.DisplayName}が回復");
            foreach (var scar in result.NewScars)
                messages.Add($"[Turn {currentTurn}] {adventurer.name}: {scar.DisplayName}が残り、称号「{scar.Title}」を獲得");
        }
        return messages;
    }

    public void AddRelic(RelicMasterData relic, string reason = "")
    {
        // 凍結中は入手そのものを起こさない。既存セーブの所持記録は消さずに残す。
        if (!GameFeatures.RelicsEnabled) return;
        if (relics.Contains(relic)) return;
        relics.Add(relic);
        RelicSystem.SetRelics(relics);
        economyLogs.Add($"遺物入手: {relic.relicName}{(string.IsNullOrEmpty(reason) ? "" : $"（{reason}）")}");
    }

    // ---- Facilities ----
    // 同じ施設かどうかはidで見る。マスタの同一インスタンスが渡ってくる前提にすると、
    // 復元やテストで作り直した同一IDの施設を二重に建ててしまう。
    public bool HasFacility(FacilityMasterData facility) =>
        facilities.Any(f => f.id == facility.id);

    public bool TryBuildFacility(FacilityMasterData facility, out string reason)
    {
        reason = "";
        if (HasFacility(facility)) { reason = $"既に建設済みです: {facility.displayName}"; return false; }
        if (GuildRank < facility.requiredGuildRank)
        {
            reason = $"ギルドランクが不足しています（必要: {Rank.Label(facility.requiredGuildRank)}）";
            return false;
        }
        if (Gold < facility.buildCostGold)
        {
            reason = $"資金が不足しています（必要: {facility.buildCostGold}G  所持: {Gold}G）";
            return false;
        }

        SpendGold(facility.buildCostGold, $"施設建設: {facility.displayName}");
        facilities.Add(facility);
        FacilitySystem.SetFacilities(facilities);
        economyLogs.Add($"施設建設: {facility.displayName}（維持費 +{facility.upkeepGoldPerTurn}G/Turn）");
        return true;
    }

    /// <summary>セーブデータからの復元専用。</summary>
    public void RestoreFacilities(IEnumerable<FacilityMasterData> facilitiesToRestore)
    {
        facilities.Clear();
        facilities.AddRange(facilitiesToRestore);
        FacilitySystem.SetFacilities(facilities);
    }

    // ---- Inventory ----
    public int GetCount(EquipmentMasterData item)
    {
        var s = inventory.FirstOrDefault(x => x.item == item);
        return s?.count ?? 0;
    }
    public bool Has(EquipmentMasterData item, int amount = 1) => GetCount(item) >= amount;

    public void AddEquipment(EquipmentMasterData item, int amount = 1, string reason = "")
    {
        var s = inventory.FirstOrDefault(x => x.item == item);
        if (s == null) { s = new EquipmentStack(item, 0); inventory.Add(s); }
        s.count += amount;
    }

    public bool TryConsumeEquipment(EquipmentMasterData item, int amount = 1, string reason = "")
    {
        var s = inventory.FirstOrDefault(x => x.item == item);
        if (s == null || s.count < amount) return false;
        s.count -= amount;
        if (s.count <= 0) inventory.Remove(s);
        return true;
    }

    public bool TryBuyEquipment(EquipmentMasterData item, int amount = 1)
    {
        int cost = item.price * amount;
        if (Gold < cost) return false;
        AddGold(-cost, $"購入: {item.displayName} x{amount}");
        AddEquipment(item, amount);
        return true;
    }

    /// <summary>買値の半額で売却する。売値は最低1G。</summary>
    public static int SellPrice(EquipmentMasterData item) => Math.Max(1, item.price / 2);

    public bool TrySellEquipment(EquipmentMasterData item, int amount = 1)
    {
        if (!TryConsumeEquipment(item, amount)) return false;
        AddGold(SellPrice(item) * amount, $"売却: {item.displayName} x{amount}");
        return true;
    }

    public IReadOnlyList<EquipmentStack> GetInventoryView() => inventory;

    // ---- Consumables ----
    public int GetConsumableCount(ConsumableMasterData item) =>
        consumables.FirstOrDefault(x => x.item == item)?.count ?? 0;

    public void AddConsumable(ConsumableMasterData item, int amount = 1)
    {
        if (amount <= 0) return;
        var stack = consumables.FirstOrDefault(x => x.item == item);
        if (stack == null) { stack = new ConsumableStack(item, 0); consumables.Add(stack); }
        stack.count += amount;
    }

    public bool TryConsumeConsumable(ConsumableMasterData item, int amount = 1)
    {
        var stack = consumables.FirstOrDefault(x => x.item == item);
        if (stack == null || amount <= 0 || stack.count < amount) return false;
        stack.count -= amount;
        if (stack.count == 0) consumables.Remove(stack);
        return true;
    }

    public IReadOnlyList<ConsumableStack> GetConsumablesView() => consumables;

    // ---- Shop stock ----
    public bool ShopNeedsRefresh(int currentTurn) =>
        LastShopRefreshTurn <= 0 || currentTurn - LastShopRefreshTurn >= 5;

    public void ReplaceShopStock(
        int currentTurn,
        Dictionary<string, int> equipmentStock,
        Dictionary<string, int> consumableStock)
    {
        LastShopRefreshTurn = currentTurn;
        shopEquipmentStock = equipmentStock;
        shopConsumableStock = consumableStock;
    }

    public void RestoreShopStock(
        int lastRefreshTurn,
        Dictionary<string, int> equipmentStock,
        Dictionary<string, int> consumableStock) =>
        ReplaceShopStock(lastRefreshTurn, equipmentStock, consumableStock);
}
