namespace Cacad;

internal sealed class App : ApplicationContext
{
    private readonly SettingsStore _settings;
    private readonly IslandWindow _island;
    private readonly SettingsForm _settingsForm;
    private IntPtr _trayIcon = IntPtr.Zero;

    public App(bool updated = false)
    {
        _settings = SettingsStore.Load();
        // If it starts with Windows, point that at this copy (so moving or reinstalling the exe keeps working).
        if (StartupRegistrar.IsEnabled) StartupRegistrar.SetEnabled(true);

        _island = new IslandWindow(_settings);
        _settingsForm = new SettingsForm(_settings, _island);

        _island.Show();          // layered window; WS_EX_NOACTIVATE keeps focus
        new Updater(_settings, _island).AfterStart(updated);
        AddTray();

        _island.FormClosed += (_, _) => ExitThread();
    }

    private void AddTray()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var pill = new SolidBrush(Color.FromArgb(255, 14, 14, 17));
            g.FillRectangle(pill, 4, 12, 24, 8);
            using var accent = new SolidBrush(Color.FromArgb(255, 233, 234, 238));
            g.FillRectangle(accent, 13, 15, 6, 2);
        }
        _trayIcon = bmp.GetHicon();

        var nid = new Native.NOTIFYICONDATA
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            HWnd = _island.HandleForTray,
            UFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            UCallbackMessage = Native.WM_APP_TRAY,
            HIcon = _trayIcon,
            SzTip = "cacad - Dynamic Island",
        };
        Native.Shell_NotifyIcon(Native.NIM_ADD, ref nid);
    }

    protected override void ExitThreadCore()
    {
        var nid = new Native.NOTIFYICONDATA
        {
            CbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            HWnd = _island.IsHandleCreated ? _island.HandleForTray : IntPtr.Zero,
        };
        if (nid.HWnd != IntPtr.Zero)
            Native.Shell_NotifyIcon(Native.NIM_DELETE, ref nid);
        if (_trayIcon != IntPtr.Zero)
        {
            Native.DestroyIcon(_trayIcon);
            _trayIcon = IntPtr.Zero;
        }

        _settings.Save();
        base.ExitThreadCore();
    }
}
