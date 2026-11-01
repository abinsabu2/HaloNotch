using System.Runtime.InteropServices;
using System.Text;
using static HaloNotch.Win.Native;

namespace HaloNotch.Win;

/// Win32 clipboard read/write. Formats password managers set to stay out of clipboard history are respected
/// (the Windows counterpart of org.nspasteboard.ConcealedType on the Mac).
static class ClipboardWin
{
    public record Content(string? Text, string[]? Files, byte[]? Dib);

    static readonly uint Exclude = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint ViewerIgnore = RegisterClipboardFormatW("Clipboard Viewer Ignore");
    static readonly uint CanIncludeInHistory = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    static readonly uint CanUploadToCloud = RegisterClipboardFormatW("CanUploadToCloudClipboard");

    public static uint Sequence => GetClipboardSequenceNumber();

    /// Null if the clipboard is busy, empty, or marked "don't record".
    public static Content? Read()
    {
        if (IsClipboardFormatAvailable(Exclude) || IsClipboardFormatAvailable(ViewerIgnore)) return null;
        if (!Open()) return null;
        try
        {
            if (IsClipboardFormatAvailable(CanIncludeInHistory) && ReadDword(CanIncludeInHistory) == 0) return null;
            if (IsClipboardFormatAvailable(CF_HDROP)) return new Content(null, ReadFiles(), null);
            if (IsClipboardFormatAvailable(CF_UNICODETEXT)) return new Content(ReadText(), null, null);
            if (IsClipboardFormatAvailable(CF_DIB)) return new Content(null, null, ReadBytes(CF_DIB));
            return null;
        }
        finally { CloseClipboard(); }
    }

    public static void WriteText(string text, bool secret = false) => Write(() =>
    {
        SetClipboardData(CF_UNICODETEXT, Global(Encoding.Unicode.GetBytes(text + "\0")));
        if (secret)
        {
            // Skipped by HaloNotch, Win+V history and cloud sync – like the Mac's ConcealedType.
            SetClipboardData(Exclude, Global(new byte[4]));
            SetClipboardData(CanIncludeInHistory, Global(new byte[4]));
            SetClipboardData(CanUploadToCloud, Global(new byte[4]));
        }
    });

    public static void WriteDib(byte[] dib) => Write(() => SetClipboardData(CF_DIB, Global(dib)));

    /// CF_HDROP: DROPFILES header (20 bytes, wide paths) + paths, each NUL-terminated, plus a final NUL.
    public static void WriteFiles(string[] paths) => Write(() =>
    {
        var list = Encoding.Unicode.GetBytes(string.Join("\0", paths) + "\0\0");
        var data = new byte[20 + list.Length];
        BitConverter.GetBytes(20).CopyTo(data, 0);      // pFiles: offset of the list
        BitConverter.GetBytes(1).CopyTo(data, 16);      // fWide
        list.CopyTo(data, 20);
        SetClipboardData(CF_HDROP, Global(data));
    });

    public static void Clear() => Write(() => { });

    static void Write(Action fill)
    {
        if (!Open()) return;
        try { EmptyClipboard(); fill(); }
        finally { CloseClipboard(); }
    }

    /// Another app may hold the clipboard for a moment – retry briefly.
    static bool Open()
    {
        for (int i = 0; i < 6; i++)
        {
            if (OpenClipboard(MessageWindow.Shared.Handle)) return true;
            Thread.Sleep(15);
        }
        return false;
    }

    static IntPtr Global(byte[] data)
    {
        var mem = GlobalAlloc(0x0002 /* GMEM_MOVEABLE */, (UIntPtr)data.Length);
        var ptr = GlobalLock(mem);
        Marshal.Copy(data, 0, ptr, data.Length);
        GlobalUnlock(mem);
        return mem;                                      // owned by the clipboard after SetClipboardData
    }

    static string? ReadText()
    {
        var h = GetClipboardData(CF_UNICODETEXT);
        if (h == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(GlobalLock(h)); }
        finally { GlobalUnlock(h); }
    }

    static byte[]? ReadBytes(uint format)
    {
        var h = GetClipboardData(format);
        if (h == IntPtr.Zero) return null;
        try
        {
            var size = (int)GlobalSize(h);
            var bytes = new byte[size];
            Marshal.Copy(GlobalLock(h), bytes, 0, size);
            return bytes;
        }
        finally { GlobalUnlock(h); }
    }

    static uint ReadDword(uint format) =>
        ReadBytes(format) is { Length: >= 4 } b ? BitConverter.ToUInt32(b, 0) : 1;

    static string[] ReadFiles()
    {
        var h = GetClipboardData(CF_HDROP);
        uint count = DragQueryFileW(h, 0xFFFFFFFF, null, 0);
        var files = new string[count];
        for (uint i = 0; i < count; i++)
        {
            var sb = new StringBuilder((int)DragQueryFileW(h, i, null, 0) + 1);
            DragQueryFileW(h, i, sb, (uint)sb.Capacity);
            files[i] = sb.ToString();
        }
        return files;
    }

    /// CF_DIB → BMP file bytes (adds the 14-byte file header) so the image decoder can read it.
    public static byte[] DibToBmp(byte[] dib)
    {
        int headerSize = BitConverter.ToInt32(dib, 0);
        int bitCount = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        int colorsUsed = BitConverter.ToInt32(dib, 32);
        int masks = headerSize == 40 && compression == 3 ? 12 : 0;            // BI_BITFIELDS
        int palette = bitCount <= 8 ? (colorsUsed == 0 ? 1 << bitCount : colorsUsed) * 4 : 0;
        var bmp = new byte[14 + dib.Length];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(14 + headerSize + masks + palette).CopyTo(bmp, 10);
        dib.CopyTo(bmp, 14);
        return bmp;
    }
}
