using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Models;

namespace StockPortal.Controllers
{
    public class DirectoryController : Controller
    {
        private readonly StockDbContext _context;

        public DirectoryController(StockDbContext context)
        {
            _context = context;
        }

        // GET: Directory
        public async Task<IActionResult> Index()
        {
            ViewBag.Users = await _context.Users.Include(u => u.Department).OrderBy(u => u.FullName).ToListAsync();
            ViewBag.Rooms = await _context.Rooms.OrderBy(r => r.Floor).ThenBy(r => r.RoomNo).ToListAsync();
            ViewBag.Suppliers = await _context.Suppliers.OrderBy(s => s.Name).ToListAsync();
            ViewBag.Departments = await _context.Departments.Include(d => d.HeadUser).OrderBy(d => d.Name).ToListAsync();

            return View();
        }

        // POST: Directory/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(User user)
        {
            if (!string.IsNullOrWhiteSpace(user.FullName))
            {
                user.IsActive = true;
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"User {user.FullName} added successfully.";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Directory/CreateRoom
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRoom(Room room)
        {
            if (room.RoomNo.HasValue && room.Floor.HasValue)
            {
                _context.Rooms.Add(room);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Room {room.RoomNo} (Floor {room.Floor}) created.";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Directory/CreateSupplier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSupplier(Supplier supplier)
        {
            if (!string.IsNullOrWhiteSpace(supplier.Name))
            {
                supplier.IsActive = true;
                _context.Suppliers.Add(supplier);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Supplier {supplier.Name} created.";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Directory/CreateDepartment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateDepartment(Department department)
        {
            if (!string.IsNullOrWhiteSpace(department.Name))
            {
                _context.Departments.Add(department);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Department {department.Name} created.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
