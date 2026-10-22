using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using StockPortal.Data;
using StockPortal.Models;

namespace StockPortal.Middleware
{
    public class DomainAuthMiddleware
    {
        private readonly RequestDelegate _next;

        public DomainAuthMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, StockDbContext dbContext)
        {
            // 1. Check if user has an explicit active session switch (e.g. testing different roles)
            var sessionUserId = context.Session.GetInt32("ActiveUserId");
            User? matchedUser = null;

            if (sessionUserId.HasValue)
            {
                matchedUser = await dbContext.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u => u.Id == sessionUserId.Value);
            }

            // 2. If no manual session override, detect domain user automatically
            if (matchedUser == null)
            {
                // Try getting domain username from Windows Identity, headers, or Environment
                string? rawDomainUser = context.User.Identity?.Name;

                if (string.IsNullOrWhiteSpace(rawDomainUser))
                {
                    rawDomainUser = context.Request.Headers["X-Remote-User"].FirstOrDefault()
                        ?? context.Request.Headers["LOGON_USER"].FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(rawDomainUser))
                {
                    // Local / Intranet Windows fallback (e.g. "Abbas")
                    rawDomainUser = Environment.UserName;
                }

                string cleanUsername = rawDomainUser ?? "Guest";
                if (cleanUsername.Contains('\\'))
                {
                    cleanUsername = cleanUsername.Split('\\').Last();
                }

                // Match against users table in database by domainUser or fullName
                matchedUser = await dbContext.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u =>
                        (u.DomainUser != null && u.DomainUser.ToLower() == cleanUsername.ToLower()) ||
                        (u.DomainUser != null && rawDomainUser != null && u.DomainUser.ToLower() == rawDomainUser.ToLower()) ||
                        (u.FullName != null && u.FullName.ToLower() == cleanUsername.ToLower())
                    );

                // Auto-provision if user does not exist yet in database
                if (matchedUser == null && !string.IsNullOrWhiteSpace(cleanUsername))
                {
                    var defaultDept = await dbContext.Departments.FirstOrDefaultAsync();
                    matchedUser = new User
                    {
                        FullName = cleanUsername,
                        DomainUser = cleanUsername,
                        Role = "Staff",
                        DepartmentId = defaultDept?.Id,
                        IsActive = true
                    };
                    dbContext.Users.Add(matchedUser);
                    await dbContext.SaveChangesAsync();
                }
            }

            // 3. Set Claims and store user in Context
            if (matchedUser != null)
            {
                context.Items["CurrentUser"] = matchedUser;

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, matchedUser.Id.ToString()),
                    new Claim(ClaimTypes.Name, matchedUser.FullName ?? matchedUser.DomainUser ?? "User"),
                    new Claim(ClaimTypes.Role, matchedUser.Role ?? "Staff"),
                    new Claim("DomainUser", matchedUser.DomainUser ?? Environment.UserName),
                    new Claim("Department", matchedUser.Department?.Name ?? "General")
                };

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                context.User = new ClaimsPrincipal(identity);
            }

            await _next(context);
        }
    }

    public static class DomainAuthMiddlewareExtensions
    {
        public static IApplicationBuilder UseDomainAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<DomainAuthMiddleware>();
        }
    }
}
