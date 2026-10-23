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

            // 1. Bypass authentication check for static assets and the AccessDenied rejection page
            if (path.StartsWith("/css") ||
                path.StartsWith("/js") ||
                path.StartsWith("/lib") ||
                path.StartsWith("/images") ||
                path.StartsWith("/favicon.ico") ||
                path.StartsWith("/account/accessdenied"))
            {
                await _next(context);
                return;
            }

            // 2. Resolve PC / Windows Username
            // Check in order:
            // a) Windows Authentication / Negotiate identity name
            // b) Reverse proxy / IIS headers (LOGON_USER, REMOTE_USER, X-Remote-User)
            // c) Local desktop workstation username (Environment.UserName)
            string? rawUser = context.User.Identity?.Name;

            if (string.IsNullOrWhiteSpace(rawUser))
            {
                rawUser = context.Request.Headers["LOGON_USER"].FirstOrDefault()
                    ?? context.Request.Headers["REMOTE_USER"].FirstOrDefault()
                    ?? context.Request.Headers["X-Remote-User"].FirstOrDefault();
            }

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
                context.Response.Redirect("/Account/AccessDenied?reason=NotFound&username=Unknown");
                return;
            }

            // 3. Optional session override (allowed only if an already-authenticated user switches role)
            var sessionUserId = context.Session.GetInt32("ActiveUserId");
            User? matchedUser = null;

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
                    // Clear invalid or deactivated override
                    context.Session.Remove("ActiveUserId");
                }
            }

            // 4. Query database for user matching PC username
            if (matchedUser == null)
            {
                var search = cleanUsername.ToLower();

                // Exact match on DomainUser (PC username) or full domain\user
                matchedUser = await dbContext.Users
                    .Include(u => u.Department)
                    .FirstOrDefaultAsync(u =>
                        (u.DomainUser != null && u.DomainUser.ToLower() == search) ||
                        (rawUser != null && u.DomainUser != null && u.DomainUser.ToLower() == rawUser.ToLower()) ||
                        (u.FullName != null && u.FullName.ToLower() == search) ||
                        (u.FullName != null && u.FullName.ToLower().StartsWith(search + " "))
                    );
            }

            // 5. REJECTION CHECK 1: User not found in database
            if (matchedUser == null)
            {
                context.Session.Clear();
                context.Response.Redirect($"/Account/AccessDenied?reason=NotFound&username={Uri.EscapeDataString(cleanUsername)}");
                return;
            }

            // 6. REJECTION CHECK 2: User found, but INACTIVE
            if (matchedUser.IsActive != true)
            {
                context.Session.Clear();
                context.Response.Redirect($"/Account/AccessDenied?reason=Inactive&username={Uri.EscapeDataString(cleanUsername)}");
                return;
            }

            // 7. AUTO-LOGIN: User found and ACTIVE!
            context.Items["CurrentUser"] = matchedUser;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, matchedUser.Id.ToString()),
                new Claim(ClaimTypes.Name, matchedUser.FullName ?? matchedUser.DomainUser ?? cleanUsername),
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
