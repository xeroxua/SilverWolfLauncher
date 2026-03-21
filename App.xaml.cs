using System;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SilverWolfLauncher
{
    public partial class App : System.Windows.Application
    {
        private System.Threading.Mutex? _mutex;

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("An unhandled exception just occurred: " + e.Exception.Message, "Exception Sample", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
        
        protected override void OnStartup(StartupEventArgs e)
        {
            const string appName = "SilverWolfLauncher_SingleInstance_Mutex";
            _mutex = new System.Threading.Mutex(true, appName, out bool createdNew);
            if (!createdNew)
            {
                MessageBox.Show("SilverWolf Launcher is already running.", "SilverWolf Launcher", MessageBoxButton.OK, MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);
            
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Exception ex = (Exception)args.ExceptionObject;
                MessageBox.Show("Fatal unhandled exception: " + ex.ToString(), "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}