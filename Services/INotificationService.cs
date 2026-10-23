using System.Collections.Generic;
using System.Threading.Tasks;
using StockPortal.Models;

namespace StockPortal.Services
{
    public interface INotificationService
    {
        Task NotifyTicketCreatedAsync(Ticket ticket, User? openedByUser);
        Task NotifyTicketMessageAsync(Ticket ticket, TicketMsg msg, User? sender);
        Task NotifyStatusChangedAsync(Ticket ticket, string newStatus, User? actor);
        Task<int> GetUnreadCountAsync(int userId);
        Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false, int limit = 50);
        Task<bool> MarkAsReadAsync(int notificationId, int userId);
        Task MarkAllAsReadAsync(int userId);
    }
}
