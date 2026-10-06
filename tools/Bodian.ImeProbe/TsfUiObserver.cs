using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace Bodian.ImeProbe;

// The default observer preserves pbShow. It never calls Activate/Deactivate or changes TSF focus.
[ComVisible(true), ClassInterface(ClassInterfaceType.None)]
public sealed class TsfUiObserver : ITfUIElementSink, IDisposable
{
    private readonly ProbeLog _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly HashSet<uint> _active = new();
    private readonly HashSet<uint> _pending = new();
    private ITfUIElementMgr? _manager;
    private ITfSource? _source;
    private uint _cookie;
    private bool _advised;
    private bool _disposed;

    private TsfUiObserver(ProbeLog log, DispatcherQueue dispatcher)
    {
        _log = log;
        _dispatcher = dispatcher;
    }

    internal static TsfUiObserver? TryCreate(ProbeLog log, DispatcherQueue dispatcher)
    {
        var observer = new TsfUiObserver(log, dispatcher);
        try
        {
            var classId = new Guid("529a9e6b-6587-4f23-ab9e-9c7d683e3c50");
            var interfaceId = typeof(ITfUIElementMgr).GUID;
            Marshal.ThrowExceptionForHR(CoCreateInstance(ref classId, 0, 1, ref interfaceId, out observer._manager));
            observer._source = (ITfSource)observer._manager;
            var sinkId = typeof(ITfUIElementSink).GUID;
            Marshal.ThrowExceptionForHR(observer._source.AdviseSink(ref sinkId, observer, out observer._cookie));
            observer._advised = true;
            log.Write($"tsf-observer-installed cookie={observer._cookie}");
            return observer;
        }
        catch (Exception exception)
        {
            log.Write($"tsf-observer-unavailable hr=0x{exception.HResult:X8} {exception.Message}");
            observer.Dispose();
            return null;
        }
    }

    internal bool ForceShow { get; set; }

    public int BeginUIElement(uint id, ref int show)
    {
        try
        {
            var original = show;
            _active.Add(id);
            if (ForceShow) show = 1;
            _log.Write($"tsf-begin id={id} incoming-show={original} outgoing-show={show} force={ForceShow}");
            InspectCandidate(id);
            QueueInspect(id);
        }
        catch (Exception exception) { _log.Write($"tsf-begin-error hr=0x{exception.HResult:X8}"); }
        return 0;
    }

    public int UpdateUIElement(uint id)
    {
        try
        {
            _active.Add(id);
            _log.Write($"tsf-update id={id}");
            InspectCandidate(id);
            QueueInspect(id);
        }
        catch (Exception exception) { _log.Write($"tsf-update-error hr=0x{exception.HResult:X8}"); }
        return 0;
    }

    public int EndUIElement(uint id)
    {
        _active.Remove(id);
        _log.Write($"tsf-end id={id}");
        return 0;
    }

    private void InspectCandidate(uint id)
    {
        if (_manager is null) return;
        var result = _manager.GetUIElement(id, out var element);
        if (result < 0 || element is null) return;
        try
        {
            if (element is not ITfCandidateListUIElement candidates)
            {
                _log.Write($"tsf-candidate id={id} supported=False");
                return;
            }
            var countResult = candidates.GetCount(out var count);
            var selectionResult = candidates.GetSelection(out var selection);
            _log.Write($"tsf-candidate id={id} supported=True behavior={element is ITfCandidateListBehaviorTag} count={count} count-hr=0x{countResult:X8} selection={selection} selection-hr=0x{selectionResult:X8}");
        }
        finally { Marshal.ReleaseComObject(element); }
    }
    private void QueueInspect(uint id)
    {
        if (!_pending.Add(id)) return;
        if (!_dispatcher.TryEnqueue(() =>
        {
            try
            {
                if (_disposed || !_active.Contains(id) || _manager is null) return;
                var result = _manager.GetUIElement(id, out var element);
                if (result < 0 || element is null)
                {
                    _log.Write($"tsf-element-unavailable id={id} hr=0x{result:X8}");
                    return;
                }
                try
                {
                    var shownResult = element.IsShown(out var shown);
                    var guidResult = element.GetGUID(out var guid);
                    _log.Write($"tsf-element id={id} guid={guid} guid-hr=0x{guidResult:X8} shown={shown} shown-hr=0x{shownResult:X8}");
                    // Run after all Begin sinks have returned, so another sink cannot undo pbShow here.
                    if (ForceShow && shownResult >= 0 && shown == 0)
                    {
                        var showResult = element.Show(1);
                        var verifyResult = element.IsShown(out var after);
                        _log.Write($"tsf-show id={id} hr=0x{showResult:X8} after={after} verify-hr=0x{verifyResult:X8}");
                    }
                }
                finally { Marshal.ReleaseComObject(element); }
            }
            catch (Exception exception) { _log.Write($"tsf-inspect-error id={id} hr=0x{exception.HResult:X8}"); }
            finally { _pending.Remove(id); }
        })) _pending.Remove(id);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_advised && _source is not null)
        {
            var result = _source.UnadviseSink(_cookie);
            _log.Write($"tsf-observer-removed hr=0x{result:X8}");
        }
        _source = null;
        if (_manager is not null) Marshal.ReleaseComObject(_manager);
        _manager = null;
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(ref Guid classId, nint outer, uint context, ref Guid interfaceId,
        [MarshalAs(UnmanagedType.Interface)] out ITfUIElementMgr manager);
}

[ComVisible(true), Guid("ea1ea136-19df-11d7-a6d2-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface ITfUIElementSink
{
    [PreserveSig] int BeginUIElement(uint id, ref int show);
    [PreserveSig] int UpdateUIElement(uint id);
    [PreserveSig] int EndUIElement(uint id);
}

[ComImport, Guid("4ea48a35-60ae-446f-8fd6-e6a8d82459f7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfSource
{
    [PreserveSig] int AdviseSink(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] object sink, out uint cookie);
    [PreserveSig] int UnadviseSink(uint cookie);
}

[ComImport, Guid("ea1ea135-19df-11d7-a6d2-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfUIElementMgr
{
    [PreserveSig] int BeginUIElement([MarshalAs(UnmanagedType.Interface)] ITfUIElement element, ref int show, out uint id);
    [PreserveSig] int UpdateUIElement(uint id);
    [PreserveSig] int EndUIElement(uint id);
    [PreserveSig] int GetUIElement(uint id, [MarshalAs(UnmanagedType.Interface)] out ITfUIElement element);
    [PreserveSig] int EnumUIElements(out nint enumerator);
}

[ComImport, Guid("ea1ea137-19df-11d7-a6d2-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfUIElement
{
    [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.BStr)] out string description);
    [PreserveSig] int GetGUID(out Guid guid);
    [PreserveSig] int Show(int show);
    [PreserveSig] int IsShown(out int show);
}

// Explicitly include the inherited ITfUIElement slots in the COM vtable.
[ComImport, Guid("ea1ea138-19df-11d7-a6d2-00065b84435c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCandidateListUIElement
{
    [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.BStr)] out string description);
    [PreserveSig] int GetGUID(out Guid guid);
    [PreserveSig] int Show(int show);
    [PreserveSig] int IsShown(out int show);
    [PreserveSig] int GetUpdatedFlags(out uint flags);
    [PreserveSig] int GetDocumentMgr(out nint document);
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetSelection(out uint index);
    [PreserveSig] int GetString(uint index, [MarshalAs(UnmanagedType.BStr)] out string text);
    [PreserveSig] int GetPageIndex([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] uint[] indices, uint size, out uint pages);
    [PreserveSig] int SetPageIndex([In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] uint[] indices, uint pages);
    [PreserveSig] int GetCurrentPage(out uint page);
}

// Used only to query support, never to invoke a method.
[ComImport, Guid("85fad185-58ce-497a-9460-355366b64b9a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfCandidateListBehaviorTag { }
