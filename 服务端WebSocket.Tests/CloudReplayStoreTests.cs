using System.Text.Json;
using GrandUMI.Persistence;
using GrandUMI.Training;
using Xunit;

namespace GrandUMI.Tests;

public sealed class CloudReplayStoreTests
{
    private const string RuntimeId = "grandumi-runtime-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task 完成后双方只能读取各自玩家视角_第三方没有参与者权限()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-auth-0001"))!;
        AppendCompleteTape(capture);
        Assert.True(store.AssociateFeedback("alice", "replay-auth-0001", "feedback-1"));
        await capture.CompleteAsync(Completion());

        var alice = store.Load("alice", "replay-auth-0001").Document;
        var bob = store.Load("bob", "replay-auth-0001").Document;
        Assert.Equal("P0-SECRET", FirstSnapshot(alice).GetProperty("my").GetProperty("handCardNumbers")[0].GetString());
        Assert.Equal("P1-SECRET", FirstSnapshot(bob).GetProperty("my").GetProperty("handCardNumbers")[0].GetString());
        Assert.Empty(FirstSnapshot(alice).GetProperty("opponent").GetProperty("handCardNumbers").EnumerateArray());
        Assert.Empty(FirstSnapshot(bob).GetProperty("opponent").GetProperty("handCardNumbers").EnumerateArray());

        var denied = Assert.Throws<CloudReplayException>(() => store.Load("mallory", "replay-auth-0001"));
        Assert.Equal("not_found", denied.Code);
        var page = store.List("alice", Query());
        Assert.Single(page.Items);
        Assert.Equal(1, page.Items[0].FeedbackCount);
    }

    [Fact]
    public async Task 显式分享默认清除全部手牌与交互秘密_完整时间线需单独选择()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-share-0001"))!;
        AppendCompleteTape(capture);
        await capture.CompleteAsync(Completion());

        var masked = store.SetShare(
            "alice", "replay-share-0001", true, CloudReplaySharePolicies.Masked, "request-share-masked-0001");
        var duplicate = store.SetShare(
            "alice", "replay-share-0001", true, CloudReplaySharePolicies.Masked, "request-share-masked-0001");
        Assert.Equal(masked.ShareToken, duplicate.ShareToken);
        var maskedDocument = store.Load("mallory", "replay-share-0001", masked.ShareToken).Document;
        foreach (var snapshot in maskedDocument.GetProperty("snapshots").EnumerateArray())
        {
            Assert.Empty(snapshot.GetProperty("my").GetProperty("handCardNumbers").EnumerateArray());
            Assert.Empty(snapshot.GetProperty("opponent").GetProperty("handCardNumbers").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("pendingPrompt").ValueKind);
            Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("requestId").ValueKind);
            Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("replayHands").ValueKind);
        }

        var full = store.SetShare(
            "alice", "replay-share-0001", true, CloudReplaySharePolicies.FullTimeline, "request-share-full-0002");
        Assert.NotEqual(masked.ShareToken, full.ShareToken);
        Assert.Equal("not_found", Assert.Throws<CloudReplayException>(
            () => store.Load("mallory", "replay-share-0001", masked.ShareToken)).Code);
        var fullDocument = store.Load("mallory", "replay-share-0001", full.ShareToken).Document;
        Assert.Equal("P0-SECRET", FirstSnapshot(fullDocument).GetProperty("my").GetProperty("handCardNumbers")[0].GetString());
        Assert.Equal(JsonValueKind.Array,
            fullDocument.GetProperty("snapshots")[1].GetProperty("replayHands").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            FirstSnapshot(fullDocument).GetProperty("pendingPrompt").ValueKind);
    }

    [Fact]
    public async Task 书签删除与分享变更均按RequestId幂等且不能跨账号修改()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-mutation-0001"))!;
        AppendCompleteTape(capture);
        await capture.CompleteAsync(Completion());

        Assert.True(store.SetBookmark("alice", "replay-mutation-0001", true, "request-bookmark-0001"));
        Assert.True(store.SetBookmark("alice", "replay-mutation-0001", false, "request-bookmark-0001"));
        Assert.Single(store.List("alice", Query() with { BookmarkedOnly = true }).Items);
        Assert.Equal("not_found", Assert.Throws<CloudReplayException>(() =>
            store.SetBookmark("mallory", "replay-mutation-0001", true, "request-bookmark-other-0002")).Code);

        Assert.True(store.Delete("alice", "replay-mutation-0001", "request-delete-0003"));
        Assert.True(store.Delete("alice", "replay-mutation-0001", "request-delete-0003"));
        Assert.Empty(store.List("alice", Query()).Items);
    }

    [Fact]
    public async Task 历史运行时没有归档时返回专用错误而不是尝试兼容执行()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => false);
        var capture = store.BeginMatch(Start("replay-runtime-0001"))!;
        AppendCompleteTape(capture);
        await capture.CompleteAsync(Completion());

        var error = Assert.Throws<CloudReplayException>(() => store.Load("alice", "replay-runtime-0001"));
        Assert.Equal("runtime_missing", error.Code);
        Assert.Contains(RuntimeId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task 保留数量超限时删除最旧未书签回放_书签仍受硬配额约束()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true, maximumReplays: 1, quotaBytes: 1024 * 1024);
        var first = store.BeginMatch(Start("replay-retention-0001", DateTime.UtcNow.AddMinutes(-2)))!;
        AppendCompleteTape(first);
        await first.CompleteAsync(Completion(DateTime.UtcNow.AddMinutes(-1)));
        var second = store.BeginMatch(Start("replay-retention-0002", DateTime.UtcNow.AddMinutes(-1)))!;
        AppendCompleteTape(second);
        await second.CompleteAsync(Completion(DateTime.UtcNow));

        var items = store.List("alice", Query()).Items;
        Assert.Single(items);
        Assert.Equal("replay-retention-0002", items[0].ReplayId);
    }

    [Fact]
    public async Task 发布前故障_保留恢复磁带且重试只发布双方各一份()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-retry-0001"))!;
        AppendCompleteTape(capture);
        store.CompletionFailureInjector = (replayId, stage) =>
            replayId == "replay-retry-0001" && stage == "after_payloads"
                ? new IOException("故障演练：载荷写入后、数据库提交前退出")
                : null;

        await Assert.ThrowsAsync<IOException>(() => capture.CompleteAsync(Completion()));

        var pending = Path.Combine(store.Root, "pending");
        Assert.True(File.Exists(Path.Combine(pending, "replay-retry-0001.meta.json")));
        Assert.True(File.Exists(Path.Combine(pending, "replay-retry-0001.p0.jsonl")));
        Assert.True(File.Exists(Path.Combine(pending, "replay-retry-0001.p1.jsonl")));
        Assert.Empty(store.List("alice", Query()).Items);
        Assert.Empty(store.List("bob", Query()).Items);

        store.CompletionFailureInjector = null;
        await capture.CompleteAsync(Completion());
        await capture.CompleteAsync(Completion());

        Assert.Single(store.List("alice", Query()).Items);
        Assert.Single(store.List("bob", Query()).Items);
        Assert.Empty(Directory.GetFiles(pending, "replay-retry-0001.*"));
    }

    [Fact]
    public async Task 进程重启_续写未完成磁带并且只发布一次()
    {
        using var workspace = new Workspace();
        var firstStore = workspace.CreateStore(_ => true);
        var firstCapture = firstStore.BeginMatch(Start("replay-resume-0001"))!;
        firstCapture.AppendSnapshot(0, Snapshot(1, false, true, "P0-SECRET", ""));
        firstCapture.AppendSnapshot(1, Snapshot(1, false, false, "P1-SECRET", ""));
        firstStore.Dispose();

        using var resumedStore = workspace.CreateStore(_ => true);
        var resumedCapture = resumedStore.ResumeMatch("replay-resume-0001")!;
        Assert.Equal(1, resumedCapture.GetRecoveryFrameState(0).FrameCount);
        Assert.Equal(1, resumedCapture.GetRecoveryFrameState(1).FrameCount);
        Assert.False(resumedCapture.GetRecoveryFrameState(0).HasTerminalFrame);
        resumedCapture.AppendSnapshot(0, Snapshot(2, true, true, "P0-FINAL", "P1-FINAL"));
        resumedCapture.AppendSnapshot(1, Snapshot(2, true, false, "P1-FINAL", "P0-FINAL"));
        await resumedCapture.CompleteAsync(Completion());
        await resumedCapture.CompleteAsync(Completion());

        Assert.Single(resumedStore.List("alice", Query()).Items);
        Assert.Single(resumedStore.List("bob", Query()).Items);
        Assert.Equal(2,
            resumedStore.Load("alice", "replay-resume-0001").Document
                .GetProperty("snapshots").GetArrayLength());
        Assert.Equal(2,
            resumedStore.Load("bob", "replay-resume-0001").Document
                .GetProperty("snapshots").GetArrayLength());
    }

    [Fact]
    public void 进程重启前只读检查_可从暂存磁带识别终局证据与Tick高水位()
    {
        using var workspace = new Workspace();
        var firstStore = workspace.CreateStore(_ => true);
        var capture = firstStore.BeginMatch(Start("replay-inspect-terminal-0001"))!;
        AppendCompleteTape(capture);
        firstStore.Dispose();

        using var resumedStore = workspace.CreateStore(_ => true);
        var pending = Assert.IsType<CloudReplayPendingRecoveryState>(
            resumedStore.InspectPendingMatch("replay-inspect-terminal-0001"));

        Assert.True(pending.HasTerminalFrame);
        Assert.Equal(2, pending.LastTick);
        Assert.Equal(2, pending.Player0.FrameCount);
        Assert.Equal(2, pending.Player1.FrameCount);
        Assert.True(pending.Player0.HasTerminalFrame);
        Assert.True(pending.Player1.HasTerminalFrame);
    }

    [Fact]
    public async Task 同结果重复终局快照按幂等忽略_回放仍只发布首份终局()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-terminal-duplicate-0001"))!;
        capture.AppendSnapshot(0, Snapshot(1, false, true, "P0-SECRET", ""));
        capture.AppendSnapshot(1, Snapshot(1, false, false, "P1-SECRET", ""));
        capture.AppendSnapshot(0, Snapshot(2, true, true, "P0-FINAL", "P1-FINAL"));
        capture.AppendSnapshot(1, Snapshot(2, true, false, "P1-FINAL", "P0-FINAL"));

        capture.AppendSnapshot(0, Snapshot(3, true, true, "P0-FINAL", "P1-FINAL"));
        capture.AppendSnapshot(1, Snapshot(3, true, false, "P1-FINAL", "P0-FINAL"));

        Assert.Null(capture.FailureReason);
        Assert.Equal(2, capture.GetRecoveryFrameState(0).FrameCount);
        Assert.Equal(2, capture.GetRecoveryFrameState(1).FrameCount);
        await capture.CompleteAsync(Completion());

        Assert.Equal(2,
            store.Load("alice", "replay-terminal-duplicate-0001").Document
                .GetProperty("snapshots").GetArrayLength());
        Assert.Equal(2,
            store.Load("bob", "replay-terminal-duplicate-0001").Document
                .GetProperty("snapshots").GetArrayLength());
    }

    [Fact]
    public async Task 终局后追加非终局状态仍严格失败()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-terminal-regression-0001"))!;
        AppendCompleteTape(capture);

        capture.AppendSnapshot(0, Snapshot(3, false, true, "P0-LATE", ""));

        Assert.Equal("云回放终局帧之后不得继续追加非终局状态。", capture.FailureReason);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => capture.CompleteAsync(Completion()));
        Assert.Equal(capture.FailureReason, error.Message);
    }

    [Fact]
    public async Task 重复终局的胜负语义冲突仍严格失败()
    {
        using var workspace = new Workspace();
        using var store = workspace.CreateStore(_ => true);
        var capture = store.BeginMatch(Start("replay-terminal-conflict-0001"))!;
        AppendCompleteTape(capture);

        capture.AppendSnapshot(0, Snapshot(3, true, false, "P0-FINAL", "P1-FINAL"));

        Assert.Equal("云回放重复终局帧的终局语义不一致。", capture.FailureReason);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => capture.CompleteAsync(Completion()));
        Assert.Equal(capture.FailureReason, error.Message);
    }

    private static CloudReplayMatchStart Start(string replayId, DateTime? startedAt = null)
        => new(
            replayId,
            startedAt ?? DateTime.UtcNow.AddMinutes(-5),
            "Casual",
            Runtime(),
            new CloudReplayPlayer("alice", "爱丽丝", true),
            new CloudReplayPlayer("bob", "鲍勃", true));

    private static CloudReplayCompletion Completion(DateTime? completedAt = null)
        => new(completedAt ?? DateTime.UtcNow, 0, false, "爱丽丝获胜", 3);

    private static CloudReplayListQuery Query()
        => new(null, null, null, false, null, null, 0, 20);

    private static ReplayRuntimeIdentity Runtime()
        => new(
            "grandumi.matchlog.v1",
            "grandumi.matchlog-adapter.v1",
            RuntimeId,
            new string('1', 40),
            "sha256:" + new string('2', 64),
            "builtin-v1",
            "sha256:" + new string('3', 64),
            "sha256:" + new string('4', 64),
            "dotnet-random-test",
            "guid-test",
            "opening-test",
            "grandumi.replay-config.v1",
            "sha256:" + new string('5', 64));

    private static void AppendCompleteTape(CloudReplayCapture capture)
    {
        capture.AppendSnapshot(0, Snapshot(1, false, true, "P0-SECRET", ""));
        capture.AppendSnapshot(1, Snapshot(1, false, false, "P1-SECRET", ""));
        capture.AppendSnapshot(0, Snapshot(2, true, true, "P0-FINAL", "P1-FINAL"));
        capture.AppendSnapshot(1, Snapshot(2, true, false, "P1-FINAL", "P0-FINAL"));
    }

    private static object Snapshot(
        int tick,
        bool isGameOver,
        bool winnerIsMe,
        string myHand,
        string opponentHand)
        => new
        {
            proto = "MsgGameState",
            tick,
            viewerKind = "player",
            phase = "Main",
            currentTurn = true,
            turnCount = 3,
            isGameOver,
            winnerIsMe,
            isDraw = false,
            gameOverReason = isGameOver ? "爱丽丝获胜" : null,
            diceWinnerIsMe = true,
            isFirstPlayer = true,
            requestId = "private-request",
            actionPayload = "{\"private\":true}",
            pendingPrompt = new { promptId = "private-prompt", validChoices = new[] { "secret-choice" } },
            replayHands = isGameOver
                ? new[] { new { tick = 1, myCardNumbers = new[] { myHand }, opponentCardNumbers = new[] { opponentHand } } }
                : null,
            my = Player("我方", "L-001", myHand),
            opponent = Player("对手", "L-002", opponentHand),
        };

    private static object Player(string name, string leader, string hand)
        => new
        {
            name,
            leaderNumber = leader,
            handCardIds = string.IsNullOrEmpty(hand) ? Array.Empty<string>() : new[] { "id-" + hand },
            handCardNumbers = string.IsNullOrEmpty(hand) ? Array.Empty<string>() : new[] { hand },
            handCardCosts = string.IsNullOrEmpty(hand) ? Array.Empty<int>() : new[] { 1 },
            handCardCounters = string.IsNullOrEmpty(hand) ? Array.Empty<int>() : new[] { 1000 },
            trashNumbers = Array.Empty<string>(),
            fieldCards = Array.Empty<object>(),
        };

    private static JsonElement FirstSnapshot(JsonElement document)
        => document.GetProperty("snapshots")[0];

    private sealed class Workspace : IDisposable
    {
        private readonly string _path;

        public Workspace()
        {
            var root = Environment.GetEnvironmentVariable("GRANDUMI_TEST_TEMP_ROOT")
                ?? throw new InvalidOperationException("云回放测试必须设置 GRANDUMI_TEST_TEMP_ROOT。");
            _path = Path.Combine(root, "cloud-replay", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_path);
        }

        public CloudReplayStore CreateStore(
            Func<string, bool> runtimeAvailable,
            int maximumReplays = CloudReplayStore.DefaultMaximumReplays,
            long quotaBytes = CloudReplayStore.DefaultQuotaBytes)
        {
            var store = new CloudReplayStore(
                _path,
                runtimeAvailable,
                maximumReplays: maximumReplays,
                quotaBytes: quotaBytes);
            store.Initialize();
            return store;
        }

        public void Dispose()
        {
            try { Directory.Delete(_path, recursive: true); } catch { }
        }
    }
}
