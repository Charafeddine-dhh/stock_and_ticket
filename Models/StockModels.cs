using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace StockPortal.Models
{
    [Table("custody_type")]
    public class CustodyType
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("type")]
        [StringLength(255)]
        public string? Type { get; set; }

        public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
    }

    [Table("ticket_status")]
    public class TicketStatus
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("status")]
        [StringLength(255)]
        public string? Status { get; set; }

        public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    }

    [Table("departments")]
    public class Department
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        [StringLength(255)]
        [Display(Name = "Department Name")]
        public string? Name { get; set; }

        [Column("headUserID")]
        public int? HeadUserId { get; set; }

        [ForeignKey("HeadUserId")]
        public virtual User? HeadUser { get; set; }

        public virtual ICollection<User> Users { get; set; } = new List<User>();
    }

    [Table("rooms")]
    public class Room
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("roomNo")]
        [Display(Name = "Room Number")]
        public int? RoomNo { get; set; }

        [Column("floor")]
        [Display(Name = "Floor")]
        public int? Floor { get; set; }

        [NotMapped]
        public string DisplayName => $"Room {RoomNo ?? 0} (Floor {Floor ?? 0})";

        public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
    }

    [Table("suppliers")]
    public class Supplier
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("name")]
        [StringLength(255)]
        [Display(Name = "Supplier Name")]
        public string? Name { get; set; }

        [Column("isActive")]
        public bool? IsActive { get; set; } = true;

        [Column("type")]
        [Display(Name = "Supplier Type")]
        public int? Type { get; set; } // 1: Items, 2: Maintenance, 3: Both

        [NotMapped]
        public string TypeName => Type switch
        {
            1 => "Equipment & Items",
            2 => "Maintenance Provider",
            3 => "Full Service (Items & Maintenance)",
            _ => "General Vendor"
        };

        public virtual ICollection<Asset> Assets { get; set; } = new List<Asset>();
    }

    [Table("users")]
    public class User
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("fullName")]
        [StringLength(255)]
        [Display(Name = "Full Name")]
        public string? FullName { get; set; }

        [Column("departmentID")]
        public int? DepartmentId { get; set; }

        [ForeignKey("DepartmentId")]
        public virtual Department? Department { get; set; }

        [Column("domainUser")]
        [StringLength(255)]
        [Display(Name = "Username / Domain")]
        public string? DomainUser { get; set; }

        [Column("role")]
        [StringLength(255)]
        [Display(Name = "Role / Title")]
        public string? Role { get; set; }

        [Column("isActive")]
        public bool? IsActive { get; set; } = true;

        public virtual ICollection<Asset> CustodyAssets { get; set; } = new List<Asset>();
        public virtual ICollection<Ticket> OpenedTickets { get; set; } = new List<Ticket>();
    }

    [Table("assets")]
    public class Asset
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("price", TypeName = "decimal(10,2)")]
        [Display(Name = "Purchase Price")]
        [DataType(DataType.Currency)]
        public decimal? Price { get; set; }

        [Column("hospitalNo")]
        [StringLength(255)]
        [Display(Name = "Asset Tag / Hospital No")]
        public string? HospitalNo { get; set; }

        [Column("serialNo")]
        [StringLength(255)]
        [Display(Name = "Serial Number")]
        public string? SerialNo { get; set; }

        [Column("bran")]
        [StringLength(255)]
        [Display(Name = "Brand / Manufacturer")]
        public string? Brand { get; set; }

        [Column("modelNo")]
        [Display(Name = "Model / Version")]
        public int? ModelNo { get; set; }

        [Column("supplierID")]
        public int? SupplierId { get; set; }

        [ForeignKey("SupplierId")]
        public virtual Supplier? Supplier { get; set; }

        [Column("status")]
        [StringLength(255)]
        [Display(Name = "Operational Status")]
        public string? Status { get; set; } = "In Service";

        [Column("userID")]
        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        [Display(Name = "Assigned Custodian")]
        public virtual User? User { get; set; }

        [Column("roomID")]
        public int? RoomId { get; set; }

        [ForeignKey("RoomId")]
        [Display(Name = "Location / Room")]
        public virtual Room? Room { get; set; }

        [Column("custodyType")]
        public int? CustodyTypeId { get; set; }

        [ForeignKey("CustodyTypeId")]
        public virtual CustodyType? CustodyType { get; set; }

        [Column("type")]
        [StringLength(255)]
        [Display(Name = "Equipment Type")]
        public string? Type { get; set; } // Laptop, Monitor, Medical Device, Server, Printer, etc.

        [Column("warrantyTill", TypeName = "date")]
        [Display(Name = "Warranty Expiration")]
        [DataType(DataType.Date)]
        public DateTime? WarrantyTill { get; set; }

        [Column("specs")]
        [Display(Name = "Technical Specifications")]
        public string? Specs { get; set; }

        [Column("buyingDate")]
        [Display(Name = "Purchase Date")]
        public DateTime? BuyingDate { get; set; }

        [Column("needsPeriodic")]
        [Display(Name = "Requires Periodic Maintenance")]
        public bool? NeedsPeriodic { get; set; } = false;

        [Column("periodicInterval")]
        [Display(Name = "Interval (Days)")]
        public int? PeriodicInterval { get; set; }

        [Column("lastPeriodic", TypeName = "date")]
        [Display(Name = "Last Periodic Check")]
        [DataType(DataType.Date)]
        public DateTime? LastPeriodic { get; set; }

        [Column("nextPeriodic", TypeName = "date")]
        [Display(Name = "Next Periodic Due")]
        [DataType(DataType.Date)]
        public DateTime? NextPeriodic { get; set; }

        public virtual ICollection<AssetHistory> History { get; set; } = new List<AssetHistory>();
        public virtual ICollection<AssetMaintenance> MaintenanceRecords { get; set; } = new List<AssetMaintenance>();
        public virtual ICollection<Ticket> Tickets { get; set; } = new List<Ticket>();
    }

    [Table("asset_history")]
    public class AssetHistory
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("assetID")]
        public int AssetId { get; set; }

        [ForeignKey("AssetId")]
        public virtual Asset? Asset { get; set; }

        [Column("userID")]
        public int UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [Column("roomID")]
        public int RoomId { get; set; }

        [ForeignKey("RoomId")]
        public virtual Room? Room { get; set; }

        [Column("CustodyType")]
        public int CustodyTypeId { get; set; }

        [ForeignKey("CustodyTypeId")]
        public virtual CustodyType? CustodyType { get; set; }

        [Column("assignedOn")]
        public DateTime AssignedOn { get; set; } = DateTime.Now;

        [Column("returnedOn")]
        public DateTime? ReturnedOn { get; set; }

        [Column("notes")]
        [StringLength(255)]
        public string? Notes { get; set; }

        [Column("timeStamp")]
        public DateTime TimeStamp { get; set; } = DateTime.Now;
    }

    [Table("tickets")]
    public class Ticket
    {
        [Key]
        [Column("id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Column("assetID")]
        public int? AssetId { get; set; }

        [ForeignKey("AssetId")]
        public virtual Asset? Asset { get; set; }

        [Column("openedBy")]
        public int? OpenedBy { get; set; }

        [ForeignKey("OpenedBy")]
        public virtual User? OpenedByUser { get; set; }

        [Column("onBehalfOf")]
        public int? OnBehalfOf { get; set; }

        [ForeignKey("OnBehalfOf")]
        public virtual User? OnBehalfOfUser { get; set; }

        [Column("type")]
        [StringLength(255)]
        [Display(Name = "Issue Category")]
        public string? Type { get; set; } = "Hardware"; // Hardware / Software / Network / Maintenance

        [Column("status")]
        public int? StatusId { get; set; } = 1; // 1: opened, 2: pending, 3: delayed, 4: canceled, 5: closed

        [ForeignKey("StatusId")]
        public virtual TicketStatus? Status { get; set; }

        [Column("priority")]
        [Display(Name = "Priority Level")]
        public int? Priority { get; set; } = 2; // 1: Low, 2: Medium, 3: High, 4: Urgent

        [Column("openedDT")]
        [Display(Name = "Date Opened")]
        public DateTime? OpenedDT { get; set; } = DateTime.Now;

        [Column("closedDT")]
        [Display(Name = "Date Closed")]
        public DateTime? ClosedDT { get; set; }

        public virtual TicketHandler? Handler { get; set; }
        public virtual ICollection<TicketMsg> Messages { get; set; } = new List<TicketMsg>();
        public virtual ICollection<AssetMaintenance> MaintenanceLogs { get; set; } = new List<AssetMaintenance>();

        [NotMapped]
        public string PriorityName => Priority switch
        {
            1 => "Low",
            2 => "Medium",
            3 => "High",
            4 => "Urgent",
            _ => "Medium"
        };
    }

    [Table("ticket_handler")]
    public class TicketHandler
    {
        [Key]
        [Column("ticketID")]
        public int TicketId { get; set; }

        [ForeignKey("TicketId")]
        public virtual Ticket? Ticket { get; set; }

        [Column("techUserID")]
        public int? TechUserId { get; set; }

        [ForeignKey("TechUserId")]
        public virtual User? TechUser { get; set; }

        [Column("assignedDT")]
        public DateTime? AssignedDT { get; set; } = DateTime.Now;
    }

    [Table("ticket_msg")]
    public class TicketMsg
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("ticketID")]
        public int? TicketId { get; set; }

        [ForeignKey("TicketId")]
        public virtual Ticket? Ticket { get; set; }

        [Column("userID")]
        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; set; }

        [Column("msg")]
        [StringLength(1023)]
        public string? Msg { get; set; }

        [Column("timeStamp")]
        public DateTime? TimeStamp { get; set; } = DateTime.Now;
    }

    [Table("asset_maintenance")]
    public class AssetMaintenance
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("assetID")]
        public int? AssetId { get; set; }

        [ForeignKey("AssetId")]
        public virtual Asset? Asset { get; set; }

        [Column("ticketID")]
        public int? TicketId { get; set; }

        [ForeignKey("TicketId")]
        public virtual Ticket? Ticket { get; set; }

        [Column("maintenanceType")]
        [Display(Name = "Maintenance Type")]
        public int? MaintenanceType { get; set; } // 1: Corrective / Repair, 2: Periodic / Preventive

        [Column("userID")]
        public int? UserId { get; set; }

        [ForeignKey("UserId")]
        public virtual User? Technician { get; set; }

        [Column("supplierID")]
        public int? SupplierId { get; set; }

        [ForeignKey("SupplierId")]
        public virtual Supplier? Supplier { get; set; }

        [Column("description")]
        [StringLength(1024)]
        public string? Description { get; set; }

        [Column("partsReplaced")]
        public string? PartsReplaced { get; set; }

        [Column("timStamp")]
        public DateTime? TimStamp { get; set; } = DateTime.Now;

        [NotMapped]
        public string TypeName => MaintenanceType == 2 ? "Preventive / Periodic" : "Breakdown / Repair";
    }
}
