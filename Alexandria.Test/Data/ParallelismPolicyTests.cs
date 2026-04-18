using Alexandria.DataAccess;

namespace Alexandria.Tests.Data;

[TestClass]
public sealed class ParallelismPolicyTests
{
    [TestMethod]
    public void MaxDegree_ClampedToInclusiveRange()
    {
        var value = ParallelismPolicy.MaxDegree;

        Assert.IsGreaterThanOrEqualTo(1, value);
        Assert.IsLessThanOrEqualTo(8, value);
    }

    [TestMethod]
    public void MaxDegree_NeverExceedsHostProcessorCount()
    {
        // 80% floor keeps us under the host capacity for any core count ≥ 2; on a
        // single-core host the floor of 0 is lifted to 1 by Clamp, which equals the count.
        Assert.IsLessThanOrEqualTo(Environment.ProcessorCount, ParallelismPolicy.MaxDegree);
    }
}
