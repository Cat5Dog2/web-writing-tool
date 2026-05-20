namespace WebWritingTool.Infrastructure.Identity;

public interface IIdentityDataSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
