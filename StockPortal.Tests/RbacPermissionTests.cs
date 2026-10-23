using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
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
    public class RbacPermissionTests
    {
        private Mock<IHttpContextAccessor> CreateMockHttpContextAccessor(User user)
        {
            var mockAccessor = new Mock<IHttpContextAccessor>();
            var httpContext = new DefaultHttpContext();
            httpContext.Items["CurrentUser"] = user;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.FullName ?? "User"),
                new Claim(ClaimTypes.Role, user.Role ?? SystemRoles.EndUser)
            };

            if (!string.IsNullOrWhiteSpace(user.MaintenanceTeam))
            {
                claims.Add(new Claim("MaintenanceTeam", user.MaintenanceTeam));
            }

            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
            mockAccessor.Setup(a => a.HttpContext).Returns(httpContext);
            return mockAccessor;
        }

        private StockDbContext CreateInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<StockDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return new StockDbContext(options);
        }

        // ==========================================
        // 1. END USER PERMISSION TESTS
        // ==========================================

        [Fact]
        public void EndUser_CanOnlyAccessAssetsInTheirCustody()
        {
            var endUser = new User
            {
                Id = 101,
                FullName = "Nurse Jackie",
                Role = SystemRoles.EndUser
            };

            var mockAccessor = CreateMockHttpContextAccessor(endUser);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            var myAsset = new Asset { Id = 1, UserId = 101, HospitalNo = "HOSP-01", MaintenanceTeam = MaintenanceTeams.IT };
            var otherAsset = new Asset { Id = 2, UserId = 102, HospitalNo = "HOSP-02", MaintenanceTeam = MaintenanceTeams.IT };

            Assert.True(currentUserService.CanAccessAsset(myAsset));
            Assert.False(currentUserService.CanAccessAsset(otherAsset));
        }

        [Fact]
        public void EndUser_CanOnlyAccessAuthoredTickets()
        {
            var endUser = new User
            {
                Id = 101,
                FullName = "Nurse Jackie",
                Role = SystemRoles.EndUser
            };

            var mockAccessor = CreateMockHttpContextAccessor(endUser);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            var myTicket = new Ticket { Id = 10, OpenedBy = 101, TargetTeam = MaintenanceTeams.IT };
            var ticketOnMyBehalf = new Ticket { Id = 11, OpenedBy = 999, OnBehalfOf = 101, TargetTeam = MaintenanceTeams.IT };
            var otherTicket = new Ticket { Id = 12, OpenedBy = 102, TargetTeam = MaintenanceTeams.IT };

            Assert.True(currentUserService.CanAccessTicket(myTicket));
            Assert.True(currentUserService.CanAccessTicket(ticketOnMyBehalf));
            Assert.False(currentUserService.CanAccessTicket(otherTicket));
        }

        [Fact]
        public void EndUser_CannotManageTicketsOrAssets()
        {
            var endUser = new User
            {
                Id = 101,
                FullName = "Nurse Jackie",
                Role = SystemRoles.EndUser
            };

            var mockAccessor = CreateMockHttpContextAccessor(endUser);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            var ticket = new Ticket { Id = 10, OpenedBy = 101, TargetTeam = MaintenanceTeams.IT };

            Assert.False(currentUserService.CanManageTicket(ticket));
            Assert.False(currentUserService.CanManageAssets());
            Assert.False(currentUserService.CanManageUsers());
        }

        // ==========================================
        // 2. MAINTENANCE USER PERMISSION TESTS
        // ==========================================

        [Fact]
        public void MaintenanceUser_CanOnlyAccessTicketsForTheirAssignedTeam()
        {
            var biomedicalTech = new User
            {
                Id = 201,
                FullName = "Tariq Ziad",
                Role = SystemRoles.Maintenance,
                MaintenanceTeam = MaintenanceTeams.Biomedical
            };

            var mockAccessor = CreateMockHttpContextAccessor(biomedicalTech);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            var biomedicalTicket = new Ticket { Id = 20, TargetTeam = MaintenanceTeams.Biomedical, OpenedBy = 101 };
            var itTicket = new Ticket { Id = 21, TargetTeam = MaintenanceTeams.IT, OpenedBy = 101 };
            var generalTicket = new Ticket { Id = 22, TargetTeam = MaintenanceTeams.General, OpenedBy = 101 };

            // Can access and manage Biomedical tickets
            Assert.True(currentUserService.CanAccessTicket(biomedicalTicket));
            Assert.True(currentUserService.CanManageTicket(biomedicalTicket));

            // Forbidden from other maintenance teams' tickets
            Assert.False(currentUserService.CanAccessTicket(itTicket));
            Assert.False(currentUserService.CanManageTicket(itTicket));
            Assert.False(currentUserService.CanAccessTicket(generalTicket));
            Assert.False(currentUserService.CanManageTicket(generalTicket));
        }

        [Fact]
        public void MaintenanceUser_CannotManageUserRolesOrSystemDirectory()
        {
            var itTech = new User
            {
                Id = 202,
                FullName = "Ahmed Mansour",
                Role = SystemRoles.Maintenance,
                MaintenanceTeam = MaintenanceTeams.IT
            };

            var mockAccessor = CreateMockHttpContextAccessor(itTech);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            Assert.False(currentUserService.CanManageUsers());
            Assert.False(currentUserService.CanManageAssets());
        }

        [Fact]
        public void MaintenanceUser_CanOnlyAccessAssetsForTheirAssignedTeam()
        {
            var generalTech = new User
            {
                Id = 6,
                FullName = "Hasan Outa",
                Role = SystemRoles.Maintenance,
                MaintenanceTeam = MaintenanceTeams.General
            };

            var mockAccessor = CreateMockHttpContextAccessor(generalTech);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            var itAsset = new Asset { Id = 1, MaintenanceTeam = MaintenanceTeams.IT, HospitalNo = "HOSP-PC-01" };
            var bioAsset = new Asset { Id = 2, MaintenanceTeam = MaintenanceTeams.Biomedical, HospitalNo = "HOSP-BIO-01" };
            var genAsset = new Asset { Id = 3, MaintenanceTeam = MaintenanceTeams.General, HospitalNo = "HOSP-GEN-01" };

            // Can access General assets only
            Assert.True(currentUserService.CanAccessAsset(genAsset));
            // Cannot access IT or Biomedical assets
            Assert.False(currentUserService.CanAccessAsset(itAsset));
            Assert.False(currentUserService.CanAccessAsset(bioAsset));
        }

        [Fact]
        public async Task MaintenanceUser_TicketsIndex_OnlyReturnsTicketsForAssignedTeamQueue()
        {
            using var context = CreateInMemoryDbContext();

            // Seed tickets for various teams, some opened by Hasan Outa (Id = 6)
            context.Tickets.AddRange(
                new Ticket { Id = 1, TargetTeam = MaintenanceTeams.IT, OpenedBy = 1 },
                new Ticket { Id = 2, TargetTeam = MaintenanceTeams.IT, OpenedBy = 6 }, // Opened by Hasan for IT
                new Ticket { Id = 3, TargetTeam = MaintenanceTeams.Biomedical, OpenedBy = 6 }, // Opened by Hasan for Bio
                new Ticket { Id = 4, TargetTeam = MaintenanceTeams.General, OpenedBy = 6 }, // General ticket
                new Ticket { Id = 5, TargetTeam = MaintenanceTeams.General, OpenedBy = 101 } // General ticket by nurse
            );
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(6);
            mockUserService.Setup(s => s.IsMaintenance()).Returns(true);
            mockUserService.Setup(s => s.GetMaintenanceTeam()).Returns(MaintenanceTeams.General);

            var controller = new TicketsController(context, mockUserService.Object);
            var result = await controller.Index(statusId: null, priority: null, type: null, search: null, filter: null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<Ticket>>(viewResult.Model);

            // Hasan Outa should only see General tickets (Tickets 4 & 5), NOT IT or Biomedical tickets (Tickets 1, 2, 3)
            Assert.Equal(2, model.Count);
            Assert.All(model, t => Assert.Equal(MaintenanceTeams.General, t.TargetTeam));
        }

        [Fact]
        public async Task MaintenanceUser_AssetsIndex_OnlyReturnsAssetsForAssignedTeam()
        {
            using var context = CreateInMemoryDbContext();

            context.Assets.AddRange(
                new Asset { Id = 1, HospitalNo = "HOSP-IT-01", MaintenanceTeam = MaintenanceTeams.IT },
                new Asset { Id = 2, HospitalNo = "HOSP-BIO-01", MaintenanceTeam = MaintenanceTeams.Biomedical },
                new Asset { Id = 3, HospitalNo = "HOSP-GEN-01", MaintenanceTeam = MaintenanceTeams.General },
                new Asset { Id = 4, HospitalNo = "HOSP-GEN-02", MaintenanceTeam = MaintenanceTeams.General }
            );
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(6);
            mockUserService.Setup(s => s.IsMaintenance()).Returns(true);
            mockUserService.Setup(s => s.GetMaintenanceTeam()).Returns(MaintenanceTeams.General);

            var controller = new AssetsController(context, mockUserService.Object);
            var result = await controller.Index(search: null, status: null, type: null, roomId: null, warranty: null);

            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<Asset>>(viewResult.Model);

            Assert.Equal(2, model.Count);
            Assert.All(model, a => Assert.Equal(MaintenanceTeams.General, a.MaintenanceTeam));
        }

        // ==========================================
        // 3. ADMIN USER PERMISSION TESTS
        // ==========================================

        [Fact]
        public void AdminUser_HasUnrestrictedAccessToAllTicketsAndAssets()
        {
            var adminUser = new User
            {
                Id = 1,
                FullName = "Abbas Al-Amiri",
                Role = SystemRoles.Admin
            };

            var mockAccessor = CreateMockHttpContextAccessor(adminUser);
            var currentUserService = new CurrentUserService(mockAccessor.Object);

            var itTicket = new Ticket { Id = 31, TargetTeam = MaintenanceTeams.IT, OpenedBy = 99 };
            var bioTicket = new Ticket { Id = 32, TargetTeam = MaintenanceTeams.Biomedical, OpenedBy = 98 };
            var genTicket = new Ticket { Id = 33, TargetTeam = MaintenanceTeams.General, OpenedBy = 97 };
            var anyAsset = new Asset { Id = 50, UserId = 88, MaintenanceTeam = MaintenanceTeams.Biomedical };

            Assert.True(currentUserService.CanAccessTicket(itTicket));
            Assert.True(currentUserService.CanManageTicket(itTicket));
            Assert.True(currentUserService.CanAccessTicket(bioTicket));
            Assert.True(currentUserService.CanManageTicket(bioTicket));
            Assert.True(currentUserService.CanAccessTicket(genTicket));
            Assert.True(currentUserService.CanManageTicket(genTicket));

            Assert.True(currentUserService.CanAccessAsset(anyAsset));
            Assert.True(currentUserService.CanManageAssets());
            Assert.True(currentUserService.CanManageUsers());
        }

        // ==========================================
        // 4. ROUTING & CREATION LOGIC TESTS
        // ==========================================

        [Fact]
        public async Task TicketCreation_AutoRoutesToAssetMaintenanceTeam()
        {
            using var context = CreateInMemoryDbContext();

            // Seed biomedical equipment
            var bioAsset = new Asset
            {
                Id = 5,
                HospitalNo = "HOSP-MRI-01",
                Brand = "Siemens",
                MaintenanceTeam = MaintenanceTeams.Biomedical,
                UserId = 101
            };
            context.Assets.Add(bioAsset);
            await context.SaveChangesAsync();

            var endUser = new User { Id = 101, FullName = "Dr. Sarah", Role = SystemRoles.EndUser };
            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(101);
            mockUserService.Setup(s => s.IsEndUser()).Returns(true);

            var controller = new TicketsController(context, mockUserService.Object);
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new DefaultHttpContext(),
                Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

            var ticket = new Ticket
            {
                AssetId = bioAsset.Id,
                OpenedBy = 101,
                OnBehalfOf = 101,
                Type = "Hardware",
                Priority = 3
            };

            var result = await controller.Create(ticket, manualAsset: null, initialMessage: "MRI calibration needed", assignedTechId: null);

            var createdTicket = await context.Tickets.FirstOrDefaultAsync(t => t.AssetId == bioAsset.Id);
            Assert.NotNull(createdTicket);
            // Automatic routing to asset's maintenance team
            Assert.Equal(MaintenanceTeams.Biomedical, createdTicket.TargetTeam);
            Assert.Equal(1, createdTicket.StatusId); // opened
        }

        // ==========================================
        // 5. CLOSED TICKET MESSAGE LOCKING TESTS (TASK 1)
        // ==========================================

        [Fact]
        public async Task ClosedTicket_DisallowsNewMessages()
        {
            using var context = CreateInMemoryDbContext();

            var closedTicket = new Ticket
            {
                Id = 77,
                StatusId = 5, // Closed
                ClosedDT = DateTime.Now.AddDays(-1),
                TargetTeam = MaintenanceTeams.IT,
                OpenedBy = 101
            };
            context.Tickets.Add(closedTicket);
            await context.SaveChangesAsync();

            var mockUserService = new Mock<ICurrentUserService>();
            mockUserService.Setup(s => s.GetUserId()).Returns(101);
            mockUserService.Setup(s => s.CanAccessTicket(It.IsAny<Ticket>())).Returns(true);

            var controller = new TicketsController(context, mockUserService.Object);
            controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new DefaultHttpContext(),
                Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());

            var actionResult = await controller.AddMessage(closedTicket.Id, userId: 101, msg: "Trying to add message to closed ticket");

            // Verify redirection and error notice
            var redirectResult = Assert.IsType<RedirectToActionResult>(actionResult);
            Assert.Equal("Details", redirectResult.ActionName);
            Assert.Equal("This ticket is closed. New messages cannot be added.", controller.TempData["ErrorMessage"]);

            // Verify message was NOT saved in database
            var msgCount = await context.TicketMessages.CountAsync(m => m.TicketId == closedTicket.Id);
            Assert.Equal(0, msgCount);
        }

        // ==========================================
        // 6. CONTROLLER API & ROUTE GUARD TESTS
        // ==========================================

        [Fact]
        public async Task DirectoryController_ForbidsNonAdminUsers()
        {
            using var context = CreateInMemoryDbContext();

            // End user
            var mockEndUser = new Mock<ICurrentUserService>();
            mockEndUser.Setup(s => s.CanManageUsers()).Returns(false);
            var dirControllerEndUser = new DirectoryController(context, mockEndUser.Object);

            var result = await dirControllerEndUser.Index();
            Assert.IsType<ForbidResult>(result);

            // Maintenance user
            var mockMaintUser = new Mock<ICurrentUserService>();
            mockMaintUser.Setup(s => s.CanManageUsers()).Returns(false);
            var dirControllerMaint = new DirectoryController(context, mockMaintUser.Object);

            var maintResult = await dirControllerMaint.Index();
            Assert.IsType<ForbidResult>(maintResult);
        }

        [Fact]
        public async Task AssetsController_ForbidsEndUserFromCreatingOrModifyingAssets()
        {
            using var context = CreateInMemoryDbContext();
            var mockEndUser = new Mock<ICurrentUserService>();
            mockEndUser.Setup(s => s.CanManageAssets()).Returns(false);

            var assetsController = new AssetsController(context, mockEndUser.Object);

            var createGet = await assetsController.Create();
            Assert.IsType<ForbidResult>(createGet);

            var createPost = await assetsController.Create(new Asset { HospitalNo = "TEST" });
            Assert.IsType<ForbidResult>(createPost);

            var editGet = await assetsController.Edit(1);
            Assert.IsType<ForbidResult>(editGet);

            var deleteGet = await assetsController.Delete(1);
            Assert.IsType<ForbidResult>(deleteGet);
        }

        [Fact]
        public async Task TicketsController_ForbidsMaintenanceUserFromManagingOtherTeamTicket()
        {
            using var context = CreateInMemoryDbContext();
            var ticket = new Ticket
            {
                Id = 88,
                TargetTeam = MaintenanceTeams.Biomedical,
                StatusId = 1
            };
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            // IT technician attempts to update Biomedical ticket
            var mockItTech = new Mock<ICurrentUserService>();
            mockItTech.Setup(s => s.CanManageTicket(It.Is<Ticket>(t => t.TargetTeam == MaintenanceTeams.Biomedical))).Returns(false);

            var ticketsController = new TicketsController(context, mockItTech.Object);

            var updateStatusResult = await ticketsController.UpdateStatus(ticket.Id, 2);
            Assert.IsType<ForbidResult>(updateStatusResult);

            var closeTicketResult = await ticketsController.CloseTicket(ticket.Id, "Trying to close other team's ticket");
            Assert.IsType<ForbidResult>(closeTicketResult);

            var assignTechResult = await ticketsController.AssignTechnician(ticket.Id, techUserId: 2);
            Assert.IsType<ForbidResult>(assignTechResult);
        }
    }
}
