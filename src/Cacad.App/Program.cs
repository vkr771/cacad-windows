namespace Cacad;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        bool updated = args.Contains("--updated");
        using var mutex = new Mutex(true, "Local\\cacad-single-instance", out var isNew);
        // Just updated: the previous copy is closing. Wait for it instead of handing over to it.
        if (!isNew && updated)
        {
            try { isNew = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { isNew = true; }
        }
        if (!isNew)
        {
            var hwnd = Native.FindWindow(null, "cacad");
            if (hwnd != IntPtr.Zero)
                Native.PostMessage(hwnd, Native.WM_CACAD_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); // PerMonitorV2

        // Recoverable UI exceptions are logged and the island keeps running; fatal ones are logged before exit.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var app = new App(updated);
        Application.Run(app);
    }

    private static void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            File.AppendAllText(Path.Combine(SettingsStore.DataDir, "crash.log"), $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n{ex}\r\n\r\n");
        }
        catch { /* nowhere to report */ }
    }
}
