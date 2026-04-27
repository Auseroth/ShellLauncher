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
    const int HOTKEY_ID = 1;

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

    [STAThread]
    static void Main()
    {
        AllocConsole();

        string configPath  = @"C:\ProgramData\ShellLauncher\config.json";
        string logFilePath = @"C:\ProgramData\ShellLauncher\log.txt";
        string readMePath  = @"C:\ProgramData\ShellLauncher\README.txt";

        SetConsoleIcon("shell_dark.ico", logFilePath);

        IntPtr consoleHandle = GetConsoleWindow();
        ShowWindow(consoleHandle, SW_HIDE);

        bool hotkeyRegistered = RegisterHotKey(IntPtr.Zero, HOTKEY_ID, MOD_CTRL | MOD_SHIFT | MOD_ALT, (uint)ConsoleKey.S);
        if (!hotkeyRegistered)
            Log(logFilePath, "Warning: Failed to register Ctrl+Shift+Alt+S hotkey.");

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

                string exeName = System.IO.Path.GetFileNameWithoutExtension(app.Path);
                launchedPids.TryGetValue(exeName, out int pid);
                if (!IsProcessRunning(exeName, logFilePath, pid == 0 ? null : pid))
                {
                    Console.WriteLine($"{app.Name} is not running. Attempting to restart...");
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
            catch {
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
                    Args = "--no-first-run --start-maximized --user-data-dir=C:\\KioskEdgeProfile https://www.google.com https://www.yahoo.com"
                },
                new AppConfig
                {
                    Name = "Notepad",
                    Path = "notepad.exe",
                    Args = ""
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

                "SETUP\n" +
                "-----\n" +
                "Option A - Use the NSIS Installer (recommended):\n" +
                "  See the BUILDING THE INSTALLER section below.\n" +
                "  The installer will automatically run EnableShell.ps1.\n\n" +
                "Option B - Manual setup:\n" +
                "  1. Copy ShellLauncher.exe and all associated files to:\n" +
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

                "CONFIGURATION\n" +
                "-------------\n" +
                "Config file location:\n" +
                "  C:\\ProgramData\\ShellLauncher\\config.json\n\n" +
                "On first run, a GUI editor will open automatically if no config exists.\n" +
                "Each row in the editor has three fields:\n" +
                "  Name  - Display name used in logs and console output\n" +
                "  Path  - Full path or filename of the executable\n" +
                "  Args  - Command-line arguments (leave blank if not needed)\n\n" +
                "Use 'Add Application' to add more rows, then 'Save and Close' when done.\n\n" +
                "Example of the saved config.json format:\n" +
                "[\n" +
                "  {\n" +
                "    \"Name\": \"Edge\",\n" +
                "    \"Path\": \"C:\\\\Program Files (x86)\\\\Microsoft\\\\Edge\\\\Application\\\\msedge.exe\",\n" +
                "    \"Args\": \"--no-first-run --start-maximized --guest https://www.example.com\"\n" +
                "  },\n" +
                "  {\n" +
                "    \"Name\": \"Notepad\",\n" +
                "    \"Path\": \"notepad.exe\",\n" +
                "    \"Args\": \"\"\n" +
                "  }\n" +
                "]\n\n" +
                "To re-open the editor after first run:\n" +
                "  Delete config.json and restart ShellLauncher, or edit the file directly.\n\n" +

                "CONSOLE WINDOW\n" +
                "--------------\n" +
                "ShellLauncher runs with a hidden console window showing live status.\n" +
                "Toggle its visibility with:\n\n" +
                "  Ctrl + Shift + Alt + S  ->  Show / Hide the console window\n\n" +
                "The console displays:\n" +
                "  - Which apps are running or being restarted\n" +
                "  - Startup and config status messages\n" +
                "  - Errors and warnings\n\n" +

                "LOGS\n" +
                "----\n" +
                "Log file location:\n" +
                "  C:\\ProgramData\\ShellLauncher\\log.txt\n\n" +
                "Logs include timestamps, restart events, and any errors encountered.\n\n" +

                "BUILDING THE INSTALLER\n" +
                "----------------------\n" +
                "The NSIS installer script is included in the repository at:\n" +
                "  ShellLauncher\\Resources\\ShellLauncher.nsi\n\n" +
                "Requirements:\n" +
                "  - NSIS 3.x  (https://nsis.sourceforge.io)\n" +
                "  - The project must be published first:\n" +
                "      dotnet publish -c Release -r win-x86 --self-contained true\n\n" +
                "Before compiling the .nsi script you MUST update these paths to match\n" +
                "your local machine:\n\n" +
                "  Outfile  - Path where the installer .exe will be written\n" +
                "             Default: C:\\temp file transfer\\...\\ShellLauncher_Install.exe\n\n" +
                "  Icon     - Path to Shell_dark.ico inside your publish output folder\n" +
                "             Default: ...\\bin\\Release\\net8.0\\publish\\win-x86\\ShellLauncher\\Shell_dark.ico\n\n" +
                "  File /r  - Path to your publish output folder\n" +
                "             Default: C:\\temp file transfer\\...\\publish\\win-x86\\ShellLauncher\\*.*\n\n" +
                "To compile once paths are updated:\n" +
                "  Right-click ShellLauncher.nsi in Windows Explorer -> 'Compile NSIS Script'\n" +
                "  Or run: makensis ShellLauncher.nsi\n\n" +
                "The installer will silently:\n" +
                "  1. Copy all files to C:\\Program Files (x86)\\ShellLauncher\\\n" +
                "  2. Create a Start Menu shortcut\n" +
                "  3. Register in Add/Remove Programs\n" +
                "  4. Automatically run EnableShell.ps1 to configure WESL and auto-login\n\n" +
                "The uninstaller will:\n" +
                "  1. Run DisableShell.ps1 to restore the default Explorer shell\n" +
                "  2. Remove all installed files and registry entries\n\n" +

                "REMOVING KIOSK MODE\n" +
                "-------------------\n" +
                "Automatic: Uninstall ShellLauncher via Add/Remove Programs.\n" +
                "  DisableShell.ps1 will run automatically and restore Explorer.\n\n" +
                "Manual: Run the following in PowerShell as Administrator:\n" +
                "  $WESL = [wmiclass]\"\\\\.\\root\\standardcimv2\\embedded:WESL_UserSetting\"\n" +
                "  $WESL.SetEnabled($false)\n\n" +

                "FILES\n" +
                "-----\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\ShellLauncher.exe     - Main executable\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\resources\\EnableShell.ps1  - Kiosk setup\n" +
                "  C:\\Program Files (x86)\\ShellLauncher\\resources\\DisableShell.ps1 - Kiosk removal\n" +
                "  C:\\ProgramData\\ShellLauncher\\config.json                   - App configuration\n" +
                "  C:\\ProgramData\\ShellLauncher\\log.txt                       - Runtime log\n" +
                "  C:\\ProgramData\\ShellLauncher\\README.txt                    - This file\n";

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
}