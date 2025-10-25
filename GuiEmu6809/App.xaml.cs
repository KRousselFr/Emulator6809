using System;
using System.Configuration;
using System.Windows;
using System.Windows.Threading;


namespace GuiEmu6809
{
    /// <summary>
    /// Logique d'interaction pour App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_DispatcherUnhandledException(
                object sender,
                DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(App.Current.MainWindow,
                            e.Exception.Message,
                            String.Format("Erreur {0} imprévue !",
                                          e.Exception.GetType().Name),
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
        }
    }
}

