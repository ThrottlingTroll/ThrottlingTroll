using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Reflection.Metadata;
using System.Threading.Tasks;

namespace ThrottlingTroll.CounterStores.EfCore
{
    /// <summary>
    /// Represents a ThrottlingTroll counter
    /// </summary>
    public class ThrottlingTrollCounter
    {
        /// <summary>
        /// Counter's Key
        /// </summary>
        public string Id { get; set; } = default!;

        /// <summary>
        /// Current count
        /// </summary>
        public long Count { get; set; }

        /// <summary>
        /// TTL
        /// </summary>
        public DateTimeOffset ExpiresAt { get; set; }
    }


    /// <summary>
    /// EF Core data context for ThrottlingTroll counters
    /// </summary>
    public class CounterDbContext : DbContext
    {
        /// <summary>
        /// Counters DbSet
        /// </summary>
        public DbSet<ThrottlingTrollCounter> ThrottlingTrollCounters { get; set; }

        /// <summary>
        /// Ctor
        /// </summary>
        public CounterDbContext(Action<DbContextOptionsBuilder> configFunc)
        {
            this._configFunc = configFunc;
        }

        private readonly Action<DbContextOptionsBuilder> _configFunc;

        /// <inheritdoc />
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            this._configFunc(optionsBuilder);
        }
    }
}
