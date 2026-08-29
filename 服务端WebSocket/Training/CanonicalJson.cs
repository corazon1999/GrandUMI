using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GrandUMI.Training;

/// <summary>训练工件与动作磁带共用的确定性 JSON 编码。</summary>
internal static class CanonicalJson
{
    public static string Hash(JsonElement element, string? excludedTopLevelProperty = null)
        => Sha256(Encode(element, excludedTopLevelProperty));

    public static string Sha256Utf8(string value)
        => Sha256(Encoding.UTF8.GetBytes(value));

    public static string Sha256(ReadOnlySpan<byte> bytes)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static byte[] Encode(JsonElement element, string? excludedTopLevelProperty = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false,
        });
        WriteElement(writer, element, excludedTopLevelProperty, isTopLevel: true);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>把任意对象 data 固化为属性有序、数字规范且脱离输入文档生命周期的对象。</summary>
    public static JsonElement NormalizeObject(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("动作 data 必须是 JSON 对象");
        using var document = JsonDocument.Parse(Encode(element));
        return document.RootElement.Clone();
    }

    private static void WriteElement(
        Utf8JsonWriter writer,
        JsonElement element,
        string? excludedTopLevelProperty,
        bool isTopLevel)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                writer.WriteStartObject();
                var properties = element.EnumerateObject()
                    .Where(property => !isTopLevel
                        || !string.Equals(property.Name, excludedTopLevelProperty, StringComparison.Ordinal))
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .ToArray();
                for (var i = 1; i < properties.Length; i++)
                {
                    if (string.Equals(properties[i - 1].Name, properties[i].Name, StringComparison.Ordinal))
                        throw new InvalidDataException($"JSON 对象包含重复属性：{properties[i].Name}");
                }
                foreach (var property in properties)
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value, excludedTopLevelProperty: null, isTopLevel: false);
                }
                writer.WriteEndObject();
                break;
            }
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteElement(writer, item, excludedTopLevelProperty: null, isTopLevel: false);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                WriteNumber(writer, element);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException("JSON 包含无法规范化的 Undefined 值");
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.TryGetInt64(out var signed))
        {
            writer.WriteNumberValue(signed);
            return;
        }
        if (element.TryGetUInt64(out var unsigned))
        {
            writer.WriteNumberValue(unsigned);
            return;
        }
        if (element.TryGetDecimal(out var decimalValue))
        {
            writer.WriteNumberValue(decimalValue);
            return;
        }
        if (element.TryGetDouble(out var doubleValue) && double.IsFinite(doubleValue))
        {
            writer.WriteNumberValue(doubleValue);
            return;
        }
        throw new InvalidDataException($"JSON 数字无法规范化：{element.GetRawText()}");
    }
}
