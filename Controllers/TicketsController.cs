using System;
using System.Linq;
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

        public TicketsController(StockDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        // GET: Tickets
        public async Task<IActionResult> Index(int? statusId, int? priority, string? type, string? search)
        {
            var query = _context.Tickets
                .Include(t => t.Asset)
                .Include(t => t.OpenedByUser)
                .Include(t => t.OnBehalfOfUser)
                .Include(t => t.Status)
                .Include(t => t.Handler).ThenInclude(h => h!.TechUser)
                .Include(t => t.Messages)
                .AsQueryable();

            if (statusId.HasValue && statusId.Value > 0)
            {
                query = query.Where(t => t.StatusId == statusId.Value);
            }

            if (priority.HasValue && priority.Value > 0)
            {
                query = query.Where(t => t.Priority == priority.Value);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(t => t.Type == type);
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

            ViewBag.Statuses = await _context.TicketStatuses.ToListAsync();

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
            await PopulateDropdowns(assetId, currentUserId);

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
        public async Task<IActionResult> Create(Ticket ticket, string? initialMessage, int? assignedTechId)
        {
            if (ModelState.IsValid)
            {
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

                // If initial message provided
                if (!string.IsNullOrWhiteSpace(initialMessage))
                {
                    _context.TicketMessages.Add(new TicketMsg
                    {
                        TicketId = ticket.Id,
                        UserId = ticket.OpenedBy,
                        Msg = initialMessage,
                        TimeStamp = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Ticket #{ticket.Id} created successfully!";
                return RedirectToAction(nameof(Details), new { id = ticket.Id });
            }

            await PopulateDropdowns(ticket.AssetId);
            return View(ticket);
        }

        // POST: Tickets/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int ticketId, int statusId)
        {
            var ticket = await _context.Tickets.FindAsync(ticketId);
            if (ticket == null) return NotFound();

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

            TempData["SuccessMessage"] = "Ticket status updated.";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/AssignTechnician
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTechnician(int ticketId, int techUserId)
        {
            var ticket = await _context.Tickets.Include(t => t.Handler).FirstOrDefaultAsync(t => t.Id == ticketId);
            if (ticket == null) return NotFound();

            if (ticket.Handler == null)
            {
                _context.TicketHandlers.Add(new TicketHandler
                {
                    TicketId = ticketId,
                    TechUserId = techUserId,
                    AssignedDT = DateTime.Now
                });
            }
            else
            {
                ticket.Handler.TechUserId = techUserId;
                ticket.Handler.AssignedDT = DateTime.Now;
                _context.Update(ticket.Handler);
            }

            if (ticket.StatusId == 1) // Move from opened to pending
            {
                ticket.StatusId = 2;
                _context.Update(ticket);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Technician assigned successfully.";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/AddMessage
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMessage(int ticketId, int userId, string msg)
        {
            if (string.IsNullOrWhiteSpace(msg))
            {
                TempData["ErrorMessage"] = "Message cannot be empty.";
                return RedirectToAction(nameof(Details), new { id = ticketId });
            }

            _context.TicketMessages.Add(new TicketMsg
            {
                TicketId = ticketId,
                UserId = userId,
                Msg = msg.Trim(),
                TimeStamp = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Message posted.";
            return RedirectToAction(nameof(Details), new { id = ticketId });
        }

        // POST: Tickets/LogMaintenance
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogMaintenance(int ticketId, int assetId, int technicianId, int? supplierId, int maintenanceType, string description, string? partsReplaced)
        {
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

        private async Task PopulateDropdowns(int? selectedAssetId = null, int? defaultUserId = null)
        {
            var assets = await _context.Assets
                .OrderBy(a => a.HospitalNo)
                .Select(a => new { a.Id, Display = $"{a.HospitalNo} - {a.Brand} ({a.Type})" })
                .ToListAsync();

            ViewBag.Assets = new SelectList(assets, "Id", "Display", selectedAssetId);
            ViewBag.Users = new SelectList(await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName", defaultUserId);
            ViewBag.Technicians = new SelectList(await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName");
            ViewBag.Statuses = new SelectList(await _context.TicketStatuses.ToListAsync(), "Id", "Status");
        }
    }
}
