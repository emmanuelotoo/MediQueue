using Microsoft.EntityFrameworkCore;

namespace MediQueue.Infrastructure.Persistence;

/// <summary>
/// The production database on Heroku. A separate type only so that it owns
/// migrations generated against Postgres.
/// </summary>
public sealed class PostgresMediQueueDbContext(DbContextOptions<PostgresMediQueueDbContext> options)
    : MediQueueDbContext(options);
