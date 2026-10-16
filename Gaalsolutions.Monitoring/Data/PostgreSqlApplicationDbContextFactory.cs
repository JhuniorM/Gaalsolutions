using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gaalsolutions.Monitoring.Data
{
    public sealed class PostgreSqlApplicationDbContextFactory
        : IDesignTimeDbContextFactory<PostgreSqlApplicationDbContext>
    {
        public PostgreSqlApplicationDbContext CreateDbContext(string[] args)
        {
            var options = new DbContextOptionsBuilder<PostgreSqlApplicationDbContext>()
                .UseNpgsql("Host=localhost;Database=GaalsolutionsMonitoring;Username=postgres;Password=design-time")
                .Options;

            return new PostgreSqlApplicationDbContext(options);
        }
    }
}
