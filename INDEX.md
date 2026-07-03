# 📚 Database Migrator - Documentation Index

## 🎯 Where to Start?

### 👤 If You're an **End User**
1. Read: **[QUICKSTART.md](QUICKSTART.md)** (5 minutes)
2. Run: `.\release\DatabaseMigrator.exe`
3. Do: Database migration!
4. Reference: **[README.md](README.md)**

### 👨‍💻 If You're a **Developer**
1. Read: **[ARCHITECTURE.md](ARCHITECTURE.md)** (design and patterns)
2. Explore: Source code in `src/`
3. Build: `dotnet build` or `.\publish.ps1`
4. Modify: Add features/fix bugs
5. Test: **[PODMAN_E2E_TESTING.md](PODMAN_E2E_TESTING.md)**

### 🚀 If You Need to **Deploy**
1. Read: **[DEPLOYMENT.md](DEPLOYMENT.md)** (requirements and best practices)
2. Build: `dotnet publish` or `.\publish.ps1`
3. Distribute: `DatabaseMigrator.exe`
4. Support: Use troubleshooting guides

---

## 📁 File Structure

```
c:\_repositories\dbmigrator
│
├── 📄 README.md ...................... Complete user guide
├── 📄 QUICKSTART.md .................. Quick start (5 min)
├── 📄 DEPLOYMENT.md .................. Deployment + troubleshooting
├── 📄 ARCHITECTURE.md ................ Technical deep dive
├── 📄 PODMAN_E2E_TESTING.md .......... E2E testing with Podman
├── 📄 INDEX.md ....................... This file
│
├── 🔨 publish.ps1 .................... Build script (PowerShell)
├── 🔨 publish.bat .................... Build script (Batch)
├── 🧪 scripts/run-e2e-matrix.ps1 ..... Podman E2E orchestration
├── 🧪 scripts/container-engine.ps1 ... Container engine resolver
│
├── 📂 src/
│   ├── DatabaseMigrator/ ............. 🎨 UI Application (Avalonia)
│   │   ├── Program.cs ................ Entry point
│   │   ├── App.axaml ................. App configuration
│   │   ├── Views/
│   │   │   └── MainWindow.axaml ...... 🎨 Main UI
│   │   └── ViewModels/
│   │       ├── MainWindowViewModel.cs  🔄 Main logic
│   │       └── ConnectionViewModel.cs  🔌 Connection management
│   │
│   └── DatabaseMigrator.Core/ ........ 📚 Core Library
│       ├── Models/
│       │   ├── ConnectionInfo.cs ...... Connection model
│       │   ├── ConnectionConfig.cs .... Save/load configuration
│       │   ├── DatabaseType.cs ........ DB type enum
│       │   ├── MigrationMode.cs ....... Migration mode enum
│       │   └── TableInfo.cs ........... Table model
│       └── Services/
│           ├── IDatabaseService.cs .... Service interface
│           ├── DatabaseService.cs ..... 🗄️ Database operations
│           ├── SchemaMigrationService.cs 📊 Schema + type mapping
│           └── LoggerService.cs ....... Logging service
│
├── 📂 init-scripts/ .................. DB init/seed scripts
├── 📂 release/ ....................... ✅ FINAL EXECUTABLE
│   └── DatabaseMigrator.exe .......... ~166 MB - READY TO USE!
│
└── DatabaseMigrator.sln .............. Visual Studio solution
```

---

## 📖 Documents by Role

### 👤 END USER

| Document | Section | Time |
|----------|---------|------|
| QUICKSTART.md | All | 5 min |
| README.md | "Usage" | 10 min |
| DEPLOYMENT.md | "Troubleshooting" | 10 min |

### 👨‍💻 DEVELOPER

| Document | Section | Time |
|----------|---------|------|
| ARCHITECTURE.md | All | 30 min |
| README.md | "Build from Source" | 5 min |
| PODMAN_E2E_TESTING.md | All | 15 min |
| Source code | Services + ViewModels | 1 hour |

### 🚀 DEVOPS / SYSTEM ADMIN

| Document | Section | Time |
|----------|---------|------|
| DEPLOYMENT.md | All | 20 min |
| ARCHITECTURE.md | "Performance", "Security" | 15 min |
| PODMAN_E2E_TESTING.md | "Troubleshooting" | 10 min |

---

## 🔑 Key Features Summary

| Feature | Description |
|---------|-------------|
| **Cross-DB Migration** | SQL Server ↔ PostgreSQL ↔ Oracle |
| **Migration Modes** | Schema+Data, Schema Only, Data Only |
| **Constraint Migration** | Primary Keys and UNIQUE constraints |
| **Auto Rollback** | Tables dropped on failure (Schema+Data) |
| **Type Mapping** | 25+ automatic type conversions |
| **Case Handling** | PostgreSQL: lowercase, Oracle: UPPERCASE |
| **Batch Processing** | 1000 rows per batch |
| **Progress Tracking** | Real-time progress bar |
| **Config Save/Load** | JSON configuration files |
| **Table Filtering** | Search and filter tables |
| **Thread-Safe UI** | Async operations with proper UI thread marshalling |

---

## 🛠️ Quick Commands

```powershell
# Build the project
dotnet build

# Publish standalone executable
.\publish.ps1

# Run the application
.\release\DatabaseMigrator.exe

# Run the E2E matrix (Podman): start DBs, seed fixtures, test, tear down
.\scripts\run-e2e-matrix.ps1
```

---

## 📞 Support

For issues and questions:
1. Check the [Troubleshooting](DEPLOYMENT.md#troubleshooting) section
2. Review application logs
3. Test with the Podman E2E environment
4. Check network connectivity
