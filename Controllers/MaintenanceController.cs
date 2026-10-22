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
    public class MaintenanceController : Controller
    {
        private readonly StockDbContext _context;

        public MaintenanceController(StockDbContext context)
        {
            _context = context;
        }

        // GET: Maintenance
        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now.Date;
            var thirtyDaysFromNow = now.AddDays(30);

            var periodicAssets = await _context.Assets
                .Where(a => a.NeedsPeriodic == true)
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .ToListAsync();

            var recentLogs = await _context.AssetMaintenances
                .Include(m => m.Asset)
                .Include(m => m.Technician)
                .Include(m => m.Supplier)
                .Include(m => m.Ticket)
                .OrderByDescending(m => m.TimStamp)
                .Take(25)
                .ToListAsync();

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

                RecentLogs = recentLogs
            };

            return View(vm);
        }

        // GET: Maintenance/Log
        public async Task<IActionResult> Log(int? assetId)
        {
            await PopulateDropdowns(assetId);

            var model = new AssetMaintenance
            {
                AssetId = assetId,
                MaintenanceType = 2, // Periodic by default
                TimStamp = DateTime.Now
            };

            return View(model);
        }

        // POST: Maintenance/Log
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Log(AssetMaintenance maintenance)
        {
            if (ModelState.IsValid)
            {
                maintenance.TimStamp = DateTime.Now;
                _context.AssetMaintenances.Add(maintenance);

                // Update asset periodic schedule if applicable
                if (maintenance.AssetId.HasValue)
                {
                    var asset = await _context.Assets.FindAsync(maintenance.AssetId.Value);
                    if (asset != null)
                    {
                        if (maintenance.MaintenanceType == 2 && asset.PeriodicInterval.HasValue)
                        {
                            asset.LastPeriodic = DateTime.Now;
                            asset.NextPeriodic = DateTime.Now.AddDays(asset.PeriodicInterval.Value);
                        }
                        _context.Update(asset);
                    }
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Maintenance work order logged successfully!";
                return RedirectToAction(nameof(Index));
            }

            await PopulateDropdowns(maintenance.AssetId);
            return View(maintenance);
        }

        private async Task PopulateDropdowns(int? selectedAssetId = null)
        {
            var assets = await _context.Assets
                .OrderBy(a => a.HospitalNo)
                .Select(a => new { a.Id, Display = $"{a.HospitalNo} - {a.Brand} ({a.Type})" })
                .ToListAsync();

            ViewBag.Assets = new SelectList(assets, "Id", "Display", selectedAssetId);
            ViewBag.Technicians = new SelectList(await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync(), "Id", "FullName");
            ViewBag.Suppliers = new SelectList(await _context.Suppliers.Where(s => s.IsActive == true).OrderBy(s => s.Name).ToListAsync(), "Id", "Name");
            ViewBag.Tickets = new SelectList(await _context.Tickets.OrderByDescending(t => t.Id).Select(t => new { t.Id, Display = $"Ticket #{t.Id} ({t.Type})" }).ToListAsync(), "Id", "Display");
        }
    }
}
