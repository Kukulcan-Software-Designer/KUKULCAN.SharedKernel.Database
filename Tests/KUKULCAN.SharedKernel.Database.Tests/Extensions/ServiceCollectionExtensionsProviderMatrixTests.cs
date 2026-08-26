using KUKULCAN.SharedKernel.Database.Tests.TestInfrastructure.internals;
using Microsoft.Extensions.Hosting;

namespace KUKULCAN.SharedKernel.Database.Tests.Extensions;

[TestFixture]
public sealed class ServiceCollectionExtensionsProviderMatrixTests
{
    private static readonly object[][] ProviderCases =
    [
        [DatabaseProvider.SqlServer, "Server=localhost;Database=KukulcanTests;Integrated Security=True;", "Microsoft.EntityFrameworkCore.SqlServer"],
        [DatabaseProvider.PostgresSql, "Host=localhost;Database=KukulcanTests;Username=test;Password=test;", "Npgsql.EntityFrameworkCore.PostgreSQL"],
        [DatabaseProvider.MySql, "Server=localhost;Database=KukulcanTests;User Id=test;Password=test;", "MySql.EntityFrameworkCore"]
    ];

    private sealed class ProviderMatrixDbContext(
        IOptions<KukulcanDatabaseOptions> options,
        ITenantContext tenantContext,
        IClock clock,
        IDomainEventDispatcher dispatcher,
        SlowQueryInterceptor? slowQueryInterceptor = null)
        : KukulcanDbContextBase(options, tenantContext, clock, dispatcher, slowQueryInterceptor);
}
