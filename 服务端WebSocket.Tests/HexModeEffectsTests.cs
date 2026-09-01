using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using GrandUMI.Game.PhaseFlow;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
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
        [24] = "OnAttackDeclared", [25] = "OnAttackDeclared", [26] = "ResolveDamage",
        [27] = "ResolveDamage", [28] = "OnCardPlayed", [29] = "OnCardPlayed", [30] = "StageSlots",
        [31] = "OnLifeAdded", [32] = "OnCardPlayed", [33] = "OnTurnStarted", [34] = "Power/AttackTarget",
        [35] = "OnCardPlayed", [36] = "HandCost", [37] = "HandCost", [38] = "Draw/Power",
        [39] = "Draw/HandCost", [40] = "OnTurnEnding", [41] = "OnEnemyAffected",
        [42] = "OnEnemyAffected", [43] = "OnLifeAdded", [44] = "OnCharacterKo/OnLeaderDamaged",
        [45] = "AttackLimit/BattleBonus", [46] = "HandCost/EffectRuntime.Copy", [47] = "OnAcquire",
        [48] = "EnemyLifeOne", [49] = "OnAcquire", [50] = "OnAttackDeclared", [51] = "CounterBonus",
        [52] = "OnAcquire/DonLimit", [53] = "AttackTarget", [54] = "OnCharacterKo",
    };

    public static IEnumerable<object[]> AllHexIds()
        => Enumerable.Range(1, 54).Select(id => new object[] { id });

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
        Assert.Equal(2, state.HandPlayCost(0, eventCard));
        OwnOnly(state, 0, 39);
        Assert.Equal(1, state.HandPlayCost(0, eventCard));
        OwnOnly(state, 0, 46);
        Assert.Equal(6, state.HandPlayCost(0, eventCard));
        OwnOnly(state, 0, 51);
        Assert.Equal(2000, HexRules.CounterBonus(state, 0, eventCard));
        OwnOnly(state, 0, 52);
        Assert.Equal(12, state.MaxDonInCostAreaFor(0));

        opponent.LifeArea.Clear();
        var shield = Card("HEX-SHIELD", CardKind.Character, power: 1000, cost: 1);
        shield.IsTapped = true;
        opponent.Characters.Add(shield);
        OwnOnly(state, 1, 53);
        Assert.False(ActionValidator.CanAttack(state, 0, me.Leader.Id, true, null).Ok);
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
        me.Deck.Add(Card("HEX-DRAW", CardKind.Character));
        var discounted = Card("HEX-E2", CardKind.Event, cost: 2);
        me.Hand.Add(discounted);
        me.CostArea.AddRange(Enumerable.Range(0, 4).Select(_ => new DonCard { State = DonState.Rest }));
        var eventCard = Card("HEX-E3", CardKind.Event, cost: 3);
        await HexRules.OnCardPlayedAsync(engine, 0, new PlayResult(PlayKind.Event, eventCard, 3));
        Assert.Single(me.Hand.Where(card => card.Info.Number == "HEX-DRAW"));
        Assert.Equal(1000, me.Leader.PowerModThisTurn);
        Assert.Equal(-1, discounted.CostModThisTurn);
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
        HexRules.OnTurnStarted(state, 0);
        Assert.Single(opponent.Characters.Where(card => card.PowerModThisTurn == -2000));

        OwnOnly(state, 0, 40);
        opponent.Characters[0].IsTapped = false;
        opponent.Characters[1].IsTapped = true;
        HexRules.OnTurnEnding(state, 0);
        Assert.Equal(-1000, opponent.Characters[0].PowerModPersistent);
        Assert.Equal(0, opponent.Characters[1].PowerModPersistent);
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
        Assert.Equal(1500, me.Leader.PowerModThisTurn);
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
        me.Deck.AddRange(Enumerable.Range(0, 9).Select(i => Card($"HEX-MILL-{i}", CardKind.Character)));
        await EffectRuntime.Resolve(state, 0, enterSource, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(6, me.Trash.Count);
        await EffectRuntime.Resolve(state, 0, enterSource, EffectTrigger.OnEnterField, new MockPromptService());
        Assert.Equal(9, me.Trash.Count);

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
        await EffectRuntime.Resolve(state, 0, eventSource, EffectTrigger.EventMain, new MockPromptService());
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
    public async Task 复制登场时_空候选静默返回不消耗首次机会()
    {
        var state = HexState();
        var me = state.Players[0];
        OwnOnly(state, 0, 16);

        // OP01-005 没有合法废弃区目标时会在脚本内静默返回。
        var silentSource = DbCard("OP01-005");
        await EffectRuntime.Resolve(
            state, 0, silentSource, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.False(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);

        // 后续真正发动的登场时仍应取得本回合第一次复制。
        var actualSource = DbCard("EB03-047");
        me.Deck.Clear();
        me.Deck.AddRange(Enumerable.Range(0, 6)
            .Select(i => Card($"HEX-ENTER-ACTUAL-{i}", CardKind.Character)));
        await EffectRuntime.Resolve(
            state, 0, actualSource, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);
        Assert.Equal(6, me.Trash.Count);
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

        var astralTask = HexRules.ApplyOnAcquireAsync(engine, 0, 6);
        await ResolvePromptsUntilComplete(engine, astralTask);
        Assert.Empty(me.Hand);
        Assert.Equal(2, me.LifeArea.Count);

        me.Deck.AddRange(Enumerable.Range(0, 30).Select(i => Card($"HEX-ACQ-{i}", CardKind.Character)));
        int lifeBefore = me.LifeArea.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 9);
        Assert.Equal(lifeBefore + 1, me.LifeArea.Count);
        Assert.Equal(1000, me.Leader.PowerModPersistent);

        await HexRules.ApplyOnAcquireAsync(engine, 0, 20);
        Assert.Single(me.Hand);
        Assert.Equal(lifeBefore, me.LifeArea.Count);

        int handBefore = me.Hand.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 49);
        Assert.Equal(handBefore + 3, me.Hand.Count);
        int donBefore = me.DonDeck.Count;
        await HexRules.ApplyOnAcquireAsync(engine, 0, 52);
        Assert.Equal(donBefore + 2, me.DonDeck.Count);

        state.HexState.Owned[0].Clear();
        state.HexState.Owned[0].Add(47);
        int ownedBefore = state.HexState.Owned[0].Count;
        var chaosTask = HexRules.ApplyOnAcquireAsync(engine, 0, 47);
        await ResolvePromptsUntilComplete(engine, chaosTask);
        Assert.Equal(ownedBefore + 2, state.HexState.Owned[0].Count);
        Assert.Equal(state.HexState.Owned[0].Count, state.HexState.Owned[0].Distinct().Count());
        Assert.Equal(1, state.HexState.Owned[0].Count(id => id == 47));
    }

    [Fact]
    public void 抽牌类型转换_每类每回合只转换第一张且继续补抽()
    {
        var state = HexState();
        var me = state.Players[0];
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
