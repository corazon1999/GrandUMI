using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using GrandUMI.Game.Snapshot;
using GrandUMI.Game.Validation;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>2026-09-03 已确认反馈第二批：四项卡牌/海克斯规则回归。</summary>
public sealed class ConfirmedFeedback20260903Batch2Tests
{
    [Fact]
    public async Task ST27_005_只KO当前费用不高于3的角色()
    {
        var state = TestScene.New().Build();
        var source = DbCard("ST27-005");
        state.Players[0].Characters.Add(source);
        var discounted = TestCard("TARGET-PRINTED-4", cost: 4);
        discounted.CostModThisTurn = -1;
        var raised = TestCard("TARGET-PRINTED-3", cost: 3);
        raised.CostModPersistent = 1;
        state.Players[1].Characters.AddRange([discounted, raised]);
        var prompts = new MockPromptService().QueueChoose(discounted.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, source, EffectTrigger.ActivatedMain, prompts);

        Assert.True(source.IsTapped);
        Assert.Equal(3, state.CurrentCostOf(1, discounted));
        Assert.Equal(4, state.CurrentCostOf(1, raised));
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Contains(discounted.Id.ToString(), prompt.choices);
        Assert.DoesNotContain(raised.Id.ToString(), prompt.choices);
        Assert.Contains(discounted, state.Players[1].Trash);
        Assert.Contains(raised, state.Players[1].Characters);
    }

    [Fact]
    public async Task EB04_013_领袖额外活跃且不占最多两张角色名额()
    {
        var state = TestScene.New("OP08-021").Build();
        var me = state.Players[0];
        me.Leader.IsTapped = true;
        var first = DbCard("EB04-013");
        var second = DbCard("OP08-022");
        var third = DbCard("OP08-023");
        first.IsTapped = second.IsTapped = third.IsTapped = true;
        me.Characters.AddRange([first, second, third]);
        // Mock 故意返回三张合法 ID，脚本仍须服从“最多 2 张”的服务端上限。
        var prompts = new MockPromptService()
            .QueueChoose(second.Id.ToString(), third.Id.ToString(), first.Id.ToString());

        await EffectRuntime.Resolve(
            state, 0, first, EffectTrigger.OnEnterField, prompts);

        Assert.False(me.Leader.IsTapped);
        Assert.False(second.IsTapped);
        Assert.False(third.IsTapped);
        Assert.True(first.IsTapped);
        var prompt = Assert.Single(prompts.ChooseHistory);
        Assert.Equal("OwnCharacter", prompt.kind);
        Assert.Equal(2, prompt.max);
        Assert.DoesNotContain(me.Leader.Id.ToString(), prompt.choices);
    }

    [Fact]
    public async Task OP13_040_动作入口同时校验实际出牌费_额外两咚和当前费用目标()
    {
        var state = TestScene.New().Build();
        var me = state.Players[0];
        var opponent = state.Players[1];
        var eventCard = DbCard("OP13-040");
        me.Hand.Add(eventCard);
        me.CostArea.AddRange(Enumerable.Range(0, 3)
            .Select(_ => new DonCard { State = DonState.Active }));

        Assert.False(ActionValidator.CanPlayCard(state, 0, 0).Ok);

        var activeTarget = TestCard("ACTIVE-COST-7", cost: 7);
        opponent.Characters.Add(activeTarget);
        Assert.False(ActionValidator.CanPlayCard(state, 0, 0).Ok);

        activeTarget.IsTapped = true;
        activeTarget.CostModPersistent = 1;
        Assert.False(ActionValidator.CanPlayCard(state, 0, 0).Ok);

        activeTarget.CostModPersistent = 0;
        Assert.True(ActionValidator.CanPlayCard(state, 0, 0).Ok);

        me.CostArea.RemoveAt(me.CostArea.Count - 1);
        Assert.False(ActionValidator.CanPlayCard(state, 0, 0).Ok);

        // 实际事件费用被减为 0 时，总要求随之变为 0+2，而不是仍按印刷费用 1 计算。
        eventCard.CostModThisTurn = -1;
        Assert.True(ActionValidator.CanPlayCard(state, 0, 0).Ok);

        var prompts = new MockPromptService()
            .QueueConfirm(true)
            .QueueChoose(activeTarget.Id.ToString());
        await EffectRuntime.Resolve(state, 0, eventCard, EffectTrigger.EventMain, prompts);

        Assert.Equal(0, me.ActiveDonCount);
        Assert.True(activeTarget.CannotActivateNextReset);
    }

    [Fact]
    public async Task 海克斯H16_旧规则拒绝可选登场时不消耗复制_下一张P107只复制首次实际发动()
    {
        var state = TestScene.New().Build();
        state.MatchKind = MatchKind.Hex;
        HexRules.Initialize(state);
        HexRules.SetRulesRevisionForReplay(state, HexRules.AstralBodyRulesRevision);
        state.HexState.Owned[0].Clear();
        state.HexState.Owned[0].Add(16);
        state.OperationClockEnabled = true;
        state.OperationClockRemainingMs[0] = 500_000;
        state.OperationTurnClockRemainingMs[0] = 200_000;
        state.Players[0].CostArea.AddRange(Enumerable.Range(0, 10)
            .Select(_ => new DonCard { State = DonState.Active }));
        state.Players[0].Trash.AddRange([
            TestCard("OPTIONAL-COST-A", cost: 1),
            TestCard("OPTIONAL-COST-B", cost: 1),
        ]);

        var decliningPrompt = new TransientClockPrompt(state, confirm: false);
        await EffectRuntime.Resolve(
            state, 0, DbCard("EB02-045"), EffectTrigger.OnEnterField, decliningPrompt);

        Assert.False(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);
        Assert.Equal(2, state.Players[0].Trash.Count);

        var firstRoger = DbCard("P-107");
        await EffectRuntime.Resolve(
            state, 0, firstRoger, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.True(state.HexState.Runtime[0].FirstEnterEffectCopiedThisTurn);
        Assert.Equal(4_000, state.Players[0].Leader.PowerModsUntilOppEnd.Sum(mod => mod.Delta));

        var secondRoger = DbCard("P-107");
        await EffectRuntime.Resolve(
            state, 0, secondRoger, EffectTrigger.OnEnterField, new MockPromptService());

        Assert.Equal(6_000, state.Players[0].Leader.PowerModsUntilOppEnd.Sum(mod => mod.Delta));

        // 私有恢复状态与确定性回放状态都必须保留“本回合首次已消耗”，防止重启后再次复制。
        var privateSnapshot = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        Assert.True(privateSnapshot.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("FirstEnterEffectCopiedThisTurn").GetBoolean());
        var replayState = DeterministicReplayCheckpointProvider.BuildFullState(state);
        Assert.True(replayState.GetProperty("hexState").GetProperty("runtime")[0]
            .GetProperty("FirstEnterEffectCopiedThisTurn").GetBoolean());
    }

    private static CardInstance DbCard(string number)
        => new() { Info = CardDatabase.Get(number)! };

    private static CardInstance TestCard(string number, int cost)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "黑",
                Kind = CardKind.Character,
                Property = "特",
                Cost = cost,
                Power = 5_000,
            },
        };

    /// <summary>模拟真实 Prompt 跨房间动作期间只变化调度字段、最终选择不发动。</summary>
    private sealed class TransientClockPrompt(GameState state, bool confirm) : IPromptService
    {
        public Task<List<string>> ChooseCards(
            int playerIdx,
            string kind,
            string text,
            IReadOnlyList<string> validChoices,
            int min,
            int max,
            Dictionary<string, object?>? extra = null)
            => Task.FromResult(validChoices.Take(max).ToList());

        public Task<bool> ConfirmOptional(int playerIdx, string text)
        {
            state.Tick++;
            state.OperationClockRemainingMs[playerIdx] -= 1_000;
            state.OperationTurnClockRemainingMs[playerIdx] -= 1_000;
            state.OperationClockSyncUtc = DateTime.UtcNow;
            state.InactivitySyncUtc = DateTime.UtcNow;
            return Task.FromResult(confirm);
        }

        public Task<int> ChooseOption(
            int playerIdx,
            string text,
            IReadOnlyList<string> options,
            Dictionary<string, object?>? extra = null)
            => Task.FromResult(0);

        public Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
            => Task.FromResult(false);
    }
}
