using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StockPortal.Models;

namespace StockPortal.Services
{
    public interface ICurrentUserService
    {
        User? GetCurrentUser();
        int GetUserId();
        string GetFullName();
        string GetDomainUser();
        string GetRole();
        string? GetMaintenanceTeam();
        string GetDepartmentName();
        bool IsAuthenticated();

        // RBAC Role Helpers
        bool IsAdmin();
        bool IsMaintenance();
        bool IsEndUser();

        // Resource Authorization Guards
        bool CanAccessTicket(Ticket ticket);
        bool CanManageTicket(Ticket ticket);
        bool CanAccessAsset(Asset asset);
        bool CanManageAssets();
        bool CanManageUsers();
    }

    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public User? GetCurrentUser()
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;

            if (context.Items.TryGetValue("CurrentUser", out var userObj) && userObj is User user)
            {
                return user;
            }

            return null;
        }

        public int GetUserId()
        {
            var user = GetCurrentUser();
            if (user != null) return user.Id;

            var claim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier);
            if (claim != null && int.TryParse(claim.Value, out int id))
            {
                return id;
            }

            return 0; // Unauthenticated / no active user
        }

        public string GetFullName()
        {
            var user = GetCurrentUser();
            return user?.FullName ?? _httpContextAccessor.HttpContext?.User.Identity?.Name ?? "Domain User";
        }

        public string GetDomainUser()
        {
            var user = GetCurrentUser();
            return user?.DomainUser ?? Environment.UserName;
        }

        public string GetRole()
        {
            var user = GetCurrentUser();
            if (user != null && !string.IsNullOrWhiteSpace(user.Role))
            {
                return user.Role.Trim().ToUpperInvariant();
            }

            var claim = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Role);
            return claim?.Value?.Trim().ToUpperInvariant() ?? SystemRoles.EndUser;
        }

        public string? GetMaintenanceTeam()
        {
            var user = GetCurrentUser();
            if (user != null)
            {
                return user.MaintenanceTeam?.Trim().ToUpperInvariant();
            }

            var claim = _httpContextAccessor.HttpContext?.User.FindFirst("MaintenanceTeam");
            return claim?.Value?.Trim().ToUpperInvariant();
        }

        public string GetDepartmentName()
        {
            var user = GetCurrentUser();
            return user?.Department?.Name ?? "General";
        }

        public bool IsAuthenticated()
        {
            return GetCurrentUser() != null || (_httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false);
        }

        public bool IsAdmin()
        {
            return string.Equals(GetRole(), SystemRoles.Admin, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsMaintenance()
        {
            return string.Equals(GetRole(), SystemRoles.Maintenance, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsEndUser()
        {
            return !IsAdmin() && !IsMaintenance();
        }

        public bool CanAccessTicket(Ticket ticket)
        {
            if (ticket == null) return false;
            if (IsAdmin()) return true;

            int currentUserId = GetUserId();

            if (IsMaintenance())
            {
                var myTeam = GetMaintenanceTeam();
                // Maintenance users access only tickets routed to their designated maintenance team
                return string.Equals(ticket.TargetTeam, myTeam, StringComparison.OrdinalIgnoreCase);
            }

            // End Users can view only tickets they authored
            return ticket.OpenedBy == currentUserId || ticket.OnBehalfOf == currentUserId;
        }

        public bool CanManageTicket(Ticket ticket)
        {
            if (ticket == null) return false;
            if (IsAdmin()) return true;

            if (IsMaintenance())
            {
                var myTeam = GetMaintenanceTeam();
                return string.Equals(ticket.TargetTeam, myTeam, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public bool CanAccessAsset(Asset asset)
        {
            if (asset == null) return false;
            if (IsAdmin()) return true;

            if (IsMaintenance())
            {
                var myTeam = GetMaintenanceTeam();
                return string.Equals(asset.MaintenanceTeam, myTeam, StringComparison.OrdinalIgnoreCase);
            }

            // End Users view only assets currently in their custody
            return asset.UserId == GetUserId();
        }

        public bool CanManageAssets()
        {
            return IsAdmin();
        }

        public bool CanManageUsers()
        {
            return IsAdmin();
        }
    }
}
