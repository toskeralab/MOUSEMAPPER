using System.Threading;
using System.Windows;
using System.Windows.Threading;
using MouseMapper.Services;

namespace MouseMapper;

/// <summary>
/// Ponto de entrada do MOUSEMAPPER. Garante instância única (duas cópias competiriam
/// pelos mesmos atalhos globais) e centraliza o tratamento de exceções não tratadas.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\MouseMapper.SingleInstance";

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "O MOUSEMAPPER já está em execução.\n\nProcure o ícone na bandeja do sistema (perto do relógio).",
                "MOUSEMAPPER",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        FileLogger.Instance.Info("==== MOUSEMAPPER iniciado ====");

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        FileLogger.Instance.Info("==== MOUSEMAPPER encerrado ====");
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLogger.Instance.Error("Exceção não tratada na UI.", e.Exception);

        MessageBox.Show(
            $"Ocorreu um erro inesperado, mas o MOUSEMAPPER continuará aberto.\n\n" +
            $"Detalhe: {e.Exception.Message}\n\n" +
            $"Os detalhes completos foram registrados em:\n{FileLogger.Instance.LogPath}",
            "MOUSEMAPPER",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        FileLogger.Instance.Error("Exceção fatal não tratada.", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        FileLogger.Instance.Error("Exceção em tarefa não observada.", e.Exception);
        e.SetObserved();
    }
}
