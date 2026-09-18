using Threadline.Comments.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Threadline.Comments.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations</c> construct the context without booting the application.
/// </summary>
/// <remarks>
/// The connection string here is only ever used to pick the provider and generate SQL — the tooling
/// never connects when scaffolding a migration. It deliberately points at a throwaway local
/// instance so that running the tooling can never touch a real database by accident.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string DesignTimeConnectionString =
        "Server=localhost,1433;Database=ThreadlineComments_DesignTime;User Id=sa;Password=DesignTime!1;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__SqlServer")
            ?? DesignTimeConnectionString;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name))
            .Options;

        return new AppDbContext(options, new SystemDateTimeProvider());
    }
}
