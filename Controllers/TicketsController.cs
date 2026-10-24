using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Models;
using StockPortal.ViewModels;
using StockPortal.Services;

namespace StockPortal.Controllers
{
    public class TicketsController : Controller
    {
        private readonly StockDbContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly INotificationService _notificationService;

        public TicketsController(StockDbContext context, ICurrentUserService currentUserService, INotificationService notificationService)
        {
            _context = context;
            _currentUserService = currentUserService;
            _notificationService = notificationService;
        }

        // GET: Tickets
        public async Task<IActionResult> Index(int? statusId, int? priority, string? type, string? search, string? filter)
        {
            var currentUserId = _currentUserService.GetUserId();

            var query = _context.Tickets
                .Include(t => t.Asset)
                .Include(t => t.OpenedByUser)
                .Include(t => t.OnBehalfOfUser)
                .Include(t => t.Status)
                .Include(t => t.Handler).ThenInclude(h => h!.TechUser)
                .Include(t => t.Messages)
                .AsQueryable();

            // RBAC Filtering
            if (_currentUserService.IsEndUser())
            {
                // End Users view only tickets they authored or on behalf
                query = query.Where(t => t.OpenedBy == currentUserId || t.OnBehalfOf == currentUserId);
            }
            else if (_currentUserService.IsMaintenance())
            {
                // Maintenance users view only tickets routed to their designated team queue
                var myTeam = _currentUserService.GetMaintenanceTeam();
                query = query.Where(t => t.TargetTeam == myTeam);
            }
            // ADMIN has unrestricted access across all tickets

            if (statusId.HasValue)
            {
                if (statusId.Value == -1)
                {
                    // Not Closed tickets (Status != 5 and ClosedDT is null)
                    query = query.Where(t => t.StatusId != 5 && t.ClosedDT == null);
                }
                else if (statusId.Value > 0)
                {
                    query = query.Where(t => t.StatusId == statusId.Value);
                }
            }

            if (priority.HasValue && priority.Value > 0)
            {
                query = query.Where(t => t.Priority == priority.Value);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(t => t.Type == type);
            }

            // Quick Filters
            if (!string.IsNullOrWhiteSpace(filter))
            {
                if (filter == "my_assigned")
                {
                    query = query.Where(t => t.Handler != null && t.Handler.TechUserId == currentUserId);
                }
                else if (filter == "my_opened")
                {
                    query = query.Where(t => t.OpenedBy == currentUserId || t.OnBehalfOf == currentUserId);
                }
                else if (filter == "unassigned")
                {
                    query = query.Where(t => t.Handler == null || t.Handler.TechUserId == null);
                }
                else if (filter == "maintenance" || filter == "in_maintenance")
                {
                    query = query.Where(t => t.StatusId == 6 || (t.Status != null && t.Status.Status != null && t.Status.Status.ToLower() == "maintenance"));
                }
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(t =>
                    t.Id.ToString() == s ||
                    (t.Asset != null && t.Asset.HospitalNo != null && t.Asset.HospitalNo.ToLower().Contains(s)) ||
                    (t.Asset != null && t.Asset.Brand != null && t.Asset.Brand.ToLower().Contains(s)) ||
                    (t.OpenedByUser != null && t.OpenedByUser.FullName != null && t.OpenedByUser.FullName.ToLower().Contains(s)) ||
                    t.Messages.Any(m => m.Msg != null && m.Msg.ToLower().Contains(s))
                );
            }

            ViewBag.SelectedStatusId = statusId;
            ViewBag.SelectedPriority = priority;
            ViewBag.SelectedType = type;
            ViewBag.Search = search;
            ViewBag.SelectedFilter = filter;

            ViewBag.Statuses = await _context.TicketStatuses.ToListAsync();
            ViewBag.AllTechnicians = await _context.Users
                .Where(u => u.IsActive == true && (u.Role == SystemRoles.Maintenance || u.Role == SystemRoles.Admin))
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var maintCountQuery = _context.Tickets.Where(t => t.StatusId == 6 || (t.Status != null && t.Status.Status != null && t.Status.Status.ToLower() == "maintenance"));
            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                maintCountQuery = maintCountQuery.Where(t => t.TargetTeam == myTeam);
            }
            ViewBag.MaintenanceCount = await maintCountQuery.CountAsync();

            var tickets = await query.OrderByDescending(t => t.OpenedDT).ToListAsync();
            return View(tickets);
        }

        // GET: Tickets/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var ticket = await _context.Tickets
                .Include(t => t.Asset).ThenInclude(a => a!.Room)
                .Include(t => t.Asset).ThenInclude(a => a!.User)
                .Include(t => t.OpenedByUser)
                .Include(t => t.OnBehalfOfUser)
                .Include(t => t.Status)
                .Include(t => t.Handler).ThenInclude(h => h!.TechUser)
                .Include(t => t.Messages).ThenInclude(m => m.User)
                .Include(t => t.MaintenanceLogs).ThenInclude(m => m.Technician)
                .Include(t => t.MaintenanceLogs).ThenInclude(m => m.Supplier)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (ticket == null) return NotFound();

            // RBAC Guard: Verify access permissions
            if (!_currentUserService.CanAccessTicket(ticket))
            {
                return Forbid();
            }

            var vm = new TicketDetailsViewModel
            {
                Ticket = ticket,
                Statuses = await _context.TicketStatuses.ToListAsync(),
                Technicians = await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(),
                Suppliers = await _context.Suppliers.Where(s => s.IsActive == true).OrderBy(s => s.Name).ToListAsync(),
                NewStatusId = ticket.StatusId,
                NewTechUserId = ticket.Handler?.TechUserId
            };

            return View(vm);
        }

        // GET: Tickets/Create
        public async Task<IActionResult> Create(int? assetId)
        {
            var currentUserId = _currentUserService.GetUserId();
            await PopulateDropdowns(assetId, currentUserId, currentUserId);

            var ticket = new Ticket
            {
                AssetId = assetId,
                OpenedBy = currentUserId,
                OnBehalfOf = currentUserId,
                Type = "Hardware",
                Priority = 2,
                StatusId = 1,
                OpenedDT = DateTime.Now
            };

            return View(ticket);
        }

        // POST: Tickets/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Ticket ticket, string? manualAsset, string? initialMessage, int? assignedTechId)
        {
            var currentUserId = _currentUserService.GetUserId();
            ticket.OpenedBy = currentUserId > 0 ? currentUserId : ticket.OpenedBy;

            bool isOnBehalfOf = ticket.OnBehalfOf.HasValue && ticket.OnBehalfOf.Value != ticket.OpenedBy;

            if (ModelState.IsValid)
            {
                // If opening on behalf of another user, personal asset dropdown is cleared
                if (isOnBehalfOf)
                {
                    ticket.AssetId = null;
                }

                // Target team routing: honor explicitly specified target team; fall back to asset's team or IT
                if (string.IsNullOrWhiteSpace(ticket.TargetTeam))
                {
                    if (ticket.AssetId.HasValue)
                    {
                        var targetAsset = await _context.Assets.FindAsync(ticket.AssetId.Value);
                        ticket.TargetTeam = targetAsset?.MaintenanceTeam ?? MaintenanceTeams.IT;
                    }
                    else
                    {
                        ticket.TargetTeam = MaintenanceTeams.IT;
                    }
                }

                ticket.OpenedDT = DateTime.Now;
                ticket.StatusId = 1; // opened
                _context.Tickets.Add(ticket);
                await _context.SaveChangesAsync();

                // If technician assigned immediately
                if (assignedTechId.HasValue && assignedTechId.Value > 0)
                {
                    _context.TicketHandlers.Add(new TicketHandler
                    {
                        TicketId = ticket.Id,
                        TechUserId = assignedTechId.Value,
                        AssignedDT = DateTime.Now
                    });
                    ticket.StatusId = 2; // pending/in-progress
                    _context.Update(ticket);
                }

                // Construct initial message with manual asset information if on behalf of another user
                var messageBuilder = new StringBuilder();

                if (isOnBehalfOf && !string.IsNullOrWhiteSpace(manualAsset))
                {
                    messageBuilder.AppendLine($"[ON BEHALF OF ASSET / PC]: {manualAsset.Trim()}");
                    messageBuilder.AppendLine();
                }

                if (!string.IsNullOrWhiteSpace(initialMessage))
                {
                    messageBuilder.Append(initialMessage.Trim());
                }

                var fullMsg = messageBuilder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(fullMsg))
                {
                    _context.TicketMessages.Add(new TicketMsg
                    {
                        TicketId = ticket.Id,
                        UserId = ticket.OpenedBy,
                        Msg = fullMsg,
                        TimeStamp = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();

                var creatorUser = _currentUserService.GetCurrentUser() ?? await _context.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == ticket.OpenedBy);
                await _notificationService.NotifyTicketCreatedAsync(ticket, creatorUser);

                TempData["SuccessMessage"] = $"Ticket #{ticket.Id} created successfully!";
                return RedirectToAction(nameof(Details), new { id = ticket.Id });
            }

            await PopulateDropdowns(ticket.AssetId, ticket.OnBehalfOf, currentUserId);
            return View(ticket);
        }

        // GET: Tickets/AutoTicket
        public async Task<IActionResult> AutoTicket(string? pcName, int? assetId)
        {
            var currentUserId = _currentUserService.GetUserId();
            var currentUser = await _context.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == currentUserId);

            // Determine client PC name
            if (string.IsNullOrWhiteSpace(pcName))
            {
                pcName = Request.Headers["X-Computer-Name"].FirstOrDefault()
                    ?? Request.Headers["COMPUTERNAME"].FirstOrDefault();
            }

            if (string.IsNullOrWhiteSpace(pcName))
            {
                pcName = Environment.MachineName;
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            // Resolve Asset by ID or by PC Name (hospitalNo in database)
            Asset? matchedAsset = null;
            if (assetId.HasValue && assetId.Value > 0)
            {
                matchedAsset = await _context.Assets
                    .Include(a => a.Room)
                    .Include(a => a.User)
                    .Include(a => a.CustodyType)
                    .Include(a => a.Supplier)
                    .FirstOrDefaultAsync(a => a.Id == assetId.Value);
            }

            if (matchedAsset == null && !string.IsNullOrWhiteSpace(pcName))
            {
                var search = pcName.Trim().ToLower();
                matchedAsset = await _context.Assets
                    .Include(a => a.Room)
                    .Include(a => a.User)
                    .Include(a => a.CustodyType)
                    .Include(a => a.Supplier)
                    .FirstOrDefaultAsync(a => a.HospitalNo != null && a.HospitalNo.ToLower() == search);
            }

            // Fallback: check if the logged in user has an assigned PC asset
            if (matchedAsset == null && currentUserId > 0)
            {
                matchedAsset = await _context.Assets
                    .Include(a => a.Room)
                    .Include(a => a.User)
                    .Include(a => a.CustodyType)
                    .Include(a => a.Supplier)
                    .FirstOrDefaultAsync(a => a.UserId == currentUserId);
            }

            var vm = new AutoTicketViewModel
            {
                DetectedPcName = matchedAsset?.HospitalNo ?? pcName ?? Environment.MachineName,
                ClientIp = clientIp,
                IsAssetFound = (matchedAsset != null),
                MatchedAsset = matchedAsset,
                SelectedAssetId = matchedAsset?.Id,
                SelectedRoomId = matchedAsset?.RoomId,
                SelectedUserId = matchedAsset?.UserId ?? currentUserId,
                Category = "Hardware",
                Priority = 2,
                UserNotes = "",
                AvailableAssets = await _context.Assets.OrderBy(a => a.HospitalNo).ToListAsync(),
                AvailableRooms = await _context.Rooms.OrderBy(r => r.RoomNo).ToListAsync(),
                AvailableUsers = await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync()
            };

            return View(vm);
        }

        // POST: Tickets/AutoTicket
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AutoTicket(AutoTicketViewModel model)
        {
            var currentUserId = _currentUserService.GetUserId();
            var currentUser = await _context.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == currentUserId);

            // 1. Resolve Asset
            Asset? asset = null;
            if (model.SelectedAssetId.HasValue && model.SelectedAssetId.Value > 0)
            {
                asset = await _context.Assets
                    .Include(a => a.Room)
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.Id == model.SelectedAssetId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(model.DetectedPcName))
            {
                var search = model.DetectedPcName.Trim().ToLower();
                asset = await _context.Assets
                    .Include(a => a.Room)
                    .Include(a => a.User)
                    .FirstOrDefaultAsync(a => a.HospitalNo != null && a.HospitalNo.ToLower() == search);
            }

            // 2. If room was selected and asset exists, update asset room if not set or changed
            Room? selectedRoom = null;
            if (model.SelectedRoomId.HasValue && model.SelectedRoomId.Value > 0)
            {
                selectedRoom = await _context.Rooms.FindAsync(model.SelectedRoomId.Value);
                if (asset != null && (asset.RoomId == null || asset.RoomId != model.SelectedRoomId.Value))
                {
                    asset.RoomId = model.SelectedRoomId.Value;
                    _context.Update(asset);
                }
            }
            else if (asset?.Room != null)
            {
                selectedRoom = asset.Room;
            }

            // 3. If user selected and asset exists, update asset custodian if not set
            User? assignedUser = null;
            if (model.SelectedUserId.HasValue && model.SelectedUserId.Value > 0)
            {
                assignedUser = await _context.Users.FindAsync(model.SelectedUserId.Value);
                if (asset != null && asset.UserId == null)
                {
                    asset.UserId = model.SelectedUserId.Value;
                    _context.Update(asset);
                }
            }
            else if (asset?.User != null)
            {
                assignedUser = asset.User;
            }

            // 4. Create Ticket
            var ticket = new Ticket
            {
                AssetId = asset?.Id,
                OpenedBy = currentUserId > 0 ? currentUserId : 1,
                OnBehalfOf = assignedUser?.Id ?? (currentUserId > 0 ? currentUserId : 1),
                Type = string.IsNullOrWhiteSpace(model.Category) ? "Hardware" : model.Category,
                Priority = model.Priority > 0 ? model.Priority : 2,
                StatusId = 1, // Opened
                OpenedDT = DateTime.Now,
                TargetTeam = asset?.MaintenanceTeam ?? MaintenanceTeams.IT
            };

            _context.Tickets.Add(ticket);
            await _context.SaveChangesAsync();

            // 5. Construct Telemetry Report Message
            var pcName = asset?.HospitalNo ?? (!string.IsNullOrWhiteSpace(model.DetectedPcName) ? model.DetectedPcName : Environment.MachineName);
            var roomDesc = selectedRoom != null ? $"Room {selectedRoom.RoomNo} (Floor {selectedRoom.Floor})" : "Unspecified";
            var userDesc = assignedUser?.FullName ?? (currentUser?.FullName ?? "Unassigned");
            var hardwareDesc = asset != null 
                ? $"{asset.Brand ?? "Generic"} {(asset.ModelNo.HasValue ? "Model " + asset.ModelNo : "")} [S/N: {asset.SerialNo ?? "N/A"}]"
                : "Workstation not yet tagged in Assets database";

            var sb = new StringBuilder();
            sb.AppendLine("🖥️ AUTOMATED SYSTEM & WORKSTATION REPORT");
            sb.AppendLine($"• Workstation / PC Name: {pcName}");
            sb.AppendLine($"• Location: {roomDesc}");
            sb.AppendLine($"• Custodian / User: {userDesc}");
            sb.AppendLine($"• Equipment: {hardwareDesc}");
            sb.AppendLine($"• Reporter: {currentUser?.FullName ?? "System"} ({currentUser?.DomainUser ?? Environment.UserName})");
            sb.AppendLine($"• Department: {currentUser?.Department?.Name ?? "General"}");
            sb.AppendLine($"• Client Host / IP: {model.ClientIp}");
            sb.AppendLine($"• Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            _context.TicketMessages.Add(new TicketMsg
            {
                TicketId = ticket.Id,
                UserId = ticket.OpenedBy,
                Msg = sb.ToString(),
                TimeStamp = DateTime.Now
            });

            // 6. User Manual Notes (if entered)
            if (!string.IsNullOrWhiteSpace(model.UserNotes))
            {
                _context.TicketMessages.Add(new TicketMsg
                {
                    TicketId = ticket.Id,
                    UserId = ticket.OpenedBy,
                    Msg = $"📝 USER NOTES:\n{model.UserNotes.Trim()}",
                    TimeStamp = DateTime.Now.AddSeconds(1)
                });
            }

            await _context.SaveChangesAsync();

            var autoCreator = _currentUserService.GetCurrentUser() ?? await _context.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == ticket.OpenedBy);
            await _notificationService.NotifyTicketCreatedAsync(ticket, autoCreator);

            TempData["SuccessMessage"] = $"Automated Ticket #{ticket.Id} created for PC '{pcName}'!";
            return RedirectToAction(nameof(Details), new { id = ticket.Id });
        }

        // GET: Tickets/GetAssetByPcName
        [HttpGet]
        public async Task<IActionResult> GetAssetByPcName(string pcName)
        {
            if (string.IsNullOrWhiteSpace(pcName)) return Json(new { found = false });

            var search = pcName.Trim().ToLower();
            var asset = await _context.Assets
                .Include(a => a.Room)
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.HospitalNo != null && a.HospitalNo.ToLower() == search);

            if (asset == null)
            {
                return Json(new { found = false });
            }

            return Json(new
            {
                found = true,
                id = asset.Id,
                hospitalNo = asset.HospitalNo,
                brand = asset.Brand,
                modelNo = asset.ModelNo,
                serialNo = asset.SerialNo,
                type = asset.Type,
                roomId = asset.RoomId,
                roomText = asset.Room != null ? $"Room {asset.Room.RoomNo} (Floor {asset.Room.Floor})" : null,
                userId = asset.UserId,
                userName = asset.User?.FullName,
                status = asset.Status,
                specs = asset.Specs
            });
        }

        // POST: Tickets/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int ticketId, int statusId)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null) return NotFound();

            // RBAC Guard: Only Admin and assigned Maintenance team can manage tickets
            if (!_currentUserService.CanManageTicket(ticket))
            {
                return Forbid();
            }

            ticket.StatusId = statusId;
            if (statusId == 5) // closed
            {
                ticket.ClosedDT = DateTime.Now;
            }
            else
            {
                ticket.ClosedDT = null;
            }

            _context.Update(ticket);
            await _context.SaveChangesAsync();

            var statusObj = await _context.TicketStatuses.FindAsync(statusId);
            var statusActor = _currentUserService.GetCurrentUser();
            await _notificationService.NotifyStatusChangedAsync(ticket, statusObj?.Status ?? statusId.ToString(), statusActor);

            TempData["SuccessMessage"] = "Ticket status updated.";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/CloseTicket
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseTicket(int ticketId, string? resolutionNotes)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null) return NotFound();

            // RBAC Guard: Only Admin and assigned Maintenance team can close tickets
            if (!_currentUserService.CanManageTicket(ticket))
            {
                return Forbid();
            }

            ticket.StatusId = 5; // closed
            ticket.ClosedDT = DateTime.Now;
            ticket.ResolutionDetails = resolutionNotes?.Trim();
            _context.Update(ticket);

            var currentUserId = _currentUserService.GetUserId();

            if (!string.IsNullOrWhiteSpace(resolutionNotes))
            {
                _context.TicketMessages.Add(new TicketMsg
                {
                    TicketId = ticketId,
                    UserId = currentUserId,
                    Msg = $"[RESOLUTION NOTE]: {resolutionNotes.Trim()}",
                    TimeStamp = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            var closeActor = _currentUserService.GetCurrentUser();
            await _notificationService.NotifyStatusChangedAsync(ticket, "Closed", closeActor);

            TempData["SuccessMessage"] = $"Ticket #{ticketId} was resolved and closed.";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/AssignTechnician
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTechnician(int ticketId, int techUserId, string? returnUrl = null)
        {
            var ticket = await _context.Tickets.Include(t => t.Handler).FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return NotFound();

            var currentUserId = _currentUserService.GetUserId();
            bool canAssign = _currentUserService.CanManageTicket(ticket)
                             || ticket.OpenedBy == currentUserId
                             || ticket.OnBehalfOf == currentUserId;

            // RBAC Guard: Admin, assigned Maintenance team, or ticket requester can assign/reassign technician
            if (!canAssign)
            {
                return Forbid();
            }

            if (ticket.StatusId == 5 || ticket.ClosedDT.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot reassign technician on a closed ticket.";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            var techUser = techUserId > 0 ? await _context.Users.FindAsync(techUserId) : null;
            if (techUser != null && techUser.Role == SystemRoles.Maintenance && !string.Equals(techUser.MaintenanceTeam, ticket.TargetTeam, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = $"Selected technician does not belong to the responsible {ticket.TargetTeam} department.";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            if (ticket.Handler == null)
            {
                _context.TicketHandlers.Add(new TicketHandler
                {
                    TicketId = ticketId,
                    TechUserId = techUserId > 0 ? techUserId : null,
                    AssignedDT = DateTime.Now
                });
            }
            else
            {
                ticket.Handler.TechUserId = techUserId > 0 ? techUserId : null;
                ticket.Handler.AssignedDT = DateTime.Now;
                _context.Update(ticket.Handler);
            }

            if (techUserId > 0 && ticket.StatusId == 1) // Move from opened to pending
            {
                ticket.StatusId = 2;
                _context.Update(ticket);
            }

            // Record timeline activity message
            if (techUser != null)
            {
                _context.TicketMessages.Add(new TicketMsg
                {
                    TicketId = ticketId,
                    UserId = currentUserId > 0 ? currentUserId : techUserId,
                    Msg = $"👤 ASSIGNED SPECIALIST: Ticket assigned to {techUser.FullName} ({techUser.RoleDisplay}).",
                    TimeStamp = DateTime.Now
                });
            }

            await _context.SaveChangesAsync();

            if (techUser != null)
            {
                var assignActor = _currentUserService.GetCurrentUser();
                await _notificationService.NotifyStatusChangedAsync(ticket, $"Assigned to {techUser.FullName}", assignActor);
            }

            TempData["SuccessMessage"] = techUser != null 
                ? $"Assigned to {techUser.FullName} successfully." 
                : "Technician unassigned.";

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/AssignTeam
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTeam(int ticketId, string targetTeam, string? returnUrl)
        {
            var ticket = await _context.Tickets.Include(t => t.Handler).FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return NotFound();

            var currentUserId = _currentUserService.GetUserId();
            bool canReassign = _currentUserService.IsAdmin()
                               || _currentUserService.CanManageTicket(ticket)
                               || ticket.OpenedBy == currentUserId
                               || ticket.OnBehalfOf == currentUserId;

            if (!canReassign)
            {
                return Forbid();
            }

            if (ticket.StatusId == 5 || ticket.ClosedDT.HasValue)
            {
                TempData["ErrorMessage"] = "Cannot reassign a closed ticket.";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            var validTeams = new[] { MaintenanceTeams.IT, MaintenanceTeams.Biomedical, MaintenanceTeams.General };
            var normalizedTeam = targetTeam?.ToUpper().Trim() ?? "";
            if (!validTeams.Contains(normalizedTeam))
            {
                TempData["ErrorMessage"] = "Invalid maintenance team selected.";
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            var oldTeam = ticket.TargetTeam;
            if (oldTeam != normalizedTeam)
            {
                ticket.TargetTeam = normalizedTeam;

                // If currently assigned tech belongs to a different maintenance team, unassign them
                if (ticket.Handler != null && ticket.Handler.TechUserId.HasValue)
                {
                    var currentTech = await _context.Users.FindAsync(ticket.Handler.TechUserId.Value);
                    if (currentTech != null && currentTech.Role == SystemRoles.Maintenance && currentTech.MaintenanceTeam != normalizedTeam)
                    {
                        ticket.Handler.TechUserId = null;
                        if (ticket.StatusId == 2)
                        {
                            ticket.StatusId = 1; // back to opened/unassigned
                        }
                    }
                }

                _context.TicketMessages.Add(new TicketMsg
                {
                    TicketId = ticket.Id,
                    UserId = currentUserId > 0 ? currentUserId : ticket.OpenedBy,
                    Msg = $"🔄 REASSIGNED RESPONSIBLE TEAM: Transferred from {oldTeam} to {normalizedTeam} maintenance team.",
                    TimeStamp = DateTime.Now
                });

                _context.Update(ticket);
                await _context.SaveChangesAsync();

                var actor = _currentUserService.GetCurrentUser();
                await _notificationService.NotifyTicketCreatedAsync(ticket, actor);

                TempData["SuccessMessage"] = $"Ticket #{ticket.Id} successfully assigned to {normalizedTeam} team.";
            }

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/AddMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMessage(int ticketId, int userId, string msg)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null) return NotFound();

            // Task 1: Lock messages once ticket is closed
            if (ticket.StatusId == 5 || ticket.ClosedDT.HasValue)
            {
                TempData["ErrorMessage"] = "This ticket is closed. New messages cannot be added.";
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            // RBAC Guard: Verify access permission
            if (!_currentUserService.CanAccessTicket(ticket))
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(msg))
            {
                TempData["ErrorMessage"] = "Message cannot be empty.";
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            var currentUserId = _currentUserService.GetUserId();
            if (currentUserId > 0)
            {
                userId = currentUserId;
            }

            var newMsg = new TicketMsg
            {
                TicketId = ticketId,
                UserId = userId,
                Msg = msg.Trim(),
                TimeStamp = DateTime.Now
            };
            _context.TicketMessages.Add(newMsg);

            await _context.SaveChangesAsync();

            var senderUser = await _context.Users.Include(u => u.Department).FirstOrDefaultAsync(u => u.Id == userId) ?? _currentUserService.GetCurrentUser();
            await _notificationService.NotifyTicketMessageAsync(ticket, newMsg, senderUser);

            TempData["SuccessMessage"] = "Message posted.";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/LogMaintenance
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogMaintenance(int ticketId, int assetId, int technicianId, int? supplierId, int maintenanceType, string description, string? partsReplaced)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null) return NotFound();

            if (!_currentUserService.CanManageTicket(ticket))
            {
                return Forbid();
            }

            var maintenance = new AssetMaintenance
            {
                TicketId = ticketId,
                AssetId = assetId,
                UserId = technicianId,
                SupplierId = supplierId,
                MaintenanceType = maintenanceType,
                Description = description,
                PartsReplaced = partsReplaced,
                TimStamp = DateTime.Now
            };

            _context.AssetMaintenances.Add(maintenance);

            // Update asset lastPeriodic if periodic
            if (maintenanceType == 2)
            {
                var asset = await _context.Assets.FindAsync(assetId);
                if (asset != null && asset.PeriodicInterval.HasValue)
                {
                    asset.LastPeriodic = DateTime.Now;
                    asset.NextPeriodic = DateTime.Now.AddDays(asset.PeriodicInterval.Value);
                    _context.Update(asset);
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Maintenance record saved and linked to ticket!";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // GET: Tickets/PrintTicket/5
        public async Task<IActionResult> PrintTicket(int? id)
        {
            if (id == null) return NotFound();

            var ticket = await _context.Tickets
                .Include(t => t.Asset).ThenInclude(a => a!.Room)
                .Include(t => t.Asset).ThenInclude(a => a!.Supplier)
                .Include(t => t.OpenedByUser).ThenInclude(u => u!.Department)
                .Include(t => t.OnBehalfOfUser)
                .Include(t => t.Status)
                .Include(t => t.Handler).ThenInclude(h => h!.TechUser)
                .Include(t => t.Messages).ThenInclude(m => m.User)
                .Include(t => t.MaintenanceLogs).ThenInclude(m => m.Technician)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (ticket == null) return NotFound();

            // RBAC Guard: Verify access permissions
            if (!_currentUserService.CanAccessTicket(ticket))
            {
                return Forbid();
            }

            return View(ticket);
        }

        // GET: Tickets/ExportCsv
        public async Task<IActionResult> ExportCsv(int? statusId, int? priority, string? type, string? search, string? filter)
        {
            var currentUserId = _currentUserService.GetUserId();

            var query = _context.Tickets
                .Include(t => t.Asset)
                .Include(t => t.OpenedByUser)
                .Include(t => t.Status)
                .Include(t => t.Handler).ThenInclude(h => h!.TechUser)
                .Include(t => t.Messages)
                .AsQueryable();

            // RBAC Filtering
            if (_currentUserService.IsEndUser())
            {
                query = query.Where(t => t.OpenedBy == currentUserId || t.OnBehalfOf == currentUserId);
            }
            else if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                query = query.Where(t => t.TargetTeam == myTeam);
            }

            if (statusId.HasValue && statusId.Value > 0) query = query.Where(t => t.StatusId == statusId.Value);
            if (priority.HasValue && priority.Value > 0) query = query.Where(t => t.Priority == priority.Value);
            if (!string.IsNullOrWhiteSpace(type)) query = query.Where(t => t.Type == type);

            if (!string.IsNullOrWhiteSpace(filter))
            {
                if (filter == "my_assigned") query = query.Where(t => t.Handler != null && t.Handler.TechUserId == currentUserId);
                else if (filter == "my_opened") query = query.Where(t => t.OpenedBy == currentUserId || t.OnBehalfOf == currentUserId);
                else if (filter == "unassigned") query = query.Where(t => t.Handler == null || t.Handler.TechUserId == null);
                else if (filter == "maintenance" || filter == "in_maintenance") query = query.Where(t => t.StatusId == 6 || (t.Status != null && t.Status.Status != null && t.Status.Status.ToLower() == "maintenance"));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(t =>
                    t.Id.ToString() == s ||
                    (t.Asset != null && t.Asset.HospitalNo != null && t.Asset.HospitalNo.ToLower().Contains(s)) ||
                    (t.OpenedByUser != null && t.OpenedByUser.FullName != null && t.OpenedByUser.FullName.ToLower().Contains(s))
                );
            }

            var tickets = await query.OrderByDescending(t => t.OpenedDT).ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Ticket ID,Target Team,Issue Category,Asset Tag,Asset Brand,Priority,Status,Requester,Assigned Technician,Opened Date,Closed Date,Total Messages");

            foreach (var t in tickets)
            {
                sb.AppendLine($"\"{t.Id}\",\"{EscapeCsv(t.TargetTeam)}\",\"{EscapeCsv(t.Type)}\",\"{EscapeCsv(t.Asset?.HospitalNo)}\",\"{EscapeCsv(t.Asset?.Brand)}\",\"{t.PriorityName}\",\"{EscapeCsv(t.Status?.Status)}\",\"{EscapeCsv(t.OpenedByUser?.FullName)}\",\"{EscapeCsv(t.Handler?.TechUser?.FullName ?? "Unassigned")}\",\"{t.OpenedDT:yyyy-MM-dd HH:mm}\",\"{t.ClosedDT:yyyy-MM-dd HH:mm}\",\"{t.Messages.Count}\"");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv", $"Tickets_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        private async Task PopulateDropdowns(int? selectedAssetId = null, int? defaultUserId = null, int? currentUserId = null)
        {
            // Only assets associated with the current user appear!
            var userAssetsQuery = _context.Assets.AsQueryable();
            if (currentUserId.HasValue && currentUserId.Value > 0)
            {
                userAssetsQuery = userAssetsQuery.Where(a => a.UserId == currentUserId.Value);
            }

            var userAssets = await userAssetsQuery
                .OrderBy(a => a.HospitalNo)
                .Select(a => new { 
                    a.Id, 
                    a.HospitalNo, 
                    a.Brand, 
                    a.Type, 
                    MaintenanceTeam = a.MaintenanceTeam ?? MaintenanceTeams.IT, 
                    Display = $"{a.HospitalNo} - {a.Brand} ({a.Type ?? "Workstation"})" 
                })
                .ToListAsync();

            ViewBag.Assets = new SelectList(userAssets, "Id", "Display", selectedAssetId);
            ViewBag.UserAssetsList = userAssets;
            ViewBag.UserAssetsCount = userAssets.Count;
            ViewBag.CurrentUserId = currentUserId ?? 0;
            ViewBag.Users = new SelectList(await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName", defaultUserId);
            var techUsers = await _context.Users
                .Where(u => u.IsActive == true && (u.Role == SystemRoles.Maintenance || u.Role == SystemRoles.Admin))
                .OrderBy(u => u.FullName)
                .Select(u => new {
                    u.Id,
                    u.FullName,
                    RoleDisplay = u.RoleDisplay,
                    MaintenanceTeam = u.MaintenanceTeam ?? "IT"
                })
                .ToListAsync();
            ViewBag.TechniciansList = techUsers;
            ViewBag.Technicians = new SelectList(techUsers, "Id", "FullName");
            ViewBag.Statuses = new SelectList(await _context.TicketStatuses.ToListAsync(), "Id", "Status");
            ViewBag.MaintenanceTeams = new SelectList(MaintenanceTeams.All, MaintenanceTeams.IT);
        }
    }
}
