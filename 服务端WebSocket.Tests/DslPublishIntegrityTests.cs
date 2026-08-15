using GrandUMI.Effects.Dsl;
using Xunit;

namespace GrandUMI.Tests;

public sealed class DslPublishIntegrityTests
{
    [Fact]
    public void MissingDslDefinitionsAreFatal()
    {
        var missingDirectory = Path.Combine(
            Path.GetPathRoot(AppContext.BaseDirectory)!,
            $"grandumi-missing-dsl-{Guid.NewGuid():N}");

        var exception = Assert.Throws<DirectoryNotFoundException>(
            () => DslInterpreter.GetDefinitionFiles(missingDirectory));

        Assert.Contains("DSL 定义目录不存在", exception.Message);
    }
}
