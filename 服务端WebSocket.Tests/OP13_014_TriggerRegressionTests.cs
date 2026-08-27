using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.PhaseFlow;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>OP13-014 生命【触发】真实引擎路径回归。</summary>
public class OP13_014_TriggerRegressionTests
{
    private static CardInstance Card(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static GameEngine NewEngine(string leaderNumber = "OP13-003")
    {
        _ = TestScene.New().Build();
        string deck = leaderNumber + "\n" + string.Join('\n', Enumerable.Repeat("OP15-003", 10));
        return new GameEngine(
            "op13-014-trigger-test",
            ("s0", "p0", deck),
            ("s1", "p1", deck),
            firstPlayer: 0,
            rngSeed: 1);
    }

    [Fact]
    public async Task DamageReveal_AcceptTriggerAndChooseAceLeader_AddsThreeThousandThisTurn()
    {
        var engine = NewEngine("OP13-002");
        var state = engine.State;
        var player = state.Players[0];
        var triggerCard = Card("OP13-014");
        var ace = player.Leader;
        player.LifeArea.Clear();
        player.Trash.Clear();
        player.Hand.Clear();
        player.Characters.Clear();
        player.LifeArea.Add(triggerCard);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        engine.Prompts.Resolve(lifePrompt.PromptId, ["trigger"]);
        var targetPrompt = await WaitForPrompt(engine, "OwnLeaderOrCharacter");

        Assert.Equal([ace.Id.ToString()], targetPrompt.ValidChoices);
        Assert.DoesNotContain(triggerCard, player.LifeArea);
        Assert.Contains(triggerCard, player.Trash);
        Assert.DoesNotContain(triggerCard, player.Hand);
        engine.Prompts.Resolve(targetPrompt.PromptId, [ace.Id.ToString()]);
        await damage;

        Assert.Contains(triggerCard, player.Trash);
        Assert.DoesNotContain(triggerCard, player.Hand);
        Assert.Equal(ace.Info.Power + 3000, state.CurrentPowerOf(0, ace));
        Assert.Null(state.PendingPrompt);

        TurnEngine.AdvanceTurn(state);

        Assert.Equal(ace.Info.Power, state.CurrentPowerOf(0, ace));
    }

    [Fact]
    public async Task DamageReveal_TargetCandidatesIncludeOnlyOwnFieldAceCards()
    {
        var engine = NewEngine();
        var state = engine.State;
        var player = state.Players[0];
        var opponent = state.Players[1];
        var triggerCard = Card("OP13-014");
        var ownAce = Card("OP13-119");
        var handAce = Card("OP16-094");
        var trashAce = Card("ST15-005");
        var opponentAce = Card("OP09-035");
        player.LifeArea.Clear();
        player.Trash.Clear();
        player.Hand.Clear();
        player.Characters.Clear();
        opponent.Characters.Clear();
        player.LifeArea.Add(triggerCard);
        player.Characters.Add(ownAce);
        player.Hand.Add(handAce);
        player.Trash.Add(trashAce);
        opponent.Characters.Add(opponentAce);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        engine.Prompts.Resolve(lifePrompt.PromptId, ["trigger"]);
        var targetPrompt = await WaitForPrompt(engine, "OwnLeaderOrCharacter");

        Assert.Equal([ownAce.Id.ToString()], targetPrompt.ValidChoices);
        engine.Prompts.Resolve(targetPrompt.PromptId, [ownAce.Id.ToString()]);
        await damage;

        Assert.Equal(ownAce.Info.Power + 3000, state.CurrentPowerOf(0, ownAce));
        Assert.Equal(handAce.Info.Power, handAce.CurrentPower(0, ownerTurn: true));
        Assert.Equal(trashAce.Info.Power, trashAce.CurrentPower(0, ownerTurn: true));
        Assert.Equal(opponentAce.Info.Power, state.CurrentPowerOf(1, opponentAce));
    }

    [Fact]
    public async Task DamageReveal_DeclineTrigger_AddsCardToHandWithoutBuffingAceLeader()
    {
        var engine = NewEngine("OP13-002");
        var player = engine.State.Players[0];
        var triggerCard = Card("OP13-014");
        player.LifeArea.Clear();
        player.Trash.Clear();
        player.Hand.Clear();
        player.LifeArea.Add(triggerCard);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        engine.Prompts.Resolve(lifePrompt.PromptId, ["hand"]);
        await damage;

        Assert.Contains(triggerCard, player.Hand);
        Assert.DoesNotContain(triggerCard, player.Trash);
        Assert.Equal(player.Leader.Info.Power, engine.State.CurrentPowerOf(0, player.Leader));
        Assert.Null(engine.State.PendingPrompt);
    }

    [Fact]
    public async Task DamageReveal_AcceptTriggerWithoutLegalTarget_ResolvesWithZeroTargets()
    {
        var engine = NewEngine();
        var player = engine.State.Players[0];
        var triggerCard = Card("OP13-014");
        player.LifeArea.Clear();
        player.Trash.Clear();
        player.Hand.Clear();
        player.Characters.Clear();
        player.LifeArea.Add(triggerCard);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 1);
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        engine.Prompts.Resolve(lifePrompt.PromptId, ["trigger"]);
        var targetPrompt = await WaitForPrompt(engine, "OwnLeaderOrCharacter");

        Assert.Empty(targetPrompt.ValidChoices);
        Assert.Equal(0, targetPrompt.MinChoose);
        Assert.Equal(1, targetPrompt.MaxChoose);
        engine.Prompts.Resolve(targetPrompt.PromptId, []);
        await damage;

        Assert.Contains(triggerCard, player.Trash);
        Assert.Empty(player.Hand);
        Assert.Null(engine.State.PendingPrompt);
    }

    [Fact]
    public async Task TwoRevealedCopies_CanBuffTheSameAceLeaderTwiceInOneTurn()
    {
        var engine = NewEngine("OP13-002");
        var state = engine.State;
        var player = state.Players[0];
        var firstTrigger = Card("OP13-014");
        var secondTrigger = Card("OP13-014");
        player.LifeArea.Clear();
        player.Trash.Clear();
        player.Hand.Clear();
        player.LifeArea.AddRange([firstTrigger, secondTrigger]);

        var damage = LifeRevealManager.DealDamageToLeader(engine, 0, 2);
        await ResolveLifeTriggerOn(engine, player.Leader);
        await ResolveLifeTriggerOn(engine, player.Leader);
        await damage;

        Assert.Contains(firstTrigger, player.Trash);
        Assert.Contains(secondTrigger, player.Trash);
        Assert.Equal(player.Leader.Info.Power + 6000, state.CurrentPowerOf(0, player.Leader));
        Assert.Null(state.PendingPrompt);
    }

    private static async Task ResolveLifeTriggerOn(GameEngine engine, CardInstance target)
    {
        var lifePrompt = await WaitForPrompt(engine, "LifeTrigger");
        engine.Prompts.Resolve(lifePrompt.PromptId, ["trigger"]);
        var targetPrompt = await WaitForPrompt(engine, "OwnLeaderOrCharacter");
        engine.Prompts.Resolve(targetPrompt.PromptId, [target.Id.ToString()]);
    }

    private static async Task<PendingPrompt> WaitForPrompt(GameEngine engine, string kind)
    {
        for (int i = 0; i < 100; i++)
        {
            if (engine.State.PendingPrompt is { } prompt && prompt.Kind == kind)
                return prompt;
            await Task.Delay(10);
        }

        throw new TimeoutException($"等待 {kind} 交互超时。");
    }
}
