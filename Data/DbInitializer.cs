using System;
using System.Linq;
using StockPortal.Models;

namespace StockPortal.Data
{
    public static class DbInitializer
    {
        public static void Initialize(StockDbContext context)
        {
            context.Database.EnsureCreated();

            // Ensure Ticket Statuses
            if (!context.TicketStatuses.Any())
            {
                context.TicketStatuses.AddRange(
                    new TicketStatus { Id = 1, Status = "opened" },
                    new TicketStatus { Id = 2, Status = "pending" },
                    new TicketStatus { Id = 3, Status = "delayed" },
                    new TicketStatus { Id = 4, Status = "canceled" },
                    new TicketStatus { Id = 5, Status = "closed" }
                );
                context.SaveChanges();
            }

            // Ensure Custody Types
            if (!context.CustodyTypes.Any())
            {
                context.CustodyTypes.AddRange(
                    new CustodyType { Id = 1, Type = "Personal" },
                    new CustodyType { Id = 2, Type = "Shared" }
                );
                context.SaveChanges();
            }

            // Seed Departments if empty
            if (!context.Departments.Any())
            {
                var depts = new[]
                {
                    new Department { Name = "Information Technology" },
                    new Department { Name = "Radiology & Imaging" },
                    new Department { Name = "Cardiology" },
                    new Department { Name = "Biomedical Engineering" },
                    new Department { Name = "Hospital Administration" }
                };
                context.Departments.AddRange(depts);
                context.SaveChanges();
            }

            // Seed Rooms if empty
            if (!context.Rooms.Any())
            {
                var rooms = new[]
                {
                    new Room { RoomNo = 101, Floor = 1 },
                    new Room { RoomNo = 102, Floor = 1 },
                    new Room { RoomNo = 105, Floor = 1 }, // Server Room
                    new Room { RoomNo = 201, Floor = 2 }, // ICU
                    new Room { RoomNo = 301, Floor = 3 }  // Radiology
                };
                context.Rooms.AddRange(rooms);
                context.SaveChanges();
            }

            // Seed Suppliers if empty
            if (!context.Suppliers.Any())
            {
                var suppliers = new[]
                {
                    new Supplier { Name = "Dell Technologies", IsActive = true, Type = 3 },
                    new Supplier { Name = "Philips Healthcare", IsActive = true, Type = 3 },
                    new Supplier { Name = "HP Enterprise", IsActive = true, Type = 1 },
                    new Supplier { Name = "Cisco Systems", IsActive = true, Type = 3 }
                };
                context.Suppliers.AddRange(suppliers);
                context.SaveChanges();
            }

            // Seed Users if empty
            string currentPcUser = string.IsNullOrWhiteSpace(Environment.UserName) ? "Abbas" : Environment.UserName;

            if (!context.Users.Any())
            {
                var itDept = context.Departments.FirstOrDefault(d => d.Name == "Information Technology");
                var radDept = context.Departments.FirstOrDefault(d => d.Name == "Radiology & Imaging");
                var cardDept = context.Departments.FirstOrDefault(d => d.Name == "Cardiology");
                var bioDept = context.Departments.FirstOrDefault(d => d.Name == "Biomedical Engineering");

                var users = new[]
                {
                    new User { FullName = "Abbas Al-Amiri", DomainUser = currentPcUser, Role = "IT Manager & Lead SysAdmin", DepartmentId = itDept?.Id, IsActive = true },
                    new User { FullName = "Ahmed Mansour", DomainUser = "a.mansour", Role = "Field Support Technician", DepartmentId = itDept?.Id, IsActive = true },
                    new User { FullName = "Dr. Sarah Jenkins", DomainUser = "s.jenkins", Role = "Lead Cardiologist", DepartmentId = cardDept?.Id, IsActive = true },
                    new User { FullName = "Dr. Elena Rostova", DomainUser = "e.rostova", Role = "Radiology Consultant", DepartmentId = radDept?.Id, IsActive = true },
                    new User { FullName = "Tariq Ziad", DomainUser = "t.ziad", Role = "Biomedical Equipment Engineer", DepartmentId = bioDept?.Id, IsActive = true },
                    new User { FullName = "Former Staff", DomainUser = "inactive.user", Role = "Inactive Account", DepartmentId = itDept?.Id, IsActive = false }
                };
                context.Users.AddRange(users);
                context.SaveChanges();

                if (itDept != null && users.Length > 0)
                {
                    itDept.HeadUserId = users[0].Id;
                    context.SaveChanges();
                }
            }

            // Seed Assets if empty
            if (!context.Assets.Any())
            {
                var users = context.Users.ToList();
                var rooms = context.Rooms.ToList();
                var suppliers = context.Suppliers.ToList();
                var custodyTypes = context.CustodyTypes.ToList();

                var abbas = users.FirstOrDefault(u => u.DomainUser == "a.amiri") ?? users.First();
                var sarah = users.FirstOrDefault(u => u.DomainUser == "s.jenkins") ?? users.First();
                var elena = users.FirstOrDefault(u => u.DomainUser == "e.rostova") ?? users.First();

                var srvRoom = rooms.FirstOrDefault(r => r.RoomNo == 105) ?? rooms[0];
                var clinicRoom = rooms.FirstOrDefault(r => r.RoomNo == 101) ?? rooms[0];
                var radRoom = rooms.FirstOrDefault(r => r.RoomNo == 301) ?? rooms[0];

                var dell = suppliers.FirstOrDefault(s => s.Name != null && s.Name.Contains("Dell")) ?? suppliers[0];
                var philips = suppliers.FirstOrDefault(s => s.Name != null && s.Name.Contains("Philips")) ?? suppliers[0];
                var hp = suppliers.FirstOrDefault(s => s.Name != null && s.Name.Contains("HP")) ?? suppliers[0];
                var cisco = suppliers.FirstOrDefault(s => s.Name != null && s.Name.Contains("Cisco")) ?? suppliers[0];

                var personal = custodyTypes.FirstOrDefault(c => c.Type == "Personal")?.Id ?? 1;
                var shared = custodyTypes.FirstOrDefault(c => c.Type == "Shared")?.Id ?? 2;

                var assetList = new[]
                {
                    new Asset
                    {
                        HospitalNo = "HOSP-SRV-001",
                        SerialNo = "SN-DELL-88219-R750",
                        Brand = "Dell EMC",
                        ModelNo = 750,
                        Price = 14500.00m,
                        Type = "Rack Server",
                        Status = "In Service",
                        SupplierId = dell.Id,
                        RoomId = srvRoom.Id,
                        UserId = abbas.Id,
                        CustodyTypeId = shared,
                        Specs = "{\"CPU\": \"2x Intel Xeon Gold 6330\", \"RAM\": \"128GB DDR4 ECC\", \"Storage\": \"8x 1.92TB SAS SSD RAID 10\"}",
                        BuyingDate = DateTime.Now.AddMonths(-14),
                        WarrantyTill = DateTime.Now.AddMonths(22),
                        NeedsPeriodic = true,
                        PeriodicInterval = 90,
                        LastPeriodic = DateTime.Now.AddDays(-75),
                        NextPeriodic = DateTime.Now.AddDays(15),
                        MaintenanceTeam = MaintenanceTeams.IT
                    },
                    new Asset
                    {
                        HospitalNo = "HOSP-US-004",
                        SerialNo = "SN-PH-99231-EPIQ7",
                        Brand = "Philips Healthcare",
                        ModelNo = 7,
                        Price = 48000.00m,
                        Type = "Ultrasound Scanner",
                        Status = "In Service",
                        SupplierId = philips.Id,
                        RoomId = radRoom.Id,
                        UserId = elena.Id,
                        CustodyTypeId = shared,
                        Specs = "{\"Model\": \"EPIQ 7 Diagnostic Ultrasound\", \"Probes\": \"Curved array, Linear, Phased array cardiac\", \"Screen\": \"21.5-inch HD Display\"}",
                        BuyingDate = DateTime.Now.AddMonths(-18),
                        WarrantyTill = DateTime.Now.AddMonths(18),
                        NeedsPeriodic = true,
                        PeriodicInterval = 180,
                        LastPeriodic = DateTime.Now.AddDays(-190), // Overdue!
                        NextPeriodic = DateTime.Now.AddDays(-10),
                        MaintenanceTeam = MaintenanceTeams.Biomedical
                    },
                    new Asset
                    {
                        HospitalNo = "HOSP-LAP-012",
                        SerialNo = "SN-HP-44102-G8",
                        Brand = "HP",
                        ModelNo = 850,
                        Price = 1250.00m,
                        Type = "Doctor Laptop",
                        Status = "Under Maintenance",
                        SupplierId = hp.Id,
                        RoomId = clinicRoom.Id,
                        UserId = sarah.Id,
                        CustodyTypeId = personal,
                        Specs = "{\"Model\": \"EliteBook 850 G8\", \"CPU\": \"Intel Core i7-1165G7\", \"RAM\": \"16GB\", \"SSD\": \"512GB NVMe\"}",
                        BuyingDate = DateTime.Now.AddMonths(-8),
                        WarrantyTill = DateTime.Now.AddMonths(28),
                        NeedsPeriodic = false,
                        MaintenanceTeam = MaintenanceTeams.IT
                    },
                    new Asset
                    {
                        HospitalNo = "HOSP-NET-003",
                        SerialNo = "SN-CS-77119-9300",
                        Brand = "Cisco",
                        ModelNo = 9300,
                        Price = 6200.00m,
                        Type = "Core Network Switch",
                        Status = "In Service",
                        SupplierId = cisco.Id,
                        RoomId = srvRoom.Id,
                        UserId = abbas.Id,
                        CustodyTypeId = shared,
                        Specs = "{\"Ports\": \"48x Gigabit PoE+\", \"Uplinks\": \"4x 10G SFP+\", \"Stacking\": \"StackWise-480\"}",
                        BuyingDate = DateTime.Now.AddMonths(-20),
                        WarrantyTill = DateTime.Now.AddMonths(40),
                        NeedsPeriodic = true,
                        PeriodicInterval = 180,
                        LastPeriodic = DateTime.Now.AddDays(-60),
                        NextPeriodic = DateTime.Now.AddDays(120),
                        MaintenanceTeam = MaintenanceTeams.IT
                    },
                    new Asset
                    {
                        HospitalNo = "HOSP-BIO-001",
                        SerialNo = "SN-GE-CARESCAPE-881",
                        Brand = "GE Healthcare",
                        ModelNo = 800,
                        Price = 12500.00m,
                        Type = "Patient Monitor",
                        Status = "In Service",
                        SupplierId = philips.Id,
                        RoomId = clinicRoom.Id,
                        UserId = sarah.Id,
                        CustodyTypeId = shared,
                        Specs = "{\"Model\": \"Carescape B850 Monitor\", \"Parameters\": \"ECG, SpO2, NIBP, Temp\"}",
                        BuyingDate = DateTime.Now.AddMonths(-12),
                        WarrantyTill = DateTime.Now.AddMonths(12),
                        NeedsPeriodic = true,
                        PeriodicInterval = 180,
                        LastPeriodic = DateTime.Now.AddDays(-60),
                        NextPeriodic = DateTime.Now.AddDays(120),
                        MaintenanceTeam = MaintenanceTeams.Biomedical
                    },
                    new Asset
                    {
                        HospitalNo = "HOSP-GEN-001",
                        SerialNo = "SN-SCH-GALAXY-300",
                        Brand = "Schneider Electric",
                        ModelNo = 300,
                        Price = 28500.00m,
                        Type = "Facility UPS",
                        Status = "In Service",
                        SupplierId = dell.Id,
                        RoomId = srvRoom.Id,
                        UserId = abbas.Id,
                        CustodyTypeId = shared,
                        Specs = "{\"Capacity\": \"40kVA / 36kW\", \"Battery Bank\": \"32x 12V 100Ah\"}",
                        BuyingDate = DateTime.Now.AddMonths(-16),
                        WarrantyTill = DateTime.Now.AddMonths(20),
                        NeedsPeriodic = true,
                        PeriodicInterval = 90,
                        LastPeriodic = DateTime.Now.AddDays(-30),
                        NextPeriodic = DateTime.Now.AddDays(60),
                        MaintenanceTeam = MaintenanceTeams.General
                    }
                };

                context.Assets.AddRange(assetList);
                context.SaveChanges();

                // Add Asset History records
                foreach (var a in assetList)
                {
                    context.AssetHistories.Add(new AssetHistory
                    {
                        AssetId = a.Id,
                        UserId = a.UserId ?? abbas.Id,
                        RoomId = a.RoomId ?? srvRoom.Id,
                        CustodyTypeId = a.CustodyTypeId ?? shared,
                        AssignedOn = a.BuyingDate ?? DateTime.Now.AddMonths(-6),
                        Notes = "Initial commissioning and asset custody tagging"
                    });
                }
                context.SaveChanges();
            }

            // Seed Tickets if empty
            if (!context.Tickets.Any())
            {
                var assets = context.Assets.ToList();
                var users = context.Users.ToList();

                var laptop = assets.FirstOrDefault(a => a.HospitalNo == "HOSP-LAP-012") ?? assets.First();
                var ultrasound = assets.FirstOrDefault(a => a.HospitalNo == "HOSP-US-004") ?? assets.First();
                var server = assets.FirstOrDefault(a => a.HospitalNo == "HOSP-SRV-001") ?? assets.First();

                var sarah = users.FirstOrDefault(u => u.DomainUser == "s.jenkins") ?? users.First();
                var elena = users.FirstOrDefault(u => u.DomainUser == "e.rostova") ?? users.First();
                var ahmed = users.FirstOrDefault(u => u.DomainUser == "a.mansour") ?? users.First();
                var abbas = users.FirstOrDefault(u => u.DomainUser == "a.amiri") ?? users.First();

                var ticket1 = new Ticket
                {
                    AssetId = laptop.Id,
                    OpenedBy = sarah.Id,
                    OnBehalfOf = sarah.Id,
                    Type = "Hardware",
                    StatusId = 2, // pending
                    Priority = 3, // High
                    OpenedDT = DateTime.Now.AddHours(-18)
                };

                var ticket2 = new Ticket
                {
                    AssetId = ultrasound.Id,
                    OpenedBy = elena.Id,
                    OnBehalfOf = elena.Id,
                    Type = "Maintenance",
                    StatusId = 1, // opened
                    Priority = 4, // Urgent
                    OpenedDT = DateTime.Now.AddHours(-4)
                };

                var ticket3 = new Ticket
                {
                    AssetId = server.Id,
                    OpenedBy = abbas.Id,
                    OnBehalfOf = abbas.Id,
                    Type = "Hardware",
                    StatusId = 5, // closed
                    Priority = 2, // Medium
                    OpenedDT = DateTime.Now.AddDays(-10),
                    ClosedDT = DateTime.Now.AddDays(-8)
                };

                context.Tickets.AddRange(ticket1, ticket2, ticket3);
                context.SaveChanges();

                // Assign handlers
                context.TicketHandlers.Add(new TicketHandler
                {
                    TicketId = ticket1.Id,
                    TechUserId = ahmed.Id,
                    AssignedDT = DateTime.Now.AddHours(-16)
                });

                // Add discussion messages
                context.TicketMessages.AddRange(
                    new TicketMsg
                    {
                        TicketId = ticket1.Id,
                        UserId = sarah.Id,
                        Msg = "Laptop displays blue screen (CRITICAL_PROCESS_DIED) shortly after booting into Windows.",
                        TimeStamp = DateTime.Now.AddHours(-18)
                    },
                    new TicketMsg
                    {
                        TicketId = ticket1.Id,
                        UserId = ahmed.Id,
                        Msg = "Hardware diagnostics run. NVMe SSD shows bad SMART sectors. Ordering replacement drive under warranty.",
                        TimeStamp = DateTime.Now.AddHours(-15)
                    },
                    new TicketMsg
                    {
                        TicketId = ticket2.Id,
                        UserId = elena.Id,
                        Msg = "Periodic preventive calibration is overdue by 10 days. Image noise detected on linear probe.",
                        TimeStamp = DateTime.Now.AddHours(-4)
                    }
                );

                // Add maintenance record for closed ticket
                context.AssetMaintenances.Add(new AssetMaintenance
                {
                    AssetId = server.Id,
                    TicketId = ticket3.Id,
                    MaintenanceType = 1, // Breakdown
                    UserId = ahmed.Id,
                    Description = "Replaced faulty hot-swap power supply unit PSU2 in Dell R750 server.",
                    PartsReplaced = "{\"Part\": \"Dell 1400W Platinum Hot-Plug Redundant PSU\", \"PartNumber\": \"DP/N 0XXV21\"}",
                    TimStamp = DateTime.Now.AddDays(-8)
                });

                context.SaveChanges();
            }
        }
    }
}
