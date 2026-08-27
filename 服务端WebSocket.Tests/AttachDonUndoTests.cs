using System.Text.Json;
using System.Reflection;
using GrandUMI.Cards;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

public class AttachDonUndoTests
{
    private static readonly JsonElement EmptyData = JsonSerializer.SerializeToElement(new { });

    [Fact]
    public void UndoSnapshot_IsNonReplaceableSoItsRequestReceiptAndActionSemanticCannotBeMergedAway()
    {
        var method = typeof(WebSocketBridge).GetMethod(
            "IsReplaceableStateSnapshot",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var snapshot = new
        {
            proto = "MsgGameState",
            lastAction = "UndoAttachDon",
            effectActivations = Array.Empty<object>(),
        };

        Assert.False((bool)method.Invoke(null, [snapshot])!);
    }

    [Fact]
    public async Task AttachAndUndo_AreServerAuthoritativeAndRestoreExactDonBatch()
    {
        var engine = CreateMainPhaseEngine();
        var state = engine.State;
        var me = state.Players[0];
        var firstDon = me.CostArea[0];
        var secondDon = me.CostArea[1];
        var untouchedDon = me.CostArea[2];

        var entry = await AttachAsync(engine, "leader", 2);

        Assert.Equal([firstDon.Id, secondDon.Id], entry.DonIds);
        Assert.Equal(DonState.Attached, firstDon.State);
        Assert.Equal(DonState.Attached, secondDon.State);
        Assert.Equal(DonState.Active, untouchedDon.State);
        Assert.Equal(me.Leader.Id, firstDon.AttachedToCardId);

        var ownerSnapshot = Snapshot(state, 0);
        Assert.True(ownerSnapshot.GetProperty("canUndoAttachDon").GetBoolean());
        Assert.Equal(entry.OperationSequence.ToString(), ownerSnapshot.GetProperty("undoAttachDonOperationId").GetString());
        Assert.Equal(2, ownerSnapshot.GetProperty("undoAttachDonCount").GetInt32());
        Assert.Equal(1, ownerSnapshot.GetProperty("undoAttachDonDepth").GetInt32());

        var opponentSnapshot = Snapshot(state, 1);
        Assert.False(opponentSnapshot.GetProperty("canUndoAttachDon").GetBoolean());
        Assert.Equal(JsonValueKind.Null, opponentSnapshot.GetProperty("undoAttachDonOperationId").ValueKind);
        var spectatorSnapshot = Snapshot(state, -1);
        Assert.False(spectatorSnapshot.GetProperty("canUndoAttachDon").GetBoolean());

        Assert.True(engine.HandleAction(0, "UndoAttachDon", UndoData(entry)));
        await engine.WaitSettledAsync();

        Assert.Equal(DonState.Active, firstDon.State);
        Assert.Equal(DonState.Active, secondDon.State);
        Assert.Equal(DonState.Active, untouchedDon.State);
        Assert.Null(firstDon.AttachedToCardId);
        Assert.Null(secondDon.AttachedToCardId);
        Assert.Empty(state.AttachDonUndoStack);
        Assert.False(Snapshot(state, 0).GetProperty("canUndoAttachDon").GetBoolean());
    }

    [Fact]
    public async Task SequentialAttach_RequiresCurrentTokenAndUndoesInLifoOrder()
    {
        var engine = CreateMainPhaseEngine();
        var state = engine.State;
        var me = state.Players[0];
        var character = new CardInstance { Info = CardDatabase.Get("OP15-003")!, TurnPlayed = 0 };
        me.Characters.Add(character);

        var first = await AttachAsync(engine, "leader", 1);
        var second = await AttachAsync(engine, character.Id.ToString(), 1);
        Assert.Equal(2, state.AttachDonUndoStack.Count);

        // 延迟到达的旧令牌不能误撤后来贴到角色上的咚。
        Assert.False(engine.HandleAction(0, "UndoAttachDon", UndoData(first)));
        Assert.Equal(2, state.AttachDonUndoStack.Count);
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(1, me.AttachedDonCount(character.Id));

        Assert.True(engine.HandleAction(0, "UndoAttachDon", UndoData(second)));
        await engine.WaitSettledAsync();
        Assert.Single(state.AttachDonUndoStack);
        Assert.Equal(first.OperationSequence, state.AttachDonUndoStack[^1].OperationSequence);
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));
        Assert.Equal(0, me.AttachedDonCount(character.Id));

        // 同一撤回再次到达也只能被拒绝，不能继续弹出并撤掉前一批。
        Assert.False(engine.HandleAction(0, "UndoAttachDon", UndoData(second)));
        Assert.Single(state.AttachDonUndoStack);
        Assert.Equal(1, me.AttachedDonCount(me.Leader.Id));

        Assert.True(engine.HandleAction(0, "UndoAttachDon", UndoData(first)));
        await engine.WaitSettledAsync();
        Assert.Empty(state.AttachDonUndoStack);
        Assert.Equal(3, me.ActiveDonCount);
    }

    [Fact]
    public async Task OtherAcceptedActionInvalidatesUndo_ButRejectedOrUnauthorizedActionDoesNot()
    {
        var preserved = CreateMainPhaseEngine();
        var preservedEntry = await AttachAsync(preserved, "leader", 1);

        Assert.False(preserved.HandleAction(1, "EndTurn", EmptyData));
        Assert.Single(preserved.State.AttachDonUndoStack);
        Assert.False(preserved.HandleAction(1, "UndoAttachDon", UndoData(preservedEntry)));
        Assert.Single(preserved.State.AttachDonUndoStack);
        Assert.True(preserved.HandleAction(0, "UndoAttachDon", UndoData(preservedEntry)));
        await preserved.WaitSettledAsync();

        var invalidated = CreateMainPhaseEngine();
        var invalidatedEntry = await AttachAsync(invalidated, "leader", 1);
        Assert.True(invalidated.HandleAction(0, "EndTurn", EmptyData));
        await invalidated.WaitSettledAsync();

        Assert.Empty(invalidated.State.AttachDonUndoStack);
        Assert.False(invalidated.HandleAction(0, "UndoAttachDon", UndoData(invalidatedEntry)));
        Assert.Equal(1, invalidated.State.Players[0].AttachedDonCount(invalidated.State.Players[0].Leader.Id));
    }

    [Fact]
    public async Task UndoDuringAttachTriggeredPrompt_CancelsOnlyThatEffectAndLeavesNoPartialMutation()
    {
        var engine = CreateMainPhaseEngine("OP02-002\nOP15-003");
        var state = engine.State;
        var me = state.Players[0];
        var target = new CardInstance { Info = CardDatabase.Get("OP15-003")!, TurnPlayed = 0 };
        state.Players[1].Characters.Add(target);

        Assert.True(engine.HandleAction(0, "AttachDon", JsonSerializer.SerializeToElement(new
        {
            targetId = "leader",
            count = 1,
        })));
        await engine.WaitSettledAsync();
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);
        var entry = Assert.Single(state.AttachDonUndoStack);

        Assert.True(engine.HandleAction(0, "UndoAttachDon", UndoData(entry)));
        await engine.WaitSettledAsync();

        Assert.Null(state.PendingPrompt);
        Assert.Equal(0, target.CostModThisTurn);
        Assert.Equal(3, me.ActiveDonCount);
        Assert.Equal(0, me.AttachedDonCount(me.Leader.Id));
        Assert.Empty(state.AttachDonUndoStack);
        Assert.False(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { target.Id.ToString() },
        })));
    }

    [Fact]
    public async Task PromptResponseAfterAttach_IsAnotherActionAndPermanentlyInvalidatesUndo()
    {
        var engine = CreateMainPhaseEngine("OP02-002\nOP15-003");
        var state = engine.State;
        var target = new CardInstance { Info = CardDatabase.Get("OP15-003")!, TurnPlayed = 0 };
        state.Players[1].Characters.Add(target);

        Assert.True(engine.HandleAction(0, "AttachDon", JsonSerializer.SerializeToElement(new
        {
            targetId = "leader",
            count = 1,
        })));
        await engine.WaitSettledAsync();
        var entry = Assert.Single(state.AttachDonUndoStack);
        var prompt = Assert.IsType<PendingPrompt>(state.PendingPrompt);

        Assert.True(engine.HandleAction(0, "PromptResponse", JsonSerializer.SerializeToElement(new
        {
            promptId = prompt.PromptId,
            chosen = new[] { target.Id.ToString() },
        })));
        await engine.WaitSettledAsync();

        Assert.Empty(state.AttachDonUndoStack);
        Assert.Equal(-1, target.CostModThisTurn);
        Assert.False(engine.HandleAction(0, "UndoAttachDon", UndoData(entry)));
    }

    [Fact]
    public async Task ReplayRebuild_RestoresUndoEligibilityAndReplaysUndoDeterministically()
    {
        TestScene.New();
        const string roomId = "attach-don-undo-replay";
        const int seed = 20260826;
        var deck = BuildLegalDeck("OP16-080", "OP16");
        var live = new GameEngine(roomId, ("s0", "alice", deck), ("s1", "bob", deck), 0, seed);
        var tape = new List<MatchReplay.ActionEntry>();
        string? lastRejection = null;
        live.OnSendToPlayer = (_, payload) =>
        {
            var message = JsonSerializer.SerializeToElement(payload);
            if (message.TryGetProperty("proto", out var proto)
                && proto.GetString() == "MsgActionRejected")
                lastRejection = message.GetProperty("reason").GetString();
        };

        async Task Apply(int playerIndex, string action, JsonElement data)
        {
            lastRejection = null;
            Assert.True(live.HandleAction(playerIndex, action, data),
                $"动作 {action} 被拒绝：{lastRejection ?? "无原因"}");
            tape.Add(new MatchReplay.ActionEntry(playerIndex, action, data.Clone()));
            await live.WaitSettledAsync();
        }

        await Apply(0, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        await Apply(1, "Mulligan", JsonSerializer.SerializeToElement(new { redraw = false }));
        Assert.True(live.State.MulliganBothDone);
        Assert.Equal(Phase.Main, live.State.Phase);
        Assert.Null(live.State.PendingPrompt);
        Assert.True(live.State.Players[0].ActiveDonCount > 0,
            $"首回合应有活跃咚，实际费用区={live.State.Players[0].CostArea.Count}，咚卡组={live.State.Players[0].DonDeck.Count}");
        await Apply(0, "AttachDon", JsonSerializer.SerializeToElement(new { targetId = "leader", count = 1 }));
        var liveEntry = Assert.Single(live.State.AttachDonUndoStack);

        var rebuiltWithAttach = await MatchReplay.RebuildAsync(
            roomId, seed, 0, ("alice", deck), ("bob", deck), tape);
        var rebuiltEntry = Assert.Single(rebuiltWithAttach.State.AttachDonUndoStack);
        Assert.Equal(liveEntry.OperationSequence, rebuiltEntry.OperationSequence);
        Assert.Equal(liveEntry.DonIds, rebuiltEntry.DonIds);
        Assert.True(Snapshot(rebuiltWithAttach.State, 0).GetProperty("canUndoAttachDon").GetBoolean());

        await Apply(0, "UndoAttachDon", UndoData(liveEntry));
        var rebuiltWithUndo = await MatchReplay.RebuildAsync(
            roomId, seed, 0, ("alice", deck), ("bob", deck), tape);

        Assert.Equal(
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(live.State)),
            JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(rebuiltWithUndo.State)));
        Assert.Empty(rebuiltWithUndo.State.AttachDonUndoStack);
    }

    private static GameEngine CreateMainPhaseEngine(string player0Deck = "OP15-001\nOP15-003")
    {
        TestScene.New();
        const string opponentDeck = "OP15-001\nOP15-003";
        var engine = new GameEngine(
            $"attach-don-undo-{Guid.NewGuid():N}",
            ("s0", "alice", player0Deck),
            ("s1", "bob", opponentDeck),
            firstPlayer: 0,
            rngSeed: 20260826);
        var state = engine.State;
        state.CurrentTurnPlayer = 0;
        state.TurnCount = 3;
        state.Phase = Phase.Main;
        state.Players[0].CostArea.Clear();
        state.Players[0].CostArea.AddRange([
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
            new DonCard { State = DonState.Active },
        ]);
        state.Players[1].Characters.Clear();
        return engine;
    }

    private static async Task<AttachDonUndoEntry> AttachAsync(
        GameEngine engine,
        string targetId,
        int count)
    {
        Assert.True(engine.HandleAction(0, "AttachDon", JsonSerializer.SerializeToElement(new
        {
            targetId,
            count,
        })));
        await engine.WaitSettledAsync();
        return engine.State.AttachDonUndoStack[^1];
    }

    private static JsonElement UndoData(AttachDonUndoEntry entry)
        => JsonSerializer.SerializeToElement(new { operationId = entry.OperationSequence.ToString() });

    private static JsonElement Snapshot(GameState state, int viewerIndex)
        => JsonSerializer.SerializeToElement(StateSnapshotBuilder.Build(state, viewerIndex));

    private static string BuildLegalDeck(string leaderNumber, string setCode)
    {
        var leader = CardDatabase.Get(leaderNumber)!;
        var pool = CardDatabase.GetBySet(setCode)
            .Where(card => card.Kind != CardKind.Leader && card.SharesColorWith(leader))
            .ToArray();
        var lines = new List<string> { leaderNumber };
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;
        while (lines.Count < 51)
        {
            var card = pool[index++ % pool.Length];
            var count = counts.GetValueOrDefault(card.Number);
            if (count >= 4) continue;
            lines.Add(card.Number);
            counts[card.Number] = count + 1;
        }
        return string.Join('\n', lines);
    }
}
