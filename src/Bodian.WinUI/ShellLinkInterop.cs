using System.Runtime.InteropServices;
using System.Text;

namespace Bodian.WinUI;

/// <summary>
/// 创建与读取 <c>.lnk</c> 需要的 COM 声明。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不用 <c>WScript.Shell</c>：</b> 它只能设目标、参数、工作目录、图标、描述，
/// <b>写不了属性存储</b>，也就设不了 <c>PKEY_AppUserModel_ID</c>。没有 AUMID，shell 依然关联不到
/// 进程，SMTC 面板上的名字和图标还是不对 —— 那这一步就白做了。
/// </para>
/// <para>
/// <b>接口方法必须按 vtable 顺序逐个声明</b>，不能只写用到的那几个：COM 调用按槽位索引，
/// 漏一个方法后面全体错位，症状是莫名其妙的访问冲突。
/// </para>
/// </remarks>
internal static class ShellLinkInterop
{
    /// <summary><c>IShellLinkW::GetPath</c> 的 <c>SLGP_RAWPATH</c>：返回存进去的原始路径。</summary>
    internal const uint SlgpRawPath = 0x4;

    /// <summary><c>IPersistFile::Load</c> 的只读模式。<b>读 .lnk 之前必须先 Load</b>，否则对象是空的。</summary>
    internal const uint StgmRead = 0;

    /// <summary>快捷方式对象的 CLSID。</summary>
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal sealed class ShellLinkCoClass
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);

        IntPtr GetIDList();

        void SetIDList(IntPtr pidl);

        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        void IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);

        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        void GetCount(out uint cProps);

        void GetAt(uint iProp, out PropertyKey pkey);

        void GetValue(ref PropertyKey key, IntPtr pv);

        void SetValue(ref PropertyKey key, IntPtr pv);

        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey
    {
        public Guid FormatId;

        public uint PropertyId;
    }

    /// <summary><c>IPropertyStore</c> 的 IID。</summary>
    internal static Guid PropertyStoreIid { get; } = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");

    /// <summary><c>PKEY_AppUserModel_ID</c>：<c>{9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3}, 5</c>。</summary>
    internal static PropertyKey AppUserModelIdKey { get; } =
        new() { FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), PropertyId = 5 };

    /// <summary>
    /// 从**文件路径**取属性存储（读取用）。
    /// </summary>
    /// <remarks>
    /// <b>读 .lnk 的属性必须用这个，不能拿 ShellLink coclass 上 QI 出来的那个。</b>
    /// 后者是懒加载的：<c>Load</c> 之后直接调 <c>GetValue</c> 会拿到未载入的空值
    /// （实测返回的 <c>vt</c> 不是 <c>VT_LPWSTR</c>，于是被当成「没有 AUMID」而每次都重写快捷方式）。
    /// 这个是属性系统按路径打开的存储，一上来就是载入好的。
    /// </remarks>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern int SHGetPropertyStoreFromParsingName(
        string pszPath,
        IntPtr pbc,
        uint flags,
        ref Guid riid,
        out IntPtr propertyStore);

    /// <summary><c>PROPVARIANT</c> 的大小：<c>vt</c>(2) + 保留(6) + 联合体(8)，x64 下 16 字节。</summary>
    internal const int PropVariantSize = 16;

    /// <summary>联合体在 <c>PROPVARIANT</c> 里的偏移。</summary>
    internal const int PropVariantValueOffset = 8;

    /// <summary><c>VT_LPWSTR</c>（<c>Marshal.WriteInt16</c> / <c>ReadInt16</c> 用的是 <c>short</c>）。</summary>
    internal const short VtLpwstr = 31;

    /// <summary>
    /// 造一个 <c>VT_LPWSTR</c> 的 <c>PROPVARIANT</c>。
    /// </summary>
    /// <remarks>
    /// <b>用裸内存而不是带显式布局的 struct</b>：有过一次教训 ——
    /// <c>out PropVariant</c> 这种声明编组回来的值是坏的（<c>vt</c> 读出来不是 31，
    /// 于是每次启动都判定「快捷方式缺 AUMID」而重写）。联合体的偏移在不同架构下还不同，
    /// 与其让编组器猜，不如自己按字节写。
    /// <para>用完要 <see cref="PropVariantClearRaw"/> 再 <c>FreeCoTaskMem</c>。</para>
    /// </remarks>
    internal static IntPtr AllocateStringPropVariant(string value)
    {
        var buffer = Marshal.AllocCoTaskMem(PropVariantSize);

        Marshal.WriteInt16(buffer, 0, VtLpwstr);
        Marshal.WriteIntPtr(buffer, PropVariantValueOffset, Marshal.StringToCoTaskMemUni(value));

        return buffer;
    }

    /// <summary>清掉 <c>PROPVARIANT</c> 里的内容（字符串会被释放）。传入的 buffer 仍要自己释放。</summary>
    [DllImport("ole32.dll")]
    internal static extern int PropVariantClearRaw(IntPtr pv);
}
