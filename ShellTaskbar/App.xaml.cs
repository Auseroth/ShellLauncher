using System.Windows;

namespace ShellTaskbar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                System.IO.File.AppendAllText(
                    @"C:\ProgramData\ShellLauncher\taskbar_crash.log",
                    $"{System.DateTime.Now:HH:mm:ss} - {args.Exception}\n\n");
                args.Handled = true;
            };

            new TaskbarWindow().Show();
        }
    }
}