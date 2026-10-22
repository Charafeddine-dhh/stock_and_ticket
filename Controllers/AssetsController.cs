using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Models;
using StockPortal.ViewModels;

namespace StockPortal.Controllers
{
    public class AssetsController : Controller
    {
        private readonly StockDbContext _context;

        public AssetsController(StockDbContext context)
        {
            _context = context;
        }

        // GET: Assets
        public async Task<IActionResult> Index(string? search, string? status, string? type, int? roomId)
        {
            var query = _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .Include(a => a.CustodyType)
                .AsQueryable();

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

            ViewBag.Search = search;
            ViewBag.SelectedStatus = status;
            ViewBag.SelectedType = type;
            ViewBag.SelectedRoomId = roomId;

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
            await PopulateDropdowns();
            return View(new Asset
            {
                Status = "In Service",
                BuyingDate = DateTime.Now,
                NeedsPeriodic = false
            });
        }

        // POST: Assets/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Asset asset)
        {
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
            if (id != asset.Id) return NotFound();

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

        private async Task PopulateDropdowns(Asset? asset = null)
        {
            ViewBag.Users = new SelectList(await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName", asset?.UserId);
            ViewBag.Rooms = new SelectList(await _context.Rooms.OrderBy(r => r.Floor).ThenBy(r => r.RoomNo).ToListAsync(), "Id", "DisplayName", asset?.RoomId);
            ViewBag.Suppliers = new SelectList(await _context.Suppliers.Where(s => s.IsActive == true).OrderBy(s => s.Name).ToListAsync(), "Id", "Name", asset?.SupplierId);
            ViewBag.CustodyTypes = new SelectList(await _context.CustodyTypes.ToListAsync(), "Id", "Type", asset?.CustodyTypeId);
        }
    }
}
