using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GrandUMI;

/// <summary>
/// WebSocket 状态快照线协议：同一消息对象只序列化一次，并可基于该连接最后真正发出的完整状态生成增量。
/// 增量只在顶层以及 my/opponent 的直接属性层合并，数组和更深层对象仍整体替换，保持语义简单可回退。
/// </summary>
public static class SnapshotWireCodec
{
    private const int FullSnapshotInterval = 32;
    private const double MinimumSavingRatio = 0.90;
    private static readonly ConditionalWeakTable<object, PreparedPayload> PayloadCache = new();
    private static readonly HashSet<string> ForceFullActions = new(StringComparer.Ordinal)
    {
        "GameStart", "Resync", "SpectateJoin",
    };

    public sealed record EncodedPayload(
        byte[] Bytes,
        bool IsStateSnapshot,
        bool IsDelta,
        JsonElement? NewBaseline,
        int Tick,
        int DeltasSinceFull);

    /// <summary>编码一条消息；调用方只应在 SendAsync 成功后提交返回的新基线。</summary>
    public static EncodedPayload Encode(
        object data,
        bool supportsDelta,
        JsonElement? baseline,
        int baselineTick,
        int deltasSinceFull)
    {
        var prepared = PayloadCache.GetValue(data, static value => Prepare(value));
        if (!prepared.IsStateSnapshot)
            return new EncodedPayload(prepared.FullBytes, false, false, null, -1, deltasSinceFull);

        var mustSendFull = !supportsDelta
            || baseline is null
            || baselineTick < 0
            || prepared.Tick <= baselineTick
            || deltasSinceFull >= FullSnapshotInterval
            || ForceFullActions.Contains(prepared.LastAction);

        if (mustSendFull)
            return Full(prepared);

        var deltaBytes = BuildDelta(baseline.GetValueOrDefault(), baselineTick, prepared.Root, prepared.Tick);
        if (deltaBytes.Length >= prepared.FullBytes.Length * MinimumSavingRatio)
            return Full(prepared);

        return new EncodedPayload(
            deltaBytes,
            true,
            true,
            prepared.Root,
            prepared.Tick,
            deltasSinceFull + 1);
    }

    /// <summary>按与前端相同的规则重建增量，供跨协议回归测试验证逐字段等价。</summary>
    public static JsonElement ApplyDelta(JsonElement baseline, JsonElement delta)
    {
        var root = JsonNode.Parse(baseline.GetRawText())?.AsObject()
            ?? throw new InvalidOperationException("完整快照不是 JSON 对象");
        var changes = delta.GetProperty("changes");
        foreach (var property in changes.EnumerateObject())
        {
            if (property.Name is "my" or "opponent"
                && property.Value.ValueKind == JsonValueKind.Object
                && root[property.Name] is JsonObject player)
            {
                foreach (var playerProperty in property.Value.EnumerateObject())
                    player[playerProperty.Name] = JsonNode.Parse(playerProperty.Value.GetRawText());
                continue;
            }

            root[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        root["proto"] = "MsgGameState";
        root["tick"] = delta.GetProperty("tick").GetInt32();
        return JsonSerializer.SerializeToElement(root);
    }

    private static EncodedPayload Full(PreparedPayload prepared)
        => new(prepared.FullBytes, true, false, prepared.Root, prepared.Tick, 0);

    private static PreparedPayload Prepare(object data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement.Clone();
        var isState = root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("proto", out var proto)
            && string.Equals(proto.GetString(), "MsgGameState", StringComparison.Ordinal);
        if (!isState)
            return new PreparedPayload(bytes, root, false, -1, "");

        var tick = root.TryGetProperty("tick", out var tickElement) && tickElement.TryGetInt32(out var parsedTick)
            ? parsedTick
            : -1;
        var lastAction = root.TryGetProperty("lastAction", out var actionElement)
            ? actionElement.GetString() ?? ""
            : "";
        return new PreparedPayload(bytes, root, true, tick, lastAction);
    }

    private static byte[] BuildDelta(JsonElement baseline, int baselineTick, JsonElement current, int currentTick)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("proto", "MsgGameStateDelta");
        writer.WriteNumber("baseTick", baselineTick);
        writer.WriteNumber("tick", currentTick);
        writer.WritePropertyName("changes");
        writer.WriteStartObject();

        foreach (var property in current.EnumerateObject())
        {
            if (property.Name is "proto" or "tick") continue;
            var hadPrevious = baseline.TryGetProperty(property.Name, out var previous);

            if (property.Name is "my" or "opponent"
                && property.Value.ValueKind == JsonValueKind.Object
                && hadPrevious
                && previous.ValueKind == JsonValueKind.Object)
            {
                var changedPlayerProperties = property.Value.EnumerateObject()
                    .Where(item => !previous.TryGetProperty(item.Name, out var oldValue)
                        || !JsonElement.DeepEquals(item.Value, oldValue))
                    .ToArray();
                if (changedPlayerProperties.Length == 0) continue;

                writer.WritePropertyName(property.Name);
                writer.WriteStartObject();
                foreach (var changed in changedPlayerProperties)
                {
                    writer.WritePropertyName(changed.Name);
                    changed.Value.WriteTo(writer);
                }
                writer.WriteEndObject();
                continue;
            }

            if (hadPrevious && JsonElement.DeepEquals(property.Value, previous)) continue;
            writer.WritePropertyName(property.Name);
            property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private sealed record PreparedPayload(
        byte[] FullBytes,
        JsonElement Root,
        bool IsStateSnapshot,
        int Tick,
        string LastAction);
}
