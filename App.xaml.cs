using System;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace SilverWolfLauncher
{
    public partial class App : System.Windows.Application
    {
        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show("An unhandled exception just occurred: " + e.Exception.Message, "Exception Sample", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        }
        
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                Exception ex = (Exception)args.ExceptionObject;
                MessageBox.Show("Fatal unhandled exception: " + ex.ToString(), "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };
        }
    }
}