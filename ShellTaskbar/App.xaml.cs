using System.Windows;

namespace ShellTaskbar
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var taskbar = new TaskbarWindow();
            taskbar.Show();
        }
    }
}