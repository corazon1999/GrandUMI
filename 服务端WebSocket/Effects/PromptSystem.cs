using GrandUMI.Game;
using GrandUMI.Game.Snapshot;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrandUMI.Effects;

/// <summary>
/// Prompt 系统：服务端发起选择 / 等待玩家响应
/// 使用 TaskCompletionSource 在效果脚本内 await，玩家发 MsgPromptResponse 后唤醒
/// </summary>
public class PromptSystem : IPromptService
{
    private const string ReturnToEffectConfirmPrefix = "__return_to_effect_confirm__";
    private readonly GameEngine _engine;
    private readonly Dictionary<string, TaskCompletionSource<PromptAnswer>> _pending = new();
    private readonly ConcurrentDictionary<int, OptionalConfirmation> _optionalConfirmations = new();

    /// <summary>本局 prompt 单调序号。promptId 必须确定性（按执行序生成），否则重放重建时
    /// 重新生成的 prompt 与录下的 PromptResponse.promptId 对不上，恢复会发散。</summary>
    private int _promptSeq;

    public GameEngine Engine => _engine;

    public PromptSystem(GameEngine engine)
    {
        _engine = engine;
    }

    internal void ClearOptionalConfirmation(int playerIdx, Guid sourceId)
    {
        if (_optionalConfirmations.TryGetValue(playerIdx, out var optional)
            && optional.SourceId == sourceId.ToString())
            _optionalConfirmations.TryRemove(playerIdx, out _);
    }

    public async Task<List<string>> ChooseCards(int playerIdx, string kind, string text,
        IReadOnlyList<string> validChoices, int min, int max,
        Dictionary<string, object?>? extra = null)
    {
        (min, max) = NormalizeChooseRange(kind, text, min, max, extra);
        // 自动注入"效果源"：让客户端在选择提示里展示正在结算哪张卡的效果。
        // CurrentSource 随 AsyncLocal 传播，覆盖 DSL / 脚本 / AtomicOps 等所有调用点；
        // 无效果上下文（如战斗流程）时为 null，自然不注入，老行为不变。
        var src = EffectRuntime.CurrentSource;
        var sourceNumber = src?.Info.Number;
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
                    var located = FindCardByIdAnyZone(_engine.State, id);
                    if (located is not null) cc.Add(new { id, number = located.Card.Info.Number });
                }
                if (cc.Count > 0) extra["choiceCards"] = cc;
            }

            // 无论调用方是否已自行传入 choiceCards，都补充候选卡所在区域。
            // 同一张卡可能同时出现在手牌与废弃区候选中（如 OP17-085），仅靠卡号无法区分；
            // 区域信息只随该玩家的私有 Prompt 下发，不会暴露手牌给对手。
            if (!extra.ContainsKey("choiceCardZones"))
            {
                var zones = new List<object>();
                foreach (var id in validChoices)
                {
                    var located = FindCardByIdAnyZone(_engine.State, id);
                    if (located is not null)
                        zones.Add(new { id, zone = located.Zone });
                }
                if (zones.Count > 0) extra["choiceCardZones"] = zones;
            }
        }

        // “是否发动”确认后的第一个、且牌局状态尚未改变的选择步骤，可返回上一级重新决定。
        // 这既覆盖成本选择，也覆盖确认发动后才出现的目标选择；状态成本一旦支付，指纹变化会
        // 关闭返回入口。没有可选发动上下文的强制选择仍只接受 min..max 范围内的正常答案。
        // 返回动作使用独立的内部合法选项，继续接受 GameEngine 对数量、重复项和伪造 ID 的统一校验。
        // 多选成本会生成 min 个不同的返回选项，确保返回动作同样满足 minChoose，且不会放宽必选规则。
        OptionalConfirmation? returnContext = null;
        var returnChoiceIds = new List<string>();
        OptionalConfirmation? optional = null;
        var hasOptional = kind != "Option" && _optionalConfirmations.TryGetValue(playerIdx, out optional);
        if (hasOptional
            && optional!.SourceId == EffectRuntime.CurrentSource?.Id.ToString())
        {
            // 同一可选效果遇到首个选择步骤后即消费上下文；即使该步本身可选零张或状态已改变，
            // 也不能把旧确认框错误带到更后的强制步骤。
            _optionalConfirmations.TryRemove(playerIdx, out _);
            if (optional.StateFingerprint == BuildOptionalStateFingerprint(_engine.State)
                && min > 0
                && max >= min)
            {
                returnContext = optional;
                returnChoiceIds.AddRange(Enumerable.Range(0, min)
                    .Select(i => $"{ReturnToEffectConfirmPrefix}:{i}"));
                extra ??= new();
                extra["canReturnToEffectConfirm"] = true;
                extra["returnChoiceIds"] = returnChoiceIds.ToArray();
            }
        }

        while (true)
        {
            var promptId = $"p{++_promptSeq}";
            var serverChoices = validChoices.Concat(returnChoiceIds).ToList();
            _engine.State.PendingPrompt = new PendingPrompt
            {
                PromptId = promptId,
                PlayerIndex = playerIdx,
                Kind = kind,
                ValidChoices = serverChoices,
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
                validChoices = serverChoices,
                minChoose = min,
                maxChoose = max,
                extra = extra ?? new(),
            });
            // 先登记再广播，避免极低延迟客户端在 _pending 写入前回包。
            // PromptResponse 在房间锁内调用 Resolve；若让 await 续程在当前线程内联执行，
            // 续程再次发起满场挤位选择时会持锁同步等待下一次响应，造成 30 秒超时。
            // 强制异步调度续程，让 Resolve 先退出并释放房间锁。
            var tcs = new TaskCompletionSource<PromptAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[promptId] = tcs;
            _engine.Broadcast("Prompt", new { kind, promptId, playerIdx });
            try
            {
                // 对局中的选择均等待玩家明确响应；PendingPrompt 会随权威快照保留，
                // 玩家重连后仍可继续完成当前选择，不再因固定时限跳过效果。
                var ans = await tcs.Task;
                _engine.State.PendingPrompt = null;
                var isReturning = returnChoiceIds.Count > 0
                    && ans.Chosen.SequenceEqual(returnChoiceIds, StringComparer.Ordinal);
                if (isReturning && returnContext is not null)
                {
                    _engine.Broadcast("EffectChoice");
                    var resume = await ChooseOption(playerIdx, returnContext.Text, new[] { "是", "否" });
                    if (resume == 0) continue;

                    RestoreUsageMarkers(returnContext.UsageMarkers);
                    throw new OptionalEffectDeclinedException();
                }

                QueueResolvedLog(playerIdx, kind, text, ans.Chosen, extra, sourceNumber);
                // 普通日志快照请求：若后续还有公开、出牌或下一次 Prompt，会自然合并进对应快照；
                // 若效果在此结束，则在当前批次结束时至少下发这一条选择日志。
                _engine.Broadcast("EffectChoice");
                return ans.Chosen;
            }
            finally
            {
                _pending.Remove(promptId);
            }
        }
    }

    /// <summary>
    /// 卡面写明“最多 N 张”时，数量范围必须是 0..N。调用脚本若误把最小值也写成 N，
    /// 在统一提示入口纠正，确保玩家可以少选或不选；成本、排序和明确必选流程不受影响。
    /// </summary>
    internal static (int Min, int Max) NormalizeChooseRange(
        string kind,
        string text,
        int min,
        int max,
        Dictionary<string, object?>? extra = null)
    {
        if (min <= 0 || max <= 0 || !text.Contains("最多", StringComparison.Ordinal))
            return (min, max);
        if (EffectRuntime.PayingCost
            || extra?.TryGetValue("isCost", out var isCost) == true && isCost is true
            || kind.Contains("Order", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("Reorder", StringComparison.OrdinalIgnoreCase))
            return (min, max);
        return (0, max);
    }

    private static string BuildOptionalStateFingerprint(GameState state)
    {
        var snapshot = JsonSerializer.SerializeToNode(PrivateStateSnapshotBuilder.Build(state))!.AsObject();
        // 玩家响应 Prompt 时，房间层会同步推进 tick、棋钟、阶段/战斗流程等外围状态；
        // 这些变化不是成本支付，不能因此误判为“已经付过成本”。这里只比较真正可能
        // 被成本改变的玩家卡牌/区域/DON 状态及持续效果。
        var node = new JsonObject
        {
            ["players"] = snapshot["players"]?.DeepClone(),
            ["continuousEffects"] = snapshot["continuousEffects"]?.DeepClone(),
        };
        RemoveVolatileOrUsageProperties(node);
        return node.ToJsonString();
    }

    private static void RemoveVolatileOrUsageProperties(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in new[]
                     {
                         "tick", "pendingPrompt", "operationClockRemainingMs", "operationClockSyncUtc",
                         "operationClockActivePlayer", "operationClockPaused", "turnOnceUsed", "oncePerTurnUsedKeys",
                     })
                obj.Remove(property);
            foreach (var child in obj.Select(pair => pair.Value).ToArray())
                RemoveVolatileOrUsageProperties(child);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) RemoveVolatileOrUsageProperties(child);
        }
    }

    private UsageMarkerSnapshot CaptureUsageMarkers()
    {
        var playerKeys = _engine.State.Players
            .Select(player => player.TurnOnceUsed.ToHashSet(StringComparer.Ordinal))
            .ToArray();
        var cardKeys = AllCards(_engine.State)
            .ToDictionary(card => card.Id, card => card.OncePerTurnUsedKeys.ToHashSet(StringComparer.Ordinal));
        return new UsageMarkerSnapshot(playerKeys, cardKeys);
    }

    private void RestoreUsageMarkers(UsageMarkerSnapshot snapshot)
    {
        for (var i = 0; i < _engine.State.Players.Length && i < snapshot.PlayerKeys.Length; i++)
        {
            _engine.State.Players[i].TurnOnceUsed.Clear();
            _engine.State.Players[i].TurnOnceUsed.UnionWith(snapshot.PlayerKeys[i]);
        }

        foreach (var card in AllCards(_engine.State))
        {
            if (!snapshot.CardKeys.TryGetValue(card.Id, out var keys)) continue;
            card.OncePerTurnUsedKeys.Clear();
            card.OncePerTurnUsedKeys.UnionWith(keys);
        }
    }

    private static IEnumerable<CardInstance> AllCards(GameState state)
    {
        foreach (var player in state.Players)
        {
            yield return player.Leader;
            if (player.StageCard is not null) yield return player.StageCard;
            foreach (var card in player.Characters) yield return card;
            foreach (var card in player.Hand) yield return card;
            foreach (var card in player.Deck) yield return card;
            foreach (var card in player.Trash) yield return card;
            foreach (var card in player.LifeArea) yield return card;
        }
    }

    /// <summary>按 id 在双方所有区域(领袖/角色/舞台/手牌/卡组/废弃/生命)反查卡牌；非卡 id(无法解析为 Guid)返回 null。
    /// 供 ChooseCards 自动补 choiceCards 时把候选卡 id 映射成卡号，使客户端能显示卡图(反馈#61)。</summary>
    private sealed record LocatedCard(CardInstance Card, int Owner, string Zone, bool IsPublic);

    private static LocatedCard? FindCardByIdAnyZone(GameState s, string id)
    {
        if (!Guid.TryParse(id, out var gid)) return null;
        for (int owner = 0; owner < s.Players.Length; owner++)
        {
            var p = s.Players[owner];
            if (p.Leader.Id == gid) return new LocatedCard(p.Leader, owner, "leader", true);
            if (p.StageCard is not null && p.StageCard.Id == gid) return new LocatedCard(p.StageCard, owner, "stage", true);
            foreach (var c in p.Characters) if (c.Id == gid) return new LocatedCard(c, owner, "character", true);
            foreach (var c in p.Hand) if (c.Id == gid) return new LocatedCard(c, owner, "hand", false);
            foreach (var c in p.Deck) if (c.Id == gid) return new LocatedCard(c, owner, "deck", false);
            foreach (var c in p.Trash) if (c.Id == gid) return new LocatedCard(c, owner, "trash", true);
            foreach (var c in p.LifeArea)
                if (c.Id == gid) return new LocatedCard(c, owner, "life", c.IsLifeFaceUp);
        }
        return null;
    }

    private void QueueResolvedLog(int playerIdx, string kind, string text, IReadOnlyList<string> chosen,
        Dictionary<string, object?>? extra, string? sourceNumber)
    {
        var labels = new List<string>();
        bool hasHiddenCard = false;
        var detailViewers = new HashSet<int> { playerIdx };
        string[]? options = null;
        if (extra is not null
            && extra.TryGetValue("options", out var rawOptions)
            && rawOptions is IEnumerable<string> optionValues)
            options = optionValues.ToArray();

        foreach (var value in chosen)
        {
            if (options is not null
                && int.TryParse(value, out var optionIndex)
                && optionIndex >= 0
                && optionIndex < options.Length)
            {
                labels.Add(options[optionIndex]);
                continue;
            }

            var located = FindCardByIdAnyZone(_engine.State, value);
            if (located is not null)
            {
                labels.Add($"{located.Card.Info.Number} {located.Card.Info.Name}");
                hasHiddenCard |= !located.IsPublic;
                if (!located.IsPublic) detailViewers.Add(located.Owner);
                continue;
            }

            labels.Add(value switch
            {
                "trigger" => "发动触发",
                "hand" => "加入手牌",
                "yes" => "是",
                "no" => "否",
                _ when Guid.TryParse(value, out _) => "已选择卡牌",
                _ => value,
            });
        }

        _engine.QueueActionLog("PromptResolved", new
        {
            player = playerIdx,
            kind,
            text,
            sourceNumber = sourceNumber ?? "",
            labels = labels.ToArray(),
            detailVisibility = hasHiddenCard ? "restricted" : "public",
            detailViewers = detailViewers.ToArray(),
        });
    }

    public async Task<bool> ConfirmOptional(int playerIdx, string text)
    {
        // 在等待玩家回答前就保留上下文，确保网络线程响应后恢复的异步续程尚未执行时，
        // 下一步成本 Prompt 也能稳定找到返回目标。
        _optionalConfirmations[playerIdx] = new OptionalConfirmation(
            text,
            EffectRuntime.CurrentSource?.Id.ToString(),
            BuildOptionalStateFingerprint(_engine.State),
            CaptureUsageMarkers());

        var r = await ChooseOption(playerIdx, text, new[] { "是", "否" });
        if (r == 0) return true;

        _optionalConfirmations.TryRemove(playerIdx, out _);
        return false;
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
        if (ans.Count == 0) return false;
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

        // 续程改为异步调度后，Resolve 返回与续程恢复之间存在一个很短的窗口。
        // 立即清除已响应的 prompt，避免客户端重复提交旧 prompt，也让 WaitSettledAsync
        // 正确等待效果链进入“下一个 prompt 或完全结束”的稳定态。
        if (_engine.State.PendingPrompt?.PromptId == promptId)
            _engine.State.PendingPrompt = null;
        tcs.TrySetResult(new PromptAnswer(chosen.ToList()));
    }

    /// <summary>
    /// 撤回刚刚同步的贴咚时，取消由该次贴咚触发、尚未作答的效果选择。
    /// 调用方必须先以服务端贴咚操作序号确认该 Prompt 确实属于当前可撤回操作。
    /// </summary>
    internal bool CancelCurrentForAttachDonUndo()
    {
        var pending = _engine.State.PendingPrompt;
        if (pending is null || !_pending.TryGetValue(pending.PromptId, out var tcs)) return false;

        _engine.RecordMatchLog("prompt_canceled", pending.PlayerIndex, new
        {
            promptId = pending.PromptId,
            reason = "attach_don_undo",
        });
        _engine.State.PendingPrompt = null;
        return tcs.TrySetException(new AttachDonUndoCanceledException());
    }

    public record PromptAnswer(List<string> Chosen);

    private sealed record OptionalConfirmation(
        string Text,
        string? SourceId,
        string StateFingerprint,
        UsageMarkerSnapshot UsageMarkers);

    private sealed record UsageMarkerSnapshot(
        HashSet<string>[] PlayerKeys,
        Dictionary<Guid, HashSet<string>> CardKeys);
}

/// <summary>玩家从成本选择返回确认框后改为不发动；由效果运行时当作正常取消结束。</summary>
public sealed class OptionalEffectDeclinedException : Exception
{
}

/// <summary>贴咚被玩家权威撤回时中止该次贴咚触发的尚未完成效果链。</summary>
public sealed class AttachDonUndoCanceledException : Exception
{
}
