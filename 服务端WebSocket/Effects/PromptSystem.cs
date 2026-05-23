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

    public GameEngine Engine => _engine;

    public PromptSystem(GameEngine engine) { _engine = engine; }

    public async Task<List<string>> ChooseCards(int playerIdx, string kind, string text,
        IReadOnlyList<string> validChoices, int min, int max,
        Dictionary<string, object?>? extra = null)
    {
        var promptId = Guid.NewGuid().ToString("N")[..10];
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
        tcs.TrySetResult(new PromptAnswer(chosen.ToList()));
    }

    public record PromptAnswer(List<string> Chosen);
}
