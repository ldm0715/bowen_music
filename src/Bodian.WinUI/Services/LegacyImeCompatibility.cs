using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Bodian.WinUI.Services;

// Optional workaround for the pinned self-contained WinUI runtime.
// Changes one import in this process only; DLL files and system settings are untouched.
internal sealed class LegacyImeCompatibility : IDisposable
{
    private readonly nint _module;
    private readonly nint _slot;
    private readonly nint _original;
    private readonly DisableLegacyIme _callback;
    private readonly Action<string> _report;
    private int _calls;
    private bool _disposed;

    private LegacyImeCompatibility(nint module, nint slot, Action<string> report)
    {
        _module = module;
        _slot = slot;
        _report = report;
        _original = Marshal.ReadIntPtr(slot);
        _callback = () =>
        {
            var calls = Interlocked.Increment(ref _calls);
            try { _report($"输入法兼容：保留旧输入法回退，调用 {calls}，线程 {GetCurrentThreadId()}"); }
            catch { /* Managed logging exceptions must never cross the native callback. */ }
            return 1;
        };
        WriteSlot(Marshal.GetFunctionPointerForDelegate(_callback));
    }

    internal static LegacyImeCompatibility? TryEnable(Action<string> report)
    {
        nint module = 0;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Microsoft.ui.xaml.dll");
            module = NativeLibrary.Load(path);
            var slot = FindDelayImport(module, "IMM32.dll", "ImmDisableLegacyIME");
            if (slot == 0) throw new NotSupportedException("Expected WinUI delay import was not found; no memory was changed.");
            var hook = new LegacyImeCompatibility(module, slot, report);
            report($"legacy-ime-hook-installed image={path} slot-rva=0x{slot - module:X}");
            return hook;
        }
        catch (Exception exception)
        {
            report($"legacy-ime-hook-unavailable {exception.GetType().Name}: {exception.Message}");
            if (module != 0) NativeLibrary.Free(module);
            return null;
        }
    }

    internal static nint FindDelayImport(nint image, string library, string function)
    {
        if (IntPtr.Size != 8 || Marshal.ReadInt16(image) != 0x5A4D) return 0;
        var pe = Marshal.ReadInt32(image, 0x3C);
        if (pe is < 0x40 or > 0x100000 || Marshal.ReadInt32(image, pe) != 0x4550) return 0;
        var optional = pe + 24;
        if ((ushort)Marshal.ReadInt16(image, optional) != 0x20B) return 0;
        var imageSize = Marshal.ReadInt32(image, optional + 56);
        var directory = optional + 112 + 13 * 8;
        var rva = Marshal.ReadInt32(image, directory);
        var size = Marshal.ReadInt32(image, directory + 4);
        if (rva <= 0 || size < 32 || (long)rva + size > imageSize) return 0;
        for (var offset = rva; (long)offset + 32 <= (long)rva + size; offset += 32)
        {
            var attributes = Marshal.ReadInt32(image, offset);
            var nameRva = Marshal.ReadInt32(image, offset + 4);
            if (nameRva == 0) break;
            if ((attributes & 1) == 0 || nameRva < 0 || nameRva >= imageSize) continue;
            var moduleName = Marshal.PtrToStringAnsi(image + nameRva);
            if (!string.Equals(moduleName, library, StringComparison.OrdinalIgnoreCase)) continue;
            var iatRva = Marshal.ReadInt32(image, offset + 12);
            var namesRva = Marshal.ReadInt32(image, offset + 16);
            if (iatRva <= 0 || namesRva <= 0) return 0;
            for (var index = 0; index < 10000; index++)
            {
                var namesOffset = (long)namesRva + index * 8L;
                var slotOffset = (long)iatRva + index * 8L;
                if (namesOffset + 8 > imageSize || slotOffset + 8 > imageSize) return 0;
                var entry = (ulong)Marshal.ReadInt64(image, (int)namesOffset);
                if (entry == 0) break;
                if ((entry & (1UL << 63)) != 0) continue;
                if (entry + 2 >= (ulong)imageSize) return 0;
                var name = Marshal.PtrToStringAnsi(image + (nint)(entry + 2));
                if (name == function) return image + (nint)slotOffset;
            }
        }
        return 0;
    }

    private void WriteSlot(nint value)
    {
        if (!VirtualProtect(_slot, (nuint)IntPtr.Size, 0x04, out var originalProtection))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try { Marshal.WriteIntPtr(_slot, value); }
        finally
        {
            if (!VirtualProtect(_slot, (nuint)IntPtr.Size, originalProtection, out _))
                _report($"输入法兼容：无法恢复导入表内存保护，错误 {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        WriteSlot(_original);
        _disposed = true;
        _report($"legacy-ime-hook-removed calls={_calls}");
        NativeLibrary.Free(_module);
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DisableLegacyIme();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(nint address, nuint size, uint protection, out uint oldProtection);
}
