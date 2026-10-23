using System;
using System.Collections.Generic;
using StockPortal.Models;

namespace StockPortal.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalAssets { get; set; }
        public int InServiceAssets { get; set; }
        public int InStockAssets { get; set; }
        public int UnderMaintenanceAssets { get; set; }
        public decimal TotalInventoryValue { get; set; }

        public int TotalTickets { get; set; }
        public int OpenTickets { get; set; }
        public int PendingTickets { get; set; }
        public int DelayedTickets { get; set; }
        public int ClosedTickets { get; set; }

        public List<Ticket> UrgentTickets { get; set; } = new();
        public List<Ticket> RecentTickets { get; set; } = new();
        public List<Asset> OverduePeriodicAssets { get; set; } = new();
        public List<Asset> UpcomingPeriodicAssets { get; set; } = new();
        public List<AssetMaintenance> RecentMaintenance { get; set; } = new();
    }

    public class TicketDetailsViewModel
    {
        public Ticket Ticket { get; set; } = null!;
        public List<TicketStatus> Statuses { get; set; } = new();
        public List<User> Technicians { get; set; } = new();
        public List<Supplier> Suppliers { get; set; } = new();

        // Forms inside Details view
        public string? NewMessage { get; set; }
        public int? NewTechUserId { get; set; }
        public int? NewStatusId { get; set; }

        // Quick Maintenance Log
        public string? MaintenanceDescription { get; set; }
        public string? MaintenancePartsReplaced { get; set; }
        public int? MaintenanceSupplierId { get; set; }
    }

    public class AssetDetailsViewModel
    {
        public Asset Asset { get; set; } = null!;
        public List<User> AvailableUsers { get; set; } = new();
        public List<Room> AvailableRooms { get; set; } = new();
        public List<CustodyType> AvailableCustodyTypes { get; set; } = new();

        // Transfer Custody Form
        public int NewUserId { get; set; }
        public int NewRoomId { get; set; }
        public int NewCustodyTypeId { get; set; }
        public string? TransferNotes { get; set; }
    }

    public class MaintenanceScheduleViewModel
    {
        public List<Asset> OverdueAssets { get; set; } = new();
        public List<Asset> DueWithin30DaysAssets { get; set; } = new();
        public List<Asset> UpToDateAssets { get; set; } = new();
        public List<AssetMaintenance> RecentLogs { get; set; } = new();
    }

    public class AutoTicketViewModel
    {
        public string DetectedPcName { get; set; } = string.Empty;
        public string ClientIp { get; set; } = string.Empty;
        public bool IsAssetFound { get; set; }
        public Asset? MatchedAsset { get; set; }

        // Form Fields
        public int? SelectedAssetId { get; set; }
        public int? SelectedRoomId { get; set; }
        public int? SelectedUserId { get; set; }
        public string Category { get; set; } = "Hardware";
        public int Priority { get; set; } = 2; // Medium
        public string? UserNotes { get; set; }

        // Dropdown data
        public List<Asset> AvailableAssets { get; set; } = new();
        public List<Room> AvailableRooms { get; set; } = new();
        public List<User> AvailableUsers { get; set; } = new();
    }
}
