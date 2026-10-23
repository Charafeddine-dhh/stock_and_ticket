using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using StockPortal.Services;

namespace StockPortal.Controllers
{
    public class NotificationsController : Controller
    {
        private readonly INotificationService _notificationService;
        private readonly ICurrentUserService _currentUserService;

        public NotificationsController(INotificationService notificationService, ICurrentUserService currentUserService)
        {
            _notificationService = notificationService;
            _currentUserService = currentUserService;
        }

        // GET: /Notifications/GetLatestUnread
        [HttpGet]
        public async Task<IActionResult> GetLatestUnread()
        {
            int currentUserId = _currentUserService.GetUserId();
            if (currentUserId <= 0)
            {
                return Json(new { count = 0, notifications = new object[0] });
            }

            var unreadList = await _notificationService.GetUserNotificationsAsync(currentUserId, unreadOnly: true);
            var items = unreadList.Take(5).Select(n => new
            {
                id = n.Id,
                title = n.Title,
                message = n.Message,
                type = n.Type,
                linkUrl = !string.IsNullOrWhiteSpace(n.LinkUrl) ? n.LinkUrl : (n.TicketId.HasValue ? $"/Tickets/Details/{n.TicketId}" : "/Notifications"),
                timeAgo = n.CreatedAt.ToString("HH:mm")
            });

            return Json(new { count = unreadList.Count, notifications = items });
        }

        // GET: /Notifications
        public async Task<IActionResult> Index(string? filter)
        {
            int currentUserId = _currentUserService.GetUserId();
            bool unreadOnly = filter == "unread";

            var notifications = await _notificationService.GetUserNotificationsAsync(currentUserId, unreadOnly);
            int unreadCount = await _notificationService.GetUnreadCountAsync(currentUserId);

            ViewBag.CurrentFilter = filter ?? "all";
            ViewBag.UnreadCount = unreadCount;

            return View(notifications);
        }

        // POST: /Notifications/MarkAsRead/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int id, string? returnUrl)
        {
            int currentUserId = _currentUserService.GetUserId();
            await _notificationService.MarkAsReadAsync(id, currentUserId);

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: /Notifications/MarkAllAsRead
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead(string? returnUrl)
        {
            int currentUserId = _currentUserService.GetUserId();
            await _notificationService.MarkAllAsReadAsync(currentUserId);

            TempData["SuccessMessage"] = "All notifications marked as read.";

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
