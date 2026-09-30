using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bodian.Core.Api.Dto;

/// <summary>
/// 宽松的 <see cref="long"/> 读取：数字、可解析的数字字符串都接受，其余（含脱敏占位串）返回 <c>null</c>。
/// </summary>
/// <remarks>
/// 存在的原因是 **fixture 被脱敏过**：<c>fixtures/login-users-login.json</c> 里
/// <c>id</c> / <c>bid</c> / <c>userInfo.id</c> / <c>token</c> 全是字符串 <c>"&lt;redacted&gt;"</c>。
/// 真实响应里它们是数字。
/// <para>
/// 如果只用 <c>NumberHandling.AllowReadingFromString</c>，<c>"&lt;redacted&gt;"</c> 仍然会抛
/// <see cref="JsonException"/>，导致**测试根本读不进这份 fixture**。这个转换器让类型保持数值语义，
/// 同时容忍脱敏串。
/// </para>
/// <para>
/// **只用在被脱敏的少数字段上**（登录响应的 id 类）。不要拿它当全局容错——
/// 那样会把真正的解析错误一起吞掉。
/// </para>
/// </remarks>
internal sealed class LenientInt64Converter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var number) ? number : null;

            case JsonTokenType.String:
                return long.TryParse(
                    reader.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : null;

            case JsonTokenType.Null:
                return null;

            default:
                // 形状与预期不符：把整个值跳掉，返回 null，而不是让反序列化失败。
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is { } v)
        {
            writer.WriteNumberValue(v);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
