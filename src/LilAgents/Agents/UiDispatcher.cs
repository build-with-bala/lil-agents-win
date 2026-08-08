using System.Windows;
using System.Windows.Threading;

namespace LilAgents.Agents;

/// <summary>
/// Marshals callbacks onto the WPF dispatcher, standing in for the macOS build's
/// ubiquitous <c>DispatchQueue.main.async</c>. Falls back to running inline when no
/// Application exists, which is how the unit tests drive sessions headlessly.
/// </summary>
public static class UiDispatcher
{
    public static Dispatcher? Dispatcher => Application.Current?.Dispatcher;

    public static void Invoke(Action action)
    {
        var dispatcher = Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }
}
