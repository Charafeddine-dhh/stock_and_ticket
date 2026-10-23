using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using StockPortal.Models;

namespace StockPortal.Data
{
    public static class DbInitializer
    {
        public static void Initialize(StockDbContext context)
        {
            context.Database.EnsureCreated();

            // Ensure notifications table exists
            try
            {
                var isSqlite = context.Database.ProviderName?.Contains("Sqlite") == true;
                if (isSqlite)
                {
                    context.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS notifications (
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            userID INTEGER NOT NULL,
                            ticketID INTEGER NULL,
                            title TEXT NOT NULL,
                            message TEXT NOT NULL,
                            type TEXT NOT NULL,
                            linkUrl TEXT NULL,
                            isRead INTEGER NOT NULL DEFAULT 0,
                            createdAt TEXT NOT NULL
                        );");
                }
                else
                {
                    context.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS `notifications` (
                            `id` int NOT NULL AUTO_INCREMENT,
                            `userID` int NOT NULL,
                            `ticketID` int NULL,
                            `title` varchar(255) NOT NULL,
                            `message` varchar(1024) NOT NULL,
                            `type` varchar(50) NOT NULL,
                            `linkUrl` varchar(255) NULL,
                            `isRead` tinyint(1) NOT NULL DEFAULT 0,
                            `createdAt` datetime NOT NULL,
                            PRIMARY KEY (`id`)
                        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");
                }
            }
            catch {}

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

            // Ensure Universal Device Forms exist
            if (!context.DeviceForms.Any())
            {
                var templates = new[]
                {
                    // --- IT MAINTENANCE TEMPLATES ---
                    new DeviceForm
                    {
                        Name = "Laptop",
                        Department = MaintenanceTeams.IT,
                        Form = @"[
                            {""name"":""Processor"",""label"":""Processor / CPU"",""type"":""text"",""required"":true,""placeholder"":""e.g. Intel Core i7-1165G7""},
                            {""name"":""RAM"",""label"":""Installed Memory (RAM)"",""type"":""select"",""options"":[""8 GB"",""16 GB"",""32 GB"",""64 GB""],""required"":true},
                            {""name"":""Storage"",""label"":""Primary Storage (SSD)"",""type"":""text"",""required"":true,""placeholder"":""e.g. 512GB NVMe SSD""},
                            {""name"":""OS"",""label"":""Operating System"",""type"":""select"",""options"":[""Windows 11 Pro"",""Windows 10 Pro"",""Ubuntu Linux"",""macOS""],""required"":true},
                            {""name"":""ScreenSize"",""label"":""Display Size"",""type"":""text"",""required"":false,""placeholder"":""e.g. 15.6 inch FHD""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Desktop Workstation",
                        Department = MaintenanceTeams.IT,
                        Form = @"[
                            {""name"":""FormFactor"",""label"":""Chassis / Form Factor"",""type"":""select"",""options"":[""Small Form Factor (SFF)"",""Mini Tower"",""All-in-One (AIO)"",""Micro / Tiny PC""],""required"":true},
                            {""name"":""CPU"",""label"":""Processor (CPU)"",""type"":""text"",""required"":true,""placeholder"":""e.g. Intel Core i5-12500 / Ryzen 5 5600G""},
                            {""name"":""RAM"",""label"":""Installed RAM"",""type"":""select"",""options"":[""8 GB"",""16 GB"",""32 GB"",""64 GB""],""required"":true},
                            {""name"":""Storage"",""label"":""Primary Drive"",""type"":""text"",""required"":true,""placeholder"":""e.g. 256GB SSD + 1TB HDD""},
                            {""name"":""GPU"",""label"":""Graphics Card"",""type"":""text"",""required"":false,""placeholder"":""e.g. Integrated Intel UHD / NVIDIA T600""},
                            {""name"":""OS"",""label"":""Operating System"",""type"":""select"",""options"":[""Windows 11 Pro"",""Windows 10 Pro""],""required"":true}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Core Switch",
                        Department = MaintenanceTeams.IT,
                        Form = @"[
                            {""name"":""Ports"",""label"":""Ethernet Port Count"",""type"":""select"",""options"":[""24-Port Gigabit"",""48-Port Gigabit"",""24-Port Multi-Gig"",""48-Port Multi-Gig""],""required"":true},
                            {""name"":""PoE"",""label"":""Power Over Ethernet"",""type"":""select"",""options"":[""PoE+ (30W/port)"",""PoE++ (60W/port)"",""Non-PoE""],""required"":true},
                            {""name"":""Uplinks"",""label"":""Uplink SFP Ports"",""type"":""text"",""required"":true,""placeholder"":""e.g. 4x 10G SFP+""},
                            {""name"":""ManagementIP"",""label"":""Default Management IP"",""type"":""text"",""required"":false,""placeholder"":""e.g. 10.10.1.5/24""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Network Printer",
                        Department = MaintenanceTeams.IT,
                        Form = @"[
                            {""name"":""Tech"",""label"":""Print Technology"",""type"":""select"",""options"":[""Laser Monochrome"",""Laser Color"",""Thermal Barcode / Wristband"",""Multi-Function Copier (MFP)""],""required"":true},
                            {""name"":""Resolution"",""label"":""Max Resolution"",""type"":""text"",""required"":false,""placeholder"":""e.g. 1200 x 1200 dpi""},
                            {""name"":""Duplex"",""label"":""Automatic Duplex (2-sided)"",""type"":""select"",""options"":[""Yes"",""No""],""required"":true},
                            {""name"":""TrayCapacity"",""label"":""Paper Tray Capacity"",""type"":""text"",""required"":false,""placeholder"":""e.g. 2x 500-sheet trays""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Rack Server",
                        Department = MaintenanceTeams.IT,
                        Form = @"[
                            {""name"":""Chassis"",""label"":""Rack Units (U)"",""type"":""select"",""options"":[""1U Rack"",""2U Rack"",""4U Rack"",""Blade Enclosure""],""required"":true},
                            {""name"":""CPU"",""label"":""Processors Installed"",""type"":""text"",""required"":true,""placeholder"":""e.g. 2x Intel Xeon Silver 4314""},
                            {""name"":""RAM"",""label"":""Installed ECC Memory"",""type"":""select"",""options"":[""64 GB DDR4"",""128 GB DDR4"",""256 GB DDR4"",""512 GB DDR4""],""required"":true},
                            {""name"":""RAID"",""label"":""Storage / RAID Setup"",""type"":""text"",""required"":true,""placeholder"":""e.g. 6x 1.92TB SAS SSD RAID 6""},
                            {""name"":""PSU"",""label"":""Power Supply Units"",""type"":""text"",""required"":false,""placeholder"":""e.g. Dual Redundant 800W Platinum""}
                        ]"
                    },

                    // --- BIOMEDICAL ENGINEERING TEMPLATES ---
                    new DeviceForm
                    {
                        Name = "Patient Monitor",
                        Department = MaintenanceTeams.Biomedical,
                        Form = @"[
                            {""name"":""Screen"",""label"":""Display Screen"",""type"":""text"",""required"":true,""placeholder"":""e.g. 19-inch High Resolution Medical Touchscreen""},
                            {""name"":""Parameters"",""label"":""Monitored Parameters"",""type"":""text"",""required"":true,""placeholder"":""e.g. ECG, SpO2, NIBP, Invasive Pressure, Temp, EtCO2""},
                            {""name"":""LeadCount"",""label"":""ECG Lead Option"",""type"":""select"",""options"":[""3-Lead"",""5-Lead"",""12-Lead Diagnostic""],""required"":true},
                            {""name"":""Battery"",""label"":""Battery Autonomy"",""type"":""text"",""required"":false,""placeholder"":""e.g. 4 Hours Lithium-ion""},
                            {""name"":""Telemetry"",""label"":""Central Telemetry Link"",""type"":""select"",""options"":[""Wired Ethernet"",""Wireless 5GHz Wi-Fi"",""Dual Link""],""required"":true}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "ICU Ventilator",
                        Department = MaintenanceTeams.Biomedical,
                        Form = @"[
                            {""name"":""VentModes"",""label"":""Ventilation Modes"",""type"":""text"",""required"":true,""placeholder"":""e.g. VC-CMV, PC-BIPAP, PSV, APRV, CPAP/NIV""},
                            {""name"":""PatientRange"",""label"":""Patient Demographic"",""type"":""select"",""options"":[""Adult & Pediatric"",""Neonatal Specialized"",""Universal (Neonatal to Adult)""],""required"":true},
                            {""name"":""GasSupply"",""label"":""Gas Supply Requirements"",""type"":""text"",""required"":true,""placeholder"":""e.g. Pipeline O2 & Medical Air (2.8 - 6 bar)""},
                            {""name"":""InternalBattery"",""label"":""Battery Backup Time"",""type"":""text"",""required"":false,""placeholder"":""e.g. 180 Minutes Internal Li-Ion""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Defibrillator",
                        Department = MaintenanceTeams.Biomedical,
                        Form = @"[
                            {""name"":""Waveform"",""label"":""Defibrillation Waveform"",""type"":""select"",""options"":[""Biphasic Truncated Exponential"",""Rectilinear Biphasic""],""required"":true},
                            {""name"":""Pacing"",""label"":""External Transcutaneous Pacing"",""type"":""select"",""options"":[""Yes - Demand and Fixed"",""No""],""required"":true},
                            {""name"":""CPRFeedback"",""label"":""Real-time CPR Sensor Feedback"",""type"":""select"",""options"":[""Equipped"",""Not Equipped""],""required"":true},
                            {""name"":""BatteryType"",""label"":""Rechargeable Battery Chemistry"",""type"":""text"",""required"":false,""placeholder"":""e.g. Smart Rechargeable Lithium-ion""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Ultrasound Scanner",
                        Department = MaintenanceTeams.Biomedical,
                        Form = @"[
                            {""name"":""Probes"",""label"":""Included Transducer Probes"",""type"":""text"",""required"":true,""placeholder"":""e.g. Convex Abdominal, Linear Vascular, Phased Cardiac""},
                            {""name"":""ProbePorts"",""label"":""Active Transducer Ports"",""type"":""select"",""options"":[""2 Active Ports"",""3 Active Ports"",""4 Active Ports""],""required"":true},
                            {""name"":""Doppler"",""label"":""Doppler Capabilities"",""type"":""text"",""required"":false,""placeholder"":""e.g. Color Doppler, Pulsed Wave, Continuous Wave, Power Doppler""},
                            {""name"":""Display"",""label"":""Monitor Specification"",""type"":""text"",""required"":false,""placeholder"":""e.g. 21.5-inch OLED Articulating Arm""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Mobile X-Ray",
                        Department = MaintenanceTeams.Biomedical,
                        Form = @"[
                            {""name"":""GeneratorOutput"",""label"":""Generator Output (kW)"",""type"":""select"",""options"":[""15 kW High Frequency"",""30 kW High Frequency"",""35 kW High Frequency"",""50 kW Ultra High Frequency""],""required"":true},
                            {""name"":""Detector"",""label"":""Flat Panel Detector (FPD)"",""type"":""text"",""required"":true,""placeholder"":""e.g. 14x17 inch Wireless Cesium Iodide (CsI)""},
                            {""name"":""DriveSystem"",""label"":""Motorized Travel Assist"",""type"":""select"",""options"":[""Dual Motorized Drive"",""Manual Push""],""required"":true}
                        ]"
                    },

                    // --- GENERAL FACILITY MAINTENANCE TEMPLATES ---
                    new DeviceForm
                    {
                        Name = "Facility UPS",
                        Department = MaintenanceTeams.General,
                        Form = @"[
                            {""name"":""Capacity"",""label"":""Apparent / Active Power"",""type"":""select"",""options"":[""10 kVA / 9 kW"",""20 kVA / 18 kW"",""40 kVA / 36 kW"",""80 kVA / 72 kW"",""120 kVA / 108 kW""],""required"":true},
                            {""name"":""BatteryBank"",""label"":""Battery Bank Specification"",""type"":""text"",""required"":true,""placeholder"":""e.g. 32x 12V 100Ah VRLA Batteries""},
                            {""name"":""Autonomy"",""label"":""Full Load Autonomy Time"",""type"":""text"",""required"":false,""placeholder"":""e.g. 45 Minutes Critical Hospital Load""},
                            {""name"":""InputVoltage"",""label"":""Nominal Input / Output"",""type"":""select"",""options"":[""380/400V 3-Phase 4-Wire"",""220/230V Single Phase""],""required"":true}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "HVAC Chiller",
                        Department = MaintenanceTeams.General,
                        Form = @"[
                            {""name"":""CoolingTons"",""label"":""Cooling Capacity (TR / kW)"",""type"":""select"",""options"":[""25 TR (88 kW)"",""50 TR (175 kW)"",""100 TR (350 kW)"",""200 TR (700 kW)""],""required"":true},
                            {""name"":""Refrigerant"",""label"":""Refrigerant Gas"",""type"":""select"",""options"":[""R-410A Eco"",""R-134a"",""R-32"",""R-407C""],""required"":true},
                            {""name"":""CompressorCount"",""label"":""Compressor Circuit Count"",""type"":""select"",""options"":[""Single Scroll"",""Dual Scroll"",""Dual Screw Hermetic""],""required"":true},
                            {""name"":""PowerFeed"",""label"":""Main Electrical Supply"",""type"":""text"",""required"":false,""placeholder"":""e.g. 380V / 3Ph / 50Hz, 125A Breaker""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Emergency Generator",
                        Department = MaintenanceTeams.General,
                        Form = @"[
                            {""name"":""PrimeRating"",""label"":""Prime Power Rating (kVA)"",""type"":""select"",""options"":[""150 kVA"",""250 kVA"",""500 kVA"",""750 kVA"",""1000 kVA""],""required"":true},
                            {""name"":""EngineMake"",""label"":""Diesel Engine Brand / Model"",""type"":""text"",""required"":true,""placeholder"":""e.g. Perkins 2506A / Cummins QSK23""},
                            {""name"":""ATS"",""label"":""Automatic Transfer Switch"",""type"":""text"",""required"":false,""placeholder"":""e.g. ASCO 7000 Series 4-Pole 800A""},
                            {""name"":""FuelTank"",""label"":""Base Fuel Tank Capacity"",""type"":""text"",""required"":false,""placeholder"":""e.g. 1000 Liters (12 hours runtime)""}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Medical Gas Manifold",
                        Department = MaintenanceTeams.General,
                        Form = @"[
                            {""name"":""GasService"",""label"":""Medical Gas Type"",""type"":""select"",""options"":[""Medical Oxygen (O2)"",""Medical Air 4-Bar"",""Surgical Air 7-Bar"",""Nitrous Oxide (N2O)"",""Medical Vacuum""],""required"":true},
                            {""name"":""BankSize"",""label"":""Cylinder Bank Configuration"",""type"":""text"",""required"":true,""placeholder"":""e.g. 2x 10-Cylinder Dual Bank Auto-Changeover""},
                            {""name"":""SupplyPressure"",""label"":""Regulated Distribution Pressure"",""type"":""text"",""required"":true,""placeholder"":""e.g. 4.2 bar (60 psig)""},
                            {""name"":""ReserveSupply"",""label"":""Emergency Reserve Bank"",""type"":""select"",""options"":[""Third Emergency Bank Attached"",""Standalone High-Pressure Pack""],""required"":false}
                        ]"
                    },
                    new DeviceForm
                    {
                        Name = "Cleanroom HEPA Filter",
                        Department = MaintenanceTeams.General,
                        Form = @"[
                            {""name"":""Efficiency"",""label"":""Filter Rating / Efficiency"",""type"":""select"",""options"":[""H14 HEPA 99.995%"",""U15 ULPA 99.9995%"",""H13 HEPA 99.95%""],""required"":true},
                            {""name"":""FlowRate"",""label"":""Rated Airflow Volume"",""type"":""text"",""required"":true,""placeholder"":""e.g. 1200 CFM (2000 m3/h)""},
                            {""name"":""PressureGauge"",""label"":""Magnehelic Differential Gauge"",""type"":""select"",""options"":[""Installed (0-500 Pa)"",""Digital Sensor to BMS""],""required"":true},
                            {""name"":""LocationServed"",""label"":""Hospital Critical Area"",""type"":""text"",""required"":false,""placeholder"":""e.g. Operating Theatre 1 / Bone Marrow Transplant Room""}
                        ]"
                    }
                };

                context.DeviceForms.AddRange(templates);
                context.SaveChanges();
            }
        }
    }
}
