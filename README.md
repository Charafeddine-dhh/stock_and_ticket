# IT Stock & Helpdesk Management Portal

An enterprise-grade, intranet-optimized IT Asset Inventory and Service Helpdesk Portal built with **ASP.NET Core 10.0 MVC** and **Entity Framework Core**. Designed for corporate, hospital, and enterprise IT environments requiring tight asset custody auditing, scheduled preventive maintenance, and ticketing workflows with Windows Domain Single Sign-On (SSO).

---

## 🌟 Key Features

### 1. 📦 Stock & Asset Management
- **Hardware Tracking:** Track asset tags (Hospital No), serial numbers, manufacturers, models, equipment types, pricing, and purchase dates.
- **Warranty Monitoring:** Real-time warranty expiration alerts (active, expiring in 30 days, expired).
- **Physical Custody Audit Trail:** Automatic tracking in `asset_history` whenever equipment changes room, floor, or assigned employee.
- **Hardware Lifecycle Management:** Decommission / retire assets with audit logs and deletion safety guards.
- **Export & Label Printing:**
  - One-click **CSV Spreadsheet Export** formatted for Microsoft Excel.
  - Printable **Thermal Barcode / Tag Labels** formatted for physical asset tagging.

### 2. 🎫 Helpdesk & Service Ticketing
- **Categorized Issue Tracking:** Hardware, Software, Network, and Maintenance categories.
- **Priority Matrix:** Low, Medium, High, and Urgent (fire-priority) queues.
- **Technician Dispatch:** Assign dedicated IT staff or field engineers to tickets.
- **Interactive Discussion Timeline:** Threaded comments, updates, and diagnostic messages with author timestamps.
- **Quick Resolution:** One-click ticket closure with resolution notes.
- **Printable Work Orders:** Formatted technician field service sheets with signature sign-off boxes.
- **Export to CSV:** Download filtered ticket audit logs for weekly reporting.

### 3. 🛠️ Scheduled & Preventive Maintenance
- **Periodic Servicing Cycles:** Configure custom inspection intervals (e.g. 90-day, 180-day server/imaging checks).
- **Overdue Inspection Alerts:** Real-time detection and dashboard warning badges for overdue maintenance.
- **Quick Checkup Completion:** Log routine maintenance checks with one click.
- **Detailed Work Orders:** Log repair descriptions, contractor details, and replaced parts/components.

### 4. 🗂️ Master Directory & Administration
- **Staff & User Management:** User directory with role definitions and active/inactive status toggling.
- **Physical Locations:** Multi-floor room directory.
- **Vendor / Contractor Directory:** Authorized suppliers classified by equipment, maintenance, or full service.
- **Hospital Departments:** Department directory with designated department heads.

### 5. 🔐 Windows Domain SSO & Developer Switching
- **Automatic Domain Detection:** Auto-authenticates the current Windows domain user without requiring manual login passwords.
- **Testing Profile Switcher:** Fast identity switcher in the top navigation bar allowing administrators and developers to switch between IT Managers, Field Technicians, and Department Doctors with a single click during testing.

### 6. 💾 Multi-Database Architecture
- **SQLite (Zero Configuration):** Default mode enables developers to clone and run immediately without setting up external database servers.
- **MariaDB / MySQL Support:** Enterprise MySQL/MariaDB provider enabled simply by updating `DatabaseProvider` in `appsettings.json`.

---

## 🚀 Getting Started

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)

### Run the Application
Clone or navigate to the repository folder and run:

```bash
# Build the application
dotnet build

# Launch the local web server
dotnet run
```

Open your browser at:
`http://localhost:5174` (or port specified in terminal).

The application automatically creates the database and populates sample hospital IT inventory, staff directory, and active service tickets on first launch.

---

## ⚙️ Configuration (`appsettings.json`)

To switch between SQLite and MySQL:

```json
{
  "DatabaseProvider": "Sqlite", // Options: "Sqlite" or "MySql"
  "ConnectionStrings": {
    "SqliteConnection": "Data Source=stockportal.db",
    "MySqlConnection": "server=localhost;port=3306;database=stock;user=root;password=;CharSet=utf8mb4;"
  }
}
```

---

## 📁 Project Structure

```
stock_and_ticket/
├── Controllers/
│   ├── AccountController.cs       # Profile, session switching
│   ├── AssetsController.cs        # Asset CRUD, custody transfer, CSV export, print tags
│   ├── DirectoryController.cs     # Users, rooms, suppliers, departments
│   ├── HomeController.cs          # Executive dashboard & metrics
│   ├── MaintenanceController.cs    # Periodic schedules, inspection logs
│   └── TicketsController.cs       # Ticketing queue, technician assignments, printable work orders
├── Data/
│   ├── StockDbContext.cs          # Entity Framework Core database context
│   └── DbInitializer.cs           # Database migration & automatic sample data seeder
├── Middleware/
│   └── DomainAuthMiddleware.cs    # Windows Domain SSO authentication
├── Models/
│   ├── ErrorViewModel.cs
│   └── StockModels.cs             # EF Core entities (Asset, Ticket, User, Room, etc.)
├── Services/
│   └── CurrentUserService.cs      # Current authenticated user provider
├── ViewModels/
│   └── PortalViewModels.cs        # Strongly typed dashboard & details view models
├── Views/                         # Razor views with Bootstrap 5 & FontAwesome UI
├── wwwroot/                       # Static CSS, JS, and vendor assets
├── appsettings.json               # Database and logging configuration
├── Program.cs                     # ASP.NET Core host & dependency injection
└── StockPortal.csproj             # .NET 10 project definition
```
