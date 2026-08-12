using System.Windows;
using System.Windows.Threading;

namespace AiUsageMonitor.App.Tests;

/// <summary>
/// One STA thread with a running dispatcher and one <see cref="Application"/>, shared by every
/// test that touches WPF. Both are process-wide singletons in WPF: a second Application throws,
/// and an object created on one STA thread cannot be touched from another.
/// </summary>
public sealed class WpfFixture : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher _dispatcher = null!;

    public WpfFixture()
    {
        using ManualResetEventSlim ready = new();

        _thread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            Application application = new();
            application.Resources.MergedDictionaries.Add(Load("Themes/Tokens.xaml"));
            application.Resources.MergedDictionaries.Add(Load("Themes/Controls.xaml"));
            application.Resources.MergedDictionaries.Add(Load("Themes/Light.xaml"));

            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(30));
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread, rethrowing whatever it threw.</summary>
    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        _dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(10));
    }

    private static ResourceDictionary Load(string relativePath) => new()
    {
        Source = new Uri(
            $"pack://application:,,,/AiUsageMonitor.App;component/{relativePath}",
            UriKind.Absolute)
    };
}

[CollectionDefinition("wpf")]
public sealed class WpfCollection : ICollectionFixture<WpfFixture>;
