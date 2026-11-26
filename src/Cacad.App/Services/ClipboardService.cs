using System.Collections.Specialized;
using System.Text.Json;
using SkiaSharp;

namespace Cacad;

public enum ClipKind { Text, Link, Image, Files }

public sealed class ClipItem
{
    public ClipKind Kind { get; set; }
    public string Text { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public DateTime At { get; set; } = DateTime.Now;
    public bool Pinned { get; set; }
    public string Source { get; set; } = "";
    public string? ImagePath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public SKImage? Image { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int ImageW { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int ImageH { get; set; }
}

/// <summary>Clipboard history kept on this PC. Fed by WM_CLIPBOARDUPDATE on the island's window.</summary>
public sealed class ClipboardService
{
    private const int Max = 40;
    private readonly List<ClipItem> _items = new();
    private bool _suppressNext;

    public event Action? Changed;
    public IReadOnlyList<ClipItem> Items => _items;

    private static string StorePath => Path.Combine(SettingsStore.DataDir, "clipboard.json");
    private static string ImageDir { get { var d = Path.Combine(SettingsStore.DataDir, "clips"); Directory.CreateDirectory(d); return d; } }

    public ClipboardService()
    {
        try
        {
            if (File.Exists(StorePath))
            {
                var saved = JsonSerializer.Deserialize<List<ClipItem>>(File.ReadAllText(StorePath)) ?? new();
                foreach (var it in saved)
                {
                    if (it.Kind == ClipKind.Image)
                    {
                        if (it.ImagePath == null || !File.Exists(it.ImagePath)) continue;
                        using var bmp = SKBitmap.Decode(it.ImagePath);
                        if (bmp == null) continue;
                        SetImage(it, bmp);
                    }
                    _items.Add(it);
                }
            }
        }
        catch { }
    }

    /// <summary>Called from WM_CLIPBOARDUPDATE.</summary>
    public void OnClipboardUpdate()
    {
        if (_suppressNext) { _suppressNext = false; return; }
        ClipItem? item = null;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data == null) return;
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                item = new ClipItem { Kind = ClipKind.Files, Files = files.ToList(), Text = Path.GetFileName(files[0]) };
            else if (Clipboard.ContainsImage() && Clipboard.GetImage() is Bitmap img)
            {
                using (img)
                {
                    item = new ClipItem { Kind = ClipKind.Image };
                    using var bmp = ToSkia(img);
                    SetImage(item, bmp);
                    item.Text = $"{img.Width} × {img.Height}";
                    item.ImagePath = Path.Combine(ImageDir, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
                    try
                    {
                        using var enc = bmp.Encode(SKEncodedImageFormat.Png, 90);
                        File.WriteAllBytes(item.ImagePath, enc.ToArray());
                    }
                    catch { item.ImagePath = null; }
                }
            }
            else if (Clipboard.ContainsText())
            {
                string t = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(t)) return;
                bool link = Uri.TryCreate(t.Trim(), UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && !t.Trim().Contains(' ');
                item = new ClipItem { Kind = link ? ClipKind.Link : ClipKind.Text, Text = t };
            }
        }
        catch { return; } // clipboard busy
        if (item == null) return;
        item.Source = ForegroundApp();

        // The same thing copied again moves to the front instead of repeating.
        var dup = _items.FirstOrDefault(i => i.Kind == item.Kind && i.Kind != ClipKind.Image && i.Text == item.Text && i.Files.SequenceEqual(item.Files));
        if (dup != null)
        {
            _items.Remove(dup);
            dup.At = DateTime.Now;
            _items.Insert(0, dup);
        }
        else
        {
            _items.Insert(0, item);
            Trim();
        }
        Save();
        Changed?.Invoke();
    }

    private void Trim()
    {
        while (_items.Count > Max)
        {
            var victim = _items.LastOrDefault(i => !i.Pinned);
            if (victim == null) break;
            Remove(victim, save: false);
        }
    }

    public void Copy(ClipItem item)
    {
        _suppressNext = true;
        try
        {
            switch (item.Kind)
            {
                case ClipKind.Files:
                    var sc = new StringCollection();
                    sc.AddRange(item.Files.ToArray());
                    Clipboard.SetFileDropList(sc);
                    break;
                case ClipKind.Image when item.ImagePath != null && File.Exists(item.ImagePath):
                    using (var img = Image.FromFile(item.ImagePath)) Clipboard.SetImage(img);
                    break;
                default:
                    Clipboard.SetText(item.Text);
                    break;
            }
            _items.Remove(item);
            item.At = DateTime.Now;
            _items.Insert(0, item);
            Changed?.Invoke();
        }
        catch { _suppressNext = false; }
    }

    public void TogglePin(ClipItem item)
    {
        item.Pinned = !item.Pinned;
        Save();
        Changed?.Invoke();
    }

    public void Remove(ClipItem item, bool save = true)
    {
        _items.Remove(item);
        item.Image?.Dispose();
        if (item.ImagePath != null) try { File.Delete(item.ImagePath); } catch { }
        if (save) { Save(); Changed?.Invoke(); }
    }

    /// <summary>Clear everything except pinned items.</summary>
    public void Clear()
    {
        foreach (var it in _items.Where(i => !i.Pinned).ToList()) Remove(it, save: false);
        Save();
        Changed?.Invoke();
    }

    private void Save()
    {
        try { File.WriteAllText(StorePath, JsonSerializer.Serialize(_items)); } catch { }
    }

    private static void SetImage(ClipItem item, SKBitmap bmp)
    {
        // Keep a small preview in memory; the full picture stays on disk.
        float k = Math.Min(1f, 320f / Math.Max(bmp.Width, bmp.Height));
        var info = new SKImageInfo(Math.Max(1, (int)(bmp.Width * k)), Math.Max(1, (int)(bmp.Height * k)), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var small = bmp.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? bmp.Copy();
        item.Image = SKImage.FromBitmap(small);
        item.ImageW = bmp.Width;
        item.ImageH = bmp.Height;
    }

    public static SKBitmap ToSkia(Bitmap img)
    {
        var bmp = new SKBitmap(img.Width, img.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var data = img.LockBits(new Rectangle(0, 0, img.Width, img.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        unsafe
        {
            for (int y = 0; y < img.Height; y++)
                Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, (byte*)bmp.GetPixels() + y * bmp.RowBytes, bmp.RowBytes, img.Width * 4);
        }
        img.UnlockBits(data);
        return bmp;
    }

    private static string ForegroundApp()
    {
        try
        {
            var hwnd = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            string? desc = null;
            try { desc = p.MainModule?.FileVersionInfo.FileDescription; } catch { }
            return string.IsNullOrWhiteSpace(desc) ? p.ProcessName : desc!;
        }
        catch { return ""; }
    }
}
