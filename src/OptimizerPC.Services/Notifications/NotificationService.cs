using OptimizerPC.Core.Models;
using OptimizerPC.Core.Abstractions;

namespace OptimizerPC.Services.Notifications;

/// <summary>
/// Central de avisos do aplicativo. Tudo fica apenas na memoria da sessao: nenhuma
/// notificacao e enviada para fora do computador.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const int MaxRecent = 100;

    private readonly List<AppNotification> _recent = new();
    private readonly object _gate = new();

    public IReadOnlyList<AppNotification> Recent
    {
        get
        {
            lock (_gate)
            {
                return _recent.ToArray();
            }
        }
    }

    public event EventHandler<AppNotification>? NotificationPublished;

    public event EventHandler? RecentChanged;

    public void Publish(AppNotification notification)
    {
        lock (_gate)
        {
            _recent.Insert(0, notification);
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveRange(MaxRecent, _recent.Count - MaxRecent);
            }
        }

        NotificationPublished?.Invoke(this, notification);
        RecentChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkAsRead(string id)
    {
        var changed = false;

        lock (_gate)
        {
            var item = _recent.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));
            if (item is not null && item.IsRead is false)
            {
                item.IsRead = true;
                changed = true;
            }
        }

        if (changed)
        {
            RecentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_recent.Count == 0)
            {
                return;
            }

            _recent.Clear();
        }

        RecentChanged?.Invoke(this, EventArgs.Empty);
    }
}
