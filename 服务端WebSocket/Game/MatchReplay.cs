using System.Text.Json;

namespace GrandUMI.Game;

/// <summary>
/// 对局重放驱动：用原始 seed + 牌组 + 有序动作磁带，重新执行一遍引擎以重建对局状态。
///
/// 这是"服务器重启后恢复对局"的核心：重启时不去序列化无法序列化的内存状态（永续效果委托、
/// 挂起的 async 续延），而是重新构造引擎并按序喂回当时记录的动作，让状态自然长回来。
///
/// 重放期间引擎不挂任何回调（OnSendToPlayer/OnBroadcast/OnReplay/OnMatchLog 全为 null），
/// 故所有对外广播、日志、录像在重放期间天然为 no-op，不会污染线上或重复落盘。
/// </summary>
public static class MatchReplay
{
    /// <summary>一条可重放的动作（与 GameEngine.HandleAction 的入参一一对应）。</summary>
    public sealed record ActionEntry(int PlayerIndex, string Action, JsonElement Data);

    /// <summary>构造一条无 payload 的动作（如 PassBlock/EndTurn）。</summary>
    public static ActionEntry Action(int playerIndex, string action)
        => new(playerIndex, action, EmptyObject());

    /// <summary>从 JSON 文本构造一条动作的 data。</summary>
    public static ActionEntry Action(int playerIndex, string action, string dataJson)
        => new(playerIndex, action, JsonDocument.Parse(dataJson).RootElement.Clone());

    private static JsonElement EmptyObject() => JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// 重建对局：构造静默引擎，按序喂入动作，每个动作后等待其异步效果链进入稳定态。
    /// 返回重建出的引擎（其 State 即恢复出的对局状态）。
    /// </summary>
    public static async Task<GameEngine> RebuildAsync(
        string roomId,
        int seed,
        int firstPlayer,
        (string account, string deckRaw) p0,
        (string account, string deckRaw) p1,
        IReadOnlyList<ActionEntry> actions,
        bool leaderKeywordWildcard = false,
        bool p0AlwaysPrompt = false,
        bool p1AlwaysPrompt = false)
    {
        var engine = new GameEngine(
            roomId,
            ("replay-p0", p0.account, p0.deckRaw),
            ("replay-p1", p1.account, p1.deckRaw),
            firstPlayer: firstPlayer,
            rngSeed: seed,
            leaderKeywordWildcard: leaderKeywordWildcard);

        // 必须在喂入动作之前恢复"防触发信息泄露"开关：它决定生命揭示是否暂停发 prompt，
        // 进而决定动作磁带里有没有对应的 PromptResponse —— 不还原会导致重放分歧。
        engine.State.Players[0].AlwaysPromptOnLifeReveal = p0AlwaysPrompt;
        engine.State.Players[1].AlwaysPromptOnLifeReveal = p1AlwaysPrompt;

        // 构造后理论上无在途链，仍等一次以防万一
        await engine.WaitSettledAsync();

        foreach (var a in actions)
        {
            if (engine.State.IsGameOver) break;
            engine.HandleAction(a.PlayerIndex, a.Action, a.Data);
            await engine.WaitSettledAsync();
        }

        return engine;
    }
}
