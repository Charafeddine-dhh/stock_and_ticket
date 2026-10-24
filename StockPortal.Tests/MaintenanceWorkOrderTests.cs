using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    public class MaintenanceWorkOrderTests
    {
        private StockDbContext GetInMemoryDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<StockDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;

            var context = new StockDbContext(options);

            // Seed reference data
            if (!context.TicketStatuses.Any())
            {
                context.TicketStatuses.AddRange(
                    new TicketStatus { Id = 1, Status = "opened" },
                    new TicketStatus { Id = 2, Status = "pending" },
                    new TicketStatus { Id = 3, Status = "delayed" },
                    new TicketStatus { Id = 4, Status = "canceled" },
                    new TicketStatus { Id = 5, Status = "closed" },
                    new TicketStatus { Id = 6, Status = "maintenance" }
                );
            }

            if (!context.CustodyTypes.Any())
            {
                context.CustodyTypes.AddRange(
                    new CustodyType { Id = 1, Type = "Personal" },
                    new CustodyType { Id = 2, Type = "Shared" },
                    new CustodyType { Id = 3, Type = "Supplier / External Maintenance" }
                );
            }

            if (!context.Suppliers.Any())
            {
                context.Suppliers.AddRange(
                    new Supplier { Id = 1, Name = "Dell Technologies", IsActive = true },
                    new Supplier { Id = 2, Name = "Philips Healthcare", IsActive = true }
                );
            }

            if (!context.Rooms.Any())
            {
                context.Rooms.AddRange(
                    new Room { Id = 1, RoomNo = 101, Floor = 1 },
                    new Room { Id = 2, RoomNo = 202, Floor = 2 }
                );
            }

            context.SaveChanges();
            return context;
        }

        private Mock<ICurrentUserService> CreateMockUserService(string role, string? team = null, int userId = 2)
        {
            var mock = new Mock<ICurrentUserService>();
            mock.Setup(s => s.GetRole()).Returns(role);
            mock.Setup(s => s.GetMaintenanceTeam()).Returns(team);
            mock.Setup(s => s.GetUserId()).Returns(userId);
            mock.Setup(s => s.IsAdmin()).Returns(role == SystemRoles.Admin);
            mock.Setup(s => s.IsMaintenance()).Returns(role == SystemRoles.Maintenance);
            mock.Setup(s => s.IsEndUser()).Returns(role == SystemRoles.EndUser);
            mock.Setup(s => s.CanManageAssets()).Returns(role == SystemRoles.Admin || role == SystemRoles.Maintenance);
            mock.Setup(s => s.CanManageAsset(It.IsAny<Asset>())).Returns((Asset a) =>
                role == SystemRoles.Admin || (role == SystemRoles.Maintenance && string.Equals(a.MaintenanceTeam, team, StringComparison.OrdinalIgnoreCase)));
            mock.Setup(s => s.CanAccessAsset(It.IsAny<Asset>())).Returns((Asset a) =>
                role == SystemRoles.Admin || (role == SystemRoles.Maintenance && string.Equals(a.MaintenanceTeam, team, StringComparison.OrdinalIgnoreCase)) || (role == SystemRoles.EndUser && a.UserId == userId));

            return mock;
        }

        [Fact]
        public async Task SendToSupplier_TransfersCustodyToSupplier_SetsAssetStatusUnderMaintenance()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockUser = CreateMockUserService(SystemRoles.Maintenance, MaintenanceTeams.IT, userId: 2);

            var asset = new Asset
            {
                Id = 1,
                HospitalNo = "HOSP-LAP-101",
                Brand = "Dell Latitude",
                MaintenanceTeam = MaintenanceTeams.IT,
                Status = "In Service",
                UserId = 10,
                RoomId = 1,
                CustodyTypeId = 1
            };
            context.Assets.Add(asset);

            context.AssetHistories.Add(new AssetHistory
            {
                AssetId = 1,
                UserId = 10,
                RoomId = 1,
                CustodyTypeId = 1,
                AssignedOn = DateTime.Now.AddMonths(-3)
            });
            await context.SaveChangesAsync();

            var controller = new MaintenanceController(context, mockUser.Object);

            var result = await controller.SendToSupplier(
                assetId: 1,
                supplierId: 1, // Dell
                description: "Motherboard power failure, needs vendor diagnosis and parts replacement",
                ticketId: null,
                technicianId: 2,
                maintenanceType: 1,
                sentDate: DateTime.Now
            );

            // Asset should be Under Maintenance and CustodyType = 3 (Supplier)
            var updatedAsset = await context.Assets.FindAsync(1);
            Assert.NotNull(updatedAsset);
            Assert.Equal("Under Maintenance", updatedAsset.Status);
            Assert.Equal(3, updatedAsset.CustodyTypeId);

            // Work order should be recorded
            var workOrder = await context.AssetMaintenances.FirstOrDefaultAsync(m => m.AssetId == 1);
            Assert.NotNull(workOrder);
            Assert.Equal("Under Maintenance", workOrder.WorkOrderStatus);
            Assert.Equal(1, workOrder.SupplierId);
            Assert.Equal(10, workOrder.PreviousUserId);
            Assert.Contains("Motherboard power failure", workOrder.Description);

            // Custody history should show supplier
            var history = await context.AssetHistories.Where(h => h.AssetId == 1).OrderByDescending(h => h.AssignedOn).FirstOrDefaultAsync();
            Assert.NotNull(history);
            Assert.Equal(3, history.CustodyTypeId);
            Assert.Contains("Dell Technologies", history.Notes);
        }

        [Fact]
        public async Task SendToSupplier_WhenLinkedToTicket_UpdatesTicketStatusToMaintenance()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockUser = CreateMockUserService(SystemRoles.Maintenance, MaintenanceTeams.IT, userId: 2);

            var asset = new Asset
            {
                Id = 2,
                HospitalNo = "HOSP-SVR-01",
                Brand = "Dell PowerEdge",
                MaintenanceTeam = MaintenanceTeams.IT,
                Status = "In Service",
                UserId = 2,
                RoomId = 1
            };
            context.Assets.Add(asset);

            var ticket = new Ticket
            {
                Id = 101,
                AssetId = 2,
                OpenedBy = 10,
                StatusId = 1, // opened
                TargetTeam = MaintenanceTeams.IT,
                Type = "Hardware"
            };
            context.Tickets.Add(ticket);
            await context.SaveChangesAsync();

            var controller = new MaintenanceController(context, mockUser.Object);

            await controller.SendToSupplier(
                assetId: 2,
                supplierId: 1,
                description: "RAID controller backplane failure",
                ticketId: 101,
                technicianId: 2,
                maintenanceType: 1,
                sentDate: DateTime.Now
            );

            var updatedTicket = await context.Tickets.Include(t => t.Messages).FirstOrDefaultAsync(t => t.Id == 101);
            Assert.NotNull(updatedTicket);
            Assert.Equal(6, updatedTicket.StatusId); // TicketStatusConstants.Maintenance
            Assert.Contains(updatedTicket.Messages, m => m.Msg.Contains("WORK ORDER"));
        }

        [Fact]
        public async Task ReturnFromSupplier_ToCentralStock_SetsAssetStatusInStockAndClearsUserId()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockUser = CreateMockUserService(SystemRoles.Maintenance, MaintenanceTeams.IT, userId: 2);

            var asset = new Asset
            {
                Id = 3,
                HospitalNo = "HOSP-PC-05",
                Brand = "HP",
                MaintenanceTeam = MaintenanceTeams.IT,
                Status = "Under Maintenance",
                UserId = 10,
                RoomId = 1,
                CustodyTypeId = 3
            };
            context.Assets.Add(asset);

            var workOrder = new AssetMaintenance
            {
                Id = 10,
                AssetId = 3,
                SupplierId = 1,
                WorkOrderStatus = "Under Maintenance",
                SentDate = DateTime.Now.AddDays(-5),
                PreviousUserId = 10,
                PreviousRoomId = 1
            };
            context.AssetMaintenances.Add(workOrder);
            await context.SaveChangesAsync();

            var controller = new MaintenanceController(context, mockUser.Object);

            var partsJson = JsonSerializer.Serialize(new[]
            {
                new { partName = "500W Power Supply", partNumber = "PSU-500", quantity = 1, cost = 85.00, underWarranty = true, notes = "Replaced OEM" }
            });

            await controller.ReturnFromSupplier(
                id: 10,
                returnDate: DateTime.Now,
                actionTaken: "Power supply replaced and load tested for 2 hours.",
                cost: 85.00m,
                partsJson: partsJson,
                returnDestination: "stock",
                newUserId: null,
                newRoomId: 2
            );

            var updatedAsset = await context.Assets.FindAsync(3);
            Assert.NotNull(updatedAsset);
            Assert.Equal("In Stock", updatedAsset.Status);
            Assert.Null(updatedAsset.UserId); // unassigned central stock
            Assert.Equal(2, updatedAsset.CustodyTypeId); // Shared / Central Stock
            Assert.Equal(2, updatedAsset.RoomId);

            var updatedOrder = await context.AssetMaintenances.FindAsync(10);
            Assert.NotNull(updatedOrder);
            Assert.Equal("Completed", updatedOrder.WorkOrderStatus);
            Assert.NotNull(updatedOrder.ReturnDate);
            Assert.Single(updatedOrder.ParsedParts);
            Assert.Equal("500W Power Supply", updatedOrder.ParsedParts[0].PartName);
        }

        [Fact]
        public async Task ReturnFromSupplier_ToUser_SetsAssetStatusInServiceAndAssignsUser()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockUser = CreateMockUserService(SystemRoles.Maintenance, MaintenanceTeams.IT, userId: 2);

            var asset = new Asset
            {
                Id = 4,
                HospitalNo = "HOSP-LAP-20",
                Brand = "Dell Latitude",
                MaintenanceTeam = MaintenanceTeams.IT,
                Status = "Under Maintenance",
                CustodyTypeId = 3
            };
            context.Assets.Add(asset);

            var workOrder = new AssetMaintenance
            {
                Id = 20,
                AssetId = 4,
                SupplierId = 1,
                WorkOrderStatus = "Under Maintenance",
                SentDate = DateTime.Now.AddDays(-3)
            };
            context.AssetMaintenances.Add(workOrder);
            await context.SaveChangesAsync();

            var controller = new MaintenanceController(context, mockUser.Object);

            await controller.ReturnFromSupplier(
                id: 20,
                returnDate: DateTime.Now,
                actionTaken: "Keyboard and trackpad replaced.",
                cost: 120.00m,
                partsJson: "[]",
                returnDestination: "user",
                newUserId: 15,
                newRoomId: 2
            );

            var updatedAsset = await context.Assets.FindAsync(4);
            Assert.NotNull(updatedAsset);
            Assert.Equal("In Service", updatedAsset.Status);
            Assert.Equal(15, updatedAsset.UserId);
            Assert.Equal(1, updatedAsset.CustodyTypeId); // Personal
        }

        [Fact]
        public async Task ReturnFromSupplier_StoresReplacedPartsInJsonFormat()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockUser = CreateMockUserService(SystemRoles.Maintenance, MaintenanceTeams.Biomedical, userId: 5);

            var asset = new Asset
            {
                Id = 5,
                HospitalNo = "HOSP-VENT-01",
                Brand = "Philips",
                MaintenanceTeam = MaintenanceTeams.Biomedical,
                Status = "Under Maintenance",
                CustodyTypeId = 3
            };
            context.Assets.Add(asset);

            var workOrder = new AssetMaintenance
            {
                Id = 30,
                AssetId = 5,
                SupplierId = 2,
                WorkOrderStatus = "Under Maintenance",
                SentDate = DateTime.Now.AddDays(-7)
            };
            context.AssetMaintenances.Add(workOrder);
            await context.SaveChangesAsync();

            var controller = new MaintenanceController(context, mockUser.Object);

            var parts = new List<ReplacedPartItem>
            {
                new ReplacedPartItem { PartName = "O2 Sensor Cell", PartNumber = "O2-MAX-250", Quantity = 2, Cost = 190.00m, UnderWarranty = true, Notes = "Calibrated" },
                new ReplacedPartItem { PartName = "Exhalation Valve Diaphragm", PartNumber = "VALVE-DIA-01", Quantity = 1, Cost = 45.00m, UnderWarranty = false, Notes = "Replaced preventative" }
            };
            var json = JsonSerializer.Serialize(parts);

            await controller.ReturnFromSupplier(
                id: 30,
                returnDate: DateTime.Now,
                actionTaken: "Full ventilator annual overhaul and flow calibration completed.",
                cost: 425.00m,
                partsJson: json,
                returnDestination: "stock",
                newUserId: null,
                newRoomId: 1
            );

            var updatedOrder = await context.AssetMaintenances.FindAsync(30);
            Assert.NotNull(updatedOrder);
            Assert.Equal("Completed", updatedOrder.WorkOrderStatus);
            Assert.False(string.IsNullOrWhiteSpace(updatedOrder.PartsReplaced));

            // Verify clean JSON deserialization
            var parsed = updatedOrder.ParsedParts;
            Assert.Equal(2, parsed.Count);
            Assert.Equal("O2 Sensor Cell", parsed[0].PartName);
            Assert.Equal(2, parsed[0].Quantity);
            Assert.True(parsed[0].UnderWarranty);
            Assert.Equal("Exhalation Valve Diaphragm", parsed[1].PartName);
        }

        [Fact]
        public async Task TicketsIndex_FiltersByMaintenanceStatus()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockUser = CreateMockUserService(SystemRoles.Admin, userId: 1);

            context.Tickets.AddRange(
                new Ticket { Id = 1, StatusId = 1, Type = "Hardware", TargetTeam = MaintenanceTeams.IT },
                new Ticket { Id = 2, StatusId = 6, Type = "Hardware", TargetTeam = MaintenanceTeams.IT }, // Maintenance
                new Ticket { Id = 3, StatusId = 6, Type = "Biomedical", TargetTeam = MaintenanceTeams.Biomedical }, // Maintenance
                new Ticket { Id = 4, StatusId = 5, Type = "Network", TargetTeam = MaintenanceTeams.IT } // Closed
            );
            await context.SaveChangesAsync();

            var controller = new TicketsController(context, mockUser.Object, new NotificationService(context));

            // Filter via statusId = 6
            var result1 = await controller.Index(statusId: 6, priority: null, type: null, search: null, filter: null);
            var viewResult1 = Assert.IsType<ViewResult>(result1);
            var model1 = Assert.IsAssignableFrom<List<Ticket>>(viewResult1.Model);
            Assert.Equal(2, model1.Count);
            Assert.All(model1, t => Assert.Equal(6, t.StatusId));

            // Filter via filter = "maintenance"
            var result2 = await controller.Index(statusId: null, priority: null, type: null, search: null, filter: "maintenance");
            var viewResult2 = Assert.IsType<ViewResult>(result2);
            var model2 = Assert.IsAssignableFrom<List<Ticket>>(viewResult2.Model);
            Assert.Equal(2, model2.Count);
            Assert.All(model2, t => Assert.Equal(6, t.StatusId));
        }

        [Fact]
        public async Task SendToSupplier_RequiresTechnicianOrAdmin_EndUserForbidden()
        {
            var context = GetInMemoryDbContext(Guid.NewGuid().ToString());
            var mockEndUser = CreateMockUserService(SystemRoles.EndUser, userId: 10);

            var controller = new MaintenanceController(context, mockEndUser.Object);

            var result = await controller.SendToSupplier(assetId: 1, ticketId: null);
            Assert.IsType<ForbidResult>(result);

            var postResult = await controller.SendToSupplier(assetId: 1, supplierId: 1, description: "Test", ticketId: null, technicianId: 10, maintenanceType: 1, sentDate: DateTime.Now);
            Assert.IsType<ForbidResult>(postResult);
        }
    }
}
