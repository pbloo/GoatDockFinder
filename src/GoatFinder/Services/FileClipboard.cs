using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace GoatFinder.Services;

public static class FileClipboard
{
    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 5, DropEffectMove = 2;

    public static void Set(IEnumerable<string> paths, bool cut)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        if (list.Count == 0) return;

        var data = new DataObject();
        data.SetFileDropList(list);
        // O Explorer usa este formato para saber se "colar" deve copiar ou mover.
        data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy)));

        TryClipboard(() => Clipboard.SetDataObject(data, true));
    }

    public static (IReadOnlyList<string> Paths, bool Cut)? Get()
    {
        (IReadOnlyList<string>, bool)? result = null;
        TryClipboard(() =>
        {
            if (!Clipboard.ContainsFileDropList()) return;
            var paths = Clipboard.GetFileDropList().Cast<string>().ToList();
            bool cut = false;
            if (Clipboard.GetData(PreferredDropEffect) is MemoryStream stream && stream.Length >= 4)
            {
                var buffer = new byte[4];
                stream.ReadExactly(buffer);
                cut = (BitConverter.ToInt32(buffer) & DropEffectMove) != 0;
            }
            result = (paths, cut);
        });
        return result;
    }

    // A área de transferência é compartilhada; outro programa pode estar com ela aberta por instantes.
    private static void TryClipboard(Action action)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { action(); return; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(40); }
        }
    }
}
