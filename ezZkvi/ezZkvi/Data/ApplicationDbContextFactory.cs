using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ezZkvi.Data;

// Migration generation needs only the model; it must not execute startup seeding/migrations.
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets<ApplicationDbContextFactory>(optional: true)
            .AddEnvironmentVariables().Build();
        var connection = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Database=ezZkvi;Username=postgres";
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection).Options);
    }
}
