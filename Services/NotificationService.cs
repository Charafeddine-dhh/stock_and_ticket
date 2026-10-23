using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Models;

namespace StockPortal.Services
{
    public class NotificationService : INotificationService
    {
        private readonly StockDbContext _context;

        public NotificationService(StockDbContext context)
        {
            _context = context;
        }

        public async Task NotifyTicketCreatedAsync(Ticket ticket, User? openedByUser)
        {
            try
            {
                var recipients = new HashSet<int>();
                int initiatorId = openedByUser?.Id ?? ticket.OpenedBy ?? 0;

                // 1. All Active Admins
                var adminIds = await _context.Users
                    .Where(u => u.Role == SystemRoles.Admin && u.IsActive == true)
                    .Select(u => u.Id)
                    .ToListAsync();
                foreach (var id in adminIds) recipients.Add(id);

                // 2. Users of the same department as the requester
                if (openedByUser?.DepartmentId != null)
                {
                    var deptUserIds = await _context.Users
                        .Where(u => u.DepartmentId == openedByUser.DepartmentId && u.IsActive == true)
                        .Select(u => u.Id)
                        .ToListAsync();
                    foreach (var id in deptUserIds) recipients.Add(id);
                }

                // 3. Maintenance team users assigned to target team (e.g. Biomedical, IT, General)
                if (!string.IsNullOrWhiteSpace(ticket.TargetTeam))
                {
                    var teamUserIds = await _context.Users
                        .Where(u => u.MaintenanceTeam == ticket.TargetTeam && u.IsActive == true)
                        .Select(u => u.Id)
                        .ToListAsync();
                    foreach (var id in teamUserIds) recipients.Add(id);
                }

                // Exclude initiator
                recipients.Remove(initiatorId);

                string requesterName = openedByUser?.FullName ?? "Hospital Staff";
                string deptName = openedByUser?.Department?.Name ?? (!string.IsNullOrWhiteSpace(ticket.TargetTeam) ? $"{ticket.TargetTeam} Team" : "Department");

                var notifications = recipients.Select(userId => new Notification
                {
                    UserId = userId,
                    TicketId = ticket.Id,
                    Title = $"New Ticket #{ticket.Id} Opened",
                    Message = $"{requesterName} opened a new ticket ({ticket.Type} - Priority: {ticket.PriorityName}) for {deptName}.",
                    Type = "TicketOpened",
                    LinkUrl = $"/Tickets/Details/{ticket.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                }).ToList();

                if (notifications.Any())
                {
                    _context.Notifications.AddRange(notifications);
                    await _context.SaveChangesAsync();
                }
            }
            catch
            {
                // Graceful fallback to avoid interrupting ticket creation workflow
            }
        }

        public async Task NotifyTicketMessageAsync(Ticket ticket, TicketMsg msg, User? sender)
        {
            try
            {
                var recipients = new HashSet<int>();
                int senderId = sender?.Id ?? msg.UserId ?? 0;

                // 1. All Active Admins
                var adminIds = await _context.Users
                    .Where(u => u.Role == SystemRoles.Admin && u.IsActive == true)
                    .Select(u => u.Id)
                    .ToListAsync();
                foreach (var id in adminIds) recipients.Add(id);

                // 2. Ticket Requester (OpenedBy / OnBehalfOf)
                if (ticket.OpenedBy.HasValue) recipients.Add(ticket.OpenedBy.Value);
                if (ticket.OnBehalfOf.HasValue) recipients.Add(ticket.OnBehalfOf.Value);

                // 3. Users of the same department as the requester
                var requesterDeptId = ticket.OpenedByUser?.DepartmentId;
                if (!requesterDeptId.HasValue && ticket.OpenedBy.HasValue)
                {
                    requesterDeptId = await _context.Users
                        .Where(u => u.Id == ticket.OpenedBy.Value)
                        .Select(u => u.DepartmentId)
                        .FirstOrDefaultAsync();
                }

                if (requesterDeptId.HasValue)
                {
                    var deptUserIds = await _context.Users
                        .Where(u => u.DepartmentId == requesterDeptId.Value && u.IsActive == true)
                        .Select(u => u.Id)
                        .ToListAsync();
                    foreach (var id in deptUserIds) recipients.Add(id);
                }

                // 4. Assigned Technician
                if (ticket.Handler?.TechUserId != null)
                {
                    recipients.Add(ticket.Handler.TechUserId.Value);
                }

                // 5. Target Maintenance Team
                if (!string.IsNullOrWhiteSpace(ticket.TargetTeam))
                {
                    var teamUserIds = await _context.Users
                        .Where(u => u.MaintenanceTeam == ticket.TargetTeam && u.IsActive == true)
                        .Select(u => u.Id)
                        .ToListAsync();
                    foreach (var id in teamUserIds) recipients.Add(id);
                }

                // Exclude sender
                recipients.Remove(senderId);

                string senderName = sender?.FullName ?? "Staff Member";
                string snippet = msg.Msg ?? string.Empty;
                if (snippet.Length > 80) snippet = snippet.Substring(0, 77) + "...";

                var notifications = recipients.Select(userId => new Notification
                {
                    UserId = userId,
                    TicketId = ticket.Id,
                    Title = $"New Message on Ticket #{ticket.Id}",
                    Message = $"{senderName}: \"{snippet}\"",
                    Type = "MessageAdded",
                    LinkUrl = $"/Tickets/Details/{ticket.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                }).ToList();

                if (notifications.Any())
                {
                    _context.Notifications.AddRange(notifications);
                    await _context.SaveChangesAsync();
                }
            }
            catch
            {
                // Graceful fallback
            }
        }

        public async Task NotifyStatusChangedAsync(Ticket ticket, string newStatus, User? actor)
        {
            try
            {
                var recipients = new HashSet<int>();
                int actorId = actor?.Id ?? 0;

                // Admins
                var adminIds = await _context.Users
                    .Where(u => u.Role == SystemRoles.Admin && u.IsActive == true)
                    .Select(u => u.Id)
                    .ToListAsync();
                foreach (var id in adminIds) recipients.Add(id);

                // Ticket Requester
                if (ticket.OpenedBy.HasValue) recipients.Add(ticket.OpenedBy.Value);
                if (ticket.OnBehalfOf.HasValue) recipients.Add(ticket.OnBehalfOf.Value);

                // Department members
                if (ticket.OpenedByUser?.DepartmentId != null)
                {
                    var deptUserIds = await _context.Users
                        .Where(u => u.DepartmentId == ticket.OpenedByUser.DepartmentId && u.IsActive == true)
                        .Select(u => u.Id)
                        .ToListAsync();
                    foreach (var id in deptUserIds) recipients.Add(id);
                }

                // Assigned tech
                if (ticket.Handler?.TechUserId != null) recipients.Add(ticket.Handler.TechUserId.Value);

                // Exclude actor
                recipients.Remove(actorId);

                string actorName = actor?.FullName ?? "System";

                var notifications = recipients.Select(userId => new Notification
                {
                    UserId = userId,
                    TicketId = ticket.Id,
                    Title = $"Ticket #{ticket.Id} Status Updated",
                    Message = $"Ticket status updated to '{newStatus}' by {actorName}.",
                    Type = "StatusChanged",
                    LinkUrl = $"/Tickets/Details/{ticket.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                }).ToList();

                if (notifications.Any())
                {
                    _context.Notifications.AddRange(notifications);
                    await _context.SaveChangesAsync();
                }
            }
            catch
            {
                // Graceful fallback
            }
        }

        public async Task<int> GetUnreadCountAsync(int userId)
        {
            if (userId <= 0) return 0;
            return await _context.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead);
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(int userId, bool unreadOnly = false, int limit = 50)
        {
            if (userId <= 0) return new List<Notification>();

            var query = _context.Notifications
                .Include(n => n.Ticket)
                .Where(n => n.UserId == userId);

            if (unreadOnly)
            {
                query = query.Where(n => !n.IsRead);
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(limit)
                .ToListAsync();
        }

        public async Task<bool> MarkAsReadAsync(int notificationId, int userId)
        {
            var notif = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);
            if (notif != null)
            {
                notif.IsRead = true;
                await _context.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task MarkAllAsReadAsync(int userId)
        {
            if (userId <= 0) return;
            var unread = await _context.Notifications
                .Where(n => n.UserId == userId && !n.IsRead)
                .ToListAsync();

            foreach (var n in unread)
            {
                n.IsRead = true;
            }

            if (unread.Any())
            {
                await _context.SaveChangesAsync();
            }
        }
    }
}
