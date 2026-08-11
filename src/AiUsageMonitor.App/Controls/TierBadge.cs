using System.Windows;
using System.Windows.Controls;
using AiUsageMonitor.Domain;

namespace AiUsageMonitor.App.Controls;

public sealed class TierBadge : Control
{
    public static readonly DependencyProperty TierProperty = DependencyProperty.Register(nameof(Tier), typeof(MechanismTier), typeof(TierBadge));

    public MechanismTier Tier { get => (MechanismTier)GetValue(TierProperty); set => SetValue(TierProperty, value); }
}
