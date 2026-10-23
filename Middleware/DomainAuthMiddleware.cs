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

            // 1. Bypass authentication check for static assets and the AccessDenied rejection page & public ticket submission
            if (path.StartsWith("/css") ||
                path.StartsWith("/js") ||
                path.StartsWith("/lib") ||
                path.StartsWith("/images") ||
                path.StartsWith("/favicon.ico") ||
                path.StartsWith("/account/accessdenied") ||
                path.StartsWith("/account/submitaccessticket"))
            {
                await _next(context);
                return;
            }

            // 2. Resolve Physical Workstation / PC Username
            // Check in strict order from network/reverse proxy/environment (NEVER cached cookie identity):
            string? rawUser = context.Request.Headers["LOGON_USER"].FirstOrDefault()
                ?? context.Request.Headers["REMOTE_USER"].FirstOrDefault()
                ?? context.Request.Headers["X-Remote-User"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(rawUser))
            {
                rawUser = Environment.UserName;
            }

            // Normalize PC username: strip DOMAIN\ or user@domain
            string cleanUsername = rawUser ?? "";
            if (cleanUsername.Contains('\\'))
            {
                cleanUsername = cleanUsername.Split('\\').Last();
            }
            if (cleanUsername.Contains('@'))
            {
                cleanUsername = cleanUsername.Split('@').First();
            }
            cleanUsername = cleanUsername.Trim();

            // If no username can be resolved at all, reject
            if (string.IsNullOrWhiteSpace(cleanUsername))
            {
                context.Session.Clear();
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Cookies.Delete("StockPortal.DomainAuth");
                context.Response.Redirect("/Account/AccessDenied?reason=NotFound&username=Unknown");
                return;
            }

            // 3. Query database strictly for the physical PC user first
            var search = cleanUsername.ToLower();
            var rawSearch = rawUser?.ToLower();

            var pcUser = await dbContext.Users
                .Include(u => u.Department)
                .FirstOrDefaultAsync(u =>
                    (u.DomainUser != null && (u.DomainUser.ToLower() == search || (rawSearch != null && u.DomainUser.ToLower() == rawSearch))) ||
                    (u.FullName != null && u.FullName.ToLower() == search)
                );
            // 4. REJECTION CHECK 1: Physical PC user not registered in database
            if (pcUser == null)
            {
                context.Session.Clear();
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Cookies.Delete("StockPortal.DomainAuth");
                context.Response.Redirect($"/Account/AccessDenied?reason=NotFound&username={Uri.EscapeDataString(cleanUsername)}");
                return;
            }

            // 5. REJECTION CHECK 2: Physical PC user is INACTIVE (isActive == 0 / false)
            if (pcUser.IsActive != true)
            {
                context.Session.Clear();
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Cookies.Delete("StockPortal.DomainAuth");
                context.Response.Redirect($"/Account/AccessDenied?reason=Inactive&username={Uri.EscapeDataString(cleanUsername)}");
                return;
            }

            // 6. Physical user is ACTIVE. Check optional session identity switch (testing feature)
            User matchedUser = pcUser;
            var sessionUserId = context.Session.GetInt32("ActiveUserId");

            if (sessionUserId.HasValue)
            {
                var switchedUser = await dbContext.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u => u.Id == sessionUserId.Value);

                if (switchedUser != null && switchedUser.IsActive == true)
                {
                    matchedUser = switchedUser;
                }
                else
                {
                    context.Session.Remove("ActiveUserId");
                }
            }

            // 7. AUTO-LOGIN: User found and ACTIVE!
            context.Items["CurrentUser"] = matchedUser;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, matchedUser.Id.ToString()),
                new Claim(ClaimTypes.Name, matchedUser.FullName ?? matchedUser.DomainUser ?? cleanUsername),
                new Claim(ClaimTypes.Role, matchedUser.Role ?? SystemRoles.EndUser),
                new Claim("DomainUser", matchedUser.DomainUser ?? cleanUsername),
                new Claim("Department", matchedUser.Department?.Name ?? "General")
            };

            if (!string.IsNullOrWhiteSpace(matchedUser.MaintenanceTeam))
            {
                claims.Add(new Claim("MaintenanceTeam", matchedUser.MaintenanceTeam));
            }

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
