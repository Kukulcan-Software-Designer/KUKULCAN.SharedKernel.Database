using KUKULCAN.SharedKernel.Database.Abstractions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

[TestFixture]
[NonParallelizable]
public sealed class TransactionErrorIntegrationTests
{
    [Test]
    public async Task UnitOfWork_FailedCommit_ShouldRollbackDatabaseTransactionAfterPostgreSqlConstraintViolation()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        await context.Database.ExecuteSqlRawAsync(
            "CREATE UNIQUE INDEX IF NOT EXISTS \"UX_Entities_TenantId_Name\" " +
            "ON \"Entities\" (\"TenantId\", \"Name\");");

        var unitOfWork = new UnitOfWork<PostgreSqlDatabaseIntegrationTests.IntegrationDbContext>(context);

        try
        {
            await unitOfWork.BeginTransactionAsync();

            context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
            {
                TenantId = tenantId,
                Name = "transaction-error"
            });
            await context.SaveChangesAsync();

            context.Entities.Add(new PostgreSqlDatabaseIntegrationTests.IntegrationEntity
            {
                TenantId = tenantId,
                Name = "transaction-error"
            });

            Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
            await unitOfWork.RollbackTransactionAsync();

            Assert.That(
                await context.Entities.IgnoreQueryFilters()
                    .CountAsync(x => x.TenantId == tenantId && x.Name == "transaction-error"),
                Is.EqualTo(0));
        }
        finally
        {
            await unitOfWork.DisposeAsync();
        }
    }
}
