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
            var path = context.Request.Path.Value?.ToLower() ?? "";

            // 1. Bypass authentication check for static assets and public/error endpoints
            if (path.StartsWith("/css") ||
                path.StartsWith("/js") ||
                path.StartsWith("/lib") ||
                path.StartsWith("/images") ||
                path.StartsWith("/favicon.ico") ||
                path.StartsWith("/account/accessdenied") ||
                path.StartsWith("/account/switchuser"))
            {
                await _next(context);
                return;
            }

            // 2. Check if user has an explicit active session switch (for testing identities)
            var sessionUserId = context.Session.GetInt32("ActiveUserId");
            User? matchedUser = null;

            if (sessionUserId.HasValue)
            {
                matchedUser = await dbContext.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u => u.Id == sessionUserId.Value);

                // If switched user was deactivated or deleted, clear the session
                if (matchedUser != null && matchedUser.IsActive != true)
                {
                    context.Session.Remove("ActiveUserId");
                    matchedUser = null;
                }
            }

            // 3. Auto-detect domain/PC user if no manual session override
            string cleanUsername = "";
            if (matchedUser == null)
            {
                string? rawDomainUser = context.User.Identity?.Name;

                if (string.IsNullOrWhiteSpace(rawDomainUser))
                {
                    rawDomainUser = context.Request.Headers["X-Remote-User"].FirstOrDefault()
                        ?? context.Request.Headers["LOGON_USER"].FirstOrDefault();
                }

                if (string.IsNullOrWhiteSpace(rawDomainUser))
                {
                    // Intranet Windows fallback (e.g. PC username "Abbas")
                    rawDomainUser = Environment.UserName;
                }

                cleanUsername = rawDomainUser ?? "Guest";
                if (cleanUsername.Contains('\\'))
                {
                    cleanUsername = cleanUsername.Split('\\').Last();
                }

                // Match against users table in database by DomainUser or FullName
                matchedUser = await dbContext.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u =>
                        (u.DomainUser != null && u.DomainUser.ToLower() == cleanUsername.ToLower()) ||
                        (u.DomainUser != null && rawDomainUser != null && u.DomainUser.ToLower() == rawDomainUser.ToLower()) ||
                        (u.FullName != null && u.FullName.ToLower() == cleanUsername.ToLower()) ||
                        (u.FullName != null && u.FullName.ToLower().StartsWith(cleanUsername.ToLower()))
                    );
            }
            else
            {
                cleanUsername = matchedUser.DomainUser ?? matchedUser.FullName ?? "User";
            }

            // 4. REJECTION CHECK: User not found in database
            if (matchedUser == null)
            {
                context.Response.Redirect($"/Account/AccessDenied?reason=NotFound&username={Uri.EscapeDataString(cleanUsername)}");
                return;
            }

            // 5. REJECTION CHECK: User found but inactive
            if (matchedUser.IsActive != true)
            {
                context.Response.Redirect($"/Account/AccessDenied?reason=Inactive&username={Uri.EscapeDataString(cleanUsername)}");
                return;
            }

            // 6. AUTO LOGIN: User is active and authorized
            context.Items["CurrentUser"] = matchedUser;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, matchedUser.Id.ToString()),
                new Claim(ClaimTypes.Name, matchedUser.FullName ?? matchedUser.DomainUser ?? "User"),
                new Claim(ClaimTypes.Role, matchedUser.Role ?? "Staff"),
                new Claim("DomainUser", matchedUser.DomainUser ?? cleanUsername),
                new Claim("Department", matchedUser.Department?.Name ?? "General")
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            context.User = new ClaimsPrincipal(identity);

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
