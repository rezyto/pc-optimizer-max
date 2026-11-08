using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PcMax.App.Services;

/// <summary>
/// Opens a URL or folder as the normal (non-elevated) user, even though PC Max runs elevated: asks the desktop's
/// Explorer window (which runs unelevated) to do the ShellExecute. This is Raymond Chen's pattern
/// ("How can I launch an unelevated process from my elevated process?"):
/// IShellWindows.FindWindowSW(SWC_DESKTOP) → IServiceProvider → IShellBrowser → IShellView →
/// IShellFolderViewDual.Application → IShellDispatch2.ShellExecute.
/// The browser therefore never runs with administrator rights.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShellDesktopLauncher
{
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;
    private const int SW_SHOWNORMAL = 1;

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");

    /// <summary>Returns false when the desktop shell is not available (Explorer not running, replaced shell…).</summary>
    public static bool TryOpen(string target)
    {
        object? shellWindowsObj = null, desktop = null, browserObj = null, viewObj = null, folderView = null, app = null;
        try
        {
            var type = Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!;
            shellWindowsObj = Activator.CreateInstance(type);
            var shellWindows = (IShellWindows)shellWindowsObj!;

            object loc = 0;        // CSIDL_DESKTOP (VT_I4)
            object? root = null;   // VT_EMPTY
            desktop = shellWindows.FindWindowSW(ref loc, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
            if (desktop is null) return false;

            var sid = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            ((IServiceProvider)desktop).QueryService(ref sid, ref iid, out browserObj);
            var browser = (IShellBrowser)browserObj;

            browser.QueryActiveShellView(out var view);
            viewObj = view;
            var dispIid = IID_IDispatch;
            view.GetItemObject(SVGIO_BACKGROUND, ref dispIid, out folderView);

            // IShellFolderViewDual and IShellDispatch2 are automation (IDispatch) interfaces: late-bound calls.
            dynamic fv = folderView;
            app = fv.Application;
            dynamic shell = app!;
            shell.ShellExecute(target, "", "", "open", SW_SHOWNORMAL);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn("Não foi possível abrir através do ambiente de trabalho do Windows", ex);
            return false;
        }
        finally
        {
            foreach (var o in new[] { app, folderView, viewObj, browserObj, desktop, shellWindowsObj })
            {
                try { if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------ COM declarations (vtable order matters)

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        int Count { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object Item([In, Optional, MarshalAs(UnmanagedType.Struct)] object index);
        [return: MarshalAs(UnmanagedType.IUnknown)] object _NewEnum();
        void Register([In, MarshalAs(UnmanagedType.IDispatch)] object pid, int hwnd, int swClass, out int cookie);
        void RegisterPending(int threadId, [In, MarshalAs(UnmanagedType.Struct)] ref object loc, [In, MarshalAs(UnmanagedType.Struct)] ref object locRoot, int swClass, out int cookie);
        void Revoke(int cookie);
        void OnNavigate(int cookie, [In, MarshalAs(UnmanagedType.Struct)] ref object loc);
        void OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW([In, MarshalAs(UnmanagedType.Struct)] ref object loc, [In, MarshalAs(UnmanagedType.Struct)] ref object? locRoot,
            int swClass, out int hwnd, int swfwOptions);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        // IOleWindow
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(int enterMode);
        // IShellBrowser
        void InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
        void SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
        void RemoveMenusSB(IntPtr hmenuShared);
        void SetStatusTextSB(IntPtr pszStatusText);
        void EnableModelessSB(int enable);
        void TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
        void BrowseObject(IntPtr pidl, uint wFlags);
        void GetViewStateStream(uint grfMode, out IntPtr ppStrm);
        void GetControlWindow(uint id, out IntPtr phwnd);
        void SendControlMsg(uint id, uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr pret);
        void QueryActiveShellView(out IShellView ppshv);
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        // IOleWindow
        void GetWindow(out IntPtr hwnd);
        void ContextSensitiveHelp(int enterMode);
        // IShellView
        [PreserveSig] int TranslateAccelerator(IntPtr pmsg);
        void EnableModeless(int enable);
        void UIActivate(uint uState);
        void Refresh();
        void CreateViewWindow(IntPtr psvPrevious, IntPtr pfs, IntPtr psb, IntPtr prcView, out IntPtr phWnd);
        void DestroyViewWindow();
        void GetCurrentInfo(IntPtr pfs);
        void AddPropertySheetPages(uint dwReserved, IntPtr pfn, IntPtr lparam);
        void SaveViewState();
        void SelectItem(IntPtr pidlItem, uint uFlags);
        void GetItemObject(uint uItem, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
    }
}
