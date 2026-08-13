namespace AiUsageMonitor.Infrastructure.Providers;

public static class ProviderOrdering
{
    /// <summary>
    /// The registry list rearranged to follow <paramref name="order"/>. Keys named in the order
    /// that no longer exist are ignored; providers the order does not mention keep their registry
    /// position, appended after the ones it does. Neither list is trusted: the order comes from a
    /// hand-editable settings file, and the registry changes when a build adds a provider.
    /// </summary>
    public static IReadOnlyList<ProviderDescriptor> Apply(
        IReadOnlyList<ProviderDescriptor> providers,
        IReadOnlyList<string> order)
    {
        if (order.Count == 0)
        {
            return providers;
        }

        HashSet<string> included = new(StringComparer.OrdinalIgnoreCase);
        List<ProviderDescriptor> result = [];

        foreach (string key in order)
        {
            ProviderDescriptor? provider = providers.FirstOrDefault(candidate =>
                StringComparer.OrdinalIgnoreCase.Equals(candidate.Key, key));
            if (provider is not null && included.Add(provider.Key))
            {
                result.Add(provider);
            }
        }

        foreach (ProviderDescriptor provider in providers)
        {
            if (included.Add(provider.Key))
            {
                result.Add(provider);
            }
        }

        return result;
    }
}
