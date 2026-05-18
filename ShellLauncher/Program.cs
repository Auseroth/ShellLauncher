using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Windows;
using ShellLauncher;


public class AppConfig
{
    public string? Name { get; set; }
    public string? Path { get; set; }
    public string? Args { get; set; }
    public bool ExcludeFromTaskbar { get; set; } = false;
    public bool RunOnce { get; set; } = false;
    public string? DependsOn { get; set; }
}

class Program
{
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("kernel32.dll")] static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] static extern bool AllocConsole();
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public POINT pt; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x; public int y; }

    const int SW_HIDE = 0;
    const int SW_SHOW = 5;
    const uint MOD_CTRL = 0x0002;
    const uint MOD_SHIFT = 0x0004;
    const uint MOD_ALT = 0x0001;
    const uint WM_HOTKEY = 0x0312;
    const int HOTKEY_ID      = 1; // Ctrl+Shift+Alt+S  — toggle console
    const int HOTKEY_ID_CFG  = 2; // Ctrl+Shift+Alt+C  — open config editor

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr LoadImage(IntPtr hInstance, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    const uint WM_SETICON = 0x0080;
    const uint IMAGE_ICON = 1;
    const uint LR_LOADFROMFILE = 0x00000010;

    static void SetConsoleIcon(string iconFileName, string logFilePath)
    {
        IntPtr hWnd = GetConsoleWindow();
        string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, iconFileName);

        IntPtr hIcon = LoadImage(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE);
        if (hIcon != IntPtr.Zero)
        {
            SendMessage(hWnd, WM_SETICON, (IntPtr)0, hIcon); // Set small icon
            SendMessage(hWnd, WM_SETICON, (IntPtr)1, hIcon); // Set big icon
        }
        else
        {
            Console.WriteLine($"Failed to load icon from path: {iconPath}");
            Log(logFilePath, $"Failed to load icon from path: {iconPath}");
        }
    }

    static Dictionary<string, int> launchedPids = new Dictionary<string, int>();
    static HashSet<string> ranOnce = new HashSet<string>();
    static HashSet<string> dependencyFired = new HashSet<string>(); // "appKey|parentPid"

    [STAThread]
    static void Main(string[] args)
    {
        string configPath  = @"C:\ProgramData\ShellLauncher\config.json";
        string logFilePath = @"C:\ProgramData\ShellLauncher\log.txt";
        string readMePath  = @"C:\ProgramData\ShellLauncher\README.txt";

        // --- Argument handling ---
        if (args.Length > 0)
        {
            string arg = args[0].ToLowerInvariant();

            if (arg == "/?" || arg == "/help" || arg == "-?" || arg == "-help")
            {
                AllocConsole();
                Console.WriteLine("ShellLauncher - Command Line Usage");
                Console.WriteLine("===================================");
                Console.WriteLine();
                Console.WriteLine("  ShellLauncher.exe          Run normally (kiosk monitoring mode).");
                Console.WriteLine("  ShellLauncher.exe /c       Open the configuration editor only,");
                Console.WriteLine("                             then exit. No monitoring is started.");
                Console.WriteLine("  ShellLauncher.exe /?       Show this help message.");
                Console.WriteLine();
                Console.WriteLine("Notes:");
                Console.WriteLine("  /c is intended for admin use via the Start Menu shortcut.");
                Console.WriteLine($"  Config file: {configPath}");
                Console.WriteLine($"  Log file:    {logFilePath}");
                Console.WriteLine();
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                return;
            }

            if (arg == "/c" || arg == "-c")
            {
                // If not elevated, relaunch this process elevated and exit
                if (!IsElevated())
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = Process.GetCurrentProcess().MainModule!.FileName,
                            Arguments = "/c",
                            Verb = "runas",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception)
                    {
                        // User cancelled the UAC prompt — just exit silently
                    }
                    return;
                }

                // Already elevated — open the editor
                List<AppConfig> existing;
                try
                {
                    existing = File.Exists(configPath)
                        ? JsonSerializer.Deserialize<List<AppConfig>>(File.ReadAllText(configPath)) ?? new List<AppConfig>()
                        : new List<AppConfig>();
                }
                catch (Exception ex)
                {
                    Log(logFilePath, $"/c: Failed to read config: {ex.Message}");
                    existing = new List<AppConfig>();
                }

                var wpfApp = new Application();
                wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var editor = new JsonEditorWindow(
                    configPath,
                    existing,
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shell_dark.ico"));
                editor.ShowDialog();

                if (!editor.LaunchAfterSave)
                {
                    wpfApp.Shutdown();
                    return;
                }

                // LaunchAfterSave = true — fall through into normal monitoring mode below
            }
        }

        // --- Normal monitoring mode ---
        AllocConsole();

        SetConsoleIcon("shell_dark.ico", logFilePath);

        IntPtr consoleHandle = GetConsoleWindow();
        ShowWindow(consoleHandle, SW_HIDE);

        bool hotkeyRegistered = RegisterHotKey(IntPtr.Zero, HOTKEY_ID,     MOD_CTRL | MOD_SHIFT | MOD_ALT, (uint)ConsoleKey.S);
        bool cfgHotkeyReg     = RegisterHotKey(IntPtr.Zero, HOTKEY_ID_CFG, MOD_CTRL | MOD_SHIFT | MOD_ALT, (uint)ConsoleKey.C);
        if (!hotkeyRegistered) Log(logFilePath, "Warning: Failed to register Ctrl+Shift+Alt+S hotkey.");
        if (!cfgHotkeyReg)     Log(logFilePath, "Warning: Failed to register Ctrl+Shift+Alt+C hotkey.");

        bool isVisible = false;

        if (!EnsureJsonFileExists(configPath, logFilePath)) return;

        List<AppConfig> apps;
        try
        {
            apps = JsonSerializer.Deserialize<List<AppConfig>>(File.ReadAllText(configPath)) ?? new List<AppConfig>();
        }
        catch (Exception ex)
        {
            Log(logFilePath, $"Failed to read or parse config file: {ex.Message}");
            return;
        }

        if (!README(readMePath)) return;

        // Taskbar is hardcoded — always launched and monitored, never needs a config entry
        string taskbarExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShellTaskbar.exe");
        int taskbarPid = 0;

        Console.WriteLine("ShellLauncher started. Monitoring applications...");

        while (true)
        {
            // Hotkey toggle — always force console to front when showing
            if (PeekMessage(out MSG msg, IntPtr.Zero, WM_HOTKEY, WM_HOTKEY, 1))
            {
                if (msg.wParam.ToInt32() == HOTKEY_ID)
                {
                    isVisible = !isVisible;
                    ShowWindow(consoleHandle, isVisible ? SW_SHOW : SW_HIDE);
                    if (isVisible)
                    {
                        BringWindowToTop(consoleHandle);
                        SetForegroundWindow(consoleHandle);
                    }
                }
                else if (msg.wParam.ToInt32() == HOTKEY_ID_CFG)
                {
                    // Open the config editor on the UI thread
                    if (Application.Current == null)
                    {
                        var wpfApp = new Application();
                        wpfApp.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    }

                    var editor = new JsonEditorWindow(
                        configPath,
                        JsonSerializer.Deserialize<List<AppConfig>>(File.ReadAllText(configPath)) ?? new List<AppConfig>(),
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shell_dark.ico"),
                        launchedPids,
                        onKillComplete: () =>
                        {
                            ranOnce.Clear();
                            dependencyFired.Clear();
                            Log(logFilePath, "All apps closed via editor. Tracking state reset.");
                        });
                    editor.ShowDialog();

                    if (editor.ShutdownAfterKill)
                    {
                        Log(logFilePath, "ShellLauncher exiting at admin request via Close All Apps.");
                        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
                        UnregisterHotKey(IntPtr.Zero, HOTKEY_ID_CFG);
                        Environment.Exit(0);
                    }

                    // Reload apps in case the user changed the config
                    try
                    {
                        apps = JsonSerializer.Deserialize<List<AppConfig>>(File.ReadAllText(configPath)) ?? new List<AppConfig>();
                        Log(logFilePath, "Config reloaded after editor closed.");
                    }
                    catch (Exception ex)
                    {
                        Log(logFilePath, $"Failed to reload config after editor: {ex.Message}");
                    }
                }
            }

            // Monitor taskbar — check by process name only (no MainWindowHandle required on startup)
            if (File.Exists(taskbarExe))
            {
                bool taskbarRunning = Process.GetProcessesByName("ShellTaskbar").Any();
                if (!taskbarRunning)
                {
                    Console.WriteLine("ShellTaskbar is not running. Attempting to restart...");
                    try
                    {
                        var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = taskbarExe,
                            UseShellExecute = true
                        });
                        if (proc != null)
                        {
                            taskbarPid = proc.Id;
                            Console.WriteLine($"Started ShellTaskbar with PID {proc.Id}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(logFilePath, $"Failed to start ShellTaskbar: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("ShellTaskbar is running.");
                }
            }

            // Monitor configured apps
            foreach (var app in apps)
            {
                if (string.IsNullOrWhiteSpace(app.Path)) continue;

                string appKey = app.Name ?? app.Path;
                string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Path);

                // --- DependsOn: fire once per parent session, never independently monitored ---
                if (!string.IsNullOrWhiteSpace(app.DependsOn))
                {
                    var dependency = apps.FirstOrDefault(a =>
                        string.Equals(a.Name, app.DependsOn, StringComparison.OrdinalIgnoreCase));

                    if (dependency == null)
                    {
                        Log(logFilePath, $"{app.Name}: unknown DependsOn '{app.DependsOn}' — check the Name spelling.");
                        continue;
                    }

                    string depExeName = System.IO.Path.GetFileNameWithoutExtension(dependency.Path);

                    // Use the process with a main window — reliable sign the parent is fully up
                    var parentProc = Process.GetProcessesByName(depExeName)
                                           .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

                    if (parentProc == null)
                    {
                        // Parent not running — clear fired state so script re-triggers on next parent launch
                        dependencyFired.RemoveWhere(k => k.StartsWith(appKey + "|"));
                        Console.WriteLine($"{app.Name}: waiting for '{app.DependsOn}'...");
                        continue;
                    }

                    // Parent is running — only fire if this is a NEW parent session (new PID)
                    string fireKey = $"{appKey}|{parentProc.Id}";
                    if (dependencyFired.Contains(fireKey))
                    {
                        Console.WriteLine($"{app.Name}: already ran for current '{app.DependsOn}' session (PID {parentProc.Id}).");
                        continue;
                    }

                    // New parent session — launch the script
                    Log(logFilePath, $"{app.Name}: '{app.DependsOn}' started (PID {parentProc.Id}), launching {app.Name}...");
                    try
                    {
                        var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = app.Path,
                            Arguments = app.Args ?? "",
                            UseShellExecute = true
                        });
                        if (proc != null)
                        {
                            launchedPids[exeName] = proc.Id;
                            dependencyFired.Add(fireKey);
                            Console.WriteLine($"Started {app.Name} with PID {proc.Id}");
                        }
                        else
                        {
                            Log(logFilePath, $"Failed to start {app.Name}: Process returned null.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(logFilePath, $"Failed to start {app.Name}: {ex.Message}");
                    }
                    continue; // DependsOn apps are NEVER independently monitored
                }

                // --- Normal monitoring (no DependsOn) ---
                if (app.RunOnce && ranOnce.Contains(appKey)) continue;

                launchedPids.TryGetValue(exeName, out int pid);
                if (!IsProcessRunning(exeName, logFilePath, pid == 0 ? null : pid))
                {
                    Console.WriteLine($"{app.Name} is not running. Attempting to restart...");
                    try
                    {
                        if (app.RunOnce) ranOnce.Add(appKey); // mark before launch so failure doesn't retry

                        var proc = Process.Start(new ProcessStartInfo
                        {
                            FileName = app.Path,
                            Arguments = app.Args ?? "",
                            UseShellExecute = true
                        });
                        if (proc != null)
                        {
                            launchedPids[exeName] = proc.Id;
                            Console.WriteLine($"Started {app.Name} with PID {proc.Id}");
                        }
                        else
                        {
                            Log(logFilePath, $"Failed to start {app.Name}: Process returned null.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(logFilePath, $"Failed to start {app.Name}: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"{app.Name} is running.");
                }
            }

            Thread.Sleep(3000);
        }
    }

    static bool IsProcessRunning(string exeName, string logFilePath, int? pid = null)
    {
        // First check: main window
        if (Process.GetProcessesByName(exeName).Any(p => p.MainWindowHandle != IntPtr.Zero))
            return true;

        // Second check: PID still alive
        if (pid.HasValue)
        {
            try
            {
                var p = Process.GetProcessById(pid.Value);
                if (!p.HasExited)
                    return true;
            }
            catch
            {
                Log(logFilePath, $"{exeName} with PID {pid.Value} not found or has exited.");
            }
        }

        return false;
    }

    static bool EnsureJsonFileExists(string filePath, string logFilePath)
    {
        if (File.Exists(filePath))
        {
            Console.WriteLine($"Config file found at {filePath}");
            return true;
        }
        try
        {
            var defaultConfigs = new List<AppConfig>
            {
                new AppConfig
                {
                    Name = "Edge",
                    Path = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                    Args = "--no-first-run --start-maximized --user-data-dir=C:\\KioskEdgeProfile https://www.google.com https://www.yahoo.com",
                    ExcludeFromTaskbar = false,
                    RunOnce = false,
                    DependsOn = null
                },
                new AppConfig
                {
                    Name = "Notepad",
                    Path = "notepad.exe",
                    Args = "",
                    ExcludeFromTaskbar = false,
                    RunOnce = false,
                    DependsOn = null
                },
                new AppConfig
                    {
                    Name = "ExampleScript",
                    Path = "Powershell.exe",
                    Args = "-windowstyle hidden -executionpolicy bypass -file \"C:\\Path\\To\\example_script.ps1\"",
                    ExcludeFromTaskbar = true,
                    RunOnce = true,
                    DependsOn = "Edge"
                    }
            };

            // A WPF Application instance is required for ShowDialog() to work
            if (Application.Current == null)
            {
                var app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }

            var editorWindow = new JsonEditorWindow(filePath, defaultConfigs,
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shell_dark.ico"));
            editorWindow.ShowDialog();

            return File.Exists(filePath);
        }
        catch (Exception ex)
        {
            Log(logFilePath, $"Failed to create or edit config file: {ex.Message}");
            return false;
        }
    }

    static bool README(string readMePath)
    {
        if (File.Exists(readMePath))
        {
            Console.WriteLine($"README file found at {readMePath}");
            return true;
        }
        try
        {
            string readmeContent =
                "========================================\n" +
                " ShellLauncher - README\n" +
                "========================================\n\n" +

                "OVERVIEW\n" +
                "--------\n" +
                "ShellLauncher monitors a list of applications and automatically restarts\n" +
                "them if they are closed or crash. It is designed for kiosk environments\n" +
                "using Windows Shell Launcher (WESL).\n\n" +
                "ShellTaskbar provides a custom taskbar with app buttons, a clock, and\n" +
                "power options. It is launched and monitored automatically by ShellLauncher.\n\n" +

                "SETUP\n" +
                "-----\n" +
                "Option A - Use the NSIS Installer (recommended):\n" +
                "  See the BUILDING THE INSTALLER section below.\n" +
                "  The installer will automatically run EnableShell.ps1.\n\n" +
                "Option B - Manual setup:\n" +
                "  1. Copy ShellLauncher.exe, ShellTaskbar.exe, and all associated\n" +
                "     files to:\n" +
                "       C:\\Program Files (x86)\\ShellLauncher\\\n\n" +
                "  2. Run Resources\\EnableShell.ps1 as Administrator to:\n" +
                "       - Enable the Windows Shell Launcher feature (WESL)\n" +
                "       - Create the KioskUser account (no password)\n" +
                "       - Configure KioskUser to launch ShellLauncher instead of Explorer\n" +
                "       - Set up auto-login for KioskUser\n\n" +
                "  3. Reboot the machine. ShellLauncher will start automatically.\n\n" +
                "  NOTE: On first launch, if no config.json exists, a configuration\n" +
                "  editor window will open. Fill in your applications and click\n" +
                "  'Save and Close'. ShellLauncher will then begin monitoring.\n\n" +

                "HOTKEYS\n" +
                "-------\n" +
                "  Ctrl + Shift + Alt + S  ->  Show / Hide the console status window\n" +
                "  Ctrl + Shift + Alt + C  ->  Open the configuration editor at any time\n\n" +

                "CONFIGURATION — EDITOR\n" +
                "----------------------\n" +
                "Config file location:\n" +
                "  C:\\ProgramData\\ShellLauncher\\config.json\n\n" +
                "On first run, the editor opens automatically if no config exists.\n" +
                "At any time, press Ctrl+Shift+Alt+C to reopen it.\n\n" +
                "Editor columns:\n" +
                "  Name              Display name used in logs and the taskbar button.\n" +
                "  Path              Full path or filename of the executable.\n" +
                "  Arguments         Command-line arguments (leave blank if not needed).\n" +
                "  Hide from Taskbar When checked, no taskbar button is shown for this app.\n" +
                "                    The app still runs and is monitored normally.\n" +
                "  Run Once          When checked, the app launches once and is never\n" +
                "                    restarted. Ignored if Depends On is set.\n" +
                "  Depends On        Enter the Name of another app in the list. This app\n" +
                "                    will only launch when that parent app has a visible\n" +
                "                    window, and will re-launch once per parent session.\n" +
                "                    Leave blank for no dependency.\n\n" +

                "CONFIGURATION — DIRECT JSON EDITING\n" +
                "------------------------------------\n" +
                "For automated deployment, edit config.json directly:\n\n" +
                "[\n" +
                "  {\n" +
                "    \"Name\": \"Edge\",\n" +
                "    \"Path\": \"C:\\\\Program Files (x86)\\\\Microsoft\\\\Edge\\\\Application\\\\msedge.exe\",\n" +
                "    \"Args\": \"--no-first-run --start-maximized --app=https://example.com\",\n" +
                "    \"ExcludeFromTaskbar\": false,\n" +
                "    \"RunOnce\": false,\n" +
                "    \"DependsOn\": null\n" +
                "  },\n" +
                "  {\n" +
                "    \"Name\": \"LoginScript\",\n" +
                "    \"Path\": \"Powershell.exe\",\n" +
                "    \"Args\": \"-WindowStyle Hidden -ExecutionPolicy Bypass -File \\\"C:\\\\Scripts\\\\login.ps1\\\"\",\n" +
                "    \"ExcludeFromTaskbar\": true,\n" +
                "    \"RunOnce\": false,\n" +
                "    \"DependsOn\": \"Edge\"\n" +
                "  }\n" +
                "]\n\n" +
                "Field reference:\n" +
                "  Name              string  — required, must be unique if used in DependsOn\n" +
                "  Path              string  — required, full path or filename on PATH\n" +
                "  Args              string  — optional, command-line arguments\n" +
                "  ExcludeFromTaskbar bool   — default false\n" +
                "  RunOnce           bool   — default false\n" +
                "  DependsOn         string  — optional, exact Name of parent app or null\n\n" +
                "Changes to config.json are picked up automatically by ShellTaskbar.\n" +
                "ShellLauncher requires a restart (or re-open via hotkey) to use changes.\n\n" +

                "SHELLTASKBAR\n" +
                "------------\n" +
                "ShellTaskbar is the custom taskbar launched by ShellLauncher. It:\n" +
                "  - Displays a button for each app not marked ExcludeFromTaskbar\n" +
                "  - Clicking a button restores/focuses the app window\n" +
                "  - Right-clicking a button gives Restore / Minimize / Maximize / Close\n" +
                "  - Displays the current time (12-hour format)\n" +
                "  - Provides a power button (Shut Down / Restart) with confirmation\n" +
                "  - Automatically reloads config.json when it changes on disk\n" +
                "  - Restarts automatically if it crashes (monitored by ShellLauncher)\n\n" +

                "CONSOLE WINDOW\n" +
                "--------------\n" +
                "ShellLauncher runs with a hidden console window showing live status.\n" +
                "Toggle its visibility with:  Ctrl + Shift + Alt + S\n\n" +
                "The console displays:\n" +
                "  - Which apps are running or being restarted\n" +
                "  - DependsOn waiting / session-fired messages\n" +
                "  - Startup and config status messages\n" +
                "  - Errors and warnings\n\n" +

                "LOGS\n" +
                "----\n" +
                "Log file location:\n" +
                "  C:\\ProgramData\\ShellLauncher\\log.txt\n\n" +
                "Logs include timestamps, restart events, dependency events, and errors.\n\n" +

                "BUILDING THE INSTALLER\n" +
                "----------------------\n" +
                "The NSIS installer script is at:\n" +
                "  ShellLauncher\\Resources\\ShellLauncher.nsi\n\n" +
                "Requirements:\n" +
                "  - NSIS 3.x  (https://nsis.sourceforge.io)\n" +
                "  - Publish both projects first (use the FolderProfile in VS or run):\n" +
                "      dotnet publish ShellLauncher\\ShellLauncher.csproj -c Release\n\n" +
                "  Publishing ShellLauncher automatically publishes ShellTaskbar into\n" +
                "  the same output folder via the CopyShellTaskbarPublish MSBuild target.\n\n" +
                "Before compiling the .nsi script, update these paths for your machine:\n" +
                "  Outfile  — path where the installer .exe will be written\n" +
                "  Icon     — path to Shell_dark.ico in the publish output\n" +
                "  File /r  — path to the publish output folder\n\n" +
                "To compile:\n" +
                "  Right-click ShellLauncher.nsi -> 'Compile NSIS Script'\n" +
                "  Or run: makensis ShellLauncher.nsi\n\n" +
                "The installer will silently:\n" +
                "  1. Copy all files to C:\\Program Files (x86)\\ShellLauncher\\\n" +
                "  2. Create a Start Menu shortcut\n" +
                "  3. Register in Add/Remove Programs\n" +
                "  4. Run EnableShell.ps1 to configure WESL and auto-login\n\n" +
                "The uninstaller will:\n" +
                "  1. Run DisableShell.ps1 to restore the default Explorer shell\n" +
                "  2. Remove all installed files and registry entries\n\n" +

                "REMOVING KIOSK MODE\n" +
                "-------------------\n" +
                "Automatic: Uninstall ShellLauncher via Add/Remove Programs.\n" +
                "  DisableShell.ps1 runs automatically and restores Explorer.\n\n" +
                "Manual: Run in PowerShell as Administrator:\n" +
                "  $WESL = [wmiclass]\"\\\\.\\root\\standardcimv2\\embedded:WESL_UserSetting\"\n" +
                "  $WESL.SetEnabled($false)\n\n" +

                "FILES\n" +
                "-----\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\ShellLauncher.exe        — Main monitor\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\ShellTaskbar.exe         — Custom taskbar\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\resources\\EnableShell.ps1  — Kiosk setup\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\resources\\DisableShell.ps1 — Kiosk removal\n" +
                "  C:\\ProgramData\\ShellLauncher\\config.json                      — App config\n" +
                "  C:\\ProgramData\\ShellLauncher\\log.txt                          — Runtime log\n" +
                "  C:\\ProgramData\\ShellLauncher\\README.txt                       — This file\n";

            File.WriteAllText(readMePath, readmeContent);
            Console.WriteLine($"README file created at {readMePath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create README file: {ex.Message}");
            return false;
        }
    }

    static void Log(string logFilePath, string message)
    {
        string logMessage = $"{DateTime.Now:HH:mm:ss} - {message}";

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logFilePath)!);
            File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to write to log: {ex.Message}");
        }

        Console.WriteLine(logMessage);
    }

    static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}