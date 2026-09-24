using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// The development database. A separate type only so that SQLite's
/// migrations, which use SQLite-specific column types, are found for SQLite
/// and never applied anywhere else.
/// </summary>
public sealed class SqliteMediQueueDbContext(DbContextOptions<SqliteMediQueueDbContext> options)
    : MediQueueDbContext(options);
