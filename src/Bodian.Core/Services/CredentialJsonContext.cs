using System.Text.Json.Serialization;
using Bodian.Core.Services.Abstractions;

namespace Bodian.Core.Services;

/// <summary>
/// 凭据落盘用的源生成上下文。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>BodianJsonContext</c> 分开，因为两者的选项不同：这里是本地持久化
/// （缩进、省略 null），那边是 API 响应（紧凑、数字宽松读取）。
/// </para>
/// <para>
/// <b>这两个选项必须与 P0 探针的 <c>SessionStore</c> 一致</b>，否则读不出探针写下的会话。
/// 字段名由 <see cref="BodianCredential"/> 的属性名决定，同样不能改。
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BodianCredential))]
internal sealed partial class CredentialJsonContext : JsonSerializerContext;
