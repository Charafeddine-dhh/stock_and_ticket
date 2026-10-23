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

            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                periodicAssetsQuery = periodicAssetsQuery.Where(a => a.MaintenanceTeam == myTeam);
                logsQuery = logsQuery.Where(m => m.Asset != null && m.Asset.MaintenanceTeam == myTeam);
            }

            var periodicAssets = await periodicAssetsQuery.ToListAsync();
            var recentLogs = await logsQuery.OrderByDescending(m => m.TimStamp).Take(50).ToListAsync();

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
            if (_currentUserService.IsEndUser()) return Forbid();

            if (assetId.HasValue && assetId.Value > 0)
            {
                var targetAsset = await _context.Assets.FindAsync(assetId.Value);
                if (targetAsset != null && !_currentUserService.CanAccessAsset(targetAsset))
                {
                    return Forbid();
                }
            }

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
            if (_currentUserService.IsEndUser()) return Forbid();

            if (maintenance.AssetId.HasValue)
            {
                var targetAsset = await _context.Assets.FindAsync(maintenance.AssetId.Value);
                if (targetAsset != null && !_currentUserService.CanManageAsset(targetAsset))
                {
                    return Forbid();
                }
            }

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
                TimStamp = DateTime.Now
            };

            _context.AssetMaintenances.Add(record);

            if (asset.PeriodicInterval.HasValue)
            {
                asset.LastPeriodic = DateTime.Now;
                asset.NextPeriodic = DateTime.Now.AddDays(asset.PeriodicInterval.Value);
                _context.Update(asset);
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Periodic check completed for asset {asset.HospitalNo ?? ("#" + asset.Id)}.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Maintenance/ExportCsv
        public async Task<IActionResult> ExportCsv()
        {
            var logs = await _context.AssetMaintenances
                .Include(m => m.Asset)
                .Include(m => m.Technician)
                .Include(m => m.Supplier)
                .Include(m => m.Ticket)
                .OrderByDescending(m => m.TimStamp)
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("ID,Date,Asset Tag,Asset Brand,Maintenance Type,Technician,Supplier,Ticket ID,Description,Parts Replaced");

            foreach (var m in logs)
            {
                sb.AppendLine($"\"{m.Id}\",\"{m.TimStamp:yyyy-MM-dd HH:mm}\",\"{EscapeCsv(m.Asset?.HospitalNo)}\",\"{EscapeCsv(m.Asset?.Brand)}\",\"{EscapeCsv(m.TypeName)}\",\"{EscapeCsv(m.Technician?.FullName)}\",\"{EscapeCsv(m.Supplier?.Name)}\",\"{(m.TicketId.HasValue ? "#" + m.TicketId.Value : "Direct")}\",\"{EscapeCsv(m.Description)}\",\"{EscapeCsv(m.PartsReplaced)}\"");
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            return File(bytes, "text/csv", $"Maintenance_Logs_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv");
        }

        private static string EscapeCsv(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        private async Task PopulateDropdowns(int? selectedAssetId = null)
        {
            var assetsQuery = _context.Assets.AsQueryable();
            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                assetsQuery = assetsQuery.Where(a => a.MaintenanceTeam == myTeam);
            }

            var assets = await assetsQuery
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
