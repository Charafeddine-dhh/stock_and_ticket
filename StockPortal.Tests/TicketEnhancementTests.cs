using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using StockPortal.Controllers;
using StockPortal.Data;
using StockPortal.Models;
using StockPortal.Services;
using Xunit;

namespace StockPortal.Tests
{
    public class TicketEnhancementTests
    {
        private StockDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<StockDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new StockDbContext(options);
        }

        // ==========================================
        // Task 4: Maintenance Sub-Type Identification Tests
        // ==========================================
        [Theory]
        [InlineData("BIOMEDICAL", "MAINTENANCE (BIOMEDICAL)")]
        [InlineData("IT", "MAINTENANCE (IT)")]
        [InlineData("GENERAL", "MAINTENANCE (GENERAL)")]
        [InlineData(null, "MAINTENANCE (GENERAL)")]
        [InlineData("", "MAINTENANCE (GENERAL)")]
        public void User_RoleDisplay_ShowsSpecificMaintenanceDomain_WhenMaintenance(string? team, string expectedDisplay)
        {
            var user = new User
            {
                FullName = "Test Maintenance Tech",
                Role = SystemRoles.Maintenance,
                MaintenanceTeam = team
            };

            Assert.Equal(expectedDisplay, user.RoleDisplay);
        }

        [Fact]
        public void User_RoleDisplay_ShowsOriginalRole_WhenNotMaintenance()
        {
            var admin = new User { Role = SystemRoles.Admin };
            var endUser = new User { Role = SystemRoles.EndUser };

            Assert.Equal(SystemRoles.Admin, admin.RoleDisplay);
            Assert.Equal(SystemRoles.EndUser, endUser.RoleDisplay);
        }

        // ==========================================
        // Task 2: Department-Scoped Ticket Counter Tests
        // ==========================================
        [Fact]
        public void DepartmentTicketCounter_FiltersByBiomedicalTeam_ForBiomedicalUser()
        {
            using var context = CreateInMemoryDbContext();

            // Seed tickets: 2 Biomedical, 3 IT, 1 General
            context.Tickets.AddRange(
                new Ticket { Id = 1, TargetTeam = MaintenanceTeams.Biomedical, StatusId = 1 }, // Opened
                new Ticket { Id = 2, TargetTeam = MaintenanceTeams.Biomedical, StatusId = 2 }, // Pending
                new Ticket { Id = 3, TargetTeam = MaintenanceTeams.Biomedical, StatusId = 5 }, // Closed (should not count)
                new Ticket { Id = 4, TargetTeam = MaintenanceTeams.IT, StatusId = 1 },
                new Ticket { Id = 5, TargetTeam = MaintenanceTeams.IT, StatusId = 2 },
                new Ticket { Id = 6, TargetTeam = MaintenanceTeams.General, StatusId = 1 }
            );
            context.SaveChanges();

            // Biomedical maintenance user query
            string biomedicalTeam = MaintenanceTeams.Biomedical;
            var openTicketsCount = context.Tickets
                .Where(t => t.StatusId == 1 || t.StatusId == 2)
                .Where(t => t.TargetTeam == biomedicalTeam)
                .Count();

            // Should be exactly 2 biomedical tickets (Tickets 1 & 2), NOT 5 open tickets across hospital
            Assert.Equal(2, openTicketsCount);
        }

        // ==========================================
        // Task 3: Notifications Service Tests
        // ==========================================
        [Fact]
        public async Task NotificationService_BroadcastsToAdminsAndDepartmentUsers_WhenTicketCreated()
        {
            using var context = CreateInMemoryDbContext();

            // Seed Users: 1 Admin, 2 Biomedical Techs, 1 Cardiology EndUser (requester), 1 IT Tech
            var admin = new User { Id = 1, FullName = "Admin User", Role = SystemRoles.Admin, IsActive = true };
            var bioTech1 = new User { Id = 2, FullName = "Bio Tech 1", Role = SystemRoles.Maintenance, MaintenanceTeam = MaintenanceTeams.Biomedical, DepartmentId = 10, IsActive = true };
            var bioTech2 = new User { Id = 3, FullName = "Bio Tech 2", Role = SystemRoles.Maintenance, MaintenanceTeam = MaintenanceTeams.Biomedical, DepartmentId = 10, IsActive = true };
            var requester = new User { Id = 4, FullName = "Dr. Cardio", Role = SystemRoles.EndUser, DepartmentId = 20, IsActive = true };
            var itTech = new User { Id = 5, FullName = "IT Tech", Role = SystemRoles.Maintenance, MaintenanceTeam = MaintenanceTeams.IT, DepartmentId = 30, IsActive = true };

            context.Users.AddRange(admin, bioTech1, bioTech2, requester, itTech);
            await context.SaveChangesAsync();

            var ticket = new Ticket
            {
                Id = 101,
                OpenedBy = requester.Id,
                TargetTeam = MaintenanceTeams.Biomedical,
                Type = "Hardware",
                Priority = 3
            };
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            var notificationService = new NotificationService(context);
            await notificationService.NotifyTicketCreatedAsync(ticket, requester);

            // Verify notifications: Admin and Biomedical Techs should receive notifications, but not the requester or unrelated IT tech
            var adminNotifs = await notificationService.GetUserNotificationsAsync(admin.Id);
            var bio1Notifs = await notificationService.GetUserNotificationsAsync(bioTech1.Id);
            var bio2Notifs = await notificationService.GetUserNotificationsAsync(bioTech2.Id);
            var requesterNotifs = await notificationService.GetUserNotificationsAsync(requester.Id);
            var itNotifs = await notificationService.GetUserNotificationsAsync(itTech.Id);

            Assert.Single(adminNotifs);
            Assert.Contains("Ticket #101 Opened", adminNotifs.First().Title);

            Assert.Single(bio1Notifs);
            Assert.Single(bio2Notifs);

            Assert.Empty(requesterNotifs); // Initiator excluded
            Assert.Empty(itNotifs); // Unrelated team excluded
        }

        [Fact]
        public async Task NotificationService_BroadcastsToDepartmentAndAdmins_WhenMessagePosted()
        {
            using var context = CreateInMemoryDbContext();

            var admin = new User { Id = 1, FullName = "Admin User", Role = SystemRoles.Admin, IsActive = true };
            var bioTech = new User { Id = 2, FullName = "Bio Tech", Role = SystemRoles.Maintenance, MaintenanceTeam = MaintenanceTeams.Biomedical, IsActive = true };
            var requester = new User { Id = 4, FullName = "Requester", Role = SystemRoles.EndUser, DepartmentId = 20, IsActive = true };

            context.Users.AddRange(admin, bioTech, requester);
            await context.SaveChangesAsync();

            var ticket = new Ticket
            {
                Id = 202,
                OpenedBy = requester.Id,
                TargetTeam = MaintenanceTeams.Biomedical
            };
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            var msg = new TicketMsg
            {
                Id = 501,
                TicketId = ticket.Id,
                UserId = requester.Id,
                Msg = "Probe cable is defective, please inspect immediately."
            };
            context.TicketMessages.Add(msg);
            await context.SaveChangesAsync();

            var notificationService = new NotificationService(context);
            await notificationService.NotifyTicketMessageAsync(ticket, msg, requester);

            var adminNotifs = await notificationService.GetUserNotificationsAsync(admin.Id);
            var bioNotifs = await notificationService.GetUserNotificationsAsync(bioTech.Id);
            var requesterNotifs = await notificationService.GetUserNotificationsAsync(requester.Id);

            Assert.Single(adminNotifs);
            Assert.Contains("Probe cable is defective", adminNotifs.First().Message);

            Assert.Single(bioNotifs);
            Assert.Empty(requesterNotifs); // Sender excluded
        }

        [Fact]
        public async Task NotificationService_MarkAsRead_UpdatesReadStatus()
        {
            using var context = CreateInMemoryDbContext();
            var user = new User { Id = 10, FullName = "Test User" };
            context.Users.Add(user);

            var notif = new Notification
            {
                Id = 1,
                UserId = user.Id,
                Title = "Test Update",
                Message = "Important maintenance update",
                IsRead = false
            };
            context.Notifications.Add(notif);
            await context.SaveChangesAsync();

            var notificationService = new NotificationService(context);
            int unreadBefore = await notificationService.GetUnreadCountAsync(user.Id);
            Assert.Equal(1, unreadBefore);

            await notificationService.MarkAsReadAsync(notif.Id, user.Id);

            int unreadAfter = await notificationService.GetUnreadCountAsync(user.Id);
            Assert.Equal(0, unreadAfter);
        }

        // ==========================================
        // Task 1 (Phase 2): Not Closed Status Tab Test
        // ==========================================
        [Fact]
        public async Task TicketsController_Index_StatusIdMinusOne_FiltersNotClosedTickets()
        {
            using var context = CreateInMemoryDbContext();

            context.Tickets.AddRange(
                new Ticket { Id = 101, StatusId = 1, OpenedDT = DateTime.Now }, // Opened
                new Ticket { Id = 102, StatusId = 2, OpenedDT = DateTime.Now }, // Pending
                new Ticket { Id = 103, StatusId = 5, ClosedDT = DateTime.Now, OpenedDT = DateTime.Now.AddDays(-1) }, // Closed
                new Ticket { Id = 104, StatusId = 3, OpenedDT = DateTime.Now } // Delayed
            );
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(1);
            mockUserService.Setup(s => s.IsAdmin()).Returns(true);

            var controller = new TicketsController(context, mockUserService.Object, new NotificationService(context));
            var result = await controller.Index(statusId: -1, priority: null, type: null, search: null, filter: null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<Ticket>>(viewResult.Model);

            // Should contain tickets 101, 102, 104, but NOT closed ticket 103
            Assert.Equal(3, model.Count);
            Assert.DoesNotContain(model, t => t.Id == 103);
            Assert.All(model, t => Assert.True(t.StatusId != 5 && t.ClosedDT == null));
        }

        // ==========================================
        // Task 3 (Phase 2): End-User Team Assignment Tests
        // ==========================================
        [Fact]
        public async Task EndUser_CanAssignTargetTeam_OnTicketCreation()
        {
            using var context = CreateInMemoryDbContext();

            // IT asset
            var itAsset = new Asset { Id = 50, HospitalNo = "PC-50", MaintenanceTeam = MaintenanceTeams.IT };
            context.Assets.Add(itAsset);
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(20);
            mockUserService.Setup(s => s.IsEndUser()).Returns(true);

            var controller = new TicketsController(context, mockUserService.Object, new NotificationService(context));
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

            var ticket = new Ticket
            {
                AssetId = itAsset.Id,
                OpenedBy = 20,
                TargetTeam = MaintenanceTeams.Biomedical, // User explicitly assigned to Biomedical
                Type = "Hardware Breakdown",
                Priority = 2
            };

            await controller.Create(ticket, manualAsset: null, initialMessage: "Medical sensor plugged into PC failed", assignedTechId: null);

            var created = await context.Tickets.FirstOrDefaultAsync(t => t.AssetId == itAsset.Id);
            Assert.NotNull(created);
            // Must preserve user's chosen team (Biomedical), NOT overwrite with asset's team (IT)
            Assert.Equal(MaintenanceTeams.Biomedical, created.TargetTeam);
        }

        [Fact]
        public async Task EndUser_CanReassignTargetTeam_ForOwnTicket()
        {
            using var context = CreateInMemoryDbContext();

            var ticket = new Ticket
            {
                Id = 200,
                OpenedBy = 25,
                TargetTeam = MaintenanceTeams.IT,
                StatusId = 1
            };
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(25);
            mockUserService.Setup(s => s.IsEndUser()).Returns(true);

            var controller = new TicketsController(context, mockUserService.Object, new NotificationService(context));
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

            var result = await controller.AssignTeam(ticket.Id, MaintenanceTeams.General, returnUrl: null);

            var updatedTicket = await context.Tickets.FindAsync(ticket.Id);
            Assert.NotNull(updatedTicket);
            Assert.Equal(MaintenanceTeams.General, updatedTicket.TargetTeam);

            // Verify timeline message was added
            var message = await context.TicketMessages.FirstOrDefaultAsync(m => m.TicketId == ticket.Id);
            Assert.NotNull(message);
            Assert.Contains("Transferred from IT to GENERAL", message.Msg);
        }

        // ==========================================
        // Task 4 (Phase 2): Logged-in Reply Author Test
        // ==========================================
        [Fact]
        public async Task AddMessage_UsesLoggedInUserId_ForMaintenanceUser()
        {
            using var context = CreateInMemoryDbContext();

            var techUser = new User { Id = 55, FullName = "Hasan Outa", Role = SystemRoles.Maintenance, MaintenanceTeam = MaintenanceTeams.General };
            context.Users.Add(techUser);

            var ticket = new Ticket
            {
                Id = 300,
                TargetTeam = MaintenanceTeams.General,
                StatusId = 1
            };
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(55);
            mockUserService.Setup(s => s.CanAccessTicket(It.IsAny<Ticket>())).Returns(true);
            mockUserService.Setup(s => s.GetCurrentUser()).Returns(techUser);

            var controller = new TicketsController(context, mockUserService.Object, new NotificationService(context));
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new Microsoft.AspNetCore.Http.DefaultHttpContext(),
                Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

            // Even if an external or forged userId is submitted in the request, author must be logged in user (55)
            await controller.AddMessage(ticket.Id, userId: 999, msg: "Inspected wiring and repaired power socket.");

            var message = await context.TicketMessages.FirstOrDefaultAsync(m => m.TicketId == ticket.Id);
            Assert.NotNull(message);
            Assert.Equal(55, message.UserId); // Must match logged in user 55
        }

        // ==========================================
        // Task 2 (Phase 2): Notifications Popup Endpoint Test
        // ==========================================
        [Fact]
        public async Task NotificationsController_GetLatestUnread_ReturnsJsonResults()
        {
            using var context = CreateInMemoryDbContext();

            var user = new User { Id = 77, FullName = "Nurse Mary" };
            context.Users.Add(user);

            context.Notifications.Add(new Notification
            {
                Id = 501,
                UserId = 77,
                Title = "New Ticket Update",
                Message = "Technician dispatched to Room 201",
                Type = "TicketStatus",
                IsRead = false,
                CreatedAt = DateTime.Now
            });
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(77);

            var notifController = new NotificationsController(new NotificationService(context), mockUserService.Object);
            var result = await notifController.GetLatestUnread();

            var jsonResult = Assert.IsType<JsonResult>(result);
            var countProp = jsonResult.Value!.GetType().GetProperty("count");
            Assert.NotNull(countProp);
            var countVal = (int)countProp.GetValue(jsonResult.Value)!;
            Assert.Equal(1, countVal);
        }
    }
}
