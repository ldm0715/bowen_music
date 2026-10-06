using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bodian.WinUI.Services;

/// <inheritdoc cref="IStartMenuShortcutInstaller" />
/// <remarks>
/// <para>
/// <b>会写用户系统，但只写一个文件</b>：
/// <c>%APPDATA%\Microsoft\Windows\Start Menu\Programs\Bodian.lnk</c>。
/// 不碰注册表、不需要管理员、不设自启动。撤销 = 删掉那个 .lnk。
/// </para>
/// <para>
/// <b>幂等</b>：目标路径与 AUMID 都对得上就一个字节都不写 —— 开发期每次启动都重写一遍文件，
/// 会让 shell 反复重扫开始菜单。
/// </para>
/// <para>
/// <b>失败不影响使用</b>：整段包在 try/catch 里，失败只记 Warning，面板退化成显示 exe 名。
/// </para>
/// <para>
/// 设置在 <c>BODIAN_SKIP_SHELL_REGISTRATION</c> 里（非空且不是 <c>0</c>）时整个跳过 ——
/// 给 CI、冒烟测试、以及不想被改开始菜单的人用。
/// </para>
/// </remarks>
public sealed class StartMenuShortcutInstaller(ILogger<StartMenuShortcutInstaller>? logger = null)
    : IStartMenuShortcutInstaller
{
    /// <summary>设了它就不写开始菜单。</summary>
    public const string SkipEnvironmentVariable = "BODIAN_SKIP_SHELL_REGISTRATION";

    private readonly ILogger _logger = logger ?? NullLogger<StartMenuShortcutInstaller>.Instance;

    public void EnsureInstalled()
    {
        if (IsSkipped())
        {
            _logger.LogDebug("已设置 {Variable}，跳过开始菜单注册", SkipEnvironmentVariable);
            return;
        }

        try
        {
            if (Environment.ProcessPath is not { Length: > 0 } exe)
            {
                _logger.LogWarning("取不到进程路径，无法注册开始菜单快捷方式");
                return;
            }

            // 改过名的话，旧快捷方式要先清掉：留着开始菜单里会同名两条、指向同一个 exe。
            RemoveLegacyShortcuts();

            var linkPath = ShortcutPath();

            if (IsUpToDate(linkPath, exe))
            {
                _logger.LogDebug("开始菜单快捷方式已是最新，跳过");
                return;
            }

            Create(linkPath, exe);

            _logger.LogInformation("已写入开始菜单快捷方式：{Link} → {Exe}", linkPath, exe);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "写开始菜单快捷方式失败，系统媒体面板将显示 exe 名而不是应用名");
        }
    }

    /// <summary>快捷方式的目标位置。</summary>
    private static string ShortcutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        AppIdentity.ShortcutFileName);

    /// <summary>删掉改名前留下的那几条快捷方式。删不掉只记日志，不影响这次安装。</summary>
    private void RemoveLegacyShortcuts()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        foreach (var name in AppIdentity.LegacyShortcutFileNames)
        {
            var path = Path.Combine(programs, name);

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    _logger.LogInformation("已删除改名前的快捷方式：{Path}", path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "删除旧快捷方式失败：{Path}", path);
            }
        }
    }

    private static bool IsSkipped() => Environment.GetEnvironmentVariable(SkipEnvironmentVariable)
        is { Length: > 0 } value && value != "0";

    /// <summary>
    /// 已有快捷方式是否已经是最新。
    /// </summary>
    /// <remarks>
    /// 读不到属性（文件是别的程序写的、或刚被另一个进程改过）时一律当「需要重建」——
    /// <b>重写是安全方向</b>：代价是一次很小的文件写，而漏修的后果是面板永远显示 exe 名。
    /// </remarks>
    private bool IsUpToDate(string linkPath, string exe)
    {
        var snapshot = Read(linkPath);

        if (snapshot is null)
        {
            _logger.LogDebug("已有快捷方式读不出来，重建");
            return false;
        }

        if (!string.Equals(snapshot.TargetPath, exe, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("快捷方式指向别的 exe，重建：{Actual}", snapshot.TargetPath);
            return false;
        }

        if (!string.Equals(snapshot.AppUserModelId, AppIdentity.AppUserModelId, StringComparison.Ordinal))
        {
            _logger.LogDebug("快捷方式缺 AUMID 或不是本应用的，重建：{Actual}", snapshot.AppUserModelId ?? "(空)");
            return false;
        }

        return true;
    }

    private ShortcutSnapshot? Read(string linkPath)
    {
        if (!File.Exists(linkPath))
        {
            return null;
        }

        object? shellLink = null;

        try
        {
            shellLink = new ShellLinkInterop.ShellLinkCoClass();

            // ★ 必须先 Load：new 出来的 ShellLink 对象是空的，不加载文件就读到空路径、空属性。
            ((ShellLinkInterop.IPersistFile)shellLink).Load(linkPath, ShellLinkInterop.StgmRead);

            var link = (ShellLinkInterop.IShellLinkW)shellLink;

            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, IntPtr.Zero, ShellLinkInterop.SlgpRawPath);

            return new ShortcutSnapshot(buffer.ToString(), ReadAppUserModelId(linkPath));
        }
        catch (Exception ex)
        {
            // 读不出来就当需要重建 —— 旧文件可能是别的程序留下的，不值得为它做兼容。
            _logger.LogDebug(ex, "读取已有快捷方式失败，将重建：{Link}", linkPath);
            return null;
        }
        finally
        {
            Release(shellLink);
        }
    }

    /// <summary>读 .lnk 上的 <c>PKEY_AppUserModel_ID</c>。没有这个属性时返回 <c>null</c>。</summary>
    /// <remarks>
    /// 走 <c>SHGetPropertyStoreFromParsingName</c> 而不是 ShellLink 对象上的属性存储 ——
    /// 后者 <c>Load</c> 之后不预热就取不到值，详见 <see cref="ShellLinkInterop"/> 里的说明。
    /// </remarks>
    private string? ReadAppUserModelId(string linkPath)
    {
        var iid = ShellLinkInterop.PropertyStoreIid;

        if (ShellLinkInterop.SHGetPropertyStoreFromParsingName(
                linkPath,
                IntPtr.Zero,
                0,
                ref iid,
                out var raw) < 0 || raw == IntPtr.Zero)
        {
            return null;
        }

        object? store = null;

        try
        {
            store = Marshal.GetObjectForIUnknown(raw);

            var key = ShellLinkInterop.AppUserModelIdKey;

            // PROPVARIANT 用裸内存接：它的联合体布局在不同架构下不同，
            // 交给编组器猜不如自己按字节取。
            var buffer = Marshal.AllocCoTaskMem(ShellLinkInterop.PropVariantSize);

            try
            {
                // 属性不存在时 GetValue 返回失败 HRESULT，投影层会抛 —— 那正是「没有 AUMID」。
                ((ShellLinkInterop.IPropertyStore)store).GetValue(ref key, buffer);

                var variantType = Marshal.ReadInt16(buffer);
                var pointer = Marshal.ReadIntPtr(buffer, ShellLinkInterop.PropVariantValueOffset);

                return variantType == ShellLinkInterop.VtLpwstr && pointer != IntPtr.Zero
                    ? Marshal.PtrToStringUni(pointer)
                    : null;
            }
            finally
            {
                ShellLinkInterop.PropVariantClear(buffer);
                Marshal.FreeCoTaskMem(buffer);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读快捷方式的 AUMID 失败");
            return null;
        }
        finally
        {
            if (store is not null && Marshal.IsComObject(store))
            {
                Marshal.ReleaseComObject(store);
            }

            Marshal.Release(raw);
        }
    }

    private static void Create(string linkPath, string exe)
    {
        object? shellLink = null;

        try
        {
            shellLink = new ShellLinkInterop.ShellLinkCoClass();
            var link = (ShellLinkInterop.IShellLinkW)shellLink;

            link.SetPath(exe);

            // 从开始菜单启动时工作目录不是 exe 所在目录 —— 不设的话同目录的 libmpv-2.dll 找不到。
            link.SetWorkingDirectory(Path.GetDirectoryName(exe) ?? string.Empty);
            link.SetDescription(AppIdentity.DisplayName);

            var persist = (ShellLinkInterop.IPersistFile)shellLink;

            // 先落盘，让 .lnk 存在。
            persist.Save(linkPath, true);

            // ★ 再写 AUMID，然后**必须再 Save 一次** —— 属性存储是在 Save 时序列化进 .lnk 的。
            //   只 Commit 不 Save 的话，文件里根本不会有这个属性（实测：Get-StartApps 里
            //   AppID 仍然是 exe 路径，而官方客户端那条是它的 AUMID）。
            var store = (ShellLinkInterop.IPropertyStore)shellLink;
            var key = ShellLinkInterop.AppUserModelIdKey;
            var value = ShellLinkInterop.AllocateStringPropVariant(AppIdentity.AppUserModelId);

            try
            {
                store.SetValue(ref key, value);
                store.Commit();
            }
            finally
            {
                ShellLinkInterop.PropVariantClear(value);
                Marshal.FreeCoTaskMem(value);
            }

            persist.Save(linkPath, true);
        }
        finally
        {
            Release(shellLink);
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is not null && Marshal.IsComObject(comObject))
        {
            Marshal.ReleaseComObject(comObject);
        }
    }

    /// <summary>已有快捷方式的两个关键字段。</summary>
    private sealed record ShortcutSnapshot(string TargetPath, string? AppUserModelId);
}
