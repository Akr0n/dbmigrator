# Database Migrator - Project Summary

## Overview

Database Migrator is a professional-grade Windows tool for migrating data between relational database systems. Built with .NET 10.0 and Avalonia UI, it provides a modern graphical interface for managing database migrations.

## Technology Stack

| Component | Technology |
|-----------|------------|
| **Runtime** | .NET 10.0 |
| **UI Framework** | Avalonia 11.3.10 |
| **MVVM Binding** | ReactiveUI + System.Reactive |
| **SQL Server Driver** | Microsoft.Data.SqlClient 6.1.3 |
| **PostgreSQL Driver** | Npgsql 10.0.1 |
| **Oracle Driver** | Oracle.ManagedDataAccess.Core 23.26.0 |
| **Packaging** | Single-file, self-contained executable |
| **Installer** | NSIS |

## Supported Database Combinations

All combinations are supported:

| From ↓ \ To → | SQL Server | PostgreSQL | Oracle |
|---------------|------------|------------|--------|
| **SQL Server** | ✅ | ✅ | ✅ |
| **PostgreSQL** | ✅ | ✅ | ✅ |
| **Oracle** | ✅ | ✅ | ✅ |

## Core Features

### Migration Modes
- **Schema + Data**: Full migration with automatic rollback on failure
- **Schema Only**: Create table structures with constraints, without data
- **Data Only**: Migrate data only (tables must exist)

### Schema Migration
- Primary Key constraint migration
- UNIQUE constraint migration
- Automatic identifier case handling:
  - PostgreSQL: lowercase identifiers
  - Oracle: UPPERCASE identifiers
  - SQL Server: original case preserved

### Data Type Mapping
- 25+ automatic type conversions
- Handles VARCHAR(MAX) → TEXT/CLOB
- Preserves types for same-database migrations
- Respects database-specific limits (Oracle VARCHAR2 max 4000, etc.)

### Performance Features
- Batch processing (1000 rows per batch)
- Controlled parallelism for row counts (10 concurrent)
- 300-second command timeout
- Transaction support with commit/rollback

### User Experience
- Modern tabbed interface
- Real-time progress tracking
- Table search and filtering
- Configuration save/load (JSON)
- Detailed error messages

### Reliability
- Automatic rollback of created tables on failure
- Table existence validation for Data Only mode
- Connection testing before migration
- Comprehensive logging

## Project Structure

```
DatabaseMigrator.sln
├── src/
│   ├── DatabaseMigrator/           # UI Application
│   │   ├── Views/                  # AXAML UI definitions
│   │   ├── ViewModels/             # MVVM view models
│   │   └── Assets/                 # Icons and resources
│   │
│   └── DatabaseMigrator.Core/      # Core Library
│       ├── Models/                 # Data models
│       └── Services/               # Business logic
│
├── init-scripts/                   # Docker initialization
├── release/                        # Published executable
└── docs/                           # Documentation
```

## Build Output

- **Executable**: `DatabaseMigrator.exe` (~166 MB)
- **Type**: Self-contained, single-file
- **Target**: Windows x64
- **Dependencies**: None (all included)

## Version History

### v1.0.0 (January 2026)
- Initial release
- SQL Server, PostgreSQL, Oracle support
- Three migration modes
- Automatic rollback feature
- Configuration save/load
- Table filtering
- Parallel row count retrieval
- Comprehensive type mapping

## Documentation

| Document | Purpose |
|----------|---------|
| [README.md](README.md) | User guide |
| [QUICKSTART.md](QUICKSTART.md) | 5-minute quick start |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Technical documentation |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Deployment and troubleshooting |
| [DOCKER_E2E_TESTING.md](DOCKER_E2E_TESTING.md) | Testing with Docker |
| [INDEX.md](INDEX.md) | Documentation index |

## License

MIT License
