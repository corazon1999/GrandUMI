using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Actions;
using GrandUMI.Game.AI;
using GrandUMI.Game.Snapshot;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class LegalActionSetTests
{
    [Fact]
    public void 主阶段枚举_全部候选通过同源校验_且无副作用与顺序稳定()
    {
        var state = TestScene.New("OP15-001", "OP15-001")
            .MyHandAdd("OP15-003")
            .MyActiveDon(5)
            .MyCharacter("OP15-003")
            .OppCharacter("OP15-003")
            .Build();
        PreparePlayingState(state, actor: 0, turnCount: 3);
        state.Players[1].Characters[0].IsTapped = true;
        var before = JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(state));
        var randomSeq = state.RandomSeq;

        var first = LegalActionService.Enumerate(state, 0, LegalActionPurpose.Training);
        var second = LegalActionService.Enumerate(state, 0, LegalActionPurpose.Training);

        Assert.NotEmpty(first.Candidates);
        Assert.Equal(first.StableHash, second.StableHash);
        Assert.Equal(first.Candidates.Select(candidate => candidate.ActionId),
            second.Candidates.Select(candidate => candidate.ActionId));
        Assert.All(first.Mask.Bits, bit => Assert.Equal(1, bit));
        Assert.Equal(first.Candidates.Count, first.Mask.Bits.Count);
        foreach (var candidate in first.Candidates.Where(candidate => !candidate.IsParameterized))
        {
            var validation = LegalActionService.Validate(state, 0, candidate.Action, candidate.Data);
            Assert.True(validation.Ok, $"{candidate.Action}/{candidate.Data.GetRawText()}：{validation.Reason}");
        }

        Assert.Equal(before, JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(state)));
        Assert.Equal(randomSeq, state.RandomSeq);
    }

    [Theory]
    [InlineData("Option", 1, 1)]
    [InlineData("Card", 0, 1)]
    [InlineData("Order", 2, 2)]
    [InlineData("Cost", 2, 3)]
    [InlineData("LifeTrigger", 1, 1)]
    public void 所有Prompt类型_由统一约束枚举并拒绝伪造选择(string kind, int min, int max)
    {
        var state = TestScene.New().Build();
        state.PendingPrompt = new PendingPrompt
        {
            PromptId = "p-stable",
            PlayerIndex = 0,
            Kind = kind,
            ValidChoices = ["a", "b", "c"],
            MinChoose = min,
            MaxChoose = max,
        };

        var set = LegalActionService.Enumerate(state, 0, LegalActionPurpose.Training);
        Assert.NotEmpty(set.Candidates);
        Assert.All(set.Candidates, candidate => Assert.Equal("PromptResponse", candidate.Action));

        var parameterized = set.Candidates.FirstOrDefault(candidate => candidate.IsParameterized);
        if (max > 1)
        {
            Assert.NotNull(parameterized);
            Assert.True(set.TryMaterialize(
                Array.IndexOf(set.Candidates.ToArray(), parameterized!),
                new[] { "a", "b" }.Take(min).ToArray(),
                out var action,
                out var data,
                out var reason), reason);
            Assert.True(LegalActionService.Validate(state, 0, action, data).Ok);
            Assert.False(set.TryMaterialize(
                Array.IndexOf(set.Candidates.ToArray(), parameterized!),
                Enumerable.Repeat("a", min).ToArray(),
                out _, out _, out _));
        }

        var forged = JsonSerializer.SerializeToElement(new
        {
            promptId = "p-stable",
            chosen = new[] { "forged" },
        });
        Assert.False(LegalActionService.Validate(state, 0, "PromptResponse", forged).Ok);
    }

    [Fact]
    public void 历史Accepted动作_只有语义命中合法集合才覆盖()
    {
        var state = TestScene.New().Build();
        state.PendingPrompt = new PendingPrompt
        {
            PromptId = "p1",
            PlayerIndex = 0,
            Kind = "Option",
            ValidChoices = ["0", "1"],
            MinChoose = 1,
            MaxChoose = 1,
        };
        var set = LegalActionService.Enumerate(state, 0, LegalActionPurpose.Training);
        var accepted = JsonSerializer.SerializeToElement(new { promptId = "p1", chosen = new[] { "1" } });
        var invalid = JsonSerializer.SerializeToElement(new { promptId = "p1", chosen = new[] { "2" } });

        Assert.True(LegalActionService.Contains(set, "PromptResponse", accepted, out var actionId, out _));
        Assert.StartsWith("sha256:", actionId);
        Assert.False(LegalActionService.Contains(set, "PromptResponse", invalid, out _, out var reason));
        Assert.Equal("accepted_action_not_in_legal_set", reason);
    }

    [Fact]
    public void Observation_不含身份和隐藏区_同语义换座哈希一致()
    {
        var first = SymmetricState(actor: 0, suffix: "a");
        var mirrored = SymmetricState(actor: 1, suffix: "b");
        first.Players[0].AccountName = "secret-account-a";
        first.Players[0].SessionId = "secret-session-a";
        first.Players[0].DisplayName = "secret-display-a";
        first.Players[1].AccountName = "secret-account-b";
        first.Players[1].SessionId = "secret-session-b";
        first.Players[1].DisplayName = "secret-display-b";

        var observation = TrainingObservationBuilder.Build(first, 0);
        var mirrorObservation = TrainingObservationBuilder.Build(mirrored, 1);
        var json = observation.Payload.GetRawText();

        Assert.DoesNotContain("secret-account", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-session", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-display", json, StringComparison.Ordinal);
        Assert.DoesNotContain("replayHands", json, StringComparison.Ordinal);
        Assert.Equal(1, observation.Payload.GetProperty("opponent").GetProperty("handCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, observation.Payload.GetProperty("opponent").GetProperty("hand").ValueKind);
        Assert.Equal(observation.StableHash, TrainingObservationBuilder.Build(first, 0).StableHash);
        Assert.Equal(observation.StableHash, mirrorObservation.StableHash);
    }

    [Fact]
    public async Task 模型越界与超时_都只从当前Mask使用确定性兜底()
    {
        var state = TestScene.New().Build();
        PreparePlayingState(state, actor: 0, turnCount: 3);
        var fallback = new DeterministicSafePolicy();

        var invalid = await AiDecisionCoordinator.DecideAsync(
            state, 0, new InvalidIndexPolicy(), fallback, TimeSpan.FromMilliseconds(50));
        Assert.NotNull(invalid);
        Assert.True(invalid.UsedFallback);
        Assert.True(LegalActionService.Validate(state, 0, invalid.Action, invalid.Data).Ok);

        var timeout = await AiDecisionCoordinator.DecideAsync(
            state, 0, new TimeoutPolicy(), fallback, TimeSpan.FromMilliseconds(20));
        Assert.NotNull(timeout);
        Assert.True(timeout.UsedFallback);
        Assert.Equal("model_timeout", timeout.FallbackReason);
        Assert.True(LegalActionService.Validate(state, 0, timeout.Action, timeout.Data).Ok);
    }

    [Fact(Timeout = 120_000)]
    public async Task Synthetic模型_双方只走LegalActionSet可完整结束一局()
    {
        TestScene.New();
        var deck = BuildLegalDeck("OP15-001");
        var engine = new GameEngine(
            "synthetic-self-play",
            ("s0", "synthetic-p0", deck),
            ("s1", "synthetic-p1", deck),
            firstPlayer: 0,
            rngSeed: 20260829);
        var policy = new SyntheticBaselinePolicy();
        var fallback = new DeterministicSafePolicy();
        var decisions = 0;

        while (!engine.State.IsGameOver && decisions < 1_000)
        {
            await engine.WaitSettledAsync();
            var actor = DecisionActor(engine.State);
            Assert.InRange(actor, 0, 1);
            var decision = await AiDecisionCoordinator.DecideAsync(
                engine.State,
                actor,
                policy,
                fallback,
                TimeSpan.FromMilliseconds(200));
            Assert.NotNull(decision);
            Assert.True(LegalActionService.Validate(engine.State, actor, decision.Action, decision.Data).Ok);
            Assert.True(engine.HandleAction(
                actor,
                decision.Action,
                decision.Data,
                source: GameActionSource.System));
            decisions++;
        }

        await engine.WaitSettledAsync();
        Assert.True(engine.State.IsGameOver, $"决策 {decisions} 次后仍未结束");
        Assert.InRange(decisions, 1, 999);
    }

    private static void PreparePlayingState(GameState state, int actor, int turnCount)
    {
        state.FirstPlayer = actor;
        state.CurrentTurnPlayer = actor;
        state.TurnCount = turnCount;
        state.Phase = Phase.Main;
        state.OpeningStage = OpeningStage.Playing;
        state.Players[0].MulliganDone = true;
        state.Players[1].MulliganDone = true;
    }

    private static GameState SymmetricState(int actor, string suffix)
    {
        var state = TestScene.New("OP15-001", "OP15-001").Build();
        state.Players[0].Hand.Add(new CardInstance { Info = CardDatabase.Get("OP15-003")! });
        state.Players[1].Hand.Add(new CardInstance { Info = CardDatabase.Get("OP15-003")! });
        state.Players[0].Deck.Add(new CardInstance { Info = CardDatabase.Get("OP15-003")! });
        state.Players[1].Deck.Add(new CardInstance { Info = CardDatabase.Get("OP15-003")! });
        PreparePlayingState(state, actor, 3);
        return state;
    }

    private static int DecisionActor(GameState state)
    {
        if (state.PendingPrompt is { } prompt) return prompt.PlayerIndex;
        if (!state.StartingPlayerChosen) return state.StartingPlayerChooser;
        if (!state.Players[0].MulliganDone) return 0;
        if (!state.Players[1].MulliganDone) return 1;
        if (state.CurrentBattle is { } battle
            && state.Phase is Phase.BattleBlock or Phase.BattleCounter)
            return battle.DefenderPlayerIndex;
        return state.CurrentTurnPlayer;
    }

    private static string BuildLegalDeck(string leaderNumber)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet("OP15")
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToList();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>();
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Count];
            if (counts.GetValueOrDefault(card.Number) >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = counts.GetValueOrDefault(card.Number) + 1;
        }
        return string.Join('\n', lines);
    }

    private sealed class InvalidIndexPolicy : IAiPolicy
    {
        public string PolicyId => "invalid";
        public string ModelHash => "invalid";
        public ValueTask<AiPolicySelection> SelectAsync(AiPolicyContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new AiPolicySelection(-1, null, PolicyId, ModelHash));
    }

    private sealed class TimeoutPolicy : IAiPolicy
    {
        public string PolicyId => "timeout";
        public string ModelHash => "timeout";
        public async ValueTask<AiPolicySelection> SelectAsync(
            AiPolicyContext context,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("取消后不应继续执行");
        }
    }
}
