using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrandUMI.Game.Logging;

/// <summary>
/// 多个日志出口共享的一份惰性 JSON 值。底层对象只物化一次，之后由各 JSONL 外层记录直接写入，
/// 避免公开快照被回放日志和训练日志各自完整遍历、序列化一遍。
/// </summary>
[JsonConverter(typeof(SharedJsonValueConverter))]
public sealed class SharedJsonValue
{
    private readonly Lazy<JsonElement> _element;
    private int _materializationCount;

    public SharedJsonValue(object value)
    {
        _element = new Lazy<JsonElement>(() =>
        {
            Interlocked.Increment(ref _materializationCount);
            return value is JsonElement element
                ? element.Clone()
                : JsonSerializer.SerializeToElement(value);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public JsonElement Element => _element.Value;

    /// <summary>用于回归测试确认多出口确实复用了同一次物化。</summary>
    public int MaterializationCount => Volatile.Read(ref _materializationCount);
}

public sealed class SharedJsonValueConverter : JsonConverter<SharedJsonValue>
{
    public override SharedJsonValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => throw new NotSupportedException("共享 JSON 值仅用于服务端写出");

    public override void Write(Utf8JsonWriter writer, SharedJsonValue value, JsonSerializerOptions options)
        => value.Element.WriteTo(writer);
}
