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
        string GetDepartmentName();
        bool IsAuthenticated();
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

            return 1; // Default to lead admin
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
            return user?.Role ?? "User";
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
    }
}
