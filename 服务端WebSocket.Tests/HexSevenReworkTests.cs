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

public sealed class HexSevenReworkTests
{
    [Fact]
    public async Task 面包按每位玩家本全局回合成功出牌序号减费并与灵巧共用同一计数()
    {
        var engine = NewEngine();
        var state = engine.State;
        ClearZones(state);
        var me = state.Players[0];
        OwnOnly(state, 0, 2, 36, 37, 38, 39);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        var first = Card("HEX-BREAD-FIRST", CardKind.Character, cost: 4);
        var second = Card("HEX-BREAD-SECOND", CardKind.Stage, cost: 3);
        var third = Card("HEX-BREAD-THIRD", CardKind.Event, cost: 4);
        var cleverDraw = Card("HEX-CLEVER-DRAW", CardKind.Event);
        me.Hand.AddRange([first, second, third]);
        me.Deck.Add(cleverDraw);

        Assert.Equal(2, state.HandPlayCost(0, first)); // 第1张 -1；两个转换对角色合计 -1。
        var firstResult = CardPlayer.Play(state, 0, 0);
        await HexRules.OnCardPlayedAsync(engine, 0, firstResult);
        Assert.Equal(2, firstResult.PaidCost);
        Assert.Equal(1, state.HexState.Runtime[0].CardsPlayedThisTurn);

        Assert.Equal(1, state.HandPlayCost(0, second)); // 第2张卡牌类型不限，-2。
        var secondResult = CardPlayer.Play(state, 0, 0);
        await HexRules.OnCardPlayedAsync(engine, 0, secondResult);
        Assert.Equal(1, secondResult.PaidCost);
        Assert.Equal(2, state.HexState.Runtime[0].CardsPlayedThisTurn);

        Assert.Equal(3, state.HandPlayCost(0, third)); // 第3张不再获得面包减费；两个转换对事件合计 -1。
        var thirdResult = CardPlayer.Play(state, 0, 0);
        await HexRules.OnCardPlayedAsync(engine, 0, thirdResult);
        Assert.Equal(3, state.HexState.Runtime[0].CardsPlayedThisTurn);
        Assert.Contains(cleverDraw, me.Hand);

        var failed = Card("HEX-BREAD-FAILED", CardKind.Character, cost: 10);
        me.Hand.Add(failed);
        Assert.False(ActionValidator.CanPlayCard(state, 0, me.Hand.IndexOf(failed)).Ok);
        Assert.Equal(3, state.HexState.Runtime[0].CardsPlayedThisTurn);

        HexRules.OnTurnStarted(state, 1);
        Assert.All(state.HexState.Runtime, runtime => Assert.Equal(0, runtime.CardsPlayedThisTurn));
    }

    [Fact]
    public async Task 对方回合反击事件计入防守方出牌序号而反击值弃牌与效果登场不计数()
    {
        var engine = NewEngine();
        var state = engine.State;
        ClearZones(state);
        var attacker = state.Players[0];
        var defender = state.Players[1];
        OwnOnly(state, 1, 36, 37);
        defender.CostArea.AddRange(Enumerable.Range(0, 6).Select(_ => new DonCard { State = DonState.Active }));
        var counterEvent = DbCard("EB03-038");
        var nextCard = Card("HEX-DEFENDER-SECOND", CardKind.Character, cost: 4);
        defender.Hand.AddRange([counterEvent, nextCard]);
        state.CurrentTurnPlayer = 0;
        state.Phase = Phase.BattleCounter;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = attacker.Leader.Id,
            TargetIsLeader = true,
        };

        Assert.True(engine.HandleAction(1, "PlayCounter", Json(new { handIndex = 0 })));
        await engine.WaitSettledAsync();
        Assert.Equal(1, state.HexState.Runtime[1].CardsPlayedThisTurn);
        Assert.Equal(2, state.HandPlayCost(1, nextCard));

        var effectEntry = Card("HEX-EFFECT-ENTRY", CardKind.Character);
        defender.Hand.Add(effectEntry);
        await AtomicOps.PlayFromHandFree(state, 1, effectEntry);
        Assert.Equal(1, state.HexState.Runtime[1].CardsPlayedThisTurn);

        OwnOnly(state, 1, 36, 37, 51);
        var counterIcon = Card("HEX-EVENT-COUNTER-ICON", CardKind.Event);
        defender.Hand.Add(counterIcon);
        Assert.True(engine.HandleAction(1, "PlayCounter", Json(new
        {
            handIndex = defender.Hand.IndexOf(counterIcon),
            useCounterIcon = true,
        })));
        Assert.Equal(1, state.HexState.Runtime[1].CardsPlayedThisTurn);
    }

    [Fact]
    public void 最终手牌费用先合并所有加减费再执行溢流与类型转换下限()
    {
        var engine = NewEngine();
        var state = engine.State;
        var card = Card("HEX-COST-ORDER", CardKind.Event, cost: 8);
        card.CostModThisTurn = -1;
        card.CostModPersistent = -1; // 可代表炼狱导管等永久实例减费。
        state.OneShotPlayDiscounts.Add(new OneShotPlayDiscount
        {
            Owner = 0,
            Amount = 2,
            MinCost = 0,
            Kind = "Event",
        });
        OwnOnly(state, 0, 36, 38, 39, 46);

        // 8-1-1-2（一次性）-1（第1张）+1-2（转换合并）=2，溢流后为4。
        Assert.Equal(4, state.HandPlayCost(0, card));

        card.CostModThisTurn = 0;
        card.CostModPersistent = 0;
        state.OneShotPlayDiscounts.Clear();
        OwnOnly(state, 0, 38, 46);
        Assert.Equal(10, state.HandPlayCost(0, card));

        var zeroEvent = Card("HEX-COST-FLOOR-EVENT", CardKind.Event, cost: 0);
        var zeroCharacter = Card("HEX-COST-FLOOR-CHAR", CardKind.Character, cost: 0);
        OwnOnly(state, 0, 38, 39, 46);
        Assert.Equal(1, state.HandPlayCost(0, zeroEvent));
        Assert.Equal(1, state.HandPlayCost(0, zeroCharacter));

        OwnOnly(state, 0, 36);
        Assert.Equal(0, state.HandPlayCost(0, zeroCharacter)); // 面包本身遵循系统通用 0 下限。
        state.HexState.Runtime[0].CardsPlayedThisTurn = 1;
        OwnOnly(state, 0, 37);
        Assert.Equal(0, state.HandPlayCost(0, zeroEvent));
    }

    [Fact]
    public void 新版转换不再替换抽牌或增加力量_旧修订保持原行为()
    {
        var current = NewState();
        var currentPlayer = current.Players[0];
        OwnOnly(current, 0, 38, 39);
        var currentEvent = Card("HEX-CURRENT-EVENT", CardKind.Event);
        var currentCharacter = Card("HEX-CURRENT-CHAR", CardKind.Character, power: 5000);
        currentPlayer.Deck.AddRange([currentEvent, currentCharacter]);

        Assert.Equal(2, TurnEngine.DrawCard(current, 0, 2));
        Assert.Equal([currentEvent, currentCharacter], currentPlayer.Hand);
        Assert.Empty(currentPlayer.Trash);
        Assert.False(current.HexState.Runtime[0].EventDrawConvertedThisTurn);
        Assert.False(current.HexState.Runtime[0].CharacterDrawConvertedThisTurn);
        currentPlayer.Characters.Add(currentCharacter);
        Assert.Equal(0, HexRules.PowerBonus(current, 0, currentCharacter));

        var legacy = NewState();
        HexRules.SetRulesRevisionForReplay(legacy, HexRules.PermanentCostFloorRulesRevision);
        var legacyPlayer = legacy.Players[0];
        OwnOnly(legacy, 0, 38, 39);
        var discardedEvent = Card("HEX-LEGACY-EVENT", CardKind.Event);
        var discardedCharacter = Card("HEX-LEGACY-CHAR", CardKind.Character, power: 5000);
        var keptEvent = Card("HEX-LEGACY-KEPT-EVENT", CardKind.Event);
        var keptCharacter = Card("HEX-LEGACY-KEPT-CHAR", CardKind.Character);
        legacyPlayer.Deck.AddRange([discardedEvent, discardedCharacter, keptEvent, keptCharacter]);

        Assert.Equal(2, TurnEngine.DrawCard(legacy, 0, 2));
        Assert.Equal([discardedEvent, discardedCharacter], legacyPlayer.Trash);
        Assert.Equal([keptEvent, keptCharacter], legacyPlayer.Hand);
        legacyPlayer.Characters.Add(discardedCharacter);
        Assert.Equal(1000, HexRules.PowerBonus(legacy, 0, discardedCharacter));
    }

    [Fact]
    public async Task 神射法师由服务端禁止两种事件发动但允许事件作为四千反击值弃牌()
    {
        var engine = NewEngine();
        var state = engine.State;
        ClearZones(state);
        var me = state.Players[0];
        var opponent = state.Players[1];
        OwnOnly(state, 0, 3, 51);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        var eventCard = DbCard("EB03-038");
        me.Hand.Add(eventCard);

        Assert.False(ActionValidator.CanPlayCard(state, 0, 0).Ok);
        var mainSnapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.False(mainSnapshot.GetProperty("my").GetProperty("handCardCanPlay")[0].GetBoolean());
        Assert.Equal(4000, mainSnapshot.GetProperty("my").GetProperty("handCardCounters")[0].GetInt32());

        state.CurrentTurnPlayer = 1;
        state.Phase = Phase.BattleCounter;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = opponent.Leader.Id,
            TargetIsLeader = true,
        };
        Assert.False(ActionValidator.CanPlayCounter(state, 0, 0, useCounterIcon: false).Ok);
        Assert.True(ActionValidator.CanPlayCounter(state, 0, 0, useCounterIcon: true).Ok);

        Assert.True(engine.HandleAction(0, "PlayCounter", Json(new { handIndex = 0, useCounterIcon = true })));
        await engine.WaitSettledAsync();

        Assert.DoesNotContain(eventCard, me.Hand);
        Assert.Contains(eventCard, me.Trash);
        Assert.Equal(4000, me.Leader.PowerModThisBattle);
        // 恰为神射法师提供的 4000；若 EB03-038 的【反击】效果也发动，会额外增加 3000。
        Assert.Equal(0, state.CurrentBattle!.DefenderBattleBonus);
        Assert.Equal(0, me.Leader.PowerModThisTurn); // 古式佳酿的“打出事件”钩子没有发动。
        Assert.Equal(0, state.HexState.Runtime[0].CardsPlayedThisTurn);
    }

    [Fact]
    public void 神射法师旧修订仍允许发动事件且只提供两千反击值()
    {
        var state = NewState();
        HexRules.SetRulesRevisionForReplay(state, HexRules.PermanentCostFloorRulesRevision);
        var me = state.Players[0];
        OwnOnly(state, 0, 51);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        var card = DbCard("EB03-038");
        me.Hand.Add(card);

        Assert.True(ActionValidator.CanPlayCard(state, 0, 0).Ok);
        Assert.Equal(2000, HandStaticCounter.Value(state, 0, card));
        state.Phase = Phase.BattleCounter;
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 1,
            DefenderPlayerIndex = 0,
            AttackerCardId = state.Players[1].Leader.Id,
            TargetIsLeader = true,
        };
        Assert.True(ActionValidator.CanPlayCounter(state, 0, 0, useCounterIcon: false).Ok);
    }

    [Fact]
    public async Task 天龙人动态修改双方场上角色费用_双方持有抵消且旧修订仍保护领袖()
    {
        var engine = NewEngine();
        var state = engine.State;
        ClearZones(state);
        var mine = Card("HEX-DRAGON-MINE", CardKind.Character, cost: 7);
        var enemy = Card("HEX-DRAGON-ENEMY", CardKind.Character, cost: 1);
        var hand = Card("HEX-DRAGON-HAND", CardKind.Character, cost: 3);
        state.Players[0].Characters.Add(mine);
        state.Players[0].Hand.Add(hand);
        state.Players[1].Characters.Add(enemy);
        OwnOnly(state, 0, 53);

        Assert.Equal(9, state.CurrentCostOf(0, mine));
        Assert.Equal(0, state.CurrentCostOf(1, enemy));
        Assert.Equal(3, state.HandPlayCost(0, hand));
        Assert.Equal(3, state.CurrentCostOf(0, hand));

        var publicSnapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0));
        Assert.Equal(9, publicSnapshot.GetProperty("my").GetProperty("fieldCards")[0].GetProperty("cost").GetInt32());
        Assert.Equal(0, publicSnapshot.GetProperty("opponent").GetProperty("fieldCards")[0].GetProperty("cost").GetInt32());
        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.Equal(9, privateSnapshot.GetProperty("players")[0].GetProperty("characters")[0]
            .GetProperty("currentCost").GetInt32());
        Assert.Equal(3, privateSnapshot.GetProperty("players")[0].GetProperty("hand")[0]
            .GetProperty("currentCost").GetInt32());

        OwnOnly(state, 1, 53);
        Assert.Equal(7, state.CurrentCostOf(0, mine));
        Assert.Equal(1, state.CurrentCostOf(1, enemy));

        OwnOnly(state, 0, 24);
        var target = Card("HEX-DRAGON-GIANT-TARGET", CardKind.Character, cost: 7);
        target.IsTapped = true;
        state.Players[1].Characters.Clear();
        state.Players[1].Characters.Add(target);
        OwnOnly(state, 1, 53);
        state.CurrentBattle = new BattleContext
        {
            AttackerPlayerIndex = 0,
            DefenderPlayerIndex = 1,
            AttackerCardId = mine.Id,
            TargetCardId = target.Id,
            TargetIsLeader = false,
        };
        await HexRules.OnAttackDeclaredAsync(engine, 0);
        Assert.Equal(3000, state.CurrentBattle.AttackerBattleBonus);

        var legacy = NewState();
        HexRules.SetRulesRevisionForReplay(legacy, HexRules.PermanentCostFloorRulesRevision);
        var shield = Card("HEX-LEGACY-DRAGON", CardKind.Character, cost: 1);
        shield.IsTapped = true;
        legacy.Players[1].Characters.Add(shield);
        legacy.Players[1].LifeArea.Clear();
        OwnOnly(legacy, 1, 53);
        Assert.Equal(1, legacy.CurrentCostOf(1, shield));
        Assert.False(ActionValidator.CanAttack(legacy, 0, legacy.Players[0].Leader.Id, true, null).Ok);
    }

    [Fact]
    public async Task 溢流从手牌事件顺序结算两次但出牌支付和事件监听只发生一次()
    {
        var engine = NewEngine();
        var state = engine.State;
        ClearZones(state);
        var me = state.Players[0];
        OwnOnly(state, 0, 3, 35, 46);
        me.CostArea.AddRange(Enumerable.Range(0, 10).Select(_ => new DonCard { State = DonState.Active }));
        var played = DbCard("OP15-019");
        var held = Card("HEX-OVERFLOW-HELD", CardKind.Event, cost: 5);
        me.Hand.AddRange([played, held]);
        me.Deck.AddRange([
            Card("HEX-OVERFLOW-DRAW-1", CardKind.Character),
            Card("HEX-OVERFLOW-DRAW-2", CardKind.Character),
        ]);
        int paidCost = state.HandPlayCost(0, played);

        Assert.True(engine.HandleAction(0, "PlayCard", Json(new { handIndex = 0 })));
        await engine.WaitSettledAsync();

        Assert.Equal(paidCost, me.CostArea.Count(don => don.State == DonState.Rest));
        Assert.Single(me.Trash.Where(card => ReferenceEquals(card, played)));
        Assert.Equal(1, state.HexState.Runtime[0].CardsPlayedThisTurn);
        Assert.Equal(-1, held.CostModPersistent);
        Assert.Equal(3000, me.Leader.PowerModThisTurn); // 古式佳酿一次 + 事件本体两次。
        Assert.Contains(me.Hand, card => card.Info.Number == "HEX-OVERFLOW-DRAW-1");
        Assert.Contains(me.Hand, card => card.Info.Number == "HEX-OVERFLOW-DRAW-2");
    }

    [Fact]
    public async Task 溢流第一次放弃可选效果后仍独立开始第二次并重新检查条件()
    {
        var state = NewState();
        ClearZones(state);
        OwnOnly(state, 0, 46);
        var me = state.Players[0];
        me.CostArea.AddRange([
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
        ]);
        var prompts = new MockPromptService().QueueConfirm(false).QueueConfirm(true);

        await EffectRuntime.ResolvePlayedEvent(
            state, 0, DbCard("EB03-038"), EffectTrigger.EventMain, prompts);

        Assert.Equal(2, prompts.ConfirmHistory.Count);
        Assert.Equal(1, me.CostArea.Count(don => don.State == DonState.Rest));
    }

    [Fact]
    public async Task 溢流第二次开始前已排空第一次产生的登场效果和提示链()
    {
        var state = NewState("OP01-001");
        ClearZones(state);
        OwnOnly(state, 0, 46);
        var me = state.Players[0];
        var firstField = Card("HEX-OVERFLOW-FIRST-FIELD", CardKind.Character, cost: 2, color: "红");
        var firstEntry = DbCard("EB03-047");
        me.Characters.Add(firstField);
        me.Hand.Add(firstEntry);
        me.Deck.AddRange(Enumerable.Range(0, 6)
            .Select(index => Card($"HEX-OVERFLOW-MILL-{index}", CardKind.Character)));
        var prompts = new MockPromptService()
            .QueueChoose(firstField.Id.ToString())
            .QueueChoose(firstEntry.Id.ToString())
            .QueueChoose(firstEntry.Id.ToString())
            .QueueChoose(firstField.Id.ToString());
        int responses = 0;
        prompts.OnChooseResponse = _ =>
        {
            responses++;
            if (responses != 3) return;
            Assert.Empty(state.PendingEnterFields);
            Assert.Equal(3, me.Trash.Count);
        };

        await EffectRuntime.ResolvePlayedEvent(
            state, 0, DbCard("EB01-020"), EffectTrigger.EventMain, prompts);

        Assert.Equal(4, responses);
        Assert.Empty(state.PendingEnterFields);
        Assert.Equal(3, me.Trash.Count);
        Assert.Contains(firstField, me.Characters);
    }

    [Fact]
    public async Task 溢流不复制被其他效果引用的事件且第一次终局时停止第二次()
    {
        var referenced = NewState();
        OwnOnly(referenced, 0, 46);
        referenced.Players[0].Deck.AddRange([
            Card("HEX-REFERENCED-DRAW-1", CardKind.Character),
            Card("HEX-REFERENCED-DRAW-2", CardKind.Character),
        ]);
        await EffectRuntime.Resolve(
            referenced, 0, DbCard("OP15-019"), EffectTrigger.EventMain, new MockPromptService());
        Assert.Single(referenced.Players[0].Hand);
        Assert.Equal(1000, referenced.Players[0].Leader.PowerModThisTurn);

        var gameOver = NewState();
        ClearZones(gameOver);
        OwnOnly(gameOver, 0, 46);
        gameOver.Players[1].Deck.Add(Card("HEX-OPPONENT-DECK", CardKind.Character));
        DeckOutRules.Arm(gameOver);
        await EffectRuntime.ResolvePlayedEvent(
            gameOver, 0, DbCard("OP15-019"), EffectTrigger.EventMain, new MockPromptService());
        Assert.True(gameOver.IsGameOver);
        // 首个“抽 1”已令对局终止，连第一次效果的后续加力都不会继续，更不会启动第二次完整结算。
        Assert.Equal(0, gameOver.Players[0].Leader.PowerModThisTurn);
    }

    [Fact]
    public void 修订十一计数和所有权进入私密快照与确定性检查点_修订十文案冻结()
    {
        var state = NewState();
        OwnOnly(state, 0, 36, 37, 38, 39, 46, 51, 53);
        state.HexState.Runtime[0].CardsPlayedThisTurn = 2;
        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var checkpoint = DeterministicReplayCheckpointProvider.BuildFullState(state);

        Assert.Equal(11, privateState.GetProperty("hexState").GetProperty("RulesRevision").GetInt32());
        Assert.Equal(2, privateState.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("CardsPlayedThisTurn").GetInt32());
        Assert.Equal(11, checkpoint.GetProperty("hexState").GetProperty("RulesRevision").GetInt32());
        Assert.Equal(2, checkpoint.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("CardsPlayedThisTurn").GetInt32());
        Assert.Equal(checkpoint.GetRawText(), DeterministicReplayCheckpointProvider.BuildFullState(state).GetRawText());

        Assert.Equal(
            "每回合抽到第1张事件时自动丢弃并抽1张；己方角色力量+1000。",
            HexCatalog.DescriptionForRevision(38, HexRules.PermanentCostFloorRulesRevision));
        Assert.Equal(
            "己方生命为0且存在休息角色时，对方不能攻击己方领袖。",
            HexCatalog.DescriptionForRevision(53, HexRules.PermanentCostFloorRulesRevision));
    }

    private static GameState NewState(string? leader = null)
    {
        var state = TestScene.New(leader).Build();
        state.MatchKind = MatchKind.Hex;
        HexRules.Initialize(state);
        state.OpeningStage = OpeningStage.Playing;
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        return state;
    }

    private static GameEngine NewEngine(int seed = 20260903)
    {
        TestScene.New();
        var deck = "OP15-001\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 50));
        var engine = new GameEngine(
            $"hex-seven-{seed}-{Guid.NewGuid():N}",
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

    private static CardInstance DbCard(string number) => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance Card(
        string number,
        CardKind kind,
        int power = 0,
        int cost = 0,
        int counter = 0,
        string color = "红")
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = color,
                Kind = kind,
                Property = "打",
                Power = power,
                Cost = cost,
                Counter = counter,
            },
        };

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

}
