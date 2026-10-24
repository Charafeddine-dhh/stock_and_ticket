using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    public class MaintenanceController : Controller
    {
        private readonly StockDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public MaintenanceController(StockDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        // GET: Maintenance
        public async Task<IActionResult> Index()
        {
            if (_currentUserService.IsEndUser())
            {
                return Forbid();
            }

            var now = DateTime.Now.Date;
            var thirtyDaysFromNow = now.AddDays(30);

            var periodicAssetsQuery = _context.Assets
                .Where(a => a.NeedsPeriodic == true)
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .AsQueryable();

            var logsQuery = _context.AssetMaintenances
                .Include(m => m.Asset)
                .Include(m => m.Technician)
                .Include(m => m.Supplier)
                .Include(m => m.Ticket)
                .AsQueryable();

            var activeWorkOrdersQuery = _context.AssetMaintenances
                .Where(m => m.WorkOrderStatus == "Under Maintenance")
                .Include(m => m.Asset).ThenInclude(a => a!.User)
                .Include(m => m.Asset).ThenInclude(a => a!.Room)
                .Include(m => m.Supplier)
                .Include(m => m.Technician)
                .Include(m => m.PreviousUser)
                .Include(m => m.Ticket).ThenInclude(t => t!.Status)
                .AsQueryable();

            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                periodicAssetsQuery = periodicAssetsQuery.Where(a => a.MaintenanceTeam == myTeam);
                logsQuery = logsQuery.Where(m => m.Asset != null && m.Asset.MaintenanceTeam == myTeam);
                activeWorkOrdersQuery = activeWorkOrdersQuery.Where(m => m.Asset != null && m.Asset.MaintenanceTeam == myTeam);
            }

            var periodicAssets = await periodicAssetsQuery.ToListAsync();
            var recentLogs = await logsQuery.OrderByDescending(m => m.TimStamp).Take(50).ToListAsync();
            var activeWorkOrders = await activeWorkOrdersQuery.OrderByDescending(m => m.SentDate).ToListAsync();

            var vm = new MaintenanceScheduleViewModel
            {
                OverdueAssets = periodicAssets
                    .Where(a => a.NextPeriodic.HasValue && a.NextPeriodic.Value < now)
                    .OrderBy(a => a.NextPeriodic)
                    .ToList(),

                DueWithin30DaysAssets = periodicAssets
                    .Where(a => a.NextPeriodic.HasValue && a.NextPeriodic.Value >= now && a.NextPeriodic.Value <= thirtyDaysFromNow)
                    .OrderBy(a => a.NextPeriodic)
                    .ToList(),

                UpToDateAssets = periodicAssets
                    .Where(a => !a.NextPeriodic.HasValue || a.NextPeriodic.Value > thirtyDaysFromNow)
                    .OrderBy(a => a.NextPeriodic)
                    .ToList(),

                RecentLogs = recentLogs,
                ActiveWorkOrders = activeWorkOrders
            };

            return View(vm);
        }

        // GET: Maintenance/SendToSupplier
        public async Task<IActionResult> SendToSupplier(int? assetId, int? ticketId)
        {
            if (_currentUserService.IsEndUser()) return Forbid();

            if (assetId.HasValue && assetId.Value > 0)
            {
                var targetAsset = await _context.Assets.FindAsync(assetId.Value);
                if (targetAsset != null && !_currentUserService.CanAccessAsset(targetAsset))
                {
                    return Forbid();
                }
            }

            await PopulateSendDropdowns(assetId, ticketId);

            var model = new AssetMaintenance
            {
                AssetId = assetId,
                TicketId = ticketId,
                MaintenanceType = 1, // Breakdown / Repair
                SentDate = DateTime.Now,
                TimStamp = DateTime.Now,
                UserId = _currentUserService.GetUserId(),
                WorkOrderStatus = "Under Maintenance"
            };

            return View(model);
        }

        // POST: Maintenance/SendToSupplier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendToSupplier(int assetId, int supplierId, string description, int? ticketId, int? technicianId, int? maintenanceType, DateTime? sentDate)
        {
            if (_currentUserService.IsEndUser()) return Forbid();

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset == null) return NotFound();

            if (!_currentUserService.CanManageAsset(asset)) return Forbid();

            if (string.Equals(asset.Status, "Under Maintenance", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("AssetId", "This asset is already currently marked as Under Maintenance.");
            }

            var supplier = await _context.Suppliers.FindAsync(supplierId);
            if (supplier == null)
            {
                ModelState.AddModelError("SupplierId", "Please select a valid supplier/vendor.");
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                ModelState.AddModelError("Description", "Please provide a detailed issue description before sending the asset to maintenance.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateSendDropdowns(assetId, ticketId);
                return View(new AssetMaintenance
                {
                    AssetId = assetId,
                    SupplierId = supplierId,
                    Description = description,
                    TicketId = ticketId,
                    MaintenanceType = maintenanceType ?? 1,
                    SentDate = sentDate ?? DateTime.Now
                });
            }

            int techId = (technicianId.HasValue && technicianId.Value > 0) ? technicianId.Value : _currentUserService.GetUserId();
            DateTime actualSentDate = sentDate ?? DateTime.Now;

            // 1. Create Maintenance Work Order
            var workOrder = new AssetMaintenance
            {
                AssetId = asset.Id,
                SupplierId = supplierId,
                UserId = techId,
                TicketId = (ticketId.HasValue && ticketId.Value > 0) ? ticketId.Value : null,
                MaintenanceType = maintenanceType ?? 1,
                Description = description.Trim(),
                WorkOrderStatus = "Under Maintenance",
                SentDate = actualSentDate,
                TimStamp = actualSentDate,
                PreviousUserId = asset.UserId,
                PreviousRoomId = asset.RoomId
            };
            _context.AssetMaintenances.Add(workOrder);
            await _context.SaveChangesAsync();

            // 2. Custody Transfer: Close previous custody history record if open
            var previousHistory = await _context.AssetHistories
                .Where(h => h.AssetId == asset.Id && h.ReturnedOn == null)
                .OrderByDescending(h => h.AssignedOn)
                .FirstOrDefaultAsync();

            if (previousHistory != null)
            {
                previousHistory.ReturnedOn = actualSentDate;
            }

            // 3. Create new Custody History record for Supplier
            _context.AssetHistories.Add(new AssetHistory
            {
                AssetId = asset.Id,
                UserId = asset.UserId ?? techId,
                RoomId = asset.RoomId ?? 1,
                CustodyTypeId = 3, // Supplier / External Maintenance
                AssignedOn = actualSentDate,
                Notes = $"Sent to external supplier: {supplier!.Name} (Work Order #{workOrder.Id})"
            });

            // 4. Update Asset Status and Custody Type
            asset.Status = "Under Maintenance";
            asset.CustodyTypeId = 3;
            _context.Update(asset);

            // 5. If linked to Ticket: update ticket status to "maintenance" (6) and log event message
            if (ticketId.HasValue && ticketId.Value > 0)
            {
                var ticket = await _context.Tickets.FindAsync(ticketId.Value);
                if (ticket != null)
                {
                    ticket.StatusId = 6; // maintenance status
                    _context.Update(ticket);

                    _context.TicketMessages.Add(new TicketMsg
                    {
                        TicketId = ticket.Id,
                        UserId = techId,
                        Msg = $"🔧 WORK ORDER #{workOrder.Id} INITIATED:\n" +
                              $"• Asset Tag: {asset.HospitalNo ?? ("#" + asset.Id)} ({asset.Brand} {asset.Type})\n" +
                              $"• External Supplier: {supplier.Name}\n" +
                              $"• Sent Date: {actualSentDate:yyyy-MM-dd HH:mm}\n" +
                              $"• Issue Description: {description.Trim()}\n" +
                              $"• Status: Custody transferred to supplier. Ticket status updated to Under Maintenance.",
                        TimeStamp = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            if (TempData != null) TempData["SuccessMessage"] = $"Asset {asset.HospitalNo ?? ("#" + asset.Id)} was dispatched to {supplier!.Name} for maintenance (Work Order #{workOrder.Id}). Custody successfully transferred to supplier.";
            return RedirectToAction(nameof(WorkOrderDetails), new { id = workOrder.Id });
        }

        // GET: Maintenance/ReturnFromSupplier/5
        public async Task<IActionResult> ReturnFromSupplier(int id)
        {
            if (_currentUserService.IsEndUser()) return Forbid();

            var workOrder = await _context.AssetMaintenances
                .Include(m => m.Asset).ThenInclude(a => a!.User)
                .Include(m => m.Asset).ThenInclude(a => a!.Room)
                .Include(m => m.Supplier)
                .Include(m => m.Technician)
                .Include(m => m.PreviousUser)
                .Include(m => m.PreviousRoom)
                .Include(m => m.Ticket)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (workOrder == null) return NotFound();

            if (workOrder.Asset != null && !_currentUserService.CanManageAsset(workOrder.Asset))
            {
                return Forbid();
            }

            if (!workOrder.IsUnderMaintenance)
            {
                if (TempData != null) TempData["InfoMessage"] = "This work order has already been completed and returned.";
                return RedirectToAction(nameof(WorkOrderDetails), new { id = workOrder.Id });
            }

            await PopulateReturnDropdowns(workOrder.PreviousUserId, workOrder.PreviousRoomId);
            return View(workOrder);
        }

        // POST: Maintenance/ReturnFromSupplier/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReturnFromSupplier(int id, DateTime? returnDate, string? actionTaken, decimal? cost, string? partsJson, string returnDestination, int? newUserId, int newRoomId)
        {
            if (_currentUserService.IsEndUser()) return Forbid();

            var workOrder = await _context.AssetMaintenances
                .Include(m => m.Asset)
                .Include(m => m.Supplier)
                .Include(m => m.Ticket)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (workOrder == null) return NotFound();

            if (workOrder.Asset != null && !_currentUserService.CanManageAsset(workOrder.Asset))
            {
                return Forbid();
            }

            var asset = workOrder.Asset;
            if (asset == null) return NotFound();

            DateTime actualReturnDate = returnDate ?? DateTime.Now;
            int techId = _currentUserService.GetUserId();

            // Validate return destination
            bool isAssigningToUser = string.Equals(returnDestination, "user", StringComparison.OrdinalIgnoreCase);
            if (isAssigningToUser && (!newUserId.HasValue || newUserId.Value <= 0))
            {
                ModelState.AddModelError("newUserId", "Please select a valid staff member to receive asset custody.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateReturnDropdowns(newUserId ?? workOrder.PreviousUserId, newRoomId);
                return View(workOrder);
            }

            // 1. Process Replaced Parts JSON
            string cleanPartsJson = "[]";
            if (!string.IsNullOrWhiteSpace(partsJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(partsJson);
                    cleanPartsJson = partsJson.Trim();
                }
                catch
                {
                    cleanPartsJson = "[]";
                }
            }

            // 2. Update Work Order Record
            workOrder.WorkOrderStatus = "Completed";
            workOrder.ReturnDate = actualReturnDate;
            workOrder.ActionTaken = actionTaken?.Trim();
            workOrder.Cost = cost;
            workOrder.PartsReplaced = cleanPartsJson;

            // 3. Reset periodic maintenance date if scheduled
            if (asset.NeedsPeriodic == true && asset.PeriodicInterval.HasValue && asset.PeriodicInterval.Value > 0)
            {
                asset.LastPeriodic = actualReturnDate;
                asset.NextPeriodic = actualReturnDate.AddDays(asset.PeriodicInterval.Value);
            }

            // 4. Custody Transfer: Close supplier custody history
            var supplierHistory = await _context.AssetHistories
                .Where(h => h.AssetId == asset.Id && h.CustodyTypeId == 3 && h.ReturnedOn == null)
                .OrderByDescending(h => h.AssignedOn)
                .FirstOrDefaultAsync();

            if (supplierHistory != null)
            {
                supplierHistory.ReturnedOn = actualReturnDate;
            }

            // 5. Create new Custody History record based on selected destination
            if (isAssigningToUser)
            {
                // Assign to specific user
                var targetUser = await _context.Users.FindAsync(newUserId!.Value);
                asset.UserId = newUserId.Value;
                asset.RoomId = newRoomId;
                asset.CustodyTypeId = 1; // Personal
                asset.Status = "In Service";

                _context.AssetHistories.Add(new AssetHistory
                {
                    AssetId = asset.Id,
                    UserId = newUserId.Value,
                    RoomId = newRoomId,
                    CustodyTypeId = 1,
                    AssignedOn = actualReturnDate,
                    Notes = $"Returned from supplier {workOrder.Supplier?.Name} and assigned to {targetUser?.FullName ?? "staff"} (Work Order #{workOrder.Id})"
                });
            }
            else
            {
                // Return to Central Stock
                asset.UserId = null;
                asset.RoomId = newRoomId;
                asset.CustodyTypeId = 2; // Shared / Central Stock
                asset.Status = "In Stock";

                _context.AssetHistories.Add(new AssetHistory
                {
                    AssetId = asset.Id,
                    UserId = techId,
                    RoomId = newRoomId,
                    CustodyTypeId = 2,
                    AssignedOn = actualReturnDate,
                    Notes = $"Returned from supplier {workOrder.Supplier?.Name} to Central Department Stock (Work Order #{workOrder.Id})"
                });
            }

            _context.Update(asset);

            // 6. Update linked Ticket if present
            if (workOrder.TicketId.HasValue)
            {
                var ticket = await _context.Tickets.FindAsync(workOrder.TicketId.Value);
                if (ticket != null)
                {
                    ticket.StatusId = 2; // Pending verification / testing
                    _context.Update(ticket);

                    var partsList = workOrder.ParsedParts;
                    string partsDesc = partsList.Any()
                        ? string.Join(", ", partsList.Select(p => $"{p.PartName} (Qty: {p.Quantity})"))
                        : "No parts replaced";

                    _context.TicketMessages.Add(new TicketMsg
                    {
                        TicketId = ticket.Id,
                        UserId = techId,
                        Msg = $"✅ WORK ORDER #{workOrder.Id} COMPLETED & ASSET RETURNED:\n" +
                              $"• Asset Tag: {asset.HospitalNo ?? ("#" + asset.Id)}\n" +
                              $"• Returned from Supplier: {workOrder.Supplier?.Name}\n" +
                              $"• Return Date: {actualReturnDate:yyyy-MM-dd HH:mm}\n" +
                              $"• Work Summary: {actionTaken ?? "Repairs and inspection completed."}\n" +
                              $"• Replaced Parts: {partsDesc}\n" +
                              $"• New Custody: {(isAssigningToUser ? "Assigned to staff" : "Stored in Central Stock")}\n" +
                              $"• Ticket Status: Set to Pending (Ready for verification / closure).",
                        TimeStamp = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            if (TempData != null) TempData["SuccessMessage"] = $"Asset {asset.HospitalNo ?? ("#" + asset.Id)} has returned from {workOrder.Supplier?.Name}. Custody updated to {(isAssigningToUser ? "User" : "Central Stock")}!";
            return RedirectToAction(nameof(WorkOrderDetails), new { id = workOrder.Id });
        }

        // GET: Maintenance/WorkOrderDetails/5
        public async Task<IActionResult> WorkOrderDetails(int id)
        {
            if (_currentUserService.IsEndUser()) return Forbid();

            var workOrder = await _context.AssetMaintenances
                .Include(m => m.Asset).ThenInclude(a => a!.User)
                .Include(m => m.Asset).ThenInclude(a => a!.Room)
                .Include(m => m.Asset).ThenInclude(a => a!.CustodyType)
                .Include(m => m.Supplier)
                .Include(m => m.Technician)
                .Include(m => m.PreviousUser)
                .Include(m => m.PreviousRoom)
                .Include(m => m.Ticket).ThenInclude(t => t!.Status)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (workOrder == null) return NotFound();

            if (workOrder.Asset != null && !_currentUserService.CanAccessAsset(workOrder.Asset))
            {
                return Forbid();
            }

            return View(workOrder);
        }

        // Legacy / Quick Log
        // GET: Maintenance/Log
        public async Task<IActionResult> Log(int? assetId)
        {
            return RedirectToAction(nameof(SendToSupplier), new { assetId });
        }

        // POST: Maintenance/QuickCompleteCheckup
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> QuickCompleteCheckup(int assetId, int technicianId, string? notes)
        {
            var asset = await _context.Assets.FindAsync(assetId);
            if (asset == null) return NotFound();

            if (!_currentUserService.CanManageAsset(asset)) return Forbid();

            var record = new AssetMaintenance
            {
                AssetId = assetId,
                UserId = technicianId,
                MaintenanceType = 2, // Periodic Check
                Description = string.IsNullOrWhiteSpace(notes) ? "Routine periodic inspection completed." : notes.Trim(),
                WorkOrderStatus = "Completed",
                TimStamp = DateTime.Now,
                ReturnDate = DateTime.Now
            };

            _context.AssetMaintenances.Add(record);

            if (asset.PeriodicInterval.HasValue)
            {
                asset.LastPeriodic = DateTime.Now;
                asset.NextPeriodic = DateTime.Now.AddDays(asset.PeriodicInterval.Value);
                _context.Update(asset);
            }

            await _context.SaveChangesAsync();
            if (TempData != null) TempData["SuccessMessage"] = $"Periodic check completed for asset {asset.HospitalNo ?? ("#" + asset.Id)}.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Maintenance/ExportCsv
        public async Task<IActionResult> ExportCsv()
        {
            var logsQuery = _context.AssetMaintenances
                .Include(m => m.Asset)
                .Include(m => m.Technician)
                .Include(m => m.Supplier)
                .Include(m => m.Ticket)
                .AsQueryable();

            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                logsQuery = logsQuery.Where(m => m.Asset != null && m.Asset.MaintenanceTeam == myTeam);
            }

            var logs = await logsQuery.OrderByDescending(m => m.TimStamp).ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ID,Work Order Status,Sent Date,Return Date,Asset Tag,Asset Brand,Team,Maintenance Type,Technician,Supplier,Ticket ID,Issue Description,Action Taken,Parts Replaced JSON,Cost");

            foreach (var m in logs)
            {
                sb.AppendLine($"\"{m.Id}\",\"{EscapeCsv(m.WorkOrderStatus)}\",\"{m.SentDate:yyyy-MM-dd HH:mm}\",\"{m.ReturnDate:yyyy-MM-dd HH:mm}\",\"{EscapeCsv(m.Asset?.HospitalNo)}\",\"{EscapeCsv(m.Asset?.Brand)}\",\"{EscapeCsv(m.Asset?.MaintenanceTeam)}\",\"{EscapeCsv(m.TypeName)}\",\"{EscapeCsv(m.Technician?.FullName)}\",\"{EscapeCsv(m.Supplier?.Name)}\",\"{(m.TicketId.HasValue ? "#" + m.TicketId.Value : "Direct")}\",\"{EscapeCsv(m.Description)}\",\"{EscapeCsv(m.ActionTaken)}\",\"{EscapeCsv(m.PartsReplaced)}\",\"{m.Cost:F2}\"");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv", $"Maintenance_Work_Orders_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        private async Task PopulateSendDropdowns(int? selectedAssetId = null, int? selectedTicketId = null)
        {
            var assetsQuery = _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Where(a => a.Status != "Retired" && a.Status != "Under Maintenance")
                .AsQueryable();

            var ticketsQuery = _context.Tickets
                .Where(t => t.StatusId != 5 && t.ClosedDT == null)
                .AsQueryable();

            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                assetsQuery = assetsQuery.Where(a => a.MaintenanceTeam == myTeam);
                ticketsQuery = ticketsQuery.Where(t => t.TargetTeam == myTeam);
            }

            var assets = await assetsQuery
                .OrderBy(a => a.HospitalNo)
                .Select(a => new
                {
                    a.Id,
                    Display = $"{a.HospitalNo} - {a.Brand} {a.Type} [Held by: {(a.User != null ? a.User.FullName : "Central Stock")} - Room {(a.Room != null ? a.Room.RoomNo : "N/A")}]"
                })
                .ToListAsync();

            var tickets = await ticketsQuery
                .OrderByDescending(t => t.Id)
                .Select(t => new
                {
                    t.Id,
                    Display = $"Ticket #{t.Id} - {t.Type} (Asset: {(t.Asset != null ? t.Asset.HospitalNo : "None")})"
                })
                .ToListAsync();

            ViewBag.Assets = new SelectList(assets, "Id", "Display", selectedAssetId);
            ViewBag.Suppliers = new SelectList(await _context.Suppliers.Where(s => s.IsActive == true).OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
            ViewBag.Technicians = new SelectList(await _context.Users.Where(u => u.IsActive == true && (u.Role == SystemRoles.Maintenance || u.Role == SystemRoles.Admin)).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName", _currentUserService.GetUserId());
            ViewBag.Tickets = new SelectList(tickets, "Id", "Display", selectedTicketId);
        }

        private async Task PopulateReturnDropdowns(int? defaultUserId = null, int? defaultRoomId = null)
        {
            var users = await _context.Users
                .Where(u => u.IsActive == true)
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, Display = $"{u.FullName} ({(u.Department != null ? u.Department.Name : "Staff")})" })
                .ToListAsync();

            var rooms = await _context.Rooms
                .OrderBy(r => r.Floor).ThenBy(r => r.RoomNo)
                .Select(r => new { r.Id, Display = $"Room {r.RoomNo} (Floor {r.Floor})" })
                .ToListAsync();

            ViewBag.Users = new SelectList(users, "Id", "Display", defaultUserId);
            ViewBag.Rooms = new SelectList(rooms, "Id", "Display", defaultRoomId);
        }
    }
}
