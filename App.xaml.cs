using System.Windows;

namespace CvaAnalyzer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            string errorMessage = $"Произошла ошибка:\n\n{e.Exception.Message}\n\n" +
                                 $"Тип: {e.Exception.GetType().Name}\n\n" +
                                 $"Стек:\n{e.Exception.StackTrace}";

            try
            {
                MessageBox.Show(errorMessage, "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                string errorMessage = $"Критическая ошибка:\n\n{ex.Message}\n\n" +
                                     $"Тип: {ex.GetType().Name}\n\n" +
                                     $"Стек:\n{ex.StackTrace}";

                try
                {
                    MessageBox.Show(errorMessage, "Критическая ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }
    }
}
