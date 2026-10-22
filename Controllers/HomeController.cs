using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.ViewModels;

namespace StockPortal.Controllers
{
    public class HomeController : Controller
    {
        private readonly StockDbContext _context;

        public HomeController(StockDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var now = DateTime.Now.Date;
            var thirtyDaysFromNow = now.AddDays(30);

            var assets = await _context.Assets
                .Include(a => a.User)
                .Include(a => a.Room)
                .Include(a => a.Supplier)
                .ToListAsync();

            var tickets = await _context.Tickets
                .Include(t => t.Asset)
                .Include(t => t.OpenedByUser)
                .Include(t => t.Status)
                .Include(t => t.Handler).ThenInclude(h => h!.TechUser)
                .OrderByDescending(t => t.OpenedDT)
                .ToListAsync();

            var recentMaintenance = await _context.AssetMaintenances
                .Include(m => m.Asset)
                .Include(m => m.Technician)
                .Include(m => m.Supplier)
                .OrderByDescending(m => m.TimStamp)
                .Take(5)
                .ToListAsync();

            var vm = new DashboardViewModel
            {
                TotalAssets = assets.Count,
                InServiceAssets = assets.Count(a => a.Status == "In Service"),
                InStockAssets = assets.Count(a => a.Status == "In Stock"),
                UnderMaintenanceAssets = assets.Count(a => a.Status == "Under Maintenance"),
                TotalInventoryValue = assets.Sum(a => a.Price ?? 0),

                TotalTickets = tickets.Count,
                OpenTickets = tickets.Count(t => t.StatusId == 1),
                PendingTickets = tickets.Count(t => t.StatusId == 2),
                DelayedTickets = tickets.Count(t => t.StatusId == 3),
                ClosedTickets = tickets.Count(t => t.StatusId == 5),

                UrgentTickets = tickets.Where(t => t.Priority >= 3 && t.StatusId != 5 && t.StatusId != 4)
                    .OrderByDescending(t => t.Priority)
                    .Take(5)
                    .ToList(),

                RecentTickets = tickets.Take(6).ToList(),

                OverduePeriodicAssets = assets
                    .Where(a => a.NeedsPeriodic == true && a.NextPeriodic.HasValue && a.NextPeriodic.Value < now)
                    .OrderBy(a => a.NextPeriodic)
                    .ToList(),

                UpcomingPeriodicAssets = assets
                    .Where(a => a.NeedsPeriodic == true && a.NextPeriodic.HasValue && a.NextPeriodic.Value >= now && a.NextPeriodic.Value <= thirtyDaysFromNow)
                    .OrderBy(a => a.NextPeriodic)
                    .ToList(),

                RecentMaintenance = recentMaintenance
            };

            return View(vm);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
