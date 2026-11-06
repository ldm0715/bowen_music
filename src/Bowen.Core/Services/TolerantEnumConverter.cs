using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bowen.Core.Models;

namespace Bowen.Core.Services;

/// <summary>
/// 枚举容错转换器：认不出来的值回落到该枚举登记的默认值。
/// </summary>
/// <remarks>
/// <para>
/// <c>JsonStringEnumConverter</c> 遇到未知名字会直接抛 <see cref="JsonException"/>，
/// 数字却会读成未定义值 —— 两个方向都要挡。挂载方式是枚举类型上的
/// <see cref="JsonConverterAttribute"/>，源生成器会按这个转换器生成读写代码。
/// </para>
/// <para>
/// <b>字符串和数字都接受。</b> 新文件一律写名字；旧文件（如早期的 <c>playback.json</c>）
/// 存的是数字，迁移时要能读出来。数字按 int 底层类型重解释 —— 项目里的配置枚举都是 int。
/// </para>
/// <para>
/// <b>组合值只给 flags 枚举放行。</b> <see cref="ShortcutModifiers"/> 是 flags，
/// <c>Control | Shift</c> 这种组合不是单成员，<see cref="Enum.IsDefined{TEnum}(TEnum)"/>
/// 会判为假；普通枚举仍要求是已定义的单个成员。
/// </para>
/// <para>
/// 默认值不等于 <c>default(T)</c> 的枚举（<see cref="StartupSection"/> 的
/// <see cref="StartupSection.Favorites"/>、<see cref="AudioQuality"/> 的
/// <see cref="AudioQuality.Lossless"/>）在 <see cref="TolerantEnumDefaults{T}"/> 里登记。
/// </para>
/// </remarks>
public sealed class TolerantEnumConverter<T> : JsonConverter<T>
    where T : struct, Enum
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        T value;

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrEmpty(text) || !Enum.TryParse<T>(text, ignoreCase: true, out value))
            {
                return TolerantEnumDefaults<T>.Value;
            }
        }
        else if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var raw))
        {
            // 配置枚举的底层类型都是 int；旧文件存的是数字，这里按同一份内存重解释。
            value = Unsafe.As<int, T>(ref raw);
        }
        else
        {
            // null、对象、数组消费掉，避免反序列化器在下一个 token 上错位。
            reader.Skip();
            return TolerantEnumDefaults<T>.Value;
        }

        var known = TolerantEnumDefaults<T>.AllowsCombinedValues || Enum.IsDefined(value);
        return known ? value : TolerantEnumDefaults<T>.Value;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        var actual = TolerantEnumDefaults<T>.AllowsCombinedValues || Enum.IsDefined(value)
            ? value
            : TolerantEnumDefaults<T>.Value;

        writer.WriteStringValue(actual.ToString());
    }
}

/// <summary>
/// 每个枚举认不出来时回落到哪个值。
/// </summary>
/// <remarks>
/// 只登记默认值不等于 <c>default(T)</c> 的两项；其余枚举的 0 就是默认值。
/// 用 <c>typeof(T)</c> 比较而不是反射，保持 AOT 兼容。
/// </remarks>
internal static class TolerantEnumDefaults<T>
    where T : struct, Enum
{
    /// <summary>该枚举的默认值。</summary>
    public static T Value { get; } = Resolve();

    /// <summary>是否允许 flags 组合值。只有 <see cref="ShortcutModifiers"/> 是 flags。</summary>
    public static bool AllowsCombinedValues { get; } = typeof(T) == typeof(ShortcutModifiers);

    private static T Resolve()
    {
        if (typeof(T) == typeof(StartupSection))
        {
            return (T)(object)StartupSection.Favorites;
        }

        if (typeof(T) == typeof(AudioQuality))
        {
            return (T)(object)AudioQuality.Lossless;
        }

        return default;
    }
}
