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
    public class AssetsController : Controller
    {
        private readonly StockDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public AssetsController(StockDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        // GET: Assets
        public async Task<IActionResult> Index(string? search, string? status, string? type, int? roomId, string? warranty)
        {
            var query = _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .Include(a => a.CustodyType)
                .AsQueryable();

            // RBAC Guard: End users can view ONLY assets currently in their custody; Maintenance users view ONLY assets for their designated team
            if (_currentUserService.IsEndUser())
            {
                int currentUserId = _currentUserService.GetUserId();
                query = query.Where(a => a.UserId == currentUserId);
            }
            else if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                query = query.Where(a => a.MaintenanceTeam == myTeam);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(a =>
                    (a.HospitalNo != null && a.HospitalNo.ToLower().Contains(s)) ||
                    (a.SerialNo != null && a.SerialNo.ToLower().Contains(s)) ||
                    (a.Brand != null && a.Brand.ToLower().Contains(s)) ||
                    (a.Type != null && a.Type.ToLower().Contains(s)) ||
                    (a.User != null && a.User.FullName != null && a.User.FullName.ToLower().Contains(s))
                );
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(a => a.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(a => a.Type == type);
            }

            if (roomId.HasValue)
            {
                query = query.Where(a => a.RoomId == roomId.Value);
            }

            if (!string.IsNullOrWhiteSpace(warranty))
            {
                var now = DateTime.Now.Date;
                if (warranty == "expired")
                {
                    query = query.Where(a => a.WarrantyTill.HasValue && a.WarrantyTill.Value < now);
                }
                else if (warranty == "expiring30")
                {
                    var future30 = now.AddDays(30);
                    query = query.Where(a => a.WarrantyTill.HasValue && a.WarrantyTill.Value >= now && a.WarrantyTill.Value <= future30);
                }
                else if (warranty == "active")
                {
                    query = query.Where(a => a.WarrantyTill.HasValue && a.WarrantyTill.Value >= now);
                }
            }

            ViewBag.Search = search;
            ViewBag.SelectedStatus = status;
            ViewBag.SelectedType = type;
            ViewBag.SelectedRoomId = roomId;
            ViewBag.SelectedWarranty = warranty;

            ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.Floor).ThenBy(r => r.RoomNo).ToListAsync();
            ViewBag.Types = await _context.Assets.Where(a => a.Type != null).Select(a => a.Type!).Distinct().ToListAsync();

            var list = await query.OrderByDescending(a => a.Id).ToListAsync();
            return View(list);
        }

        // GET: Assets/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var asset = await _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .Include(a => a.CustodyType)
                .Include(a => a.History).ThenInclude(h => h.User)
                .Include(a => a.History).ThenInclude(h => h.Room)
                .Include(a => a.History).ThenInclude(h => h.CustodyType)
                .Include(a => a.MaintenanceRecords).ThenInclude(m => m.Technician)
                .Include(a => a.MaintenanceRecords).ThenInclude(m => m.Supplier)
                .Include(a => a.Tickets).ThenInclude(t => t.Status)
                .Include(a => a.Tickets).ThenInclude(t => t.Handler).ThenInclude(h => h!.TechUser)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (asset == null) return NotFound();

            // RBAC Guard
            if (!_currentUserService.CanAccessAsset(asset))
            {
                return Forbid();
            }

            var vm = new AssetDetailsViewModel
            {
                Asset = asset,
                AvailableUsers = await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(),
                AvailableRooms = await _context.Rooms.OrderBy(r => r.Floor).ThenBy(r => r.RoomNo).ToListAsync(),
                AvailableCustodyTypes = await _context.CustodyTypes.ToListAsync(),
                NewUserId = asset.UserId ?? 0,
                NewRoomId = asset.RoomId ?? 0,
                NewCustodyTypeId = asset.CustodyTypeId ?? 1
            };

            return View(vm);
        }

        // GET: Assets/Create
        public async Task<IActionResult> Create()
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();

            await PopulateDropdowns();
            return View(new Asset
            {
                Status = "In Service",
                BuyingDate = DateTime.Now,
                NeedsPeriodic = false,
                MaintenanceTeam = MaintenanceTeams.IT
            });
        }

        // POST: Assets/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Asset asset)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();

            // Validate unique HospitalNo
            if (!string.IsNullOrWhiteSpace(asset.HospitalNo) && 
                await _context.Assets.AnyAsync(a => a.HospitalNo == asset.HospitalNo.Trim()))
            {
                ModelState.AddModelError("HospitalNo", "An asset with this Hospital Tag Number already exists.");
            }

            if (ModelState.IsValid)
            {
                // Calculate next periodic if needed
                if (asset.NeedsPeriodic == true && asset.PeriodicInterval.HasValue && asset.PeriodicInterval.Value > 0)
                {
                    asset.LastPeriodic ??= DateTime.Now;
                    asset.NextPeriodic = asset.LastPeriodic.Value.AddDays(asset.PeriodicInterval.Value);
                }

                _context.Assets.Add(asset);
                await _context.SaveChangesAsync();

                // Record initial custody history
                if (asset.UserId.HasValue && asset.RoomId.HasValue)
                {
                    _context.AssetHistories.Add(new AssetHistory
                    {
                        AssetId = asset.Id,
                        UserId = asset.UserId.Value,
                        RoomId = asset.RoomId.Value,
                        CustodyTypeId = asset.CustodyTypeId ?? 1,
                        AssignedOn = DateTime.Now,
                        Notes = "Initial commissioning & assignment"
                    });
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = $"Asset {asset.HospitalNo ?? ("#" + asset.Id)} was registered successfully!";
                return RedirectToAction(nameof(Details), new { id = asset.Id });
            }

            await PopulateDropdowns(asset);
            return View(asset);
        }

        // GET: Assets/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();
            if (id == null) return NotFound();

            var asset = await _context.Assets.FindAsync(id);
            if (asset == null) return NotFound();

            await PopulateDropdowns(asset);
            return View(asset);
        }

        // POST: Assets/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Asset asset)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();
            if (id != asset.Id) return NotFound();

            // Validate unique HospitalNo if changed
            if (!string.IsNullOrWhiteSpace(asset.HospitalNo) && 
                await _context.Assets.AnyAsync(a => a.HospitalNo == asset.HospitalNo.Trim() && a.Id != id))
            {
                ModelState.AddModelError("HospitalNo", "An asset with this Hospital Tag Number already exists.");
            }

            if (ModelState.IsValid)
            {
                try
                {
                    if (asset.NeedsPeriodic == true && asset.PeriodicInterval.HasValue && asset.PeriodicInterval.Value > 0)
                    {
                        if (asset.LastPeriodic.HasValue)
                        {
                            asset.NextPeriodic = asset.LastPeriodic.Value.AddDays(asset.PeriodicInterval.Value);
                        }
                    }
                    else
                    {
                        asset.NextPeriodic = null;
                    }

                    _context.Update(asset);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Asset updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Assets.Any(e => e.Id == asset.Id)) return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Details), new { id = asset.Id });
            }

            await PopulateDropdowns(asset);
            return View(asset);
        }

        // POST: Assets/TransferCustody
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferCustody(int assetId, int newUserId, int newRoomId, int newCustodyTypeId, string? notes)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();

            var asset = await _context.Assets.FindAsync(assetId);
            if (asset == null) return NotFound();

            // Close existing history record if open
            var previousHistory = await _context.AssetHistories
                .Where(h => h.AssetId == assetId && h.ReturnedOn == null)
                .OrderByDescending(h => h.AssignedOn)
                .FirstOrDefaultAsync();

            if (previousHistory != null)
            {
                previousHistory.ReturnedOn = DateTime.Now;
            }

            // Create new history entry
            var history = new AssetHistory
            {
                AssetId = asset.Id,
                UserId = newUserId,
                RoomId = newRoomId,
                CustodyTypeId = newCustodyTypeId,
                AssignedOn = DateTime.Now,
                Notes = notes ?? "Custody re-assigned"
            };
            _context.AssetHistories.Add(history);

            // Update asset record
            asset.UserId = newUserId;
            asset.RoomId = newRoomId;
            asset.CustodyTypeId = newCustodyTypeId;
            _context.Update(asset);

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Asset custody and location updated successfully!";
            return RedirectToAction(nameof(Details), new { id = assetId });
        }

        // POST: Assets/Retire/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Retire(int id, string? reason)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();

            var asset = await _context.Assets.FindAsync(id);
            if (asset == null) return NotFound();

            asset.Status = "Retired";
            _context.Update(asset);

            // Record history notes
            _context.AssetHistories.Add(new AssetHistory
            {
                AssetId = asset.Id,
                UserId = asset.UserId ?? 1,
                RoomId = asset.RoomId ?? 1,
                CustodyTypeId = asset.CustodyTypeId ?? 1,
                AssignedOn = DateTime.Now,
                ReturnedOn = DateTime.Now,
                Notes = $"Asset retired / decommissioned. Reason: {reason ?? "End of life cycle"}"
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Asset {asset.HospitalNo ?? ("#" + asset.Id)} was marked as Retired.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // GET: Assets/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();
            if (id == null) return NotFound();

            var asset = await _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Tickets)
                .Include(a => a.MaintenanceRecords)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (asset == null) return NotFound();

            return View(asset);
        }

        // POST: Assets/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!_currentUserService.CanManageAssets()) return Forbid();

            var asset = await _context.Assets
                .Include(a => a.Tickets)
                .Include(a => a.MaintenanceRecords)
                .Include(a => a.History)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (asset != null)
            {
                var tag = asset.HospitalNo ?? ("#" + asset.Id);

                // If asset has active tickets, prevent deletion
                if (asset.Tickets.Any(t => t.StatusId != 5 && t.StatusId != 4))
                {
                    TempData["ErrorMessage"] = $"Cannot delete asset {tag} because it has open or pending tickets. Close or reassign tickets first, or retire the asset.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                _context.Assets.Remove(asset);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Asset {tag} was deleted from inventory.";
            }

            return RedirectToAction(nameof(Index));
        }

        // GET: Assets/PrintTag/5
        public async Task<IActionResult> PrintTag(int? id)
        {
            if (id == null) return NotFound();

            var asset = await _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (asset == null) return NotFound();

            if (!_currentUserService.CanAccessAsset(asset)) return Forbid();

            return View(asset);
        }

        // GET: Assets/ExportCsv
        public async Task<IActionResult> ExportCsv(string? search, string? status, string? type, int? roomId)
        {
            var query = _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .Include(a => a.CustodyType)
                .AsQueryable();

            if (_currentUserService.IsEndUser())
            {
                int currentUserId = _currentUserService.GetUserId();
                query = query.Where(a => a.UserId == currentUserId);
            }
            else if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                query = query.Where(a => a.MaintenanceTeam == myTeam);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                query = query.Where(a =>
                    (a.HospitalNo != null && a.HospitalNo.ToLower().Contains(s)) ||
                    (a.SerialNo != null && a.SerialNo.ToLower().Contains(s)) ||
                    (a.Brand != null && a.Brand.ToLower().Contains(s)) ||
                    (a.Type != null && a.Type.ToLower().Contains(s)) ||
                    (a.User != null && a.User.FullName != null && a.User.FullName.ToLower().Contains(s))
                );
            }

            if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);
            if (!string.IsNullOrWhiteSpace(type)) query = query.Where(a => a.Type == type);
            if (roomId.HasValue) query = query.Where(a => a.RoomId == roomId.Value);

            var assets = await query.OrderBy(a => a.Id).ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ID,Hospital Tag,Maintenance Team,Serial Number,Brand,Model,Type,Status,Room Location,Custodian,Custody Type,Supplier,Price,Buying Date,Warranty Expiration,Needs Periodic,Next Periodic");

            foreach (var a in assets)
            {
                sb.AppendLine($"\"{a.Id}\",\"{EscapeCsv(a.HospitalNo)}\",\"{EscapeCsv(a.MaintenanceTeam)}\",\"{EscapeCsv(a.SerialNo)}\",\"{EscapeCsv(a.Brand)}\",\"{a.ModelNo}\",\"{EscapeCsv(a.Type)}\",\"{EscapeCsv(a.Status)}\",\"{EscapeCsv(a.Room?.DisplayName)}\",\"{EscapeCsv(a.User?.FullName)}\",\"{EscapeCsv(a.CustodyType?.Type)}\",\"{EscapeCsv(a.Supplier?.Name)}\",\"{a.Price:F2}\",\"{a.BuyingDate:yyyy-MM-dd}\",\"{a.WarrantyTill:yyyy-MM-dd}\",\"{(a.NeedsPeriodic == true ? "Yes" : "No")}\",\"{a.NextPeriodic:yyyy-MM-dd}\"");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv", $"Assets_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        private async Task PopulateDropdowns(Asset? asset = null)
        {
            ViewBag.Users = new SelectList(await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName", asset?.UserId);
            ViewBag.Rooms = new SelectList(await _context.Rooms.OrderBy(r => r.Floor).ThenBy(r => r.RoomNo).ToListAsync(), "Id", "DisplayName", asset?.RoomId);
            ViewBag.Suppliers = new SelectList(await _context.Suppliers.Where(s => s.IsActive == true).OrderBy(s => s.Name).ToListAsync(), "Id", "Name", asset?.SupplierId);
            ViewBag.CustodyTypes = new SelectList(await _context.CustodyTypes.ToListAsync(), "Id", "Type", asset?.CustodyTypeId);
            ViewBag.MaintenanceTeams = new SelectList(MaintenanceTeams.All, asset?.MaintenanceTeam ?? MaintenanceTeams.IT);
        }
    }
}
