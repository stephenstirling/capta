using Capta.Recording;

namespace Capta.Tests;

public class RecordingTimelineTests
{
    [Fact]
    public void StartsAtZeroAndAdvances()
    {
        var timeline = new RecordingTimeline();

        Assert.Equal(TimeSpan.Zero, timeline.Elapsed);
        timeline.Start();
        Thread.Sleep(30);

        Assert.True(timeline.Elapsed > TimeSpan.Zero);
        Assert.False(timeline.IsPaused);
        Assert.False(timeline.IsStopped);
    }

    [Fact]
    public void PauseFreezesMediaTimeAndIsIdempotent()
    {
        var timeline = new RecordingTimeline();
        timeline.Start();
        Thread.Sleep(30);

        Assert.True(timeline.Pause());
        var pausedAt = timeline.Elapsed;
        Thread.Sleep(60);

        Assert.Equal(pausedAt, timeline.Elapsed);
        Assert.True(timeline.IsPaused);
        Assert.False(timeline.Pause());
        Assert.Equal(pausedAt, timeline.Elapsed);
    }

    [Fact]
    public void ResumeContinuesFromThePausedPosition()
    {
        var timeline = new RecordingTimeline();
        timeline.Start();
        Thread.Sleep(30);
        timeline.Pause();
        var pausedAt = timeline.Elapsed;
        Thread.Sleep(50);

        Assert.True(timeline.Resume());
        Thread.Sleep(30);

        Assert.True(timeline.Elapsed > pausedAt);
        Assert.False(timeline.IsPaused);
        Assert.False(timeline.Resume());
    }

    [Fact]
    public void MultiplePauseCyclesEachFreezeOnlyTheirOwnInterval()
    {
        var timeline = new RecordingTimeline();
        timeline.Start();
        Thread.Sleep(30);
        Assert.True(timeline.Pause());
        var firstPause = timeline.Elapsed;
        Thread.Sleep(40);
        Assert.Equal(firstPause, timeline.Elapsed);

        Assert.True(timeline.Resume());
        Thread.Sleep(30);
        Assert.True(timeline.Pause());
        var secondPause = timeline.Elapsed;
        Assert.True(secondPause > firstPause);
        Thread.Sleep(40);
        Assert.Equal(secondPause, timeline.Elapsed);

        Assert.True(timeline.Resume());
        Thread.Sleep(30);
        Assert.True(timeline.Elapsed > secondPause);
    }

    [Fact]
    public void StopWhilePausedIsTerminalAndIdempotent()
    {
        var timeline = new RecordingTimeline();
        timeline.Start();
        Thread.Sleep(30);
        timeline.Pause();
        var pausedAt = timeline.Elapsed;

        Assert.Equal(pausedAt, timeline.Stop());
        Thread.Sleep(50);

        Assert.Equal(pausedAt, timeline.Elapsed);
        Assert.Equal(pausedAt, timeline.Stop());
        Assert.True(timeline.IsStopped);
        Assert.False(timeline.IsPaused);
        Assert.False(timeline.Resume());
        Assert.False(timeline.Pause());
    }

    [Fact]
    public void EachRecordingGetsAnIndependentTimeline()
    {
        var first = new RecordingTimeline();
        first.Start();
        Thread.Sleep(30);
        first.Stop();

        var second = new RecordingTimeline();
        Assert.Equal(TimeSpan.Zero, second.Elapsed);
        second.Start();
        Thread.Sleep(30);

        Assert.True(second.Elapsed > TimeSpan.Zero);
        Assert.False(second.IsStopped);
    }

    [Fact]
    public void TimelineCannotBeRestartedAfterItHasStartedOrStopped()
    {
        var timeline = new RecordingTimeline();
        timeline.Start();

        Assert.Throws<InvalidOperationException>(timeline.Start);
        timeline.Stop();
        Assert.Throws<InvalidOperationException>(timeline.Start);
    }

    [Fact]
    public void PauseAndResumeBeforeStartAreNoOps()
    {
        var timeline = new RecordingTimeline();

        Assert.False(timeline.Pause());
        Assert.False(timeline.Resume());
        Assert.Equal(TimeSpan.Zero, timeline.Elapsed);
    }
}
