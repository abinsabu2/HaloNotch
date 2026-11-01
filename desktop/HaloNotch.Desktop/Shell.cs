using System.Diagnostics;

namespace HaloNotch;

/// Opens files, folders, URLs and ms-settings: pages with the default handler.
public static class Shell
{
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: could not open {target} – {e.Message}"); }
    }

    /// Explorer window with the file selected (Mac: "Show in Finder").
    public static void Reveal(string path)
    {
        if (OperatingSystem.IsWindows()) Open("explorer.exe", $"/select,\"{path}\"");
        else Open(Path.GetDirectoryName(path) ?? path);
    }

    static void Open(string exe, string args)
    {
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); }
        catch (Exception e) { Debug.WriteLine($"HaloNotch: could not start {exe} – {e.Message}"); }
    }
}
