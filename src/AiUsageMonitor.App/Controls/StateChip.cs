using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Controls;

public sealed class StateChip : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(ConnectionState), typeof(StateChip));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(StateChip), new PropertyMetadata(string.Empty));

    public ConnectionState State { get => (ConnectionState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
}
