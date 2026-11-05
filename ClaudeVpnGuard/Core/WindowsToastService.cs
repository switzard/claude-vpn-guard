using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace ClaudeVpnGuard.Core;

/// <summary>
/// Real Windows notifications that appear in Action Center (system notification area).
/// </summary>
public static class WindowsToastService
{
    /// <returns>True when the toast was handed to the OS notifier.</returns>
    public static bool Show(string title, string body, string severity = "Info")
    {
        try
        {
            var builder = new ToastContentBuilder()
                .AddText(title)
                .AddText(body)
                .AddAttributionText("Claude Code Guardian");

            var xml = builder.GetToastContent().GetXml();
            var toast = new ToastNotification(xml)
            {
                ExpirationTime = DateTimeOffset.Now.AddMinutes(10)
            };

            if (string.Equals(severity, "Error", StringComparison.OrdinalIgnoreCase))
                toast.Priority = ToastNotificationPriority.High;

            ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
            return true;
        }
        catch
        {
            // Toast API unavailable — balloon still fires from Notifier.
            return false;
        }
    }
}
