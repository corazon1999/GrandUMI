using GrandUMI.Game;

namespace GrandUMI.Effects;

/// <summary>
/// Prompt 系统：服务端发起选择 / 等待玩家响应
/// 使用 TaskCompletionSource 在效果脚本内 await，玩家发 MsgPromptResponse 后唤醒
/// </summary>
public class PromptSystem : IPromptService
{
    private const int TimeoutSeconds = 30;
    private readonly GameEngine _engine;
    private readonly Dictionary<string, TaskCompletionSource<PromptAnswer>> _pending = new();

    /// <summary>本局 prompt 单调序号。promptId 必须确定性（按执行序生成），否则重放重建时
    /// 重新生成的 prompt 与录下的 PromptResponse.promptId 对不上，恢复会发散。</summary>
    private int _promptSeq;

    public GameEngine Engine => _engine;

    public PromptSystem(GameEngine engine) { _engine = engine; }

    public async Task<List<string>> ChooseCards(int playerIdx, string kind, string text,
        IReadOnlyList<string> validChoices, int min, int max,
        Dictionary<string, object?>? extra = null)
    {
        var promptId = $"p{++_promptSeq}";

        // 自动注入"效果源"：让客户端在选择提示里展示正在结算哪张卡的效果。
        // CurrentSource 随 AsyncLocal 传播，覆盖 DSL / 脚本 / AtomicOps 等所有调用点；
        // 无效果上下文（如战斗流程）时为 null，自然不注入，老行为不变。
        var src = EffectRuntime.CurrentSource;
        if (src is not null)
        {
            extra ??= new();
            if (!extra.ContainsKey("sourceNumber")) extra["sourceNumber"] = src.Info.Number;
            if (!extra.ContainsKey("sourceId")) extra["sourceId"] = src.Id;
        }

        // 自动注入 choiceCards：把候选卡 id 反查成卡号下发，让客户端显示卡图。覆盖所有未显式传
        // choiceCards 的调用——尤其选择隐藏区(卡组/废弃/生命/手牌)卡时，前端无法从场上反查番号、
        // 会显示空白占位(反馈#61)。仅发给选择方(快照按 PlayerIndex 过滤 extra)，不泄露隐藏区给对手；
        // 非卡 id(选项序号/trigger/hand/是否等)经 Guid 解析失败自动跳过，不影响这类 prompt。
        if (validChoices.Count > 0)
        {
            extra ??= new();
            if (!extra.ContainsKey("choiceCards"))
            {
                var cc = new List<object>();
                foreach (var id in validChoices)
                {
                    var ci = FindCardByIdAnyZone(_engine.State, id);
                    if (ci is not null) cc.Add(new { id, number = ci.Info.Number });
                }
                if (cc.Count > 0) extra["choiceCards"] = cc;
            }
        }

        _engine.State.PendingPrompt = new PendingPrompt
        {
            PromptId = promptId,
            PlayerIndex = playerIdx,
            Kind = kind,
            ValidChoices = validChoices.ToList(),
            MinChoose = min,
            MaxChoose = max,
            PromptText = text,
            Extra = extra ?? new(),
        };
        _engine.RecordMatchLog("prompt_created", playerIdx, new
        {
            promptId,
            playerIndex = playerIdx,
            kind,
            text,
            validChoices,
            minChoose = min,
            maxChoose = max,
            extra = extra ?? new(),
        });
        _engine.Broadcast("Prompt", new { kind, promptId, playerIdx });

        var tcs = new TaskCompletionSource<PromptAnswer>();
        _pending[promptId] = tcs;
        try
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds));
            var done = await Task.WhenAny(tcs.Task, timeout);
            if (done == timeout)
            {
                _pending.Remove(promptId);
                _engine.State.PendingPrompt = null;
                _engine.RecordMatchLog("prompt_timeout", playerIdx, new { promptId });
                _engine.Broadcast("PromptTimeout", new { promptId });
                return new List<string>();  // 超时视为不选
            }
            var ans = await tcs.Task;
            _engine.State.PendingPrompt = null;
            return ans.Chosen;
        }
        finally
        {
            _pending.Remove(promptId);
        }
    }

    /// <summary>按 id 在双方所有区域(领袖/角色/舞台/手牌/卡组/废弃/生命)反查卡牌；非卡 id(无法解析为 Guid)返回 null。
    /// 供 ChooseCards 自动补 choiceCards 时把候选卡 id 映射成卡号，使客户端能显示卡图(反馈#61)。</summary>
    private static CardInstance? FindCardByIdAnyZone(GameState s, string id)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        foreach (var p in s.Players)
        {
            if (p.Leader.Id == gid) return p.Leader;
            if (p.StageCard is not null && p.StageCard.Id == gid) return p.StageCard;
            foreach (var c in p.Characters) if (c.Id == gid) return c;
            foreach (var c in p.Hand) if (c.Id == gid) return c;
            foreach (var c in p.Deck) if (c.Id == gid) return c;
            foreach (var c in p.Trash) if (c.Id == gid) return c;
            foreach (var c in p.LifeArea) if (c.Id == gid) return c;
        }
        return null;
    }

    public async Task<bool> ConfirmOptional(int playerIdx, string text)
    {
        var r = await ChooseOption(playerIdx, text, new[] { "是", "否" });
        return r == 0;
    }

    public async Task<int> ChooseOption(int playerIdx, string text, IReadOnlyList<string> options)
    {
        var ans = await ChooseCards(playerIdx, "Option", text,
            Enumerable.Range(0, options.Count).Select(i => i.ToString()).ToList(),
            min: 1, max: 1,
            extra: new Dictionary<string, object?> { ["options"] = options });
        if (ans.Count == 0) return -1;
        return int.TryParse(ans[0], out var v) ? v : -1;
    }

    public async Task<bool> AskLifeTrigger(int playerIdx, CardInstance lifeCard, bool hasRealTrigger)
    {
        var extra = new Dictionary<string, object?>
        {
            ["lifeCardNumber"] = lifeCard.Info.Number,
            ["hasRealTrigger"] = hasRealTrigger,
        };
        var ans = await ChooseCards(playerIdx, "LifeTrigger",
            hasRealTrigger ? "是否发动触发效果？" : "是否发动触发效果？",
            new[] { "trigger", "hand" }, 1, 1, extra);
        if (ans.Count == 0) return false; // 超时默认加入手牌
        return ans[0] == "trigger" && hasRealTrigger;
    }

    /// <summary>客户端通过 MsgPromptResponse 调用</summary>
    public void Resolve(string promptId, IReadOnlyList<string> chosen)
    {
        if (!_pending.TryGetValue(promptId, out var tcs)) return;
        var actor = _engine.State.PendingPrompt?.PlayerIndex;
        _engine.RecordMatchLog("prompt_response", actor, new
        {
            promptId,
            chosen = chosen.ToArray(),
        });
        tcs.TrySetResult(new PromptAnswer(chosen.ToList()));
    }

    public record PromptAnswer(List<string> Chosen);
}
