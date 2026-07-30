using SentinelX.Core.Models;

namespace SentinelX.Core.Tests.Models;

public class EngineResultTests
{
    [Fact]
    public void Error_DefaultsToNull()
    {
        var result = new EngineResult(
            EngineCategory.Obs, 100, Array.Empty<Finding>(), DateTimeOffset.Now, TimeSpan.Zero);

        Assert.Null(result.Error);
    }

    [Fact]
    public void Findings_WithSameValues_AreEqual()
    {
        var a = new Finding("Title", "Explanation", Severity.Warning, 0.5, "Action", "Impact");
        var b = new Finding("Title", "Explanation", Severity.Warning, 0.5, "Action", "Impact");

        Assert.Equal(a, b);
    }
}
