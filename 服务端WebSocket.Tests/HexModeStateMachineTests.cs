using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Hex;
using GrandUMI.Game.Snapshot;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class HexModeStateMachineTests
{
    private static readonly int[] RainbowIds = [4, 5, 9, 10, 11, 12, 13, 14, 15, 19, 28, 35, 38, 39, 40, 46, 47, 53];
    private static readonly int[] GoldIds = [1, 2, 3, 6, 7, 16, 17, 18, 21, 26, 27, 29, 32, 36, 37, 48, 49, 51];
    private static readonly int[] SilverIds = [8, 20, 22, 23, 24, 25, 30, 31, 33, 34, 41, 42, 43, 44, 45, 50, 52, 54];

    [Fact]
    public void 海克斯目录_品质编号与策划清单完全一致()
    {
        Assert.Equal(RainbowIds, HexCatalog.ForTier(HexTier.Rainbow).Select(item => item.Id).Order().ToArray());
        Assert.Equal(GoldIds, HexCatalog.ForTier(HexTier.Gold).Select(item => item.Id).Order().ToArray());
        Assert.Equal(SilverIds, HexCatalog.ForTier(HexTier.Silver).Select(item => item.Id).Order().ToArray());
        Assert.Equal(54, HexCatalog.All.Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task 草拟状态机_隔离候选_幂等锁定_并在双方第二与第五回合后暂停()
    {
        var engine = CreateEngine(seed: 20260901);
        Assert.True(engine.HandleAction(0, "Mulligan", Json(new { redraw = false })));
        Assert.True(engine.HandleAction(1, "Mulligan", Json(new { redraw = false })));

        var silver = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        Assert.Equal(HexTier.Silver, silver.Tier);
        Assert.Equal(OpeningStage.HexDraft, engine.State.OpeningStage);
        Assert.Equal(0, engine.State.TurnCount);
        Assert.All(silver.Candidates, candidates => Assert.Equal(3, candidates.Distinct().Count()));
        Assert.All(silver.Candidates.SelectMany(items => items), id => Assert.Contains(id, SilverIds));

        var player0 = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 0));
        var player1 = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 1));
        var spectator = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, -1));
        Assert.Equal(
            silver.Candidates[0],
            CandidateIds(player0.GetProperty("hexState").GetProperty("activeDraft")));
        Assert.Equal(
            silver.Candidates[1],
            CandidateIds(player1.GetProperty("hexState").GetProperty("activeDraft")));
        Assert.Equal(
            JsonValueKind.Null,
            spectator.GetProperty("hexState").GetProperty("activeDraft").GetProperty("candidates").ValueKind);

        // 选秀期间不能偷跑普通动作；伪造候选也不能改变锁定状态。
        Assert.False(engine.HandleAction(0, "EndTurn", Json(new { })));
        var invalid = GoldIds.First(id => !silver.Candidates[0].Contains(id));
        Assert.False(engine.HandleAction(0, "ChooseHex", Json(new { roundId = silver.RoundId, hexId = invalid })));
        Assert.False(silver.Locked[0]);

        var p0Choice = silver.Candidates[0][0];
        Assert.True(engine.HandleAction(0, "ChooseHex", Json(new { roundId = silver.RoundId, hexId = p0Choice })));
        Assert.True(silver.Locked[0]);
        // 相同轮次、相同选项的重复请求是幂等成功，不会再次结算。
        Assert.True(engine.HandleAction(0, "ChooseHex", Json(new { roundId = silver.RoundId, hexId = p0Choice })));
        Assert.Equal(p0Choice, silver.LockedChoices[0]);
        var opponentView = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 1));
        var opponentDraft = opponentView.GetProperty("hexState").GetProperty("activeDraft");
        Assert.True(opponentDraft.GetProperty("opponentLocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, opponentDraft.GetProperty("mySelectedHexId").ValueKind);

        Assert.True(engine.HandleAction(1, "ChooseHex", Json(new
        {
            roundId = silver.RoundId,
            hexId = silver.Candidates[1][0],
        })));
        await engine.WaitSettledAsync();
        Assert.Null(engine.State.HexState.ActiveDraft);
        Assert.Equal(1, engine.State.TurnCount);
        Assert.Equal(OpeningStage.Playing, engine.State.OpeningStage);
        Assert.Single(engine.State.HexState.Owned[0]);
        Assert.Single(engine.State.HexState.Owned[1]);

        // 双方各自完成第 2 个回合前不得开启金色；第 4 次结束回合后暂停在下一回合开始之前。
        for (var i = 0; i < 3; i++)
        {
            Assert.True(engine.HandleAction(engine.State.CurrentTurnPlayer, "EndTurn", Json(new { })));
            await engine.WaitSettledAsync();
            Assert.Null(engine.State.HexState.ActiveDraft);
        }
        Assert.True(engine.HandleAction(engine.State.CurrentTurnPlayer, "EndTurn", Json(new { })));
        await engine.WaitSettledAsync();
        var gold = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        Assert.Equal(HexTier.Gold, gold.Tier);
        Assert.Equal(new[] { 2, 2 }, engine.State.HexState.CompletedOwnTurns);
        Assert.Equal(4, engine.State.TurnCount);

        await ResolveDraftAvoidingAcquirePrompt(engine, gold, avoidHexId: 6);
        Assert.Equal(5, engine.State.TurnCount);

        for (var i = 0; i < 5; i++)
        {
            Assert.True(engine.HandleAction(engine.State.CurrentTurnPlayer, "EndTurn", Json(new { })));
            await engine.WaitSettledAsync();
            Assert.Null(engine.State.HexState.ActiveDraft);
        }
        Assert.True(engine.HandleAction(engine.State.CurrentTurnPlayer, "EndTurn", Json(new { })));
        await engine.WaitSettledAsync();
        var rainbow = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        Assert.Equal(HexTier.Rainbow, rainbow.Tier);
        Assert.Equal(new[] { 5, 5 }, engine.State.HexState.CompletedOwnTurns);
        Assert.Equal(10, engine.State.TurnCount);
    }

    [Fact]
    public async Task 系统自动选择_动作来源与截止时间可逐字重放()
    {
        const int seed = 987654;
        const string roomId = "hex-replay-active-draft";
        var live = CreateEngine(seed, roomId);
        var tape = new List<MatchReplay.ActionEntry>();

        async Task Apply(int player, string action, object data, GameActionSource source)
        {
            var element = Json(data);
            Assert.True(live.HandleAction(player, action, element, source: source));
            await live.WaitSettledAsync();
            var draft = live.State.HexState.ActiveDraft;
            tape.Add(new MatchReplay.ActionEntry(
                player,
                action,
                element,
                source,
                draft?.RoundId,
                draft?.DeadlineUtc));
        }

        await Apply(0, "Mulligan", new { redraw = false }, GameActionSource.Player);
        await Apply(1, "Mulligan", new { redraw = false }, GameActionSource.Player);
        var round = Assert.IsType<HexDraftRound>(live.State.HexState.ActiveDraft);
        await Apply(0, "ChooseHex", new { roundId = round.RoundId, auto = true }, GameActionSource.System);
        Assert.True(round.Locked[0]);
        Assert.False(round.Locked[1]);

        var rebuilt = await MatchReplay.RebuildAsync(
            roomId,
            seed,
            firstPlayer: 0,
            ("alice", BuildLegalDeck()),
            ("bob", BuildLegalDeck()),
            tape,
            matchKind: MatchKind.Hex);

        Assert.Equal(
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(live.State)),
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(rebuilt.State)));
        Assert.Equal(live.State.RandomSeq, rebuilt.State.RandomSeq);
        Assert.Equal(
            live.State.HexState.ActiveDraft!.LockedChoices,
            rebuilt.State.HexState.ActiveDraft!.LockedChoices);
    }

    [Fact]
    public async Task 选秀授予故障_步骤游标前移且重试不重复已完成效果()
    {
        var engine = CreateEngine(seed: 490049);
        var player = engine.State.Players[0];
        int handBefore = player.Hand.Count;
        var round = CreateLockedDraft(engine.State, HexTier.Gold, player0Choice: 49, player1Choice: 1);
        engine.State.HexState.GrantStepFaultInjector = boundary =>
        {
            if (boundary.HexId == 49 && boundary.CompletedStep == 1)
                throw new InjectedDraftSettlementException();
        };

        await Assert.ThrowsAsync<InjectedDraftSettlementException>(() => HexRules.ResolveDraftAsync(engine));

        Assert.False(engine.State.HexState.DraftResolving);
        Assert.Null(engine.State.HexState.ActiveDraft);
        var pending = Assert.IsType<HexDraftSettlement>(engine.State.HexState.PendingSettlement);
        Assert.Equal(round.RoundId, pending.RoundId);
        Assert.True(pending.RootOwnershipCommitted);
        Assert.Equal(0, pending.NextGrantIndex);
        Assert.Equal(1, pending.Grants[0].NextStep);
        Assert.False(pending.Grants[0].Completed);
        Assert.Equal(handBefore + 3, player.Hand.Count);
        Assert.Contains(49, engine.State.HexState.Owned[0]);
        Assert.Contains(1, engine.State.HexState.Owned[1]);
        Assert.True(engine.State.HexState.BlocksOrdinaryActions);

        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(engine.State));
        var persistedGrant = privateState.GetProperty("hexState")
            .GetProperty("pendingSettlement")
            .GetProperty("grants")[0];
        Assert.Equal(1, persistedGrant.GetProperty("NextStep").GetInt32());
        Assert.Contains(
            "pendingSettlement",
            DeterministicReplayCheckpointProvider.BuildFullState(engine.State).GetRawText());

        engine.State.HexState.GrantStepFaultInjector = null;
        var (resolved, _) = await HexRules.ResolveDraftAsync(engine);

        Assert.Equal(round.RoundId, resolved.RoundId);
        Assert.Equal(handBefore + 3, player.Hand.Count);
        Assert.Null(engine.State.HexState.PendingSettlement);
        Assert.False(engine.State.HexState.DraftResolving);
        Assert.False(engine.State.HexState.BlocksOrdinaryActions);
        Assert.Single(engine.State.HexState.ResolvedDrafts.Where(item => item.RoundId == round.RoundId));
    }

    [Fact]
    public async Task 选秀授予动作_进程重放后获得时效果只执行一次()
    {
        const int seed = 52;
        const string roomId = "hex-replay-resolved-draft";
        var live = CreateEngine(seed, roomId);
        var tape = new List<MatchReplay.ActionEntry>();

        async Task Apply(int player, string action, object data)
        {
            var element = Json(data);
            Assert.True(live.HandleAction(player, action, element));
            await live.WaitSettledAsync();
            var activeDraft = live.State.HexState.ActiveDraft;
            tape.Add(new MatchReplay.ActionEntry(
                player,
                action,
                element,
                GameActionSource.Player,
                activeDraft?.RoundId,
                activeDraft?.DeadlineUtc));
        }

        await Apply(0, "Mulligan", new { redraw = false });
        await Apply(1, "Mulligan", new { redraw = false });
        var round = Assert.IsType<HexDraftRound>(live.State.HexState.ActiveDraft);

        // 固定 seed 的银色候选覆盖“额外两张 DON!!”，让重复执行具备可观测性。
        int owner = round.Candidates[0].Contains(52) ? 0 : 1;
        Assert.Contains(52, round.Candidates[owner]);
        int[] choices =
        [
            owner == 0 ? 52 : round.Candidates[0][0],
            owner == 1 ? 52 : round.Candidates[1][0],
        ];
        int donBefore = live.State.Players[owner].DonDeck.Count;
        await Apply(0, "ChooseHex", new { roundId = round.RoundId, hexId = choices[0] });
        await Apply(1, "ChooseHex", new { roundId = round.RoundId, hexId = choices[1] });

        Assert.Null(live.State.HexState.PendingSettlement);
        Assert.False(live.State.HexState.DraftResolving);
        Assert.Equal(donBefore + 2, live.State.Players[owner].DonDeck.Count);

        var rebuilt = await MatchReplay.RebuildAsync(
            roomId,
            seed,
            firstPlayer: 0,
            ("alice", BuildLegalDeck()),
            ("bob", BuildLegalDeck()),
            tape,
            matchKind: MatchKind.Hex);

        Assert.Null(rebuilt.State.HexState.PendingSettlement);
        Assert.False(rebuilt.State.HexState.DraftResolving);
        Assert.Equal(donBefore + 2, rebuilt.State.Players[owner].DonDeck.Count);
        Assert.Equal(
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(live.State)),
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(rebuilt.State)));
    }

    private static async Task ResolveDraftAvoidingAcquirePrompt(GameEngine engine, HexDraftRound round, int avoidHexId)
    {
        for (var player = 0; player < 2; player++)
        {
            var choice = round.Candidates[player].First(id => id != avoidHexId);
            Assert.True(engine.HandleAction(player, "ChooseHex", Json(new { roundId = round.RoundId, hexId = choice })));
        }
        await engine.WaitSettledAsync();
        Assert.Null(engine.State.PendingPrompt);
    }

    private static HexDraftRound CreateLockedDraft(
        GameState state,
        HexTier tier,
        int player0Choice,
        int player1Choice)
    {
        var round = new HexDraftRound
        {
            RoundId = $"fault-{tier.ToString().ToLowerInvariant()}",
            Tier = tier,
            DeadlineUtc = DateTime.UtcNow.AddMinutes(1),
        };
        round.Candidates[0].Add(player0Choice);
        round.Candidates[1].Add(player1Choice);
        round.LockedChoices[0] = player0Choice;
        round.LockedChoices[1] = player1Choice;
        round.Locked[0] = true;
        round.Locked[1] = true;
        state.HexState.ActiveDraft = round;
        state.HexState.ResumePoint = HexDraftResumePoint.None;
        return round;
    }

    private sealed class InjectedDraftSettlementException : Exception;

    private static int[] CandidateIds(JsonElement draft)
        => draft.GetProperty("candidates")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToArray();

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    private static GameEngine CreateEngine(int seed, string? roomId = null)
    {
        TestScene.New();
        var deck = BuildLegalDeck();
        return new GameEngine(
            roomId ?? $"hex-state-{seed}",
            ("s0", "alice", deck),
            ("s1", "bob", deck),
            firstPlayer: 0,
            rngSeed: seed,
            matchKind: MatchKind.Hex);
    }

    private static string BuildLegalDeck()
    {
        const string leaderNumber = "OP15-001";
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
}
