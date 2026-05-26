namespace WebWritingTool.E2ETests.Support;

[CollectionDefinition(Name)]
public sealed class E2ETestCollection : ICollectionFixture<E2ETestFixture>
{
    public const string Name = "E2E";
}
