using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Bodian.ImeProbe;

// Diagnostic experiment only. Redirect one import in this process's local WinUI image.
// No system setting or DLL file is changed. Do not ship as an app fix without IME/focus validation.
internal sealed class LegacyImeInitializationHook : IDisposable
{
    private readonly nint _module;
    private readonly nint _slot;
    private readonly nint _original;
    private readonly DisableLegacyIme _callback;
    private readonly ProbeLog _log;
    private int _calls;
    private bool _disposed;

    private LegacyImeInitializationHook(nint module, nint slot, ProbeLog log)
    {
        _module = module;
        _slot = slot;
        _log = log;
        _original = Marshal.ReadIntPtr(slot);
        _callback = () =>
        {
            var calls = Interlocked.Increment(ref _calls);
            _log.Write($"legacy-ime-disable-bypassed call={calls} thread={NativeWindows.GetCurrentThreadId()}");
            return 1;
        };
        WriteSlot(Marshal.GetFunctionPointerForDelegate(_callback));
    }

    internal static LegacyImeInitializationHook? TryInstall(ProbeLog log)
    {
        nint module = 0;
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Microsoft.ui.xaml.dll");
            module = NativeLibrary.Load(path);
            var slot = FindDelayImport(module, "IMM32.dll", "ImmDisableLegacyIME");
            if (slot == 0) throw new NotSupportedException("Expected WinUI delay import was not found; no memory was changed.");
            var hook = new LegacyImeInitializationHook(module, slot, log);
            log.Write($"legacy-ime-hook-installed image={path} slot-rva=0x{slot - module:X}");
            return hook;
        }
        catch (Exception exception)
        {
            log.Write($"legacy-ime-hook-unavailable {exception.GetType().Name}: {exception.Message}");
            if (module != 0) NativeLibrary.Free(module);
            return null;
        }
    }

    private static nint FindDelayImport(nint image, string library, string function)
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
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        WriteSlot(_original);
        _disposed = true;
        _log.Write($"legacy-ime-hook-removed calls={_calls}");
        NativeLibrary.Free(_module);
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DisableLegacyIme();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(nint address, nuint size, uint protection, out uint oldProtection);
}
