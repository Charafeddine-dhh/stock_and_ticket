using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Models;
using StockPortal.Services;

namespace StockPortal.Controllers
{
    public class DeviceFormsController : Controller
    {
        private readonly StockDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public DeviceFormsController(StockDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        // GET: DeviceForms
        public async Task<IActionResult> Index()
        {
            if (!_currentUserService.CanManageDeviceForms())
            {
                return Forbid();
            }

            var query = _context.DeviceForms.AsQueryable();

            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                query = query.Where(f => f.Department == myTeam);
            }

            var forms = await query.OrderBy(f => f.Department).ThenBy(f => f.Name).ToListAsync();
            return View(forms);
        }

        // GET: DeviceForms/Create
        public IActionResult Create()
        {
            if (!_currentUserService.CanManageDeviceForms())
            {
                return Forbid();
            }

            var defaultDept = _currentUserService.IsMaintenance()
                ? (_currentUserService.GetMaintenanceTeam() ?? MaintenanceTeams.IT)
                : MaintenanceTeams.IT;

            var model = new DeviceForm
            {
                Department = defaultDept,
                Form = "[]"
            };

            ViewBag.AvailableTeams = MaintenanceTeams.All;
            return View(model);
        }

        // POST: DeviceForms/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DeviceForm deviceForm)
        {
            if (!_currentUserService.CanManageDeviceForms())
            {
                return Forbid();
            }

            // Maintenance users can only create forms for their assigned maintenance department
            if (_currentUserService.IsMaintenance())
            {
                deviceForm.Department = _currentUserService.GetMaintenanceTeam() ?? MaintenanceTeams.IT;
            }

            if (string.IsNullOrWhiteSpace(deviceForm.Name))
            {
                ModelState.AddModelError("Name", "Device Name / Type is required.");
            }

            // Validate duplicate name within same department
            if (await _context.DeviceForms.AnyAsync(f => f.Name.ToLower() == deviceForm.Name.Trim().ToLower() && f.Department == deviceForm.Department))
            {
                ModelState.AddModelError("Name", $"A device form for '{deviceForm.Name}' already exists in the {deviceForm.Department} department.");
            }

            // Ensure form JSON is valid
            if (string.IsNullOrWhiteSpace(deviceForm.Form) || deviceForm.Form == "[]")
            {
                ModelState.AddModelError("Form", "Please add at least one field to this device specification form.");
            }

            if (ModelState.IsValid)
            {
                deviceForm.Name = deviceForm.Name.Trim();
                _context.DeviceForms.Add(deviceForm);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Device Form for '{deviceForm.Name}' ({deviceForm.Department}) created successfully!";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.AvailableTeams = MaintenanceTeams.All;
            return View(deviceForm);
        }

        // GET: DeviceForms/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            if (!_currentUserService.CanManageDeviceForms()) return Forbid();

            var form = await _context.DeviceForms.FindAsync(id.Value);
            if (form == null) return NotFound();

            // RBAC Guard: URL tampering prevention
            if (!_currentUserService.CanManageDeviceForm(form))
            {
                return Forbid();
            }

            ViewBag.AvailableTeams = MaintenanceTeams.All;
            return View(form);
        }

        // POST: DeviceForms/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DeviceForm deviceForm)
        {
            if (id != deviceForm.Id) return NotFound();
            if (!_currentUserService.CanManageDeviceForms()) return Forbid();

            var existing = await _context.DeviceForms.FindAsync(id);
            if (existing == null) return NotFound();

            // RBAC Guard: URL tampering prevention
            if (!_currentUserService.CanManageDeviceForm(existing))
            {
                return Forbid();
            }

            // Maintenance technicians cannot transfer forms across departments
            if (_currentUserService.IsMaintenance())
            {
                deviceForm.Department = existing.Department;
            }

            if (string.IsNullOrWhiteSpace(deviceForm.Name))
            {
                ModelState.AddModelError("Name", "Device Name / Type is required.");
            }

            if (await _context.DeviceForms.AnyAsync(f => f.Name.ToLower() == deviceForm.Name.Trim().ToLower() && f.Department == deviceForm.Department && f.Id != id))
            {
                ModelState.AddModelError("Name", $"Another device form named '{deviceForm.Name}' already exists in {deviceForm.Department}.");
            }

            if (ModelState.IsValid)
            {
                existing.Name = deviceForm.Name.Trim();
                existing.Department = deviceForm.Department;
                existing.Form = deviceForm.Form;

                _context.Update(existing);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Device Form '{existing.Name}' updated successfully!";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.AvailableTeams = MaintenanceTeams.All;
            return View(deviceForm);
        }

        // POST: DeviceForms/Delete/5 (POST only to prevent URL bar tampering)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!_currentUserService.CanManageDeviceForms()) return Forbid();

            var form = await _context.DeviceForms.FindAsync(id);
            if (form == null) return NotFound();

            if (!_currentUserService.CanManageDeviceForm(form))
            {
                return Forbid();
            }

            // Check if any registered assets are currently using this form/type
            bool inUse = await _context.Assets.AnyAsync(a => a.Type == form.Name && a.MaintenanceTeam == form.Department);
            if (inUse)
            {
                TempData["ErrorMessage"] = $"Cannot delete form '{form.Name}' because active hospital assets are currently registered under this device type.";
                return RedirectToAction(nameof(Index));
            }

            _context.DeviceForms.Remove(form);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Device template '{form.Name}' was deleted.";
            return RedirectToAction(nameof(Index));
        }

        // GET: DeviceForms/GetSchema/5 (API for dynamic client-side form rendering)
        [HttpGet]
        public async Task<IActionResult> GetSchema(int id)
        {
            var form = await _context.DeviceForms.FindAsync(id);
            if (form == null) return NotFound();

            // Allow if user has access to this department
            if (_currentUserService.IsMaintenance())
            {
                var myTeam = _currentUserService.GetMaintenanceTeam();
                if (!string.Equals(form.Department, myTeam, StringComparison.OrdinalIgnoreCase))
                {
                    return Forbid();
                }
            }

            return Json(new
            {
                id = form.Id,
                name = form.Name,
                department = form.Department,
                fields = form.Fields
            });
        }
    }
}
