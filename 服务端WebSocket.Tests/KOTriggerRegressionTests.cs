using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>KO 入口统一及缺失【KO时】卡效的回归测试。</summary>
public class KOTriggerRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    [Fact]
    public async Task OP16_109_BattleKO_DrawsAndKOsUpToTwoCostOneCharacters()
    {
        var state = TestScene.New("OP16-080")
            .MyCharacter("OP16-109")
            .OppCharacter("OP09-095")
            .OppCharacter("OP09-095")
            .MyDeckTop("OP15-003")
            .Build();
        var docQ = state.Players[0].Characters[0];

        await BattleEngine.KOCardAsync(state, 0, docQ, new MockPromptService());

        Assert.Single(state.Players[0].Hand);
        Assert.Empty(state.Players[1].Characters);
        Assert.Equal(2, state.Players[1].Trash.Count);
    }

    [Fact]
    public async Task OP16_109_DoesNothingWithoutBlackbeardLeader()
    {
        var state = TestScene.New("OP16-001")
            .MyCharacter("OP16-109")
            .OppCharacter("OP09-095")
            .MyDeckTop("OP15-003")
            .Build();
        var docQ = state.Players[0].Characters[0];

        await BattleEngine.KOCardAsync(state, 0, docQ, new MockPromptService());

        Assert.Empty(state.Players[0].Hand);
        Assert.Single(state.Players[1].Characters);
    }

    [Fact]
    public async Task LegacySynchronousEffectKO_StillResolvesVictimOnKO()
    {
        var state = TestScene.New("OP16-080", "OP16-080")
            .OppCharacter("OP09-095")
            .OppCharacter("OP16-109")
            .Build();
        var opponent = state.Players[1];
        opponent.Deck.Add(Card("OP15-003"));
        var nullifyTarget = opponent.Characters[0];
        var docQ = opponent.Characters[1];
        var prompts = new MockPromptService()
            .QueueChoose(nullifyTarget.Id.ToString())
            .QueueChoose(docQ.Id.ToString());

        await EffectRuntime.Resolve(state, 0, Card("OP16-119"),
            EffectTrigger.OnLifeRevealTrigger, prompts);

        Assert.DoesNotContain(docQ, opponent.Characters);
        Assert.Contains(docQ, opponent.Trash);
        Assert.Single(opponent.Hand);
        Assert.Empty(state.PendingKOEffects);
    }

    [Fact]
    public async Task OP17_063_NullifiesTargetBeforeEffectKO_AndPreventsOnKO()
    {
        var state = TestScene.New("OP17-039", "OP16-080")
            .MyActiveDon(1)
            .OppCharacter("OP16-109")
            .Build();
        var ged = Card("OP17-063");
        ged.TurnPlayed = state.TurnCount;
        state.Players[0].Characters.Add(ged);
        var returnedDon = state.Players[0].CostArea.Single();
        var victim = state.Players[1].Characters.Single();
        state.Players[1].Deck.Add(Card("OP15-003"));
        var prompts = new MockPromptService()
            .QueueChoose(returnedDon.Id.ToString())
            .QueueChoose(victim.Id.ToString());

        await EffectRuntime.Resolve(state, 0, ged, EffectTrigger.ActivatedMain, prompts);

        Assert.True(victim.IsEffectsNullified);
        Assert.Contains(victim, state.Players[1].Trash);
        Assert.Empty(state.Players[1].Hand);
    }

    [Fact]
    public async Task EffectBatchKO_PreservesEffectReasonForConditionalOnKO()
    {
        var state = TestScene.New().MyCharacter("EB01-057").Build();
        var victim = state.Players[0].Characters[0];
        state.Players[0].Deck.Add(Card("OP15-003"));

        await AtomicOps.KOCardsByEffectAsync(state, 0, new[] { victim },
            new MockPromptService(), actingSide: 1);

        Assert.Single(state.Players[0].LifeArea);
    }

    [Fact]
    public async Task LifeTriggerThatInvokesOnKO_ReusesOnKOImplementation()
    {
        _ = TestScene.New().Build();
        string deck = "OP16-080\n" + string.Join('\n', Enumerable.Repeat("OP09-095", 10));
        var engine = new GameEngine("ko-life-trigger",
            ("s0", "p0", deck), ("s1", "p1", deck), firstPlayer: 0, rngSeed: 1);
        var player = engine.State.Players[0];
        player.Hand.Clear();
        player.LifeArea.Clear();
        player.Deck.Clear();
        var triggerCard = Card("OP16-102");
        player.LifeArea.Add(triggerCard);
        player.Deck.Add(Card("OP15-003"));

        var damageTask = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        for (int i = 0; i < 100 && engine.State.PendingPrompt is null; i++)
            await Task.Delay(10);
        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        engine.Prompts.Resolve(prompt.PromptId, new[] { "trigger" });
        await damageTask;

        Assert.Contains(triggerCard, player.Trash);
        Assert.Single(player.Hand);
    }

    [Fact]
    public async Task EB03_057_OnKO_CanTrashOpponentTopLife()
    {
        var state = TestScene.New().MyCharacter("EB03-057").Build();
        var yamato = state.Players[0].Characters[0];
        var life = Card("OP15-003");
        state.Players[1].LifeArea.Add(life);

        await BattleEngine.KOCardAsync(state, 0, yamato, new MockPromptService().QueueConfirm(true));

        Assert.Empty(state.Players[1].LifeArea);
        Assert.Contains(life, state.Players[1].Trash);
    }

    [Fact]
    public async Task OP15_048_OnOpponentTurnKO_ReturnsOpponentHandToDeckBottom()
    {
        var state = TestScene.New().MyCharacter("OP15-048").Build();
        state.CurrentTurnPlayer = 1;
        var chinJao = state.Players[0].Characters[0];
        var hand = Card("OP15-003");
        state.Players[1].Hand.Add(hand);

        await BattleEngine.KOCardAsync(state, 0, chinJao, new MockPromptService());

        Assert.Empty(state.Players[1].Hand);
        Assert.Same(hand, state.Players[1].Deck[^1]);
    }

    [Fact]
    public async Task OP15_063_OnKO_WithAtMostSixDon_KOsPowerTwoThousandCharacter()
    {
        var state = TestScene.New().MyCharacter("OP15-063").OppCharacter("OP09-095").Build();
        var gedatsu = state.Players[0].Characters[0];
        var target = state.Players[1].Characters[0];

        await BattleEngine.KOCardAsync(state, 0, gedatsu, new MockPromptService());

        Assert.DoesNotContain(target, state.Players[1].Characters);
        Assert.Contains(target, state.Players[1].Trash);
    }

    [Fact]
    public void PutInTrashIsNotKO_AndDoesNotResolveOnKO()
    {
        var state = TestScene.New("OP16-080").MyCharacter("OP16-109").MyDeckTop("OP15-003").Build();
        var docQ = state.Players[0].Characters[0];

        BattleEngine.KOCard(state, 0, docQ);

        Assert.Empty(state.Players[0].Hand);
        Assert.Empty(state.PendingKOEffects);
    }
}
