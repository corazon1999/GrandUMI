using System.Text.Json;
using GrandUMI.Cards;
using GrandUMI.Effects.Dsl;
using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using Xunit;

namespace GrandUMI.Tests;

/// <summary>
/// 阶段0：验证"重放重建"核心假设——同 seed + 同动作磁带，重新执行引擎得到逐字节相同的对局状态。
/// 这是"服务器重启后恢复对局"功能成立的前提。
/// </summary>
public class ReplayEquivalenceTests
{
    private static bool _loaded;

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? root = null;
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "卡牌数据"))) { root = dir.FullName; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(root);
        CardDatabase.LoadFrom(Path.Combine(root!, "卡牌数据"));
        DslInterpreter.LoadDirectory(Path.Combine(root!, "服务端WebSocket", "Effects", "Definitions"));
        _loaded = true;
    }

    // 取自真实对局日志的一副合法牌组（领袖 OP16-080 + 50 张）。双方共用即可。
    private const string Deck =
        "OP16-080\nOP16-103\nOP16-103\nOP16-103\nOP16-103\nOP16-109\nOP16-109\nOP16-109\nOP16-109\n" +
        "OP16-110\nOP16-110\nOP16-110\nOP16-110\nOP16-115\nOP16-115\nOP09-096\nOP09-096\nOP09-096\nOP09-096\n" +
        "OP09-099\nOP09-099\nOP09-099\nOP09-099\nOP16-104\nOP16-104\nOP16-104\nOP16-104\nOP09-086\nOP09-086\n" +
        "OP09-086\nOP09-086\nEB04-058\nEB04-058\nEB04-058\nEB04-058\nOP16-108\nOP16-108\nOP16-108\nOP16-108\n" +
        "OP16-119\nOP16-119\nOP16-119\nOP16-119\nOP16-116\nOP16-116\nOP14-112\nOP14-112\nOP14-112\n" +
        "OP09-093\nOP09-093\nOP09-093";

    /// <summary>一条不依赖手牌内容、永远合法的动作磁带：双方换牌（含一次重抽，触发二次洗牌）+ 多回合结束。</summary>
    private static List<MatchReplay.ActionEntry> FixedTape() => new()
    {
        MatchReplay.Action(0, "Mulligan", "{\"redraw\":true}"),   // 触发卡组回收+重洗（RNG）
        MatchReplay.Action(1, "Mulligan", "{\"redraw\":false}"),
        MatchReplay.Action(0, "EndTurn"),
        MatchReplay.Action(1, "EndTurn"),
        MatchReplay.Action(0, "EndTurn"),
        MatchReplay.Action(1, "EndTurn"),
        MatchReplay.Action(0, "EndTurn"),
    };

    private static string SnapshotJson(GameEngine e)
        => JsonSerializer.Serialize(PrivateStateSnapshotBuilder.Build(e.State));

    [Fact]
    public async Task SameSeedSameTape_ProducesIdenticalState()
    {
        EnsureLoaded();
        const int seed = 123456;

        // 恢复场景里 roomId 不变，故两次重建用同一 roomId
        var e1 = await MatchReplay.RebuildAsync("same-room", seed, 0,
            ("alice", Deck), ("bob", Deck), FixedTape());
        var e2 = await MatchReplay.RebuildAsync("same-room", seed, 0,
            ("alice", Deck), ("bob", Deck), FixedTape());

        var s1 = SnapshotJson(e1);
        var s2 = SnapshotJson(e2);

        // 逐字节相等：含所有卡 GUID。证明确定性 ID + 确定性 RNG + 重放节拍三者都成立。
        Assert.Equal(s1, s2);
    }

    [Fact]
    public async Task DifferentSeed_ProducesDifferentState()
    {
        EnsureLoaded();

        var e1 = await MatchReplay.RebuildAsync("r1", 111, 0,
            ("alice", Deck), ("bob", Deck), FixedTape());
        var e2 = await MatchReplay.RebuildAsync("r2", 999, 0,
            ("alice", Deck), ("bob", Deck), FixedTape());

        // 不同 seed 洗牌不同 → 状态应不同（否则说明 RNG 根本没生效，determinism 测试也就没意义）。
        Assert.NotEqual(SnapshotJson(e1), SnapshotJson(e2));
    }

    /// <summary>
    /// 真实路径验证：用"自动驾驶"驱动一局（含打牌、效果、prompt 响应、攻击/结束回合），
    /// 记录实际应用的动作磁带，再用同 seed 重放，断言终局状态逐字节相等。
    /// 这覆盖了恢复功能最关键也最易出错的部分：带 prompt 的异步效果链能否被重放精确重建。
    /// </summary>
    [Fact]
    public async Task AutopilotedGame_WithPrompts_ReplaysIdentically()
    {
        EnsureLoaded();
        const int seed = 424242;

        var live = new GameEngine("auto",
            ("s0", "alice", Deck), ("s1", "bob", Deck), firstPlayer: 0, rngSeed: seed);
        await live.WaitSettledAsync();

        var tape = new List<MatchReplay.ActionEntry>();
        async Task Apply(int pi, string action, string dataJson)
        {
            var entry = MatchReplay.Action(pi, action, dataJson);
            tape.Add(entry);
            live.HandleAction(entry.PlayerIndex, entry.Action, entry.Data);
            await live.WaitSettledAsync();
        }

        await Apply(0, "Mulligan", "{\"redraw\":false}");
        await Apply(1, "Mulligan", "{\"redraw\":false}");

        int promptCount = 0;
        for (int step = 0; step < 120 && !live.State.IsGameOver; step++)
        {
            var pp = live.State.PendingPrompt;
            if (pp is not null)
            {
                promptCount++;
                var pick = pp.ValidChoices.Take(Math.Max(pp.MinChoose, 0)).ToList();
                var chosenJson = JsonSerializer.Serialize(pick);
                await Apply(pp.PlayerIndex, "PromptResponse",
                    $"{{\"promptId\":\"{pp.PromptId}\",\"chosen\":{chosenJson}}}");
                continue;
            }

            int cp = live.State.CurrentTurnPlayer;
            // 偶数步尝试打出手牌首张（可能被拒，无副作用且确定性），奇数步结束回合保证推进
            if (step % 2 == 0) await Apply(cp, "PlayCard", "{\"handIndex\":0}");
            else await Apply(cp, "EndTurn", "{}");
        }

        var liveSnap = SnapshotJson(live);

        var rebuilt = await MatchReplay.RebuildAsync("auto", seed, 0,
            ("alice", Deck), ("bob", Deck), tape);
        var rebuiltSnap = SnapshotJson(rebuilt);

        Assert.Equal(liveSnap, rebuiltSnap);
        Assert.True(live.State.TurnCount > 3, $"对局应推进多回合，实际 TurnCount={live.State.TurnCount}");
        Assert.True(promptCount > 0, "自动对局未触发任何 prompt，未覆盖到带 prompt 的重放路径");
    }

    [Fact]
    public async Task DeterministicIds_AreStableAcrossRebuilds()
    {
        EnsureLoaded();
        const int seed = 777;

        var e1 = await MatchReplay.RebuildAsync("r1", seed, 0,
            ("alice", Deck), ("bob", Deck), new List<MatchReplay.ActionEntry>());
        var e2 = await MatchReplay.RebuildAsync("r2", seed, 0,
            ("alice", Deck), ("bob", Deck), new List<MatchReplay.ActionEntry>());

        // 领袖与各区卡的 GUID 在两次重建中必须完全一致
        Assert.Equal(e1.State.Players[0].Leader.Id, e2.State.Players[0].Leader.Id);
        Assert.Equal(
            e1.State.Players[0].Deck.Select(c => c.Id),
            e2.State.Players[0].Deck.Select(c => c.Id));
        Assert.Equal(
            e1.State.Players[1].Hand.Select(c => c.Id),
            e2.State.Players[1].Hand.Select(c => c.Id));
    }
}
