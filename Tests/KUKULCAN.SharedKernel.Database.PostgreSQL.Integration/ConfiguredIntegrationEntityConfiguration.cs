using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace KUKULCAN.SharedKernel.Database.Integration;

internal sealed class ConfiguredIntegrationEntityConfiguration
    : IEntityTypeConfiguration<PostgreSqlDatabaseIntegrationTests.ConfiguredIntegrationEntity>
{
    public void Configure(
        EntityTypeBuilder<PostgreSqlDatabaseIntegrationTests.ConfiguredIntegrationEntity> builder)
    {
        builder.ToTable("integration_configured_entities");
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Name)
            .HasMaxLength(64);
    }
}
