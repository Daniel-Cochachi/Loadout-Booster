using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Loadout.Services;
using Loadout.ViewModels;

namespace Loadout;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        // Velopack: gestiona install/update/uninstall antes de arrancar WPF.
        // Sin esto, el Setup.exe y los deltas no pueden aplicarse.
        Velopack.VelopackApp.Build().Run();

        App app = new();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args)
            => Loadout.Services.Log.Error("UnhandledException (AppDomain)", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            Loadout.Services.Log.Error("UnhandledException (Dispatcher)", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args)
            => Loadout.Services.Log.Error("UnobservedTaskException", args.Exception);

        // Instancia única: si ya corre (bandeja), lo despierta en vez de duplicar
        bool fresh;
        try { _instanceMutex = new Mutex(true, @"Local\LoadoutSingleInstance", out fresh); }
        catch (AbandonedMutexException)
        {
            fresh = true; // el anterior murió sin soltar: somos los únicos
            try { _instanceMutex = Mutex.OpenExisting(@"Local\LoadoutSingleInstance"); } catch { }
        }
        if (!fresh)
        {
            WakeExisting();
            Shutdown();
            return;
        }

        RenderOptions.ProcessRenderMode = RenderMode.Default;
        base.OnStartup(e);

        // BD se crea sola al arrancar (ventana primero, datos después en MainWindow)
        var db = new DatabaseService();
        db.InitializeDatabase();

        // El MainWindow crea su VM con este servicio compartido
        Current.Properties["Db"] = db;
    }

    private static void WakeExisting()
    {
        try
        {
            int self = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("Loadout"))
            {
                try
                {
                    if (p.Id == self || p.MainWindowHandle == IntPtr.Zero) continue;
                    ShowWindow(p.MainWindowHandle, 9);
                    SetForegroundWindow(p.MainWindowHandle);
                    break;
                }
                catch { }
                finally { try { p.Dispose(); } catch { } }
            }
        }
        catch { }
    }
}
