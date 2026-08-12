using System.Windows;
using System.Windows.Threading;
using AiUsageMonitor.App.Interop;
using AiUsageMonitor.App.Theming;
using AiUsageMonitor.App.ViewModels;
using AiUsageMonitor.App.Views;
using AiUsageMonitor.Infrastructure.Logging;
using AiUsageMonitor.Infrastructure.Providers;
using AiUsageMonitor.Infrastructure.Refresh;
using AiUsageMonitor.Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiUsageMonitor.App;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private SingleInstance? _instance;

    public IServiceProvider Services => _services
        ?? throw new InvalidOperationException("Services are not available until startup has run.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire("AiUsageMonitor.SingleInstance", out _instance))
        {
            SingleInstance.BroadcastShow();
            Shutdown();
            return;
        }

        // The widget is hidden, not closed, when the user dismisses it, so WPF must not treat a
        // window disappearing as the end of the application.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        AppSettingsStore store = new(AppSettingsStore.DefaultPath);
        SettingsLoadResult loaded = store.Load();

        ServiceCollection services = new();
        services.AddSingleton(store);
        services.AddSingleton(loaded.Settings);
        services.AddSingleton(provider => new SettingsService(
            provider.GetRequiredService<AppSettingsStore>(),
            loaded.Settings,
            provider.GetRequiredService<ILogger<SettingsService>>()));
        services.AddSingleton(new ThemeManager(this));
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new RollingFileLoggerProvider(
                new RollingFileWriter(RollingFileLoggerProvider.DefaultDirectory, "app")));
        });

        services.AddSingleton<IReadOnlyList<ProviderDescriptor>>(ProviderRegistry.CreateDefault());
        services.AddSingleton(provider => new ProviderRefreshService(
            provider.GetRequiredService<IReadOnlyList<ProviderDescriptor>>(),
            timeout: TimeSpan.FromSeconds(30),
            baseInterval: loaded.Settings.RefreshInterval,
            provider.GetRequiredService<ILogger<ProviderRefreshService>>()));

        _services = services.BuildServiceProvider();
        _logger = _services.GetRequiredService<ILogger<App>>();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        if (loaded.CorruptBackupPath is not null)
        {
            _logger.LogWarning(
                "Settings file could not be read and was moved to {Backup}; defaults are in use.",
                loaded.CorruptBackupPath);
        }

        _services.GetRequiredService<ThemeManager>().Apply(loaded.Settings.Theme);
        _logger.LogInformation("Startup complete.");

        try
        {
            MainViewModel model = new(
                _services.GetRequiredService<ProviderRefreshService>(),
                _services.GetRequiredService<IReadOnlyList<ProviderDescriptor>>(),
                loaded.Settings,
                () => DateTimeOffset.Now,
                action => Dispatcher.Invoke(action));

            new WidgetWindow(
                model,
                _services.GetRequiredService<SettingsService>(),
                _services.GetRequiredService<ProviderRefreshService>(),
                _services.GetRequiredService<ThemeManager>()).Show();
        }
        catch (Exception ex)
        {
            // A widget that cannot show its window has nothing to offer, and the dispatcher
            // handler below would otherwise swallow this and leave a running process with no
            // window and no tray icon - invisible, unkillable except through Task Manager.
            // Startup failures are fatal; only post-startup exceptions are survivable.
            _logger.LogCritical(ex, "The main window could not be created; shutting down.");
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled exception on the dispatcher thread.");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception; the process is terminating.");

    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        _services?.GetService<ThemeManager>()?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
