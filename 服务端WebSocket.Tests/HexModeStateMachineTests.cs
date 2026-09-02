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
    private static readonly int[] RainbowIds = [5, 9, 10, 11, 12, 13, 14, 19, 27, 28, 29, 35, 38, 39, 40, 46, 47, 53];
    private static readonly int[] GoldIds = [1, 2, 3, 4, 6, 7, 15, 16, 17, 18, 21, 26, 32, 36, 37, 49, 51, 56];
    private static readonly int[] SilverIds = [8, 20, 22, 23, 24, 25, 31, 33, 34, 41, 42, 43, 44, 45, 50, 52, 54, 55];

    [Fact]
    public void 海克斯目录_品质编号与策划清单完全一致()
    {
        Assert.Equal(RainbowIds, HexCatalog.ForTier(HexTier.Rainbow).Select(item => item.Id).Order().ToArray());
        Assert.Equal(GoldIds, HexCatalog.ForTier(HexTier.Gold).Select(item => item.Id).Order().ToArray());
        Assert.Equal(SilverIds, HexCatalog.ForTier(HexTier.Silver).Select(item => item.Id).Order().ToArray());
        Assert.All(Enum.GetValues<HexTier>(), tier => Assert.Equal(18, HexCatalog.ForTier(tier).Count));
        Assert.Equal(54, HexCatalog.Regular.Select(item => item.Id).Distinct().Count());
        Assert.Equal(new[] { 30, 48 }, HexCatalog.Alternatives.Select(item => item.Id).Order().ToArray());
        Assert.Equal(56, HexCatalog.All.Select(item => item.Id).Distinct().Count());
        Assert.DoesNotContain(HexCatalog.Regular, item => HexCatalog.IsAlternative(item.Id));
        Assert.Equal("Rainbow", HexTier.Rainbow.ToString());
        Assert.Equal("棱彩", HexCatalog.TierDisplayName(HexTier.Rainbow));
        Assert.DoesNotContain(HexCatalog.All, item => item.Description.Contains("彩色", StringComparison.Ordinal));
        Assert.Equal(Enumerable.Range(1, 54), HexCatalog.RegularForRevision(HexRules.LegacyRulesRevision).Select(item => item.Id));
        Assert.Equal(18, HexCatalog.ForTier(HexTier.Silver, HexRules.LegacyRulesRevision).Count);
        Assert.Equal(18, HexCatalog.ForTier(HexTier.Gold, HexRules.LegacyRulesRevision).Count);
        Assert.Equal(18, HexCatalog.ForTier(HexTier.Rainbow, HexRules.LegacyRulesRevision).Count);
        Assert.Equal(HexTier.Rainbow, HexCatalog.TierForRevision(4, HexRules.LegacyRulesRevision));
        Assert.Equal(HexTier.Gold, HexCatalog.TierForRevision(27, HexRules.LegacyRulesRevision));
        Assert.False(HexCatalog.IsAlternative(30, HexRules.LegacyRulesRevision));
        Assert.Equal(2, HexRules.BalanceRulesRevision);
        Assert.Equal(3, HexRules.PerSlotRefreshRulesRevision);
        Assert.Equal(4, HexRules.TransmutationPresentationRulesRevision);
        Assert.Equal(5, HexRules.CatalogConfigurationRulesRevision);
        Assert.Equal(6, HexRules.AstralBodyRulesRevision);
        Assert.Equal(HexRules.AstralBodyRulesRevision, HexRules.CurrentRulesRevision);
        Assert.Equal(
            "获得时选择1张手牌放入生命区，然后从卡组顶将1张卡牌加入生命区。",
            HexCatalog.Get(6).Description);
        Assert.Equal(
            "获得时选择2张手牌，按顺序放入生命区。",
            HexCatalog.DescriptionForRevision(6, HexRules.CatalogConfigurationRulesRevision));
        Assert.Equal(
            "每回合1次，己方效果使敌方角色离场，或使敌方角色由活跃转为休息时，己方领袖本回合力量+2000。",
            HexCatalog.Get(42).Description);
        Assert.Equal("获得时确定性随机获得1个金色海克斯。", HexCatalog.Get(55).Description);
        Assert.Equal(
            "获得时确定性随机获得1个其他银色海克斯和1个金色海克斯。",
            HexCatalog.DescriptionForRevision(55, HexRules.PerSlotRefreshRulesRevision));
        Assert.Equal(
            HexCatalog.Regular.Select(item => item.Id),
            HexCatalog.RegularForRevision(HexRules.BalanceRulesRevision).Select(item => item.Id));
        Assert.True(HexCatalog.IsAlternative(30, HexRules.BalanceRulesRevision));
    }

    [Fact]
    public async Task 好友房海克斯_重放保留私房来源并由独立开关启用规则()
    {
        TestScene.New();
        const int seed = 20260903;
        var deck = BuildLegalDeck();

        var rebuilt = await MatchReplay.RebuildAsync(
            "friendly-hex-replay",
            seed,
            firstPlayer: 0,
            ("alice", deck),
            ("bob", deck),
            Array.Empty<MatchReplay.ActionEntry>(),
            matchKind: MatchKind.RoomCode,
            hexMode: true);

        Assert.Equal(MatchKind.RoomCode, rebuilt.State.MatchKind);
        Assert.True(rebuilt.State.HexState.Enabled);
        Assert.Equal(HexRules.CurrentRulesRevision, rebuilt.State.HexState.RulesRevision);
        Assert.Equal(3, rebuilt.State.HexState.DraftTierSequence.Count);
        Assert.Equal(HexCatalogConfiguration.BuiltIn.Digest, rebuilt.State.HexState.CatalogDigest);
    }

    [Fact]
    public void 动态品质目录锁定到房间且公开快照携带内容身份()
    {
        var engine = CreateEngine(seed: 20260902);
        var assignments = HexCatalogConfiguration.BuiltIn.Assignments
            .Select(item => item.Id == 1
                ? item with { Tier = HexTier.Silver }
                : item.Id == 8
                    ? item with { Tier = HexTier.Gold }
                    : item)
            .ToArray();
        var pinned = HexCatalogConfiguration.Create(
            8,
            assignments,
            publishedAt: 1788278400000,
            publishedBy: "Admin",
            sourceDraftRevision: 4,
            sourceRequestId: "publish-pinned");

        HexRules.SetRulesRevisionForReplay(engine.State, HexRules.CurrentRulesRevision);
        HexRules.SetCatalogForReplay(engine.State, pinned);

        Assert.Equal(8, engine.State.HexState.CatalogRevision);
        Assert.Equal(pinned.Digest, engine.State.HexState.CatalogDigest);
        Assert.Equal(HexTier.Silver, HexCatalog.TierForState(1, engine.State.HexState));
        Assert.Contains(HexCatalog.ForTier(HexTier.Silver, engine.State.HexState), item => item.Id == 1);
        Assert.DoesNotContain(HexCatalog.ForTier(HexTier.Gold, engine.State.HexState), item => item.Id == 1);

        // 外部 active 后续变化不会回读或改写本局复制的映射。
        _ = HexCatalogConfiguration.Create(
            9,
            HexCatalogConfiguration.BuiltIn.Assignments.Select(item =>
                item.Id == 2
                    ? item with { Tier = HexTier.Rainbow }
                    : item.Id == 5
                        ? item with { Tier = HexTier.Gold }
                        : item));
        Assert.Equal(HexTier.Silver, HexCatalog.TierForState(1, engine.State.HexState));
        Assert.Equal(8, engine.State.HexState.CatalogRevision);

        var publicHex = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 0))
            .GetProperty("hexState");
        Assert.Equal(8, publicHex.GetProperty("catalogRevision").GetInt64());
        Assert.Equal(pinned.Digest, publicHex.GetProperty("catalogDigest").GetString());
        var privateHex = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(engine.State))
            .GetProperty("hexState");
        Assert.Equal(56, privateHex.GetProperty("catalogTiers").GetArrayLength());
    }

    [Fact]
    public void 质变公开投影_隐藏三个本体并保留派生来源于双方观战与回放状态()
    {
        var engine = CreateEngine(seed: 475556);
        var state = engine.State;
        state.HexState.Owned[0].AddRange([47, 1, 55, 2, 56, 5]);
        state.HexState.GrantedByTransmutation[0].UnionWith([1, 2, 5]);
        state.HexState.Owned[1].AddRange([55, 3]);
        state.HexState.GrantedByTransmutation[1].Add(3);

        Assert.All(new[] { 47, 55, 56 }, id => Assert.True(HexRules.Has(state, 0, id)));

        static JsonElement HexStateFor(GameState gameState, int viewer, int spectatorPlayer = 0)
            => JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(
                gameState,
                viewer,
                spectatorPlayerIndex: spectatorPlayer)).GetProperty("hexState");

        static void AssertPlayerZeroProjection(JsonElement hexState)
        {
            var owned = hexState.GetProperty("myOwned").EnumerateArray().ToArray();
            Assert.Equal(new[] { 1, 2, 5 }, owned.Select(item => item.GetProperty("id").GetInt32()));
            Assert.Equal(
                new[] { "质变-大力", "质变-灵巧", "质变-尖端发明家" },
                owned.Select(item => item.GetProperty("name").GetString()));
            Assert.All(owned, item => Assert.True(item.GetProperty("grantedByTransmutation").GetBoolean()));
            Assert.Equal("Gold", owned[0].GetProperty("tier").GetString());
            Assert.Equal(HexCatalog.Get(1).Description, owned[0].GetProperty("description").GetString());
            Assert.DoesNotContain(owned, item => HexCatalog.IsTransmutation(item.GetProperty("id").GetInt32()));
        }

        var owner = HexStateFor(state, 0);
        AssertPlayerZeroProjection(owner);

        var opponent = HexStateFor(state, 1);
        Assert.Equal(
            new[] { "质变-大力", "质变-灵巧", "质变-尖端发明家" },
            opponent.GetProperty("opponentOwned").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()));

        AssertPlayerZeroProjection(HexStateFor(state, -1, spectatorPlayer: 0));
        var playerOneSpectator = HexStateFor(state, -1, spectatorPlayer: 1);
        Assert.Equal(
            new[] { "质变-古式佳酿" },
            playerOneSpectator.GetProperty("myOwned").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()));

        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state))
            .GetProperty("hexState");
        Assert.Equal(new[] { 47, 1, 55, 2, 56, 5 }, privateState.GetProperty("owned")[0]
            .EnumerateArray().Select(item => item.GetInt32()));
        Assert.Equal(new[] { 1, 2, 5 }, privateState.GetProperty("grantedByTransmutation")[0]
            .EnumerateArray().Select(item => item.GetInt32()));

        var fullCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(state).GetProperty("hexState");
        Assert.Equal(new[] { 1, 2, 5 }, fullCheckpoint.GetProperty("grantedByTransmutation")[0]
            .EnumerateArray().Select(item => item.GetInt32()));
        var publicCheckpoint = DeterministicReplayCheckpointProvider.BuildPublicState(state).GetProperty("hexState");
        Assert.Equal(new[] { 1, 2, 5 }, publicCheckpoint.GetProperty("owned")[0]
            .EnumerateArray().Select(item => item.GetInt32()));
        Assert.Equal(new[] { 1, 2, 5 }, publicCheckpoint.GetProperty("grantedByTransmutation")[0]
            .EnumerateArray().Select(item => item.GetInt32()));

        state.HexState.DraftTierSequence.Clear();
        state.HexState.DraftTierSequence.AddRange([HexTier.Rainbow, HexTier.Gold, HexTier.Silver]);
        var draft = HexRules.StartDraft(state, 0, 1, HexDraftResumePoint.None);
        Assert.DoesNotContain(47, draft.Candidates);
    }

    [Fact]
    public void 上一规则修订版_保留质变本体旧文案且不改变旧检查点结构()
    {
        var engine = CreateEngine(seed: 355001);
        var state = engine.State;
        HexRules.SetRulesRevisionForReplay(state, HexRules.PerSlotRefreshRulesRevision);
        state.HexState.Owned[0].AddRange([55, 8, 1]);
        state.HexState.GrantedByTransmutation[0].UnionWith([8, 1]);

        var visible = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, 0))
            .GetProperty("hexState").GetProperty("myOwned").EnumerateArray().ToArray();
        Assert.Equal(new[] { 55, 8, 1 }, visible.Select(item => item.GetProperty("id").GetInt32()));
        Assert.Equal("质变：黄金阶", visible[0].GetProperty("name").GetString());
        Assert.Equal(
            "获得时确定性随机获得1个其他银色海克斯和1个金色海克斯。",
            visible[0].GetProperty("description").GetString());
        Assert.All(visible, item => Assert.False(item.GetProperty("grantedByTransmutation").GetBoolean()));

        var fullCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(state).GetProperty("hexState");
        var publicCheckpoint = DeterministicReplayCheckpointProvider.BuildPublicState(state).GetProperty("hexState");
        Assert.False(fullCheckpoint.TryGetProperty("grantedByTransmutation", out _));
        Assert.False(publicCheckpoint.TryGetProperty("grantedByTransmutation", out _));
        Assert.Equal(new[] { 55, 8, 1 }, publicCheckpoint.GetProperty("owned")[0]
            .EnumerateArray().Select(item => item.GetInt32()));
    }

    [Fact]
    public async Task 私密选秀_双方分别在自己第一三六回合开始前触发且互不等待()
    {
        var engine = CreateEngine(seed: 20260901);
        Assert.Equal([HexTier.Silver, HexTier.Gold, HexTier.Silver], engine.State.HexState.DraftTierSequence);
        await MulliganBoth(engine);

        var first = AssertDraft(engine, player: 0, ownTurn: 1, HexTier.Silver);
        Assert.Equal(0, engine.State.TurnCount);
        Assert.Equal(OpeningStage.HexDraft, engine.State.OpeningStage);
        AssertPrivateDraft(engine, first);

        // 非拥有者不会命中海克斯冻结分支；其动作只按普通开局/回合规则拒绝。
        var nonOwnerEndTurn = engine.HandleActionWithReceipt(1, "EndTurn", Json(new { }));
        Assert.False(nonOwnerEndTurn.Accepted);
        Assert.NotEqual("当前正在进行海克斯选秀，暂时无法执行其他操作", nonOwnerEndTurn.RejectionReason);
        Assert.False(engine.HandleAction(1, "ChooseHex", Json(new { roundId = first.RoundId, hexId = first.Candidates[0] })));

        await ResolveCurrentDraft(engine);
        Assert.Equal(1, engine.State.TurnCount);
        Assert.Single(engine.State.HexState.Owned[0]);
        Assert.Empty(engine.State.HexState.Owned[1]);

        // P0 第 1 回合结束后，只让即将开始第 1 回合的 P1 选择；P0 不再等待自己的选择。
        await EndTurn(engine);
        var second = AssertDraft(engine, player: 1, ownTurn: 1, HexTier.Silver);
        Assert.Equal(1, engine.State.TurnCount);
        AssertPrivateDraft(engine, second);
        await ResolveCurrentDraft(engine);
        Assert.Equal(2, engine.State.TurnCount);
        Assert.Single(engine.State.HexState.Owned[1]);

        // 双方自己的第 2 回合开始前均不触发。
        await EndTurn(engine);
        Assert.Null(engine.State.HexState.ActiveDraft);
        Assert.Equal(3, engine.State.TurnCount);
        await EndTurn(engine);
        Assert.Null(engine.State.HexState.ActiveDraft);
        Assert.Equal(4, engine.State.TurnCount);

        // P0/P1 分别在自己的第 3 回合开始前使用共享序列的第 2 项。
        await EndTurn(engine);
        AssertDraft(engine, player: 0, ownTurn: 3, HexTier.Gold);
        await ResolveCurrentDraft(engine);
        await EndTurn(engine);
        AssertDraft(engine, player: 1, ownTurn: 3, HexTier.Gold);
        await ResolveCurrentDraft(engine);

        // 推进双方第 4、5 回合；P0/P1 分别在第 6 回合前使用共享序列的重复银色项。
        for (var i = 0; i < 4; i++)
        {
            await EndTurn(engine);
            Assert.Null(engine.State.HexState.ActiveDraft);
        }
        await EndTurn(engine);
        AssertDraft(engine, player: 0, ownTurn: 6, HexTier.Silver);
        await ResolveCurrentDraft(engine);
        await EndTurn(engine);
        AssertDraft(engine, player: 1, ownTurn: 6, HexTier.Silver);
        await ResolveCurrentDraft(engine);

        Assert.Equal(new[] { 6, 5 }, engine.State.HexState.CompletedOwnTurns);
        Assert.Equal(3, engine.State.HexState.ResolvedDrafts.Count(item => item.PlayerIndex == 0));
        Assert.Equal(3, engine.State.HexState.ResolvedDrafts.Count(item => item.PlayerIndex == 1));
        Assert.Equal(
            engine.State.HexState.ResolvedDrafts.Where(item => item.PlayerIndex == 0).Select(item => item.Tier),
            engine.State.HexState.ResolvedDrafts.Where(item => item.PlayerIndex == 1).Select(item => item.Tier));
    }

    [Fact]
    public async Task 海克斯可见性_刷新与锁定全程私密且结算后仅选中定义立即向双方公开()
    {
        var engine = CreateEngine(seed: 20260901);
        var delivered = new List<(int PlayerIndex, JsonElement Snapshot)>();
        engine.OnSendToPlayer = (playerIndex, payload) =>
            delivered.Add((playerIndex, JsonSerializer.SerializeToElement(payload)));

        await MulliganBoth(engine);
        var round = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        var originalCandidates = round.Candidates.ToArray();
        int replacedHexId = originalCandidates[0];

        delivered.Clear();
        Assert.True(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 0,
            expectedHexId = replacedHexId,
        })));
        int replacementHexId = round.Candidates[0];
        Assert.NotEqual(replacedHexId, replacementHexId);

        var ownerRefresh = DeliveredSnapshot(delivered, 0, "HexCandidateRefreshed");
        var opponentRefresh = DeliveredSnapshot(delivered, 1, "HexCandidateRefreshed");
        Assert.Equal(
            round.Candidates,
            CandidateIds(ownerRefresh.GetProperty("hexState").GetProperty("activeDraft")));
        var ownerDraft = ownerRefresh.GetProperty("hexState").GetProperty("activeDraft");
        Assert.Equal(2, ownerDraft.GetProperty("refreshRemaining").GetInt32());
        Assert.Equal(new[] { false, true, true }, ownerDraft.GetProperty("refreshAvailableByCandidate")
            .EnumerateArray().Select(item => item.GetBoolean()).ToArray());
        Assert.Equal(new[] { 0 }, ownerDraft.GetProperty("refreshedCandidateIndices")
            .EnumerateArray().Select(item => item.GetInt32()).ToArray());
        Assert.Equal(
            JsonValueKind.Null,
            opponentRefresh.GetProperty("hexState").GetProperty("activeDraft").ValueKind);
        var opponentRefreshPayload = JsonSerializer.Deserialize<JsonElement>(
            opponentRefresh.GetProperty("actionPayload").GetString()!);
        Assert.False(opponentRefreshPayload.TryGetProperty("replacedHexId", out _));
        Assert.False(opponentRefreshPayload.TryGetProperty("replacementHexId", out _));

        int choice = round.Candidates.First(id => id is not 20 and not 52 and not 55);
        var nonSelectedPrivateIds = originalCandidates
            .Append(replacementHexId)
            .Where(id => id != choice)
            .Distinct()
            .ToArray();

        delivered.Clear();
        Assert.True(engine.HandleAction(0, "ChooseHex", Json(new
        {
            roundId = round.RoundId,
            hexId = choice,
        })));
        await engine.WaitSettledAsync();

        // 锁定屏障先于异步授予结算下发：本人仍可见锁定结果，对手既看不到候选，也拿不到所选编号。
        var ownerLocked = DeliveredSnapshot(delivered, 0, "HexChoiceLocked");
        var opponentLocked = DeliveredSnapshot(delivered, 1, "HexChoiceLocked");
        var ownerLockedHex = ownerLocked.GetProperty("hexState");
        Assert.True(ownerLockedHex.GetProperty("activeDraft").GetProperty("myLocked").GetBoolean());
        Assert.Equal(choice, ownerLockedHex.GetProperty("activeDraft").GetProperty("mySelectedHexId").GetInt32());
        Assert.Equal(JsonValueKind.Null, opponentLocked.GetProperty("hexState").GetProperty("activeDraft").ValueKind);
        Assert.Empty(opponentLocked.GetProperty("hexState").GetProperty("myOwned").EnumerateArray());
        Assert.Empty(opponentLocked.GetProperty("hexState").GetProperty("opponentOwned").EnumerateArray());
        var opponentLockedPayload = JsonSerializer.Deserialize<JsonElement>(
            opponentLocked.GetProperty("actionPayload").GetString()!);
        Assert.False(opponentLockedPayload.TryGetProperty("choice", out _));
        Assert.False(opponentLockedPayload.TryGetProperty("hexId", out _));

        // 结算完成的即时屏障向双方公开同一份权威目录定义；未选与被刷新候选不进入任何已拥有列表。
        var ownerResolved = DeliveredSnapshot(delivered, 0, "HexDraftResolved");
        var opponentResolved = DeliveredSnapshot(delivered, 1, "HexDraftResolved");
        var ownerResolvedHex = ownerResolved.GetProperty("hexState");
        var opponentResolvedHex = opponentResolved.GetProperty("hexState");
        Assert.Equal(JsonValueKind.Null, ownerResolvedHex.GetProperty("activeDraft").ValueKind);
        Assert.Equal(JsonValueKind.Null, opponentResolvedHex.GetProperty("activeDraft").ValueKind);

        var definition = HexCatalog.Get(choice);
        AssertSingleOwnedDefinition(ownerResolvedHex.GetProperty("myOwned"), definition);
        Assert.Empty(ownerResolvedHex.GetProperty("opponentOwned").EnumerateArray());
        Assert.Empty(opponentResolvedHex.GetProperty("myOwned").EnumerateArray());
        AssertSingleOwnedDefinition(opponentResolvedHex.GetProperty("opponentOwned"), definition);
        foreach (int privateId in nonSelectedPrivateIds)
        {
            Assert.DoesNotContain(privateId, OwnedHexIds(ownerResolvedHex));
            Assert.DoesNotContain(privateId, OwnedHexIds(opponentResolvedHex));
        }
    }

    [Fact]
    public async Task 逐槽刷新_三个槽位可任意顺序各刷新一次且同槽重试幂等()
    {
        var engine = CreateEngine(seed: 20260901);
        var logs = new List<string>();
        engine.OnMatchLog = (kind, _, _) => logs.Add(kind);
        await MulliganBoth(engine);
        var round = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        var before = round.Candidates.ToArray();

        Assert.False(engine.HandleAction(1, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 0,
            expectedHexId = before[0],
        })));
        Assert.False(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 9,
            expectedHexId = before[0],
        })));
        Assert.False(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 1,
            expectedHexId = before[0],
        })));

        var refreshSlot1 = new { roundId = round.RoundId, candidateIndex = 1, expectedHexId = before[1] };
        Assert.True(engine.HandleAction(0, "RefreshHex", Json(refreshSlot1)));
        Assert.True(round.RefreshUsed);
        Assert.Equal(1, round.RefreshedCandidateIndex);
        Assert.Single(round.Refreshes);
        Assert.Equal(2, HexRules.RefreshRemaining(round, engine.State.HexState.RulesRevision));
        Assert.False(HexRules.RefreshAvailableForCandidate(round, 1, engine.State.HexState.RulesRevision));
        Assert.True(HexRules.RefreshAvailableForCandidate(round, 0, engine.State.HexState.RulesRevision));
        Assert.True(HexRules.RefreshAvailableForCandidate(round, 2, engine.State.HexState.RulesRevision));
        Assert.NotEqual(before[1], round.Candidates[1]);
        Assert.DoesNotContain(round.Candidates[1], before);
        Assert.Equal(3, round.Candidates.Distinct().Count());
        Assert.All(round.Candidates, id => Assert.Contains(id, SilverIds));
        Assert.Contains("hex_draft_candidate_refreshed", logs);

        // 相同业务请求即使用新 requestId 重试也只返回幂等成功，不再消费 RNG。
        int randomSeqAfterRefresh = engine.State.RandomSeq;
        Assert.True(engine.HandleAction(0, "RefreshHex", Json(refreshSlot1)));
        Assert.Equal(randomSeqAfterRefresh, engine.State.RandomSeq);
        Assert.Single(round.Refreshes);
        // 同槽用替换后的编号请求属于第二次刷新，而不是上一请求重试。
        Assert.False(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 1,
            expectedHexId = round.Candidates[1],
        })));

        // 另外两个槽仍可用，并且可以按 2 → 0 的顺序分别消费自己的机会。
        Assert.True(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 2,
            expectedHexId = before[2],
        })));
        Assert.True(HexRules.RefreshAvailableForCandidate(round, 0, engine.State.HexState.RulesRevision));
        Assert.False(HexRules.RefreshAvailableForCandidate(round, 2, engine.State.HexState.RulesRevision));
        Assert.True(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 0,
            expectedHexId = before[0],
        })));
        Assert.Equal(0, HexRules.RefreshRemaining(round, engine.State.HexState.RulesRevision));
        Assert.Equal([0, 1, 2], HexRules.RefreshedCandidateIndices(
            round,
            engine.State.HexState.RulesRevision));
        Assert.Equal(3, round.Refreshes.Count);
        Assert.Equal(3, logs.Count(kind => kind == "hex_draft_candidate_refreshed"));
        Assert.Equal(6, engine.State.HexState.Appeared[0].Count);
        Assert.Empty(engine.State.HexState.Appeared[1]);
        Assert.Equal(6, engine.State.HexState.Appeared[0].Intersect(
            HexCatalog.ForTier(round.Tier).Select(item => item.Id)).Count());

        // 把槽 1 的旧期望编号挪到槽 0 不构成幂等重试。
        Assert.False(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 0,
            expectedHexId = before[1],
        })));

        Assert.False(engine.HandleAction(0, "ChooseHex", Json(new
        {
            roundId = round.RoundId,
            hexId = before[1],
        })));
        int choice = round.Candidates.First(id => id is not 6 and not 47 and not 55 and not 56);
        Assert.True(engine.HandleAction(0, "ChooseHex", Json(new { roundId = round.RoundId, hexId = choice })));
        await engine.WaitSettledAsync();
        Assert.Contains(choice, engine.State.HexState.Owned[0]);

        Assert.False(engine.HandleAction(0, "RefreshHex", Json(refreshSlot1)));
        Assert.False(engine.HandleAction(1, "RefreshHex", Json(refreshSlot1)));
        // 已结算轮次上同一个选择是幂等成功，不会重复授予。
        Assert.True(engine.HandleAction(0, "ChooseHex", Json(new { roundId = round.RoundId, hexId = choice })));
        Assert.Single(engine.State.HexState.ResolvedDrafts.Where(item => item.RoundId == round.RoundId));
    }

    [Fact]
    public async Task 刷新与超时自动选择_动作来源确定性重放且当前候选生效()
    {
        const int seed = 987654;
        const string roomId = "hex-refresh-timeout-replay";
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
        int removed = round.Candidates[0];
        await Apply(0, "RefreshHex", new
        {
            roundId = round.RoundId,
            candidateIndex = 0,
            expectedHexId = removed,
        }, GameActionSource.Player);
        int replacement = round.Candidates[0];
        Assert.NotEqual(removed, replacement);

        for (int candidateIndex = 1; candidateIndex < 3; candidateIndex++)
        {
            int expected = round.Candidates[candidateIndex];
            await Apply(0, "RefreshHex", new
            {
                roundId = round.RoundId,
                candidateIndex,
                expectedHexId = expected,
            }, GameActionSource.Player);
        }
        Assert.Equal(3, round.Refreshes.Count);
        Assert.Equal(6, live.State.HexState.Appeared[0].Count);

        // 在尚未选择时模拟进程重启：逐槽按钮、出现历史与 RNG 必须完整恢复。
        var activeRebuilt = await MatchReplay.RebuildAsync(
            roomId,
            seed,
            firstPlayer: 0,
            ("alice", BuildLegalDeck()),
            ("bob", BuildLegalDeck()),
            tape,
            matchKind: MatchKind.Hex);
        var rebuiltRound = Assert.IsType<HexDraftRound>(activeRebuilt.State.HexState.ActiveDraft);
        Assert.Equal([0, 1, 2], HexRules.RefreshedCandidateIndices(
            rebuiltRound,
            activeRebuilt.State.HexState.RulesRevision));
        Assert.Equal(0, HexRules.RefreshRemaining(
            rebuiltRound,
            activeRebuilt.State.HexState.RulesRevision));
        Assert.Equal(live.State.HexState.Appeared[0].Order(), activeRebuilt.State.HexState.Appeared[0].Order());
        Assert.Equal(
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(live.State)),
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(activeRebuilt.State)));
        var persisted = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(live.State));
        Assert.Equal(6, persisted.GetProperty("hexState").GetProperty("appeared")[0].GetArrayLength());
        Assert.Equal(3, persisted.GetProperty("hexState").GetProperty("activeDraft")
            .GetProperty("refreshes").GetArrayLength());
        var fullCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(live.State);
        Assert.Equal(6, fullCheckpoint.GetProperty("hexState").GetProperty("appeared")[0].GetArrayLength());
        Assert.Equal(3, fullCheckpoint.GetProperty("hexState").GetProperty("activeDraft")
            .GetProperty("refreshes").GetArrayLength());
        var publicCheckpoint = DeterministicReplayCheckpointProvider.BuildPublicState(live.State);
        Assert.Equal(6, publicCheckpoint.GetProperty("hexState").GetProperty("appearedCounts")[0].GetInt32());
        Assert.False(publicCheckpoint.GetProperty("hexState").TryGetProperty("appeared", out _));

        Assert.False(live.HandleAction(0, "ChooseHex", Json(new { roundId = round.RoundId, auto = true })));
        await Apply(0, "ChooseHex", new { roundId = round.RoundId, auto = true }, GameActionSource.System);
        Assert.Contains(live.State.HexState.Owned[0].Single(), round.Candidates);

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
            live.State.HexState.ResolvedDrafts.Select(item => item.Choice),
            rebuilt.State.HexState.ResolvedDrafts.Select(item => item.Choice));
    }

    [Fact]
    public async Task 整局出现历史_同玩家连续同品质全刷十八张无重复且双方历史独立()
    {
        var engine = CreateEngine(seed: 26090218);
        var state = engine.State;
        state.HexState.DraftTierSequence.Clear();
        state.HexState.DraftTierSequence.AddRange([HexTier.Silver, HexTier.Silver, HexTier.Silver]);
        var allShown = new HashSet<int>();

        foreach (int ownTurn in HexRules.DraftOwnTurns)
        {
            var round = HexRules.StartDraft(
                state,
                playerIndex: 0,
                ownTurn,
                HexDraftResumePoint.None);
            var shownThisRound = new HashSet<int>(round.Candidates);
            Assert.Empty(allShown.Intersect(shownThisRound));

            for (int candidateIndex = 0; candidateIndex < round.Candidates.Count; candidateIndex++)
            {
                int expected = round.Candidates[candidateIndex];
                var refreshed = HexRules.RefreshCandidate(
                    state,
                    playerIndex: 0,
                    round.RoundId,
                    candidateIndex,
                    expected);
                Assert.Equal(HexRefreshStatus.Refreshed, refreshed.Status);
                Assert.True(shownThisRound.Add(round.Candidates[candidateIndex]));
            }

            Assert.Equal(6, shownThisRound.Count);
            Assert.Empty(allShown.Intersect(shownThisRound));
            allShown.UnionWith(shownThisRound);

            int choice = round.Candidates.First(id => id is not 6 and not 47 and not 55 and not 56);
            Assert.Equal(
                HexChoiceStatus.Ready,
                HexRules.LockChoice(state, 0, round.RoundId, choice, automatic: false).Status);
            await HexRules.ResolveDraftAsync(engine);
        }

        Assert.Equal(18, allShown.Count);
        Assert.Equal(SilverIds, allShown.Order().ToArray());
        Assert.Equal(allShown.Order(), state.HexState.Appeared[0].Order());
        Assert.Empty(state.HexState.Appeared[1]);

        // P0 已经看完整个银色池，P1 仍按自己的空历史正常获得三个候选。
        var playerOneRound = HexRules.StartDraft(
            state,
            playerIndex: 1,
            ownTurnNumber: 1,
            HexDraftResumePoint.None);
        Assert.Equal(3, playerOneRound.Candidates.Count);
        Assert.All(playerOneRound.Candidates, id => Assert.Contains(id, state.HexState.Appeared[0]));
        Assert.Equal(playerOneRound.Candidates.Order(), state.HexState.Appeared[1].Order());
    }

    [Fact]
    public void 候选不足_启动选秀与刷新均明确拒绝且不消耗随机状态()
    {
        var startEngine = CreateEngine(seed: 26090202);
        var startState = startEngine.State;
        startState.HexState.DraftTierSequence.Clear();
        startState.HexState.DraftTierSequence.AddRange([HexTier.Silver, HexTier.Silver, HexTier.Silver]);
        startState.HexState.Appeared[0].UnionWith(SilverIds.Take(16));
        int draftSequenceBefore = startState.HexState.DraftSequence;
        int randomBefore = startState.RandomSeq;

        var startError = Assert.Throws<InvalidOperationException>(() => HexRules.StartDraft(
            startState,
            playerIndex: 0,
            ownTurnNumber: 1,
            HexDraftResumePoint.None));
        Assert.Contains("未出现候选不足", startError.Message);
        Assert.Null(startState.HexState.ActiveDraft);
        Assert.Equal(draftSequenceBefore, startState.HexState.DraftSequence);
        Assert.Equal(randomBefore, startState.RandomSeq);
        Assert.Equal(16, startState.HexState.Appeared[0].Count);

        var refreshEngine = CreateEngine(seed: 26090203);
        var refreshState = refreshEngine.State;
        refreshState.HexState.DraftTierSequence.Clear();
        refreshState.HexState.DraftTierSequence.AddRange([HexTier.Silver, HexTier.Silver, HexTier.Silver]);
        refreshState.HexState.Appeared[0].UnionWith(SilverIds.Take(15));
        var round = HexRules.StartDraft(
            refreshState,
            playerIndex: 0,
            ownTurnNumber: 1,
            HexDraftResumePoint.None);
        Assert.Equal(18, refreshState.HexState.Appeared[0].Count);
        var candidatesBefore = round.Candidates.ToArray();
        randomBefore = refreshState.RandomSeq;

        var refresh = HexRules.RefreshCandidate(
            refreshState,
            playerIndex: 0,
            round.RoundId,
            candidateIndex: 0,
            expectedHexId: round.Candidates[0]);
        Assert.Equal(HexRefreshStatus.Rejected, refresh.Status);
        Assert.Contains("没有未出现过", refresh.Reason);
        Assert.Equal(candidatesBefore, round.Candidates);
        Assert.Empty(round.Refreshes);
        Assert.Equal(randomBefore, refreshState.RandomSeq);
    }

    [Fact]
    public async Task 上一规则修订版_继续沿用整轮一次刷新且不会写入新版出现历史()
    {
        const int previousRulesRevision = HexRules.BalanceRulesRevision;
        var engine = CreateEngine(seed: 26090204);
        HexRules.SetRulesRevisionForReplay(engine.State, previousRulesRevision);
        await MulliganBoth(engine);
        var round = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        var before = round.Candidates.ToArray();

        Assert.True(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 0,
            expectedHexId = before[0],
        })));
        Assert.False(engine.HandleAction(0, "RefreshHex", Json(new
        {
            roundId = round.RoundId,
            candidateIndex = 1,
            expectedHexId = before[1],
        })));
        Assert.Empty(round.Refreshes);
        Assert.Empty(engine.State.HexState.Appeared[0]);
        Assert.Equal(0, HexRules.RefreshRemaining(round, previousRulesRevision));
        Assert.False(HexRules.RefreshAvailableForCandidate(round, 1, previousRulesRevision));

        var snapshot = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 0));
        var draft = snapshot.GetProperty("hexState").GetProperty("activeDraft");
        Assert.Equal(0, draft.GetProperty("refreshRemaining").GetInt32());
        Assert.All(draft.GetProperty("refreshAvailableByCandidate").EnumerateArray(), item => Assert.False(item.GetBoolean()));
        Assert.Equal(new[] { 0 }, draft.GetProperty("refreshedCandidateIndices")
            .EnumerateArray().Select(item => item.GetInt32()).ToArray());

        var legacyFullCheckpoint = DeterministicReplayCheckpointProvider.BuildFullState(engine.State)
            .GetProperty("hexState");
        Assert.False(legacyFullCheckpoint.TryGetProperty("appeared", out _));
        Assert.False(legacyFullCheckpoint.GetProperty("activeDraft").TryGetProperty("refreshes", out _));
        var legacyPublicCheckpoint = DeterministicReplayCheckpointProvider.BuildPublicState(engine.State)
            .GetProperty("hexState");
        Assert.False(legacyPublicCheckpoint.TryGetProperty("appearedCounts", out _));
        Assert.False(legacyPublicCheckpoint.GetProperty("activeDraft")
            .TryGetProperty("refreshedCandidateIndices", out _));
    }

    [Fact]
    public async Task 旧版海克斯房间_锁定旧池规则版本后动作重放与私有快照一致()
    {
        const int seed = 20260901;
        const string roomId = "hex-legacy-rules-replay";
        var live = CreateEngine(seed, roomId);
        HexRules.SetRulesRevisionForReplay(live.State, HexRules.LegacyRulesRevision);
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
        Assert.All(round.Candidates, id => Assert.Contains(id,
            HexCatalog.ForTier(HexTier.Silver, HexRules.LegacyRulesRevision).Select(item => item.Id)));
        int choice = round.Candidates.First(id => id is not 20 and not 52);
        await Apply(0, "ChooseHex", new { roundId = round.RoundId, hexId = choice });

        var rebuilt = await MatchReplay.RebuildAsync(
            roomId,
            seed,
            firstPlayer: 0,
            ("alice", BuildLegalDeck()),
            ("bob", BuildLegalDeck()),
            tape,
            matchKind: MatchKind.Hex,
            hexRulesRevision: HexRules.LegacyRulesRevision);

        Assert.Equal(HexRules.LegacyRulesRevision, rebuilt.State.HexState.RulesRevision);
        Assert.Equal(
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(live.State)),
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(rebuilt.State)));
    }

    [Fact]
    public async Task 选秀授予故障_单玩家步骤游标前移且重试不重复已完成效果()
    {
        var engine = CreateEngine(seed: 490049);
        var player = engine.State.Players[0];
        int handBefore = player.Hand.Count;
        var round = CreateLockedDraft(engine.State, HexTier.Gold, playerIndex: 0, choice: 49);
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
        Assert.True(engine.State.HexState.BlocksOrdinaryActionsFor(0));
        Assert.False(engine.State.HexState.BlocksOrdinaryActionsFor(1));

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
        Assert.Equal(0, resolved.PlayerIndex);
        Assert.Equal(handBefore + 3, player.Hand.Count);
        Assert.Null(engine.State.HexState.PendingSettlement);
        Assert.False(engine.State.HexState.DraftResolving);
        Assert.Single(engine.State.HexState.ResolvedDrafts.Where(item => item.RoundId == round.RoundId));
    }

    [Fact]
    public async Task 星界躯体授予故障_恢复时不重复手牌入生命并继续卡组顶步骤()
    {
        var engine = CreateEngine(seed: 600006);
        var player = engine.State.Players[0];
        player.Hand.Clear();
        player.Deck.Clear();
        player.LifeArea.Clear();
        var handCard = TestCard("HEX-ASTRAL-HAND");
        var deckTop = TestCard("HEX-ASTRAL-DECK-TOP");
        var deckBottom = TestCard("HEX-ASTRAL-DECK-BOTTOM");
        player.Hand.Add(handCard);
        player.Deck.AddRange([deckTop, deckBottom]);
        var round = CreateLockedDraft(engine.State, HexTier.Gold, playerIndex: 0, choice: 6);
        engine.State.HexState.GrantStepFaultInjector = boundary =>
        {
            if (boundary.HexId == 6 && boundary.CompletedStep == 1)
                throw new InjectedDraftSettlementException();
        };

        var resolving = HexRules.ResolveDraftAsync(engine);
        await RespondToPendingPrompt(engine);
        await Assert.ThrowsAsync<InjectedDraftSettlementException>(() => resolving);

        Assert.Empty(player.Hand);
        Assert.Equal([handCard], player.LifeArea);
        Assert.Equal([deckTop, deckBottom], player.Deck);
        var pending = Assert.IsType<HexDraftSettlement>(engine.State.HexState.PendingSettlement);
        var grant = Assert.Single(pending.Grants);
        Assert.Equal(1, grant.NextStep);
        Assert.Equal(2, grant.PlannedStepCount);

        var persistedGrant = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(engine.State))
            .GetProperty("hexState")
            .GetProperty("pendingSettlement")
            .GetProperty("grants")[0];
        Assert.Equal(1, persistedGrant.GetProperty("NextStep").GetInt32());
        Assert.Equal(2, persistedGrant.GetProperty("PlannedStepCount").GetInt32());

        engine.State.HexState.GrantStepFaultInjector = null;
        var (resolved, _) = await HexRules.ResolveDraftAsync(engine);

        Assert.Equal(round.RoundId, resolved.RoundId);
        Assert.Empty(player.Hand);
        Assert.Equal([deckTop, handCard], player.LifeArea);
        Assert.Equal([deckBottom], player.Deck);
        Assert.Null(engine.State.HexState.PendingSettlement);
    }

    [Fact]
    public async Task 旧版星界躯体选秀结算_保持两张手牌入生命且不取卡组顶()
    {
        var engine = CreateEngine(seed: 500006);
        var state = engine.State;
        HexRules.SetRulesRevisionForReplay(state, HexRules.CatalogConfigurationRulesRevision);
        var player = state.Players[0];
        player.Hand.Clear();
        player.Deck.Clear();
        player.LifeArea.Clear();
        var firstHand = TestCard("HEX-LEGACY-DRAFT-H1");
        var secondHand = TestCard("HEX-LEGACY-DRAFT-H2");
        var deckTop = TestCard("HEX-LEGACY-DRAFT-D1");
        player.Hand.AddRange([firstHand, secondHand]);
        player.Deck.Add(deckTop);
        CreateLockedDraft(state, HexTier.Gold, playerIndex: 0, choice: 6);

        var resolving = HexRules.ResolveDraftAsync(engine);
        await RespondToPendingPrompt(engine);
        await RespondToPendingPrompt(engine);
        await resolving;

        Assert.Empty(player.Hand);
        Assert.Equal([secondHand, firstHand], player.LifeArea);
        Assert.Equal([deckTop], player.Deck);
        Assert.Null(state.HexState.PendingSettlement);
    }

    [Fact]
    public async Task 黄金阶授予故障_随机计划持久化且重试不重抽不重复子授予()
    {
        var engine = CreateEngine(seed: 550056);
        var state = engine.State;
        state.HexState.Owned[0].AddRange(HexCatalog.Regular
            .Select(item => item.Id)
            .Where(id => id is not 55 and not 1));
        int randomBefore = state.RandomSeq;
        var round = CreateLockedDraft(state, HexTier.Silver, playerIndex: 0, choice: 55);
        state.HexState.GrantStepFaultInjector = boundary =>
        {
            if (boundary.HexId == 55 && boundary.CompletedStep == 1)
                throw new InjectedDraftSettlementException();
        };

        await Assert.ThrowsAsync<InjectedDraftSettlementException>(() => HexRules.ResolveDraftAsync(engine));

        var pending = Assert.IsType<HexDraftSettlement>(state.HexState.PendingSettlement);
        var rootGrant = Assert.Single(pending.Grants.Where(item => item.HexId == 55));
        Assert.Equal(1, rootGrant.NextStep);
        Assert.Equal(1, rootGrant.PlannedStepCount);
        Assert.Equal([1], rootGrant.PlannedChildHexIds);
        Assert.Contains(1, state.HexState.Owned[0]);
        Assert.Contains(1, state.HexState.GrantedByTransmutation[0]);
        Assert.Equal(randomBefore + 1, state.RandomSeq);

        var privateState = JsonSerializer.SerializeToElement(PrivateStateSnapshotBuilder.Build(state));
        var persistedRoot = privateState.GetProperty("hexState")
            .GetProperty("pendingSettlement")
            .GetProperty("grants")[0];
        Assert.Equal(1, persistedRoot.GetProperty("plannedChildHexIds")[0].GetInt32());

        state.HexState.GrantStepFaultInjector = null;
        var (resolved, _) = await HexRules.ResolveDraftAsync(engine);

        Assert.Equal(round.RoundId, resolved.RoundId);
        Assert.Contains(1, state.HexState.Owned[0]);
        Assert.Equal(randomBefore + 1, state.RandomSeq);
        Assert.Equal(state.HexState.Owned[0].Count, state.HexState.Owned[0].Distinct().Count());
        Assert.DoesNotContain(state.HexState.Owned[0], HexCatalog.IsAlternative);
        Assert.Null(state.HexState.PendingSettlement);
    }

    private static HexDraftRound AssertDraft(
        GameEngine engine,
        int player,
        int ownTurn,
        HexTier tier)
    {
        var round = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        Assert.Equal(player, round.PlayerIndex);
        Assert.Equal(ownTurn, round.OwnTurnNumber);
        Assert.Equal(tier, round.Tier);
        Assert.Equal(3, round.Candidates.Distinct().Count());
        Assert.All(round.Candidates, id =>
        {
            Assert.Equal(tier, HexCatalog.Get(id).Tier);
            Assert.False(HexCatalog.IsAlternative(id));
        });
        return round;
    }

    private static void AssertPrivateDraft(GameEngine engine, HexDraftRound round)
    {
        int owner = round.PlayerIndex;
        var ownerView = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, owner));
        var otherView = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, 1 - owner));
        var spectator = JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(engine.State, -1));
        var ownerHex = ownerView.GetProperty("hexState");
        Assert.Equal(round.Candidates, CandidateIds(ownerHex.GetProperty("activeDraft")));
        Assert.Equal(
            HexCatalog.TierDisplayName(round.Tier),
            ownerHex.GetProperty("activeDraft").GetProperty("tierLabel").GetString());
        Assert.Equal(
            engine.State.HexState.DraftTierSequence.Select(tier => tier.ToString()),
            ownerHex.GetProperty("tierSequence").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(JsonValueKind.Null, otherView.GetProperty("hexState").GetProperty("activeDraft").ValueKind);
        Assert.Equal(JsonValueKind.Null, spectator.GetProperty("hexState").GetProperty("activeDraft").ValueKind);
    }

    private static async Task MulliganBoth(GameEngine engine)
    {
        Assert.True(engine.HandleAction(0, "Mulligan", Json(new { redraw = false })));
        Assert.True(engine.HandleAction(1, "Mulligan", Json(new { redraw = false })));
        await engine.WaitSettledAsync();
    }

    private static async Task EndTurn(GameEngine engine)
    {
        Assert.Null(engine.State.PendingPrompt);
        Assert.True(engine.HandleAction(engine.State.CurrentTurnPlayer, "EndTurn", Json(new { })));
        await engine.WaitSettledAsync();
    }

    private static async Task ResolveCurrentDraft(GameEngine engine)
    {
        var round = Assert.IsType<HexDraftRound>(engine.State.HexState.ActiveDraft);
        int choice = round.Candidates.First(id => id is not 6 and not 47 and not 55 and not 56);
        Assert.True(engine.HandleAction(round.PlayerIndex, "ChooseHex", Json(new
        {
            roundId = round.RoundId,
            hexId = choice,
        })));
        await engine.WaitSettledAsync();
        Assert.Null(engine.State.PendingPrompt);
        Assert.Null(engine.State.HexState.ActiveDraft);
    }

    private static HexDraftRound CreateLockedDraft(
        GameState state,
        HexTier tier,
        int playerIndex,
        int choice)
    {
        var round = new HexDraftRound
        {
            RoundId = $"fault-{tier.ToString().ToLowerInvariant()}",
            PlayerIndex = playerIndex,
            OwnTurnNumber = 3,
            Tier = tier,
            DeadlineUtc = DateTime.UtcNow.AddMinutes(1),
            LockedChoice = choice,
            Locked = true,
        };
        round.Candidates.Add(choice);
        state.HexState.ActiveDraft = round;
        state.HexState.ResumePoint = HexDraftResumePoint.None;
        return round;
    }

    private static async Task RespondToPendingPrompt(GameEngine engine, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (engine.State.PendingPrompt is null && Environment.TickCount64 < deadline)
            await Task.Delay(5);
        var prompt = Assert.IsType<PendingPrompt>(engine.State.PendingPrompt);
        Assert.Equal("HexAstralBody", prompt.Kind);
        Assert.Equal(1, prompt.MinChoose);
        Assert.Equal(1, prompt.MaxChoose);
        Assert.False(engine.HandleAction(prompt.PlayerIndex, "PromptResponse", Json(new
        {
            promptId = prompt.PromptId,
            chosen = Array.Empty<string>(),
        })));
        Assert.Same(prompt, engine.State.PendingPrompt);
        Assert.True(engine.HandleAction(prompt.PlayerIndex, "PromptResponse", Json(new
        {
            promptId = prompt.PromptId,
            chosen = prompt.ValidChoices.Take(Math.Max(prompt.MinChoose, 1)).ToArray(),
        })));
    }

    private static CardInstance TestCard(string number)
        => new()
        {
            Info = new CardInfo
            {
                Number = number,
                Name = number,
                Color = "红",
                Kind = CardKind.Character,
                Property = "打",
            },
        };

    private sealed class InjectedDraftSettlementException : Exception;

    private static int[] CandidateIds(JsonElement draft)
        => draft.GetProperty("candidates")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .ToArray();

    private static JsonElement DeliveredSnapshot(
        IEnumerable<(int PlayerIndex, JsonElement Snapshot)> delivered,
        int playerIndex,
        string lastAction)
        => Assert.Single(delivered.Where(item =>
            item.PlayerIndex == playerIndex
            && item.Snapshot.GetProperty("lastAction").GetString() == lastAction)).Snapshot;

    private static void AssertSingleOwnedDefinition(JsonElement owned, HexDefinition definition)
    {
        var actual = Assert.Single(owned.EnumerateArray());
        Assert.Equal(definition.Id, actual.GetProperty("id").GetInt32());
        Assert.Equal(definition.Name, actual.GetProperty("name").GetString());
        Assert.Equal(definition.Tier.ToString(), actual.GetProperty("tier").GetString());
        Assert.Equal(HexCatalog.TierDisplayName(definition.Tier), actual.GetProperty("tierLabel").GetString());
        Assert.Equal(definition.Description, actual.GetProperty("description").GetString());
    }

    private static int[] OwnedHexIds(JsonElement hexState)
        => hexState.GetProperty("myOwned").EnumerateArray()
            .Concat(hexState.GetProperty("opponentOwned").EnumerateArray())
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
