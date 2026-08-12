namespace AiUsageMonitor.App.ViewModels;

/// <summary>
/// One option in a mutually exclusive group, rendered as a radio button. Deliberately generic over
/// nothing: every setting it serves - theme, refresh interval, stale threshold - is an int or an
/// enum backed by one, so a shared int keeps a single control template serving all three.
/// </summary>
public sealed class ChoiceViewModel : ObservableObject
{
    private readonly Func<int> _read;
    private readonly Action<int> _write;

    public ChoiceViewModel(string label, int value, string groupName, Func<int> read, Action<int> write)
    {
        Label = label;
        Value = value;
        GroupName = groupName;
        _read = read;
        _write = write;
    }

    public string Label { get; }

    public int Value { get; }

    /// <summary>WPF scopes radio buttons by name, not by container, so every group needs its own.</summary>
    public string GroupName { get; }

    /// <summary>
    /// Writes only on selection. A radio button reports both sides of a move - the outgoing option
    /// goes false as the incoming one goes true - and acting on the false would write the old value
    /// back over the new one, with event order deciding which survived.
    /// </summary>
    public bool IsSelected
    {
        get => _read() == Value;
        set
        {
            if (value)
            {
                _write(Value);
            }
        }
    }

    public void Refresh() => Raise(nameof(IsSelected));
}
