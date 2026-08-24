using KUKULCAN.SharedKernel.Database.Extensions;
using Microsoft.EntityFrameworkCore.Metadata;
using NUnit.Framework;

namespace KUKULCAN.SharedKernel.Database.Integration;

public sealed class MissingCoverageIntegrationTests
{
    [Test]
    public async Task KukulcanDbContextBase_ShouldApplyEntityConfigurationsFromDerivedContextAssembly()
    {
        Guid tenantId = Guid.NewGuid();
        await using PostgreSqlDatabaseIntegrationTests.IntegrationDbContext context =
            await IntegrationTestDatabase.CreateContextAsync(tenantId);

        await context.Database.EnsureCreatedAsync();
        IEntityType? entityType = context.Model.FindEntityType(typeof(PostgreSqlDatabaseIntegrationTests.ConfiguredIntegrationEntity));
        IProperty? nameProperty = entityType?.FindProperty(nameof(PostgreSqlDatabaseIntegrationTests.ConfiguredIntegrationEntity.Name));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entityType, Is.Not.Null);
            Assert.That(entityType!.GetTableName(), Is.EqualTo("integration_configured_entities"));
            Assert.That(nameProperty, Is.Not.Null);
            Assert.That(nameProperty!.GetMaxLength(), Is.EqualTo(64));
        }
    }

    [Test]
    public void ApplySoftDeleteFilter_ShouldRejectNullModelBuilder()
    {
        Assert.That(
            () => ModelBuilderExtensions.ApplySoftDeleteFilter(null!),
            Throws.ArgumentNullException);
    }
}
