using ModManager.Core;

namespace ModManager.Tests;

public class IntakeUpdateTests
{
    [Fact]
    public void Plan_types_and_updated_result_exist()
    {
        var col = new IntakeCollision("ersc.dll", "ersc.dll", @"C:\game\ersc.dll", @"C:\drop\ersc.dll");
        var plan = new IntakePlan(new[] { new IntakeItem("new.dll", "new.dll", @"C:\drop\new.dll") }, new[] { col }, Array.Empty<SkippedItem>());
        Assert.Equal("new.dll", plan.ToAdd[0].Name);
        Assert.Equal("ersc.dll", plan.Collisions[0].Name);

        var result = new IntakeResult();
        result.Updated.Add("ersc.dll");
        Assert.Single(result.Updated);
    }
}
