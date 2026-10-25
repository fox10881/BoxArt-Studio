using System;
using System.Runtime.InteropServices;
using System.Text;

namespace GameIconsStudio.Services;

/// <summary>
/// Unicode-safe .lnk read/write via the native IShellLinkW + IPersistFile
/// COM interfaces. Replaces late-bound WScript.Shell, whose ANSI entry
/// points mangle non-ANSI (e.g. Arabic) paths into "???".
/// </summary>
public sealed class ShellShortcut : IDisposable
{
    private const int BufferSize = 2048;

    // CLSID_ShellLink
    private static readonly Guid ShellLinkClsid = new("00021401-0000-0000-C000-000000000046");

    // Member order must match the IShellLinkW vtable.
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    // Member order must match the IPersistFile vtable (IPersist + 5 methods).
    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    private readonly IShellLinkW _link;
    private readonly IPersistFile _persist;

    private ShellShortcut()
    {
        _link = (IShellLinkW)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ShellLinkClsid)!)!;
        _persist = (IPersistFile)_link;
    }

    /// <summary>Open an existing .lnk — the path itself is LPWStr, so Arabic
    /// folder names round-trip correctly.</summary>
    public static ShellShortcut Open(string path)
    {
        var s = new ShellShortcut();
        s._persist.Load(path, 0 /* STGM_READ */);
        return s;
    }

    /// <summary>Create a fresh, unloaded shortcut.</summary>
    public static ShellShortcut Create() => new();

    private static string GetBuf(Action<StringBuilder> getter)
    {
        var sb = new StringBuilder(BufferSize);
        getter(sb);
        return sb.ToString();
    }

    public string TargetPath
    {
        get => GetBuf(sb => _link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0));
        set => _link.SetPath(value ?? string.Empty);
    }

    public string Arguments
    {
        get => GetBuf(sb => _link.GetArguments(sb, sb.Capacity));
        set => _link.SetArguments(value ?? string.Empty);
    }

    public string WorkingDirectory
    {
        get => GetBuf(sb => _link.GetWorkingDirectory(sb, sb.Capacity));
        set => _link.SetWorkingDirectory(value ?? string.Empty);
    }

    /// <summary>Combined "path,index" form, matching WScript.Shell semantics.</summary>
    public string IconLocation
    {
        get
        {
            var sb = new StringBuilder(BufferSize);
            _link.GetIconLocation(sb, sb.Capacity, out var index);
            return $"{sb},{index}";
        }
        set
        {
            var sep = value?.LastIndexOf(',') ?? -1;
            var file = sep >= 0 ? value![..sep] : value ?? string.Empty;
            var idx = sep >= 0 && int.TryParse(value![(sep + 1)..], out var i) ? i : 0;
            _link.SetIconLocation(file, idx);
        }
    }

    /// <summary>Save back to the file it was loaded from.</summary>
    public void Save() => _persist.Save(null, true);

    /// <summary>Save to an explicit path (Save-As semantics).</summary>
    public void SaveAs(string path) => _persist.Save(path, true);

    public void Dispose() => Marshal.FinalReleaseComObject(_link);
}
