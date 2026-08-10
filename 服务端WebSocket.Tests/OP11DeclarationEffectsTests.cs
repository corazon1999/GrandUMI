using System.Collections.Concurrent;
using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Validation;
using Xunit;

namespace GrandUMI.Tests;

public class OP11DeclarationEffectsTests
{
    private static CardInstance Card(string number) => new() { Info = CardDatabase.Get(number)! };

    [Theory]
    [InlineData("OP11-066", true)]
    [InlineData("OP11-066", false)]
    [InlineData("OP11-071", true)]
    [InlineData("OP11-071", false)]
    [InlineData("OP11-073", true)]
    [InlineData("OP11-073", false)]
    [InlineData("OP11-074", true)]
    [InlineData("OP11-074", false)]
    [InlineData("OP11-079", true)]
    [InlineData("OP11-079", false)]
    [InlineData("OP11-081", true)]
    [InlineData("OP11-081", false)]
    public async Task DeclaredCostEffect_RevealsOpponentDeckTopToBothPlayers_AndResolvesByMatch(
        string cardNumber,
        bool isMatch)
    {
        var scenario = CreateScenario(cardNumber);
        int declaredCost = isMatch ? scenario.Top.Info.Cost : (scenario.Top.Info.Cost + 1) % 11;

        await ResolveWithAutomaticAnswers(
            scenario.Engine,
            scenario.Source,
            TriggerOf(cardNumber),
            declaredCost,
            scenario.Target);

        Assert.Equal(scenario.OriginalOpponentDeckOrder,
            scenario.Opponent.Deck.Select(card => card.Id));
        AssertPublicReveal(scenario.Messages[0], scenario.Top.Info.Number);
        AssertPublicReveal(scenario.Messages[1], scenario.Top.Info.Number);
        AssertOutcome(scenario, cardNumber, isMatch);
    }

    [Theory]
    [InlineData("OP11-062", true)]
    [InlineData("OP01-001", false)]
    public async Task OP11_073_GrantsRushOnlyWithBigMomPiratesLeader(
        string leaderNumber,
        bool shouldHaveRush)
    {
        var state = TestScene.New(leaderNumber).Build();
        state.TurnCount = 3;
        var source = Card("OP11-073");
        source.TurnPlayed = state.TurnCount;
        state.Players[0].Characters.Add(source);

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(shouldHaveRush, state.HasContinuousKeyword(source, "速攻"));
        Assert.Equal(shouldHaveRush,
            ActionValidator.CanAttack(state, 0, source.Id, targetIsLeader: true, targetId: null).Ok);

        source.IsEffectsNullified = true;
        Assert.False(state.HasContinuousKeyword(source, "速攻"));
    }

    private static DeclarationScenario CreateScenario(string cardNumber)
    {
        const string deck = "OP01-001\nOP11-062\nOP11-074\nOP11-081\nOP15-001\nOP15-002";
        var engine = new GameEngine($"op11-declaration-{cardNumber}",
            ("s0", "alice", deck), ("s1", "bob", deck), 0, 1100 + int.Parse(cardNumber[^3..]));
        var messages = new[] { new ConcurrentQueue<string>(), new ConcurrentQueue<string>() };
        engine.OnSendToPlayer = (playerIndex, payload) =>
            messages[playerIndex].Enqueue(JsonSerializer.Serialize(payload));

        var me = engine.State.Players[0];
        var opponent = engine.State.Players[1];
        me.Hand.Clear();
        me.Deck.Clear();
        me.Trash.Clear();
        me.Characters.Clear();
        me.CostArea.Clear();
        me.DonDeck.Clear();
        opponent.Deck.Clear();
        opponent.Trash.Clear();
        opponent.Characters.Clear();

        var source = Card(cardNumber);
        if (source.Info.Kind == CardKind.Character)
            me.Characters.Add(source);

        var top = Card("OP11-074");
        var second = Card("OP11-071");
        opponent.Deck.AddRange([top, second]);
        var originalOpponentDeckOrder = opponent.Deck.Select(card => card.Id).ToArray();

        var target = Card("OP11-074");
        opponent.Characters.Add(target);

        var discarded = Card("OP11-074");
        var drawn = Card("OP11-071");
        me.Hand.Add(discarded);
        me.Deck.Add(drawn);

        for (int i = 0; i < 5; i++)
            me.CostArea.Add(new DonCard { State = DonState.Active });
        for (int i = 0; i < 2; i++)
            me.DonDeck.Add(new DonCard { State = DonState.InDeck });

        return new DeclarationScenario(
            engine, me, opponent, source, top, target, discarded, drawn,
            originalOpponentDeckOrder, messages);
    }

    private static EffectTrigger TriggerOf(string cardNumber) => cardNumber switch
    {
        "OP11-066" or "OP11-071" or "OP11-074" => EffectTrigger.ActivatedMain,
        "OP11-073" => EffectTrigger.OnOppAttackDeclare,
        "OP11-079" => EffectTrigger.EventCounter,
        "OP11-081" => EffectTrigger.EventMain,
        _ => throw new ArgumentOutOfRangeException(nameof(cardNumber)),
    };

    private static async Task ResolveWithAutomaticAnswers(
        GameEngine engine,
        CardInstance source,
        EffectTrigger trigger,
        int declaredCost,
        CardInstance target)
    {
        var handledPromptIds = new HashSet<string>();
        var resolveTask = EffectRuntime.Resolve(engine.State, 0, source, trigger, engine.Prompts);

        for (int i = 0; i < 500 && !resolveTask.IsCompleted; i++)
        {
            if (engine.State.PendingPrompt is { } prompt
                && handledPromptIds.Add(prompt.PromptId))
            {
                IReadOnlyList<string> chosen = prompt.Kind switch
                {
                    "Option" when prompt.PromptText == "宣言任意的费用" =>
                        new[] { declaredCost.ToString() },
                    "Option" => new[] { "0" },
                    "ReturnOwnDon" => prompt.ValidChoices.Take(prompt.MaxChoose).ToArray(),
                    "OwnHandDiscard" => prompt.ValidChoices.Take(1).ToArray(),
                    "OpponentCharacter" => new[] { target.Id.ToString() },
                    "OwnLeaderOrCharacter" => new[] { engine.State.Players[0].Leader.Id.ToString() },
                    _ => prompt.ValidChoices.Take(prompt.MinChoose).ToArray(),
                };
                engine.Prompts.Resolve(prompt.PromptId, chosen);
            }
            await Task.Delay(2);
        }

        await resolveTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static void AssertPublicReveal(ConcurrentQueue<string> messages, string expectedNumber)
    {
        var revealMessages = messages
            .Select(message => JsonDocument.Parse(message))
            .Where(document => document.RootElement.TryGetProperty("lastAction", out var action)
                && action.GetString() == "RevealCards")
            .ToList();
        try
        {
            var revealMessage = Assert.Single(revealMessages);
            var revealedNumbers = revealMessage.RootElement
                .GetProperty("reveal")
                .GetProperty("cardNumbers")
                .EnumerateArray()
                .Select(value => value.GetString())
                .ToArray();
            Assert.Equal(new[] { expectedNumber }, revealedNumbers);
        }
        finally
        {
            foreach (var document in revealMessages) document.Dispose();
        }
    }

    private static void AssertOutcome(DeclarationScenario scenario, string cardNumber, bool isMatch)
    {
        switch (cardNumber)
        {
            case "OP11-066":
                Assert.Equal(isMatch, scenario.Opponent.Trash.Contains(scenario.Target));
                Assert.Single(scenario.Me.CostArea.Where(don => don.State == DonState.Rest));
                break;
            case "OP11-071":
                Assert.Contains(scenario.Discarded, scenario.Me.Trash);
                Assert.Equal(isMatch, scenario.Me.Hand.Contains(scenario.Drawn));
                Assert.Equal(isMatch ? 6 : 5, scenario.Me.ActiveDonCount);
                break;
            case "OP11-073":
                Assert.Equal(isMatch ? 2000 : 0, scenario.Me.Leader.PowerModThisTurn);
                Assert.Empty(scenario.Me.CostArea);
                break;
            case "OP11-074":
                Assert.Equal(isMatch, scenario.Target.IsTapped);
                Assert.True(scenario.Source.IsTapped);
                Assert.Equal(4, scenario.Me.ActiveDonCount);
                break;
            case "OP11-079":
                Assert.Equal(isMatch ? 5000 : 0, scenario.Me.Leader.PowerModThisBattle);
                break;
            case "OP11-081":
                Assert.Equal(isMatch, scenario.Opponent.Trash.Contains(scenario.Target));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cardNumber));
        }
    }

    private sealed record DeclarationScenario(
        GameEngine Engine,
        PlayerState Me,
        PlayerState Opponent,
        CardInstance Source,
        CardInstance Top,
        CardInstance Target,
        CardInstance Discarded,
        CardInstance Drawn,
        Guid[] OriginalOpponentDeckOrder,
        ConcurrentQueue<string>[] Messages);
}
