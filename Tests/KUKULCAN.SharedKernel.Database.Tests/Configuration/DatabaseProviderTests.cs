namespace KUKULCAN.SharedKernel.Database.Tests.Configuration;

[TestFixture]
public sealed class DatabaseProviderTests
{
    [Test]
    public void EnumValues_ShouldRemainStable()
    {
        Assert.Multiple(() =>
        {
            Assert.That((int)DatabaseProvider.SqlServer, Is.EqualTo(0));
            Assert.That((int)DatabaseProvider.PostgresSql, Is.EqualTo(1));
        });
    }
}
