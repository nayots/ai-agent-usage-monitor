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
    private Exception? _startupFailure;

    public WpfFixture()
    {
        using ManualResetEventSlim ready = new();

        _thread = new Thread(() =>
        {
            try
            {
                _dispatcher = Dispatcher.CurrentDispatcher;

                Application application = new();
                application.Resources.MergedDictionaries.Add(Load("Themes/Tokens.xaml"));
                application.Resources.MergedDictionaries.Add(Load("Themes/Controls.xaml"));
                application.Resources.MergedDictionaries.Add(Load("Themes/Light.xaml"));
            }
            catch (Exception ex)
            {
                // A malformed dictionary throws HERE, on a thread nobody is awaiting. Uncaught it
                // would never reach the test run: the wait below would burn its full 30 seconds and
                // every test in the collection would then fail against a null dispatcher, hiding
                // the XamlParseException that is the entire reason this project exists. Capture it
                // and rethrow on the constructing thread instead.
                _startupFailure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (_startupFailure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        if (!ready.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("The WPF STA thread did not signal readiness within 30 seconds.");
        }

        if (_startupFailure is not null)
        {
            throw new InvalidOperationException(
                "The shared WPF Application could not be created. The inner exception is the real failure.",
                _startupFailure);
        }
    }

    /// <summary>Runs <paramref name="action"/> on the STA thread, rethrowing whatever it threw.</summary>
    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public void Dispose()
    {
        // Null when the constructor failed before the dispatcher was captured.
        _dispatcher?.InvokeShutdown();
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
