using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Services;

namespace StockPortal.Controllers
{
    public class AccountController : Controller
    {
        private readonly StockDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public AccountController(StockDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        // GET: Account/SwitchUser?id=2
        public async Task<IActionResult> SwitchUser(int id, string? returnUrl)
        {
            if (id > 0)
            {
                var user = await _context.Users.FindAsync(id);
                if (user != null)
                {
                    HttpContext.Session.SetInt32("ActiveUserId", user.Id);
                    TempData["SuccessMessage"] = $"Switched profile to: {user.FullName} ({user.Role})";
                }
            }
            else
            {
                // Revert to detected domain user
                HttpContext.Session.Remove("ActiveUserId");
                TempData["SuccessMessage"] = $"Reverted to Windows Domain identity ({Environment.UserName}).";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        // GET: Account/Profile
        public async Task<IActionResult> Profile()
        {
            var user = _currentUserService.GetCurrentUser();
            if (user == null)
            {
                return RedirectToAction("Index", "Home");
            }

            var myAssets = await _context.Assets
                .Where(a => a.UserId == user.Id)
                .Include(a => a.Room)
                .Include(a => a.CustodyType)
                .ToListAsync();

            var myTickets = await _context.Tickets
                .Where(t => t.OpenedBy == user.Id || t.OnBehalfOf == user.Id)
                .Include(t => t.Asset)
                .Include(t => t.Status)
                .OrderByDescending(t => t.OpenedDT)
                .ToListAsync();

            ViewBag.MyAssets = myAssets;
            ViewBag.MyTickets = myTickets;
            ViewBag.AllUsers = await _context.Users.Where(u => u.IsActive == true).OrderBy(u => u.FullName).ToListAsync();

            return View(user);
        }
    }
}
