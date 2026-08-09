using TalesAlarm.Infrastructure;

namespace TalesAlarm.Tests.Infrastructure;

public sealed class SingleInstanceServiceTests
{
    // Break caught: a second process either becomes another owner or cannot signal the existing window.
    [Fact]
    public async Task SecondInstanceSignalsOwner()
    {
        var name = $"TalesAlarm.Tests.{Guid.NewGuid():N}";
        await using var owner = new SingleInstanceService(name);
        await using var second = new SingleInstanceService(name);
        Assert.True(await owner.TryAcquireAsync());
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.ActivationRequested += (_, _) => activated.TrySetResult();

        Assert.False(await second.TryAcquireAsync());
        await second.SignalOwnerAsync();

        await activated.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    // Break caught: repeated ownership checks start multiple pipe loops or change the ownership result.
    [Fact]
    public async Task TryAcquireAsync_WhenCalledTwiceByOwner_RemainsOwner()
    {
        var name = $"TalesAlarm.Tests.{Guid.NewGuid():N}";
        await using var owner = new SingleInstanceService(name);

        Assert.True(await owner.TryAcquireAsync());
        Assert.True(await owner.TryAcquireAsync());
    }
}
