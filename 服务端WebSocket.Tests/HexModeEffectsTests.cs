using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class HexModeEffectsTests
{
    private static readonly IReadOnlyDictionary<int, string> RuleSurfaceById = new Dictionary<int, string>
    {
        [1] = "PowerBonus", [2] = "OnCardPlayed", [3] = "OnCardPlayed", [4] = "OnLeaderDamaged",
        [5] = "EffectRuntime.OncePerTurn", [6] = "OnAcquire", [7] = "ActionValidator.HasKeyword",
        [8] = "OnLeaderDamaged", [9] = "OnAcquire", [10] = "OnAttackDeclared", [11] = "PowerBonus",
        [12] = "Power/Counter/AttachDon", [13] = "EffectRuntime.Copy", [14] = "OnAttackDeclared",
        [15] = "OnEnter.VirtualAttack", [16] = "EffectRuntime.Copy", [17] = "EffectRuntime.Copy",
        [18] = "PowerBonus", [19] = "CanRest", [20] = "OnAcquire/PowerBonus",
        [21] = "OnEnemyAffected", [22] = "OnCharacterKo", [23] = "OnCharacterKo",
        [24] = "OnAttackDeclared", [25] = "OnAttackDeclared", [26] = "OnAttackDeclared",
        [27] = "Legacy.ResolveDamage", [28] = "OnCardPlayed", [29] = "OnCardPlayed", [30] = "StageSlots",
        [31] = "OnLifeAdded", [32] = "OnCardPlayed", [33] = "OnTurnStarted", [34] = "Power/AttackTarget",
        [35] = "OnCardPlayed", [36] = "PlayedCardOrder/HandCost", [37] = "PlayedCardOrder/HandCost", [38] = "HandCost",
        [39] = "HandCost", [40] = "OnTurnEnding", [41] = "OnEnemyAffected",
        [42] = "OnEnemyAffected", [43] = "OnLifeAdded", [44] = "OnCharacterKo/OnLeaderDamaged",
        [45] = "AttackLimit/BattleBonus", [46] = "HandCost/EffectRuntime.Copy", [47] = "OnAcquire",
        [48] = "EnemyLifeOne", [49] = "OnAcquire", [50] = "OnAttackDeclared", [51] = "CounterBonus/EventGate",
        [52] = "OnAcquire/DonLimit", [53] = "FieldCost", [54] = "OnCharacterKo",
        [55] = "OnAcquire/TierGrant", [56] = "OnAcquire/PrismaticGrant",
    };

    public static IEnumerable<object[]> AllHexIds()
        => HexCatalog.All.Select(item => new object[] { item.Id });

    [Theory]
    [MemberData(nameof(AllHexIds))]
    public void 每个海克斯编号都有服务端权威规则入口(int id)
    {
        Assert.Equal(id, HexCatalog.Get(id).Id);
        Assert.True(RuleSurfaceById.TryGetValue(id, out var surface));
        Assert.False(string.IsNullOrWhiteSpace(surface));
    }

    [Fact]
    public void 静态力量费用反击与攻击限制_按原始值和最终费用生效()
    {
        var state = HexState();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var big = Card("HEX-C8000", CardKind.Character, power: 8000, cost: 8);
        me.Characters.Add(big);

        OwnOnly(state, 0, 1);
        Assert.Equal(2000, HexRules.PowerBonus(state, 0, big));

        OwnOnly(state, 0, 7);
        Assert.True(ActionValidator.HasKeyword(state, me.Leader, "不可阻挡"));

        me.CostArea.AddRange([
            new DonCard { State = DonState.Attached, AttachedToCardId = big.Id },
            new DonCard { State = DonState.Attached, AttachedToCardId = big.Id },
        ]);
        OwnOnly(state, 0, 11);
        Assert.Equal(2000, HexRules.PowerBonus(state, 0, big));

        OwnOnly(state, 0, 12);
        Assert.Equal(1000, HexRules.PowerBonus(state, 0, big));
        Assert.Equal(1000, HexRules.CounterBonus(state, 0, big));
        me.CostArea.Add(new DonCard { State = DonState.Active });
        Assert.False(ActionValidator.CanAttachDon(state, 0, "leader").Ok);

        var twin = Card("HEX-C8000", CardKind.Character, power: 8000, cost: 8);
        me.Characters.Add(twin);
        OwnOnly(state, 0, 18);
        Assert.Equal(3000, HexRules.PowerBonus(state, 0, big));
        Assert.Equal(3000, HexRules.PowerBonus(state, 0, twin));

        var weak = Card("HEX-C5000", CardKind.Character, power: 5000, cost: 1);
        var strong = Card("HEX-C6000", CardKind.Character, power: 6000, cost: 1);
        me.Characters.AddRange([weak, strong]);
        OwnOnly(state, 0, 19);
        Assert.False(HexRules.CanRest(state, weak));
        Assert.True(HexRules.CanRest(state, strong));

        OwnOnly(state, 0, 20);
        Assert.Equal(2000, HexRules.PowerBonus(state, 0, me.Leader));
        OwnOnly(state, 0, 34);
        Assert.Equal(2000, HexRules.PowerBonus(state, 0, me.Leader));
        Assert.False(ActionValidator.CanAttack(state, 0, me.Leader.Id, true, null).Ok);

        var character = Card("HEX-C5", CardKind.Character, power: 5000, cost: 5);
        var eventCard = Card("HEX-E3", CardKind.Event, cost: 3);
        OwnOnly(state, 0, 36);
        Assert.Equal(4, state.HandPlayCost(0, character));
        OwnOnly(state, 0, 37);
        state.HexState.Runtime[0].CardsPlayedThisTurn = 1;
        Assert.Equal(1, state.HandPlayCost(0, eventCard));
        OwnOnly(state, 0, 39);
        Assert.Equal(1, state.HandPlayCost(0, eventCard));
        OwnOnly(state, 0, 46);
        Assert.Equal(6, state.HandPlayCost(0, eventCard));
        OwnOnly(state, 0, 51);
        Assert.Equal(4000, HexRules.CounterBonus(state, 0, eventCard));
        OwnOnly(state, 0, 52);
        Assert.Equal(12, state.MaxDonInCostAreaFor(0));

        var ownDragon = Card("HEX-OWN-DRAGON", CardKind.Character, power: 1000, cost: 1);
        var enemyDragon = Card("HEX-ENEMY-DRAGON", CardKind.Character, power: 1000, cost: 1);
        me.Characters.Add(ownDragon);
        opponent.Characters.Add(enemyDragon);
        OwnOnly(state, 0, 53);
        Assert.Equal(3, state.CurrentCostOf(0, ownDragon));
        Assert.Equal(0, state.CurrentCostOf(1, enemyDragon));
    }

    [Fact]
    public void 七项重做费用_按出牌序号类型转换和溢流上限统一结算()
    {
        var state = HexState();
        var me = state.Players[0];
        var character = Card("HEX-BREAD-CHARACTER", CardKind.Character, cost: 5);
        character.CostModThisTurn = -1;
        character.CostModPersistent = -1;
        state.OneShotPlayDiscounts.Add(new OneShotPlayDiscount
        {
            Owner = 0,
            Amount = 1,
            MinCost = 0,
            Kind = "Character",
        });
        OwnOnly(state, 0, 36, 38, 39);
        Assert.Equal(1, state.HandPlayCost(0, character));

        var eventCard = Card("HEX-CHEESE-EVENT", CardKind.Event, cost: 8);
        eventCard.CostModThisTurn = -1;
        eventCard.CostModPersistent = -1;
        state.OneShotPlayDiscounts.Clear();
        state.OneShotPlayDiscounts.Add(new OneShotPlayDiscount
        {
            Owner = 0,
            Amount = 2,
            MinCost = 0,
            Kind = "Event",
        });
        OwnOnly(state, 0, 36, 38, 39, 46);
        Assert.Equal(4, state.HandPlayCost(0, eventCard));

        eventCard.CostModThisTurn = 0;
        eventCard.CostModPersistent = 0;
        state.OneShotPlayDiscounts.Clear();
        OwnOnly(state, 0, 38, 46);
        Assert.Equal(10, state.HandPlayCost(0, eventCard));

        var zeroEvent = Card("HEX-TRANSFORM-ZERO", CardKind.Event, cost: 0);
        OwnOnly(state, 0, 39, 46);
        Assert.Equal(1, state.HandPlayCost(0, zeroEvent));
    }

    [Fact]
    public async Task 炼狱导管_只永久降低每次触发时已经在手牌中的事件()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 0, 35);
        var firstHeldEvent = Card("HEX-CONDUIT-FIRST", CardKind.Event, cost: 4);
        var secondHeldEvent = Card("HEX-CONDUIT-SECOND", CardKind.Event, cost: 3);
        var heldCharacter = Card("HEX-CONDUIT-CHARACTER", CardKind.Character, cost: 2);
        var firstPlayedEvent = Card("HEX-CONDUIT-PLAYED-1", CardKind.Event);
        me.Hand.AddRange([firstPlayedEvent, firstHeldEvent, secondHeldEvent, heldCharacter]);

        await HexRules.OnCardPlayedAsync(engine, 0, CardPlayer.Play(state, 0, 0));

        Assert.Equal(0, firstPlayedEvent.CostModPersistent);
        Assert.Equal(-1, firstHeldEvent.CostModPersistent);
        Assert.Equal(-1, secondHeldEvent.CostModPersistent);
        Assert.Equal(0, firstHeldEvent.CostModThisTurn);
        Assert.Equal(0, heldCharacter.CostModPersistent);

        var laterDrawnEvent = Card("HEX-CONDUIT-LATER-DRAW", CardKind.Event, cost: 5);
        me.Deck.Add(laterDrawnEvent);
        Assert.Equal(1, TurnEngine.DrawCard(state, 0, 1));
        Assert.Equal(0, laterDrawnEvent.CostModPersistent);

        var secondPlayedEvent = Card("HEX-CONDUIT-PLAYED-2", CardKind.Event);
        me.Hand.Add(secondPlayedEvent);
        await HexRules.OnCardPlayedAsync(
            engine,
            0,
            CardPlayer.Play(state, 0, me.Hand.IndexOf(secondPlayedEvent)));

        Assert.Equal(0, secondPlayedEvent.CostModPersistent);
        Assert.Equal(-2, firstHeldEvent.CostModPersistent);
        Assert.Equal(-2, secondHeldEvent.CostModPersistent);
        Assert.Equal(-1, laterDrawnEvent.CostModPersistent);

        firstHeldEvent.CostModThisTurn = -3;
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(0, firstHeldEvent.CostModThisTurn);
        Assert.Equal(-2, firstHeldEvent.CostModPersistent);

        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var privateCard = privateState.GetProperty("players")[0].GetProperty("hand")
            .EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == firstHeldEvent.Info.Number);
        Assert.Equal(-2, privateCard.GetProperty("costModPersistent").GetInt32());
        var checkpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        var replayCard = checkpoint.GetProperty("players")[0].GetProperty("hand")
            .EnumerateArray()
            .Single(card => card.GetProperty("number").GetString() == firstHeldEvent.Info.Number);
        Assert.Equal(-2, replayCard.GetProperty("CostModPersistent").GetInt32());
    }

    [Fact]
    public async Task 上一规则修订_炼狱导管与面包减费继续使用回合内和零费语义()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        HexRules.SetRulesRevisionForReplay(state, HexRules.UltimateRefreshRulesRevision);
        OwnOnly(state, 0, 35, 36, 37);
        var eventCard = Card("HEX-LEGACY-CONDUIT", CardKind.Event, cost: 1);
        var character = Card("HEX-LEGACY-BREAD", CardKind.Character, cost: 1);
        me.Hand.AddRange([eventCard, character]);

        await HexRules.OnCardPlayedAsync(
            engine,
            0,
            new PlayResult(PlayKind.Event, Card("HEX-LEGACY-PLAYED", CardKind.Event), 0));

        Assert.Equal(-1, eventCard.CostModThisTurn);
        Assert.Equal(0, eventCard.CostModPersistent);
        Assert.Equal(0, state.HandPlayCost(0, eventCard));
        Assert.Equal(0, state.HandPlayCost(0, character));
        Assert.Equal(
            "每从手牌打出1张事件，使手中全部事件费用-1至回合结束。",
            HexCatalog.DescriptionForRevision(35, state.HexState.RulesRevision));
        Assert.Equal(
            "手牌中角色实际支付费用-1。",
            HexCatalog.DescriptionForRevision(36, state.HexState.RulesRevision));
        Assert.Equal(
            "手牌中事件实际支付费用-1。",
            HexCatalog.DescriptionForRevision(37, state.HexState.RulesRevision));

        var snapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(HexRules.UltimateRefreshRulesRevision,
            snapshot.GetProperty("hexState").GetProperty("RulesRevision").GetInt32());
        TurnEngine.EnterEndPhase(state);
        Assert.Equal(0, eventCard.CostModThisTurn);
        Assert.Equal(0, eventCard.CostModPersistent);
    }

    [Fact]
    public async Task 出牌攻击和回合钩子_覆盖成长刷新狙击与累计条件()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        ClearZones(state);

        Own(state, 0, 2, 3, 28, 29, 32, 35);
        state.HexState.Runtime[0].CardsPlayedThisTurn = 2;
        me.Deck.Add(Card("HEX-DRAW", CardKind.Event));
        var discounted = Card("HEX-E2", CardKind.Event, cost: 2);
        me.Hand.Add(discounted);
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Rest }));
        var eventCard = Card("HEX-E3", CardKind.Event, cost: 3);
        await HexRules.OnCardPlayedAsync(engine, 0, new PlayResult(PlayKind.Event, eventCard, 3));
        var drawnDuringPlayHook = Assert.Single(me.Hand.Where(card => card.Info.Number == "HEX-DRAW"));
        Assert.Equal(1000, me.Leader.PowerModThisTurn);
        Assert.Equal(-1, discounted.CostModPersistent);
        Assert.Equal(-1, drawnDuringPlayHook.CostModPersistent);
        Assert.Equal(0, discounted.CostModThisTurn);
        Assert.Equal(3, me.CostArea.Count(don => don.State == DonState.Active));

        var tenCost = Card("HEX-C10", CardKind.Character, power: 10000, cost: 10);
        me.Characters.Add(tenCost);
        await HexRules.OnCardPlayedAsync(engine, 0, new PlayResult(PlayKind.Character, tenCost, 10));
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
        Assert.Contains(me.Leader.PowerModsUntilOppEnd, mod => mod.Delta == 2000);
        Assert.Contains(tenCost.PowerModsUntilOppEnd, mod => mod.Delta == 1000);

        Own(state, 0, 10, 14, 24, 25, 45, 50);
        var attacker = Card("HEX-ATTACKER", CardKind.Character, power: 6000, cost: 5);
        me.Characters.Clear();
        me.Characters.AddRange([attacker, Card("HEX-ALLY", CardKind.Character)]);
        me.Leader.IsTapped = true;
        var target = Card("HEX-TARGET8", CardKind.Character, power: 9000, cost: 8);
        target.IsTapped = true;
        opponent.Characters.Add(target);
        var handEvent = Card("HEX-HAND-E", CardKind.Event, cost: 4);
        me.Hand.Add(handEvent);
        me.LifeArea.AddRange([Card("HEX-L1", CardKind.Character), Card("HEX-L2", CardKind.Character)]);
        me.Deck.AddRange(Enumerable.Range(0, 4).Select(i => Card($"HEX-LADD-{i}", CardKind.Character)));
        state.HexState.Runtime[0].RestingCharacterAttacksThisGame = 9;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = attacker.Id,
            TargetCardId = target.Id,
            TargetIsLeader = false,
        };
        await HexRules.OnAttackDeclaredAsync(engine, 0);
        Assert.False(me.Leader.IsTapped);
        Assert.Equal(-1, handEvent.CostModThisTurn);
        Assert.Equal(5000, state.CurrentBattle.AttackerBattleBonus);
        Assert.Equal(-1000, target.PowerModThisTurn);
        Assert.Equal(4, me.LifeArea.Count);
        Assert.False(HexRules.CanDeclareAnotherAttack(state, 0));

        OwnOnly(state, 0, 33);
        opponent.Characters.Add(Card("HEX-RANDOM", CardKind.Character));
        var powerBeforeTurnStarted = opponent.Characters.ToDictionary(
            card => card.Id,
            card => card.PowerModThisTurn);
        HexRules.OnTurnStarted(state, 0);
        Assert.Single(opponent.Characters.Where(card =>
            card.PowerModThisTurn == powerBeforeTurnStarted[card.Id] - 2000));

        OwnOnly(state, 0, 40);
        opponent.Characters[0].IsTapped = false;
        opponent.Characters[1].IsTapped = true;
        HexRules.OnTurnEnding(state, 0);
        Assert.Equal(-1000, opponent.Characters[0].PowerModPersistent);
        Assert.Equal(0, opponent.Characters[1].PowerModPersistent);
    }

    [Fact]
    public async Task 终极刷新_实际打出原本费用十的卡后最多活跃八张且每个全局回合一次()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 0, 28);
        me.CostArea.AddRange(Enumerable.Range(0, 10)
            .Select(_ => new DonCard { State = DonState.Rest }));
        int donCountBefore = me.CostArea.Count;
        int donDeckCountBefore = me.DonDeck.Count;

        var first = Card("HEX-ULTIMATE-FIRST", CardKind.Character, cost: 10);
        first.CostModThisTurn = -10;
        me.Hand.Add(first);
        var firstResult = CardPlayer.Play(state, 0, 0);
        Assert.Equal(0, firstResult.PaidCost);
        await HexRules.OnCardPlayedAsync(engine, 0, firstResult);

        Assert.Equal(8, me.CostArea.Count(don => don.State == DonState.Active));
        Assert.Equal(2, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.Equal(donCountBefore, me.CostArea.Count);
        Assert.Equal(donDeckCountBefore, me.DonDeck.Count);
        Assert.True(state.HexState.Runtime[0].UltimateRefreshUsedThisTurn);

        var second = Card("HEX-ULTIMATE-SECOND", CardKind.Character, cost: 10);
        second.CostModThisTurn = -10;
        me.Hand.Add(second);
        await HexRules.OnCardPlayedAsync(engine, 0, CardPlayer.Play(state, 0, 0));
        Assert.Equal(2, me.CostArea.Count(don => don.State == DonState.Rest));

        state.CurrentTurnPlayer = 1;
        HexRules.OnTurnStarted(state, 1);
        Assert.False(state.HexState.Runtime[0].UltimateRefreshUsedThisTurn);
        var nextTurn = Card("HEX-ULTIMATE-NEXT-TURN", CardKind.Event, cost: 10);
        nextTurn.CostModThisTurn = -10;
        me.Hand.Add(nextTurn);
        await HexRules.OnCardPlayedAsync(engine, 0, CardPlayer.Play(state, 0, 0));
        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
    }

    [Fact]
    public async Task 终极刷新_按原本费用恰为十判定且效果登场不触发()
    {
        var reducedEngine = CreateEngine();
        var reducedState = reducedEngine.State;
        var reduced = reducedState.Players[0];
        ClearZones(reducedState);
        OwnOnly(reducedState, 0, 28);
        reduced.CostArea.AddRange(Enumerable.Range(0, 4)
            .Select(_ => new DonCard { State = DonState.Rest }));
        var attached = new DonCard
        {
            State = DonState.Attached,
            AttachedToCardId = reduced.Leader.Id,
        };
        reduced.CostArea.Add(attached);
        var originalTen = Card("HEX-ULTIMATE-REDUCED", CardKind.Character, cost: 10);
        originalTen.CostModThisTurn = -10;
        reduced.Hand.Add(originalTen);
        await HexRules.OnCardPlayedAsync(reducedEngine, 0, CardPlayer.Play(reducedState, 0, 0));
        Assert.Equal(4, reduced.CostArea.Count(don => don.State == DonState.Active));
        Assert.Equal(DonState.Attached, attached.State);
        Assert.Equal(reduced.Leader.Id, attached.AttachedToCardId);

        var raisedEngine = CreateEngine();
        var raisedState = raisedEngine.State;
        var raised = raisedState.Players[0];
        ClearZones(raisedState);
        OwnOnly(raisedState, 0, 28);
        raised.CostArea.AddRange(Enumerable.Range(0, 10)
            .Select(_ => new DonCard { State = DonState.Active }));
        var originalNine = Card("HEX-ULTIMATE-RAISED", CardKind.Character, cost: 9);
        originalNine.CostModThisTurn = 1;
        raised.Hand.Add(originalNine);
        var raisedResult = CardPlayer.Play(raisedState, 0, 0);
        Assert.Equal(10, raisedResult.PaidCost);
        await HexRules.OnCardPlayedAsync(raisedEngine, 0, raisedResult);
        Assert.All(raised.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        Assert.False(raisedState.HexState.Runtime[0].UltimateRefreshUsedThisTurn);

        var effectEntryEngine = CreateEngine();
        var effectEntryState = effectEntryEngine.State;
        var effectEntryPlayer = effectEntryState.Players[0];
        ClearZones(effectEntryState);
        OwnOnly(effectEntryState, 0, 28);
        effectEntryPlayer.CostArea.AddRange(Enumerable.Range(0, 3)
            .Select(_ => new DonCard { State = DonState.Rest }));
        var effectEntry = Card("HEX-ULTIMATE-EFFECT-ENTRY", CardKind.Character, cost: 10);
        effectEntryPlayer.Hand.Add(effectEntry);
        await AtomicOps.PlayFromHandFree(effectEntryState, 0, effectEntry);
        Assert.All(effectEntryPlayer.CostArea, don => Assert.Equal(DonState.Rest, don.State));
        Assert.False(effectEntryState.HexState.Runtime[0].UltimateRefreshUsedThisTurn);
    }

    [Fact]
    public async Task 终极刷新_上一规则修订继续活跃全部休息咚并保留恢复快照字段()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        HexRules.SetRulesRevisionForReplay(state, HexRules.ScopeReworkRulesRevision);
        OwnOnly(state, 0, 28);
        state.CurrentTurnPlayer = 1;
        me.CostArea.AddRange(Enumerable.Range(0, 10)
            .Select(_ => new DonCard { State = DonState.Rest }));
        var legacyPlay = Card("HEX-ULTIMATE-LEGACY", CardKind.Event, cost: 10);
        legacyPlay.CostModThisTurn = -10;
        me.Hand.Add(legacyPlay);

        await HexRules.OnCardPlayedAsync(engine, 0, CardPlayer.Play(state, 0, 0));

        Assert.All(me.CostArea, don => Assert.Equal(DonState.Active, don.State));
        var snapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var hexState = snapshot.GetProperty("hexState");
        Assert.Equal(HexRules.ScopeReworkRulesRevision, hexState.GetProperty("RulesRevision").GetInt32());
        Assert.True(hexState.GetProperty("runtime")[0]
            .GetProperty("UltimateRefreshUsedThisTurn").GetBoolean());
        var publicState = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        var owned = Assert.Single(publicState.GetProperty("hexState").GetProperty("myOwned").EnumerateArray());
        Assert.Equal(
            "每回合1次，从手牌打出原本费用10的卡后，全部非赋予中的休息咚!!转活跃。",
            owned.GetProperty("description").GetString());
    }

    [Fact]
    public async Task 巨人杀手_以目标当前费用而非原本费用判定()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        ClearZones(state);
        OwnOnly(state, 0, 24);
        var attacker = Card("HEX-GIANT-SLAYER", CardKind.Character, power: 5000, cost: 3);
        me.Characters.Add(attacker);

        async Task<int> DeclareAgainst(CardInstance target)
        {
            opponent.Characters.Clear();
            opponent.Characters.Add(target);
            state.CurrentBattle = new BattleContext
            {
                AttackerPlayerIndex = 0,
                DefenderPlayerIndex = 1,
                AttackerCardId = attacker.Id,
                TargetCardId = target.Id,
                TargetIsLeader = false,
            };
            await HexRules.OnAttackDeclaredAsync(engine, 0);
            return state.CurrentBattle.AttackerBattleBonus;
        }

        var discountedEight = Card("HEX-COST8-DISCOUNTED", CardKind.Character, cost: 8);
        discountedEight.CostModThisTurn = -1;
        Assert.Equal(7, state.CurrentCostOf(1, discountedEight));
        Assert.Equal(0, await DeclareAgainst(discountedEight));

        var raisedSeven = Card("HEX-COST7-RAISED", CardKind.Character, cost: 7);
        raisedSeven.CostModPersistent = 1;
        Assert.Equal(8, state.CurrentCostOf(1, raisedSeven));
        Assert.Equal(3000, await DeclareAgainst(raisedSeven));
    }

    [Fact]
    public async Task 万用瞄准镜_仅角色攻击获得本次战斗力量并同步权威快照()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 0, 13, 15, 26);
        var attacker = Card("HEX-SCOPE-CHARACTER", CardKind.Character, power: 5000);
        me.Characters.Add(attacker);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = attacker.Id,
            TargetIsLeader = true,
        };

        await HexRules.OnAttackDeclaredAsync(engine, 0);

        Assert.Equal(1000, attacker.PowerModThisBattle);
        Assert.Equal(6000, state.CurrentPowerOf(0, attacker));
        Assert.Equal(0, HexRules.AttackSuccessDeficit(state, 0));
        var publicState = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.Equal(6000, publicState.GetProperty("my").GetProperty("fieldCards")[0]
            .GetProperty("powerCurrent").GetInt32());
        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(1000, privateState.GetProperty("players")[0].GetProperty("characters")[0]
            .GetProperty("powerModThisBattle").GetInt32());
        var checkpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        Assert.Equal(1000, checkpoint.GetProperty("players")[0].GetProperty("characters")[0]
            .GetProperty("PowerModThisBattle").GetInt32());
        Assert.True(HexRules.ShouldCopyEffect(state, 0, EffectTrigger.OnAttackDeclare, alreadyCopied: false));
        Assert.Equal(1000, attacker.PowerModThisBattle);

        BattleEngine.EndBattle(state);
        Assert.Equal(0, attacker.PowerModThisBattle);
        Assert.Equal(5000, state.CurrentPowerOf(0, attacker));

        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = me.Leader.Id,
            TargetIsLeader = true,
        };
        int leaderBefore = state.CurrentPowerOf(0, me.Leader);
        await HexRules.OnAttackDeclaredAsync(engine, 0);
        Assert.Equal(0, me.Leader.PowerModThisBattle);
        Assert.Equal(leaderBefore, state.CurrentPowerOf(0, me.Leader));

        BattleEngine.EndBattle(state);
        var virtualAttackCharacter = DbCard("EB01-004");
        Assert.True(HexRules.ShouldTriggerAttackEffectOnEntry(state, 0, virtualAttackCharacter));
        Assert.Null(state.CurrentBattle);
        Assert.Equal(0, virtualAttackCharacter.PowerModThisBattle);
    }

    [Fact]
    public async Task 万用瞄准镜旧规则_保留低力量成功且强化版优先的历史语义()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        HexRules.SetRulesRevisionForReplay(state, HexRules.BoardingSalvoRulesRevision);
        var attacker = Card("HEX-LEGACY-SCOPE", CardKind.Character, power: 5000);
        me.Characters.Add(attacker);
        OwnOnly(state, 0, 26);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = attacker.Id,
            TargetIsLeader = true,
        };

        await HexRules.OnAttackDeclaredAsync(engine, 0);

        Assert.Equal(0, attacker.PowerModThisBattle);
        Assert.Equal(1000, HexRules.AttackSuccessDeficit(state, 0));
        Own(state, 0, 27);
        Assert.Equal(2000, HexRules.AttackSuccessDeficit(state, 0));
    }

    [Fact]
    public async Task 超凡邪恶_战斗KO累计跨回合持久且仅己方回合投影到所有快照()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 0, 22);
        state.CurrentTurnPlayer = 1;
        int opponentTurnBaseline = state.CurrentPowerOf(0, me.Leader);
        state.CurrentTurnPlayer = 0;
        int ownTurnBaseline = state.CurrentPowerOf(0, me.Leader);

        var characterAttacker = Card("HEX-NON-LEADER-KO", CardKind.Character);
        me.Characters.Add(characterAttacker);
        await HexRules.OnCharacterKoAsync(engine, 1, "battle", characterAttacker.Id, actingSide: 0);
        await HexRules.OnCharacterKoAsync(engine, 1, "effect", me.Leader.Id, actingSide: 0);
        Assert.Equal(0, state.HexState.Runtime[0].TranscendentEvilOwnTurnPower);

        await HexRules.OnCharacterKoAsync(engine, 1, "battle", me.Leader.Id, actingSide: 0);
        await HexRules.OnCharacterKoAsync(engine, 1, "battle", me.Leader.Id, actingSide: 0);

        Assert.Equal(1000, state.HexState.Runtime[0].TranscendentEvilOwnTurnPower);
        Assert.Equal(0, me.Leader.PowerModThisTurn);
        Assert.Equal(ownTurnBaseline + 1000, state.CurrentPowerOf(0, me.Leader));

        var ownerView = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        var opponentView = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 1));
        Assert.Equal(ownTurnBaseline + 1000, ownerView.GetProperty("my").GetProperty("leaderPower").GetInt32());
        Assert.Equal(ownTurnBaseline + 1000, opponentView.GetProperty("opponent").GetProperty("leaderPower").GetInt32());

        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(1000, privateState.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("TranscendentEvilOwnTurnPower").GetInt32());
        var replayCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);
        Assert.Equal(1000, replayCheckpoint.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("TranscendentEvilOwnTurnPower").GetInt32());

        state.CurrentTurnPlayer = 1;
        HexRules.OnTurnStarted(state, 1);
        Assert.Equal(1000, state.HexState.Runtime[0].TranscendentEvilOwnTurnPower);
        Assert.Equal(opponentTurnBaseline, state.CurrentPowerOf(0, me.Leader));
        var defenseView = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.Equal(opponentTurnBaseline, defenseView.GetProperty("my").GetProperty("leaderPower").GetInt32());

        state.CurrentTurnPlayer = 0;
        HexRules.OnTurnStarted(state, 0);
        Assert.Equal(ownTurnBaseline + 1000, state.CurrentPowerOf(0, me.Leader));
    }

    [Fact]
    public async Task 旧版房间恢复_超凡邪恶与巨人杀手继续沿用建局时语义()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        ClearZones(state);
        HexRules.SetRulesRevisionForReplay(state, HexRules.LegacyRulesRevision);
        OwnOnly(state, 0, 22, 24);

        await HexRules.OnCharacterKoAsync(engine, 1, "battle", me.Leader.Id, actingSide: 0);
        Assert.Equal(500, me.Leader.PowerModThisTurn);
        Assert.Equal(0, state.HexState.Runtime[0].TranscendentEvilOwnTurnPower);

        var attacker = Card("HEX-LEGACY-GIANT-SLAYER", CardKind.Character, cost: 1);
        var discountedEight = Card("HEX-LEGACY-COST8", CardKind.Character, cost: 8);
        discountedEight.CostModThisTurn = -1;
        me.Characters.Add(attacker);
        opponent.Characters.Add(discountedEight);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = attacker.Id,
            TargetCardId = discountedEight.Id,
            TargetIsLeader = false,
        };

        await HexRules.OnAttackDeclaredAsync(engine, 0);

        Assert.Equal(7, state.CurrentCostOf(1, discountedEight));
        Assert.Equal(3000, state.CurrentBattle.AttackerBattleBonus);
    }

    [Fact]
    public async Task 生命伤害KO和敌方效果钩子_按实际事件且限次触发()
    {
        var engine = CreateEngine(seed: 7);
        var state = engine.State;
        var me = state.Players[0];
        var opponent = state.Players[1];
        ClearZones(state);

        Own(state, 0, 4, 8);
        var attacker = Card("HEX-POWER12000", CardKind.Character, power: 12000);
        me.Characters.Add(attacker);
        me.Deck.AddRange([
            Card("HEX-DRAW-4", CardKind.Character),
            Card("HEX-LIFE-8", CardKind.Character),
        ]);
        await HexRules.OnLeaderDamagedAsync(engine, defender: 1, damage: 1, attacker);
        Assert.Single(me.Hand);
        Assert.Single(me.LifeArea);
        Assert.True(state.HexState.Runtime[0].SoulSiphonUsedThisTurn);

        Own(state, 0, 22, 44, 54);
        OwnOnlyAdd(state, 1, 23);
        var ally = Card("HEX-ALLY-KO", CardKind.Character, power: 5000);
        me.Characters.Add(ally);
        await HexRules.OnCharacterKoAsync(engine, 1, "battle", me.Leader.Id, actingSide: 0);
        Assert.Equal(1000, me.Leader.PowerModThisTurn);
        Assert.Equal(500, state.HexState.Runtime[0].TranscendentEvilOwnTurnPower);
        Assert.Equal(0, ally.PowerModThisTurn);
        Assert.Equal(1000, state.HexState.Runtime[0].TankEngineOpponentTurnPower);
        Assert.True(state.HexState.Runtime[0].NavyCarnivalUsedThisTurn);
        await HexRules.OnLeaderDamagedAsync(engine, defender: 0, damage: 1, attacker: null);
        Assert.Equal(0, state.HexState.Runtime[0].TankEngineOpponentTurnPower);

        Own(state, 0, 21, 41, 42);
        var affected = Card("HEX-AFFECTED", CardKind.Character, power: 5000);
        opponent.Characters.Add(affected);
        me.Deck.Add(Card("HEX-SLAP-DRAW", CardKind.Character));
        int leaderBeforeAffected = me.Leader.PowerModThisTurn;
        var affectedTask = HexRules.OnEnemyAffectedByOwnEffectAsync(
            engine, actingSide: 0, affectedOwner: 1, affected, wasActiveRested: true, leftField: false);
        await ResolvePromptsUntilComplete(engine, affectedTask);
        Assert.Equal(-3000, affected.PowerModThisTurn);
        Assert.Equal(leaderBeforeAffected + 2000, me.Leader.PowerModThisTurn);
        Assert.Single(me.Trash);
        Assert.True(state.HexState.Runtime[0].SlapUsedThisTurn);

        OwnOnly(state, 0, 43);
        int opponentLeaderBefore = opponent.Leader.PowerModThisTurn;
        await HexRules.OnLifeAddedAsync(engine, 0, 2);
        Assert.Equal(opponentLeaderBefore - 2000, opponent.Leader.PowerModThisTurn);

        OwnOnly(state, 0, 31);
        me.Deck.AddRange(Enumerable.Range(0, 100).Select(i => Card($"HEX-HEAL-{i}", CardKind.Character)));
        int lifeBefore = me.LifeArea.Count;
        await HexRules.OnLifeAddedAsync(engine, 0, 64);
        Assert.True(state.HexState.Runtime[0].CriticalHealSucceededThisTurn);
        Assert.Equal(lifeBefore + 1, me.LifeArea.Count);

        OwnOnly(state, 0, 48);
        me.Deck.AddRange(Enumerable.Range(0, 20).Select(i => Card($"HEX-KING-{i}", CardKind.Character)));
        int ownedBefore = state.HexState.Owned[0].Count;
        int handBefore = me.Hand.Count;
        var kingTask = HexRules.OnEnemyLifeReachedOneAsync(engine, 0);
        await ResolvePromptsUntilComplete(engine, kingTask);
        Assert.True(state.HexState.Runtime[0].KingUsedThisGame);
        Assert.Equal(ownedBefore + 1, state.HexState.Owned[0].Count);
        Assert.True(me.Hand.Count >= handBefore + 2);
    }

    [Fact]
    public async Task 自己效果KO自己角色_不会误算为己方KO敌方()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 0, 44, 54);
        var ownVictim = Card("HEX-OWN-VICTIM", CardKind.Character, power: 5000);
        me.Characters.Add(ownVictim);

        await HexRules.OnCharacterKoAsync(engine, victimOwner: 0, reason: "effect", attackerId: null, actingSide: 0);

        Assert.Equal(0, state.HexState.Runtime[0].TankEngineOpponentTurnPower);
        Assert.False(state.HexState.Runtime[0].TankEngineUsedThisTurn);
        Assert.False(state.HexState.Runtime[0].NavyCarnivalUsedThisTurn);
        Assert.Equal(0, me.Leader.PowerModThisTurn);

        var opponentStage = Card("HEX-STAGE-KO", CardKind.Stage);
        state.Players[1].StageCard = opponentStage;
        await HexRules.OnGameEventAsync(
            state,
            EffectTrigger.OnAnyCharKOd,
            engine.Prompts,
            new Dictionary<string, object?>
            {
                ["cardId"] = opponentStage.Id.ToString(),
                ["owner"] = 1,
                ["reason"] = "effect",
                ["actingSide"] = 0,
            });
        Assert.False(state.HexState.Runtime[0].TankEngineUsedThisTurn);
        Assert.False(state.HexState.Runtime[0].NavyCarnivalUsedThisTurn);
    }

    [Fact]
    public async Task 霸王色霸气_启动横置成本失败后不继续结算()
    {
        var state = HexState();
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 1, 19); // H19 为全场规则，对手拥有也必须拦截己方成本。

        var source = Card("OP01-051", CardKind.Character, power: 5000, cost: 8);
        var handTarget = Card("HEX-HAND-C3", CardKind.Character, power: 3000, cost: 3);
        me.Characters.Add(source);
        me.Hand.Add(handTarget);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, new MockPromptService());

        Assert.False(source.IsTapped);
        Assert.Contains(handTarget, me.Hand);
        Assert.DoesNotContain(handTarget, me.Characters);
        Assert.Empty(me.TurnOnceUsed);
    }

    [Fact]
    public async Task 霸王色霸气_多角色横置成本预检失败时不留下半支付()
    {
        var state = HexState();
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 1, 19);

        var source = DbCard("OP05-089");
        source.PowerModPersistent = 10000; // 自身可休息，只让另一张弱角色触发 H19 拒绝。
        var weakOther = Card("HEX-WEAK-SECOND-COST", CardKind.Character, power: 5000, cost: 1);
        var reward = new CardInstance
        {
            Info = new CardInfo
            {
                Number = "HEX-BLACK-COST1",
                Name = "HEX-BLACK-COST1",
                Color = "黑",
                Kind = CardKind.Character,
                Property = "打",
                Power = 1000,
                Cost = 1,
            },
        };
        me.Characters.AddRange([source, weakOther]);
        me.Trash.Add(reward);
        var prompts = new MockPromptService();

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.False(source.IsTapped);
        Assert.False(weakOther.IsTapped);
        Assert.Contains(reward, me.Trash);
        Assert.Empty(prompts.ConfirmHistory);
        Assert.Empty(prompts.ChooseHistory);
    }

    [Fact]
    public void 霸王色霸气_攻击与阻挡的验证和直接入口均不可绕过()
    {
        var state = HexState();
        ClearZones(state);
        OwnOnly(state, 0, 19);
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;

        var attacker = Card("HEX-WEAK-ATTACKER", CardKind.Character, power: 5000);
        state.Players[0].Characters.Add(attacker);
        Assert.False(ActionValidator.CanAttack(state, 0, attacker.Id, targetIsLeader: true, targetId: null).Ok);
        Assert.Throws<InvalidOperationException>(() =>
            BattleEngine.StartAttack(state, attacker.Id, targetIsLeader: true, targetId: null));
        Assert.False(attacker.IsTapped);
        Assert.Null(state.CurrentBattle);

        var blocker = new CardInstance
        {
            Info = new CardInfo
            {
                Number = "HEX-WEAK-BLOCKER",
                Name = "HEX-WEAK-BLOCKER",
                Color = "红",
                Kind = CardKind.Character,
                Property = "打",
                Power = 5000,
                Abilities = ["阻挡者"],
            },
        };
        state.Players[1].Characters.Add(blocker);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = attacker.Id,
            TargetIsLeader = true,
        };
        state.Phase = Phase.BattleBlock;

        Assert.False(ActionValidator.CanDeclareBlocker(state, 1, blocker.Id).Ok);
        Assert.Throws<InvalidOperationException>(() => BattleEngine.DeclareBlocker(state, blocker.Id));
        Assert.False(blocker.IsTapped);
        Assert.False(state.CurrentBattle.BlockerDeclared);
    }

    [Fact]
    public async Task 霸王色霸气_指定休息登场的弱角色改为活跃登场()
    {
        var state = HexState();
        ClearZones(state);
        OwnOnly(state, 1, 19);
        var character = Card("HEX-RESTED-ENTRY", CardKind.Character, power: 5000, cost: 3);
        state.Players[0].Trash.Add(character);

        await AtomicOps.PlayFromTrashFree(state, 0, character, restState: true);

        Assert.Contains(character, state.Players[0].Characters);
        Assert.False(character.IsTapped);
    }

    [Fact]
    public async Task 效果复制与每回合两次_实际重复结算且不会无限递归()
    {
        var state = HexState();
        var me = state.Players[0];
        var opponent = state.Players[1];
        opponent.LifeArea.AddRange([Card("HEX-OL1", CardKind.Character), Card("HEX-OL2", CardKind.Character)]);

        OwnOnly(state, 0, 13);
        var attackSource = DbCard("EB01-003");
        me.Characters.Add(attackSource);
        await EffectRuntime.Resolve(state, 0, attackSource, EffectTrigger.OnAttackDeclare, new MockPromptService());
        Assert.Equal(4000, attackSource.PowerModThisTurn);

        OwnOnly(state, 0, 16);
        var enterSource = DbCard("EB03-047");
        me.Deck.Clear();
        me.Deck.AddRange(Enumerable.Range(0, 12).Select(i => Card($"HEX-MILL-{i}", CardKind.Character)));
        await EffectRuntime.Resolve(state, 0, enterSource, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(3, me.Trash.Count);
        await EffectRuntime.Resolve(state, 0, enterSource, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(9, me.Trash.Count);
        await EffectRuntime.Resolve(state, 0, enterSource, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(12, me.Trash.Count);
        Assert.Equal(2, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);

        OwnOnly(state, 0, 17);
        var koSource = DbCard("OP15-012");
        me.Deck.AddRange(Enumerable.Range(0, 4).Select(i => Card($"HEX-KO-DRAW-{i}", CardKind.Character)));
        int handBefore = me.Hand.Count;
        await EffectRuntime.Resolve(state, 0, koSource, EffectTrigger.OnKO, new MockPromptService());
        Assert.Equal(handBefore + 2, me.Hand.Count);
        await EffectRuntime.Resolve(state, 0, koSource, EffectTrigger.OnKO, new MockPromptService());
        Assert.Equal(handBefore + 3, me.Hand.Count);

        OwnOnly(state, 0, 46);
        var eventSource = DbCard("OP15-019");
        me.Deck.AddRange(Enumerable.Range(0, 4).Select(i => Card($"HEX-EVENT-DRAW-{i}", CardKind.Character)));
        handBefore = me.Hand.Count;
        int leaderBefore = me.Leader.PowerModThisTurn;
        await EffectRuntime.ResolvePlayedEvent(state, 0, eventSource, EffectTrigger.EventMain, new MockPromptService());
        Assert.Equal(handBefore + 2, me.Hand.Count);
        Assert.Equal(leaderBefore + 2000, me.Leader.PowerModThisTurn);

        var inventorState = HexState("ST01-001");
        var inventor = inventorState.Players[0];
        OwnOnly(inventorState, 0, 5);
        inventor.CostArea.AddRange(Enumerable.Range(0, 3).Select(_ => new DonCard { State = DonState.Rest }));
        var prompts = new MockPromptService();
        await EffectRuntime.Resolve(inventorState, 0, inventor.Leader, EffectTrigger.ActivatedMain, prompts);
        await EffectRuntime.Resolve(inventorState, 0, inventor.Leader, EffectTrigger.ActivatedMain, prompts);
        await EffectRuntime.Resolve(inventorState, 0, inventor.Leader, EffectTrigger.ActivatedMain, prompts);
        Assert.Equal(2, inventor.CostArea.Count(don => don.State == DonState.Attached));
    }

    [Fact]
    public async Task 登舰礼炮_无效果无效放弃与空候选均不计数_第二个实际发动才复制()
    {
        var state = HexState();
        var me = state.Players[0];
        OwnOnly(state, 0, 16);

        // 无【登场时】的卡与已被无效的【登场时】都不属于“发动”。
        await EffectRuntime.Resolve(
            state, 0, Card("HEX-NO-ENTER", CardKind.Character),
            EffectTrigger.OnEnterField, new MockPromptService());
        var nullifiedSource = DbCard("EB03-047");
        nullifiedSource.IsEffectsNullified = true;
        await EffectRuntime.Resolve(
            state, 0, nullifiedSource, EffectTrigger.OnEnterField, new MockPromptService());

        // EB04-032 有合法成本但玩家放弃，不计为发动。
        var optionalSource = DbCard("EB04-032");
        me.Hand.Add(DbCard("ST04-002"));
        await EffectRuntime.Resolve(
            state, 0, optionalSource, EffectTrigger.OnEnterField,
            new MockPromptService().QueueChooseEmpty());

        // OP01-005 没有合法废弃区目标时会在脚本内静默返回。
        var silentSource = DbCard("OP01-005");
        await EffectRuntime.Resolve(
            state, 0, silentSource, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(0, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.False(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);

        // 第一个实际发动只推进计数，第二个才额外结算；复制本身不会作为第三次再次计数。
        var actualSource = DbCard("EB03-047");
        me.Deck.Clear();
        me.Deck.AddRange(Enumerable.Range(0, 12)
            .Select(i => Card($"HEX-ENTER-ACTUAL-{i}", CardKind.Character)));
        await EffectRuntime.Resolve(
            state, 0, actualSource, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(1, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.Equal(3, me.Trash.Count);

        await EffectRuntime.Resolve(
            state, 0, actualSource, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(2, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.False(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);
        Assert.Equal(9, me.Trash.Count);
    }

    [Fact]
    public async Task 登舰礼炮_双方独立计数_回合开始共同重置_旧房间仍复制首个发动()
    {
        var state = HexState();
        OwnOnly(state, 0, 16);
        OwnOnly(state, 1, 16);
        var source = DbCard("EB03-047");
        state.Players[0].Deck.AddRange(Enumerable.Range(0, 15)
            .Select(i => Card($"HEX-P0-ENTER-{i}", CardKind.Character)));
        state.Players[1].Deck.AddRange(Enumerable.Range(0, 15)
            .Select(i => Card($"HEX-P1-ENTER-{i}", CardKind.Character)));

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        await EffectRuntime.Resolve(state, 1, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(3, state.Players[0].Trash.Count);
        Assert.Equal(3, state.Players[1].Trash.Count);
        Assert.Equal(1, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.Equal(1, state.HexState.Runtime[1].ActivatedEnterEffectsThisTurn);

        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(9, state.Players[0].Trash.Count);
        Assert.Equal(3, state.Players[1].Trash.Count);
        Assert.Equal(2, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.Equal(1, state.HexState.Runtime[1].ActivatedEnterEffectsThisTurn);

        HexRules.OnTurnStarted(state, 1);
        Assert.All(state.HexState.Runtime, runtime => Assert.Equal(0, runtime.ActivatedEnterEffectsThisTurn));
        await EffectRuntime.Resolve(state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(12, state.Players[0].Trash.Count);
        Assert.Equal(1, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);

        var legacy = HexState();
        HexRules.SetRulesRevisionForReplay(legacy, HexRules.AstralBodyRulesRevision);
        OwnOnly(legacy, 0, 16);
        legacy.Players[0].Deck.AddRange(Enumerable.Range(0, 9)
            .Select(i => Card($"HEX-LEGACY-ENTER-{i}", CardKind.Character)));
        await EffectRuntime.Resolve(
            legacy, 0, DbCard("EB03-047"), EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(6, legacy.Players[0].Trash.Count);
        Assert.True(legacy.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);
        Assert.Equal(0, legacy.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
    }

    [Fact]
    public async Task 登舰礼炮_效果登场队列中的嵌套登场时按实际发动顺序成为第二个()
    {
        var state = HexState();
        var me = state.Players[0];
        OwnOnly(state, 0, 16);
        me.Deck.AddRange(Enumerable.Range(0, 9)
            .Select(i => Card($"HEX-NESTED-ENTER-{i}", CardKind.Character)));

        await EffectRuntime.Resolve(
            state, 0, DbCard("EB03-047"), EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(1, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.Equal(3, me.Trash.Count);

        // 效果登场统一在当前效果链结束后由待处理队列排空；该卡的登场时应成为第2个并结算两次。
        var nestedSource = DbCard("EB03-047");
        me.Characters.Add(nestedSource);
        state.EnqueueEnterField(0, nestedSource, "deck");
        await EffectRuntime.DrainPendingEnterFields(state, new MockPromptService());

        Assert.Empty(state.PendingEnterFields);
        Assert.Equal(2, state.HexState.Runtime[0].ActivatedEnterEffectsThisTurn);
        Assert.Equal(9, me.Trash.Count);
    }

    [Fact]
    public async Task 复制KO时_回合条件不成立不消耗首次机会()
    {
        var state = HexState();
        var me = state.Players[0];
        OwnOnly(state, 0, 17);
        state.CurrentTurnPlayer = 0;

        // P-090 仅在对方回合中发动；我方回合触发会在脚本内静默返回。
        var silentSource = DbCard("P-090");
        await EffectRuntime.Resolve(
            state, 0, silentSource, EffectTrigger.OnKO, new MockPromptService());

        Assert.False(state.HexState.Runtime[0].FirstKoEffectCopiedThisTurn);

        // 后续真正发动的 KO 时仍应结算两次。
        var actualSource = DbCard("OP15-012");
        me.Deck.Clear();
        me.Deck.AddRange(Enumerable.Range(0, 2)
            .Select(i => Card($"HEX-KO-ACTUAL-{i}", CardKind.Character)));
        int handBefore = me.Hand.Count;
        await EffectRuntime.Resolve(
            state, 0, actualSource, EffectTrigger.OnKO, new MockPromptService());

        Assert.True(state.HexState.Runtime[0].FirstKoEffectCopiedThisTurn);
        Assert.Equal(handBefore + 2, me.Hand.Count);
    }

    [Fact]
    public async Task 复制触发效果_DSL条件不成立不消耗登场与KO首次机会()
    {
        var state = HexState();
        OwnOnly(state, 0, 16, 17);

        // 两张均为纯 DSL：当前领袖既不含《七水之城》，也不含《因佩尔地狱》。
        await EffectRuntime.Resolve(
            state, 0, DbCard("EB01-031"), EffectTrigger.OnEnterField, new MockPromptService());
        await EffectRuntime.Resolve(
            state, 0, DbCard("EB01-036"), EffectTrigger.OnKO, new MockPromptService());

        Assert.False(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);
        Assert.False(state.HexState.Runtime[0].FirstKoEffectCopiedThisTurn);
    }

    [Fact]
    public async Task 获得时效果_顺序生命随机授予抽牌与真实咚均落权威状态()
    {
        var engine = CreateEngine(seed: 17);
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        me.Hand.AddRange([
            Card("HEX-H1", CardKind.Character),
            Card("HEX-H2", CardKind.Character),
        ]);
        me.Deck.AddRange([
            Card("HEX-ASTRAL-D1", CardKind.Character),
            Card("HEX-ASTRAL-D2", CardKind.Character),
        ]);
        OwnOnly(state, 0, 43);

        var astralTask = HexRules.ApplyOnAcquireAsync(engine, 0, 6);
        await ResolvePromptsUntilComplete(engine, astralTask);
        Assert.Equal(["HEX-H2"], me.Hand.Select(card => card.Info.Number));
        Assert.Equal(2, me.LifeArea.Count);
        Assert.Equal(["HEX-ASTRAL-D1", "HEX-H1"], me.LifeArea.Select(card => card.Info.Number));
        Assert.Equal(["HEX-ASTRAL-D2"], me.Deck.Select(card => card.Info.Number));
        Assert.Equal(-2000, state.Players[1].Leader.PowerModThisTurn);

        me.Deck.AddRange(Enumerable.Range(0, 30).Select(i => Card($"HEX-ACQ-{i}", CardKind.Character)));
        int lifeBefore = me.LifeArea.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 9);
        Assert.Equal(lifeBefore + 1, me.LifeArea.Count);
        Assert.Equal(1000, me.Leader.PowerModPersistent);

        int handBeforeGlassCannon = me.Hand.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 20);
        Assert.Equal(handBeforeGlassCannon + 1, me.Hand.Count);
        Assert.Equal(lifeBefore, me.LifeArea.Count);

        int handBefore = me.Hand.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 49);
        Assert.Equal(handBefore + 3, me.Hand.Count);
        int donBefore = me.DonDeck.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 52);
        Assert.Equal(donBefore + 2, me.DonDeck.Count);

        state.HexState.Owned[0].Clear();
        state.HexState.Owned[0].AddRange(HexCatalog.Regular
            .Select(item => item.Id)
            .Where(id => id is not 1 and not 2));
        int ownedBefore = state.HexState.Owned[0].Count;
        var chaosTask = HexRules.ApplyOnAcquireAsync(engine, 0, 47);
        await ResolvePromptsUntilComplete(engine, chaosTask);
        Assert.Equal(ownedBefore + 2, state.HexState.Owned[0].Count);
        Assert.Equal(state.HexState.Owned[0].Count, state.HexState.Owned[0].Distinct().Count());
        Assert.Equal(1, state.HexState.Owned[0].Count(id => id == 47));
        Assert.Equal(new[] { 1, 2 }, state.HexState.GrantedByTransmutation[0].Order());
        Assert.DoesNotContain(state.HexState.Owned[0], HexCatalog.IsAlternative);
    }

    [Fact]
    public async Task 星界躯体_新版无手牌仍补卡组顶且旧房间保持两手牌语义()
    {
        var currentEngine = CreateEngine(seed: 600006);
        var current = currentEngine.State.Players[0];
        ClearZones(currentEngine.State);
        current.Deck.AddRange([
            Card("HEX-CURRENT-D1", CardKind.Character),
            Card("HEX-CURRENT-D2", CardKind.Character),
        ]);

        await HexRules.ApplyOnAcquireAsync(currentEngine, 0, 6);

        Assert.Equal(["HEX-CURRENT-D1"], current.LifeArea.Select(card => card.Info.Number));
        Assert.Equal(["HEX-CURRENT-D2"], current.Deck.Select(card => card.Info.Number));

        var legacyEngine = CreateEngine(seed: 500005);
        var legacyState = legacyEngine.State;
        var legacy = legacyState.Players[0];
        ClearZones(legacyState);
        HexRules.SetRulesRevisionForReplay(legacyState, HexRules.CatalogConfigurationRulesRevision);
        legacy.Hand.AddRange([
            Card("HEX-LEGACY-H1", CardKind.Character),
            Card("HEX-LEGACY-H2", CardKind.Character),
        ]);
        legacy.Deck.Add(Card("HEX-LEGACY-D1", CardKind.Character));

        var legacyTask = HexRules.ApplyOnAcquireAsync(legacyEngine, 0, 6);
        await ResolvePromptsUntilComplete(legacyEngine, legacyTask);

        Assert.Empty(legacy.Hand);
        Assert.Equal(["HEX-LEGACY-H2", "HEX-LEGACY-H1"], legacy.LifeArea.Select(card => card.Info.Number));
        Assert.Equal(["HEX-LEGACY-D1"], legacy.Deck.Select(card => card.Info.Number));

        var emptyDeckEngine = CreateEngine(seed: 600007);
        var emptyDeckState = emptyDeckEngine.State;
        var emptyDeckPlayer = emptyDeckState.Players[0];
        ClearZones(emptyDeckState);
        DeckOutRules.Arm(emptyDeckState);
        emptyDeckPlayer.Hand.Add(Card("HEX-EMPTY-DECK-H1", CardKind.Character));

        var emptyDeckTask = HexRules.ApplyOnAcquireAsync(emptyDeckEngine, 0, 6);
        await ResolvePromptsUntilComplete(emptyDeckEngine, emptyDeckTask);

        Assert.Equal(["HEX-EMPTY-DECK-H1"], emptyDeckPlayer.LifeArea.Select(card => card.Info.Number));
        Assert.True(emptyDeckState.IsGameOver);
    }

    [Fact]
    public async Task 两种阶级质变_新版分别只授予金色与棱彩且确定性排除自身备选与重复()
    {
        var first = CreateEngine(seed: 556056);
        var second = CreateEngine(seed: 556056);

        static void Prepare(GameState state)
        {
            state.HexState.Owned[0].Clear();
            state.HexState.Owned[0].AddRange(HexCatalog.Regular
                .Where(item => item.Tier == HexTier.Silver && item.Id is not 8 and not 23)
                .Select(item => item.Id));
            Own(state, 0, HexCatalog.Regular
                .Where(item => item.Tier == HexTier.Gold && item.Id is not 1 and not 2)
                .Select(item => item.Id)
                .ToArray());
            Own(state, 0, HexCatalog.Regular
                .Where(item => item.Tier == HexTier.Rainbow && item.Id is not 5 and not 10)
                .Select(item => item.Id)
                .ToArray());
            Own(state, 0, 55, 56);
        }

        Prepare(first.State);
        Prepare(second.State);
        int firstRandomBefore = first.State.RandomSeq;
        int secondRandomBefore = second.State.RandomSeq;

        await HexRules.ApplyOnAcquireAsync(first, 0, 55);
        await HexRules.ApplyOnAcquireAsync(first, 0, 56);
        await HexRules.ApplyOnAcquireAsync(second, 0, 55);
        await HexRules.ApplyOnAcquireAsync(second, 0, 56);

        static int[] NewlyGranted(GameState state)
            => state.HexState.Owned[0]
                .Where(id => id is 1 or 2 or 5 or 8 or 10 or 23)
                .Order()
                .ToArray();

        Assert.Equal(NewlyGranted(first.State), NewlyGranted(second.State));
        Assert.Empty(NewlyGranted(first.State).Where(id => HexCatalog.Get(id).Tier == HexTier.Silver));
        Assert.Single(NewlyGranted(first.State).Where(id => HexCatalog.Get(id).Tier == HexTier.Gold));
        Assert.Single(NewlyGranted(first.State).Where(id => HexCatalog.Get(id).Tier == HexTier.Rainbow));
        Assert.Equal(firstRandomBefore + 2, first.State.RandomSeq);
        Assert.Equal(secondRandomBefore + 2, second.State.RandomSeq);
        Assert.Equal(NewlyGranted(first.State), first.State.HexState.GrantedByTransmutation[0].Order());
        Assert.Equal(
            first.State.HexState.GrantedByTransmutation[0].Order(),
            second.State.HexState.GrantedByTransmutation[0].Order());
        Assert.Equal(first.State.HexState.Owned[0].Count, first.State.HexState.Owned[0].Distinct().Count());
        Assert.Equal(1, first.State.HexState.Owned[0].Count(id => id == 55));
        Assert.Equal(1, first.State.HexState.Owned[0].Count(id => id == 56));
        Assert.DoesNotContain(first.State.HexState.Owned[0], HexCatalog.IsAlternative);
    }

    [Fact]
    public async Task 黄金阶质变_上一规则修订版继续确定性授予一个银色和一个金色()
    {
        var engine = CreateEngine(seed: 355055);
        var state = engine.State;
        HexRules.SetRulesRevisionForReplay(state, HexRules.PerSlotRefreshRulesRevision);
        state.HexState.Owned[0].Clear();
        state.HexState.Owned[0].AddRange(HexCatalog.Regular
            .Where(item => item.Tier == HexTier.Silver && item.Id != 8)
            .Select(item => item.Id));
        Own(state, 0, HexCatalog.Regular
            .Where(item => item.Tier == HexTier.Gold && item.Id != 1)
            .Select(item => item.Id)
            .ToArray());

        int randomBefore = state.RandomSeq;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 55);

        Assert.Contains(8, state.HexState.Owned[0]);
        Assert.Contains(1, state.HexState.Owned[0]);
        Assert.Equal(randomBefore + 2, state.RandomSeq);
        Assert.Empty(state.HexState.GrantedByTransmutation[0]);
    }

    [Fact]
    public async Task 两种质变_目标池耗尽时不抛错不消费随机也不重复授予()
    {
        var engine = CreateEngine(seed: 555656);
        var state = engine.State;
        state.HexState.Owned[0].Clear();
        state.HexState.Owned[0].AddRange(HexCatalog.Regular.Select(item => item.Id));
        int ownedBefore = state.HexState.Owned[0].Count;
        int randomBefore = state.RandomSeq;

        await HexRules.ApplyOnAcquireAsync(engine, 0, 55);
        await HexRules.ApplyOnAcquireAsync(engine, 0, 56);

        Assert.Equal(ownedBefore, state.HexState.Owned[0].Count);
        Assert.Equal(randomBefore, state.RandomSeq);
        Assert.Equal(state.HexState.Owned[0].Count, state.HexState.Owned[0].Distinct().Count());
    }

    [Fact]
    public void 抽牌类型转换_每类每回合只转换第一张且继续补抽()
    {
        var state = HexState();
        var me = state.Players[0];
        HexRules.SetRulesRevisionForReplay(state, HexRules.PermanentCostFloorRulesRevision);
        Own(state, 0, 38, 39);
        me.Deck.AddRange([
            Card("HEX-E-FIRST", CardKind.Event),
            Card("HEX-C-FIRST", CardKind.Character),
            Card("HEX-E-SECOND", CardKind.Event),
            Card("HEX-C-SECOND", CardKind.Character),
        ]);

        Assert.Equal(2, TurnEngine.DrawCard(state, 0, 2));
        Assert.Equal(new[] { "HEX-E-FIRST", "HEX-C-FIRST" }, me.Trash.Select(card => card.Info.Number).ToArray());
        Assert.Equal(new[] { "HEX-E-SECOND", "HEX-C-SECOND" }, me.Hand.Select(card => card.Info.Number).ToArray());
        Assert.True(state.HexState.Runtime[0].EventDrawConvertedThisTurn);
        Assert.True(state.HexState.Runtime[0].CharacterDrawConvertedThisTurn);
    }

    [Fact]
    public async Task 三号船坞_第三张精确废弃所选槽且快照同时公开两槽()
    {
        var engine = CreateEngine();
        var state = engine.State;
        var me = state.Players[0];
        ClearZones(state);
        OwnOnly(state, 0, 30);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        var first = Card("HEX-STAGE-1", CardKind.Stage);
        var second = Card("HEX-STAGE-2", CardKind.Stage);
        var third = Card("HEX-STAGE-3", CardKind.Stage);
        me.Hand.AddRange([first, second, third]);

        Assert.True(engine.HandleAction(0, "PlayCard", Json(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        Assert.True(engine.HandleAction(0, "PlayCard", Json(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        Assert.Same(first, me.StageCard);
        Assert.Same(second, me.ExtraStageCard);

        Assert.True(engine.HandleAction(0, "PlayCard", Json(new
        {
            handIndex = 0,
            overflowTrashStageId = second.Id.ToString(),
        })));
        await engine.WaitSettledAsync();
        Assert.Same(first, me.StageCard);
        Assert.Same(third, me.ExtraStageCard);
        Assert.Contains(second, me.Trash);

        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.Equal(2, snapshot.GetProperty("my").GetProperty("stages").GetArrayLength());
        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(third.Id.ToString(), privateSnapshot.GetProperty("players")[0]
            .GetProperty("extraStage").GetProperty("id").GetString());
    }

    private static GameState HexState(string? leader = null)
    {
        var state = TestScene.New(leader).Build();
        state.MatchKind = MatchKind.Hex;
        HexRules.Initialize(state);
        state.OpeningStage = OpeningStage.Playing;
        return state;
    }

    private static GameEngine CreateEngine(int seed = 20260901)
    {
        TestScene.New();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 50));
        var engine = new GameEngine(
            $"hex-effects-{seed}-{Guid.NewGuid():N}",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: seed,
            matchKind: MatchKind.Hex);
        engine.State.OpeningStage = OpeningStage.Playing;
        engine.State.CurrentTurnPlayer = 0;
        engine.State.TurnCount = 3;
        engine.State.Phase = Phase.Main;
        engine.State.HexState.ActiveDraft = null;
        engine.State.HexState.DraftResolving = false;
        return engine;
    }

    private static void ClearZones(GameState state)
    {
        foreach (var player in state.Players)
        {
            player.Hand.Clear();
            player.Deck.Clear();
            player.Trash.Clear();
            player.LifeArea.Clear();
            player.Characters.Clear();
            player.StageCard = null;
            player.ExtraStageCard = null;
            player.CostArea.Clear();
            player.DonDeck.Clear();
        }
    }

    private static void OwnOnly(GameState state, int player, params int[] ids)
    {
        state.HexState.Owned[player].Clear();
        state.HexState.Owned[player].AddRange(ids);
    }

    private static void Own(GameState state, int player, params int[] ids)
    {
        foreach (var id in ids)
            if (!state.HexState.Owned[player].Contains(id)) state.HexState.Owned[player].Add(id);
    }

    private static void OwnOnlyAdd(GameState state, int player, params int[] ids)
        => Own(state, player, ids);

    private static CardInstance DbCard(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance Card(
        string number,
        CardKind kind,
        int power = 0,
        int cost = 0,
        int counter = 0)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "红",
                Kind = kind,
                Property = "打",
                Power = power,
                Cost = cost,
                Counter = counter,
            },
        };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static async Task ResolvePromptsUntilComplete(GameEngine engine, Task task, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            if (engine.State.PendingPrompt is { } prompt)
            {
                var chosen = prompt.ValidChoices.Take(Math.Max(prompt.MinChoose, 1)).ToArray();
                Assert.True(engine.HandleAction(prompt.PlayerIndex, "PromptResponse", Json(new
                {
                    promptId = prompt.PromptId,
                    chosen,
                })));
            }
            else
            {
                await Task.Delay(5);
            }
        }
        if (!task.IsCompleted) throw new TimeoutException("等待海克斯效果 Prompt 结算超时");
        await task;
        await engine.WaitSettledAsync();
    }
}
