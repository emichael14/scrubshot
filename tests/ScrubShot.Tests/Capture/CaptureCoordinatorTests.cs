// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Capture;
using ScrubShot.Capture.Coordination;
using ScrubShot.Capture.Overlay;
using ScrubShot.Core.Coordination;
using ScrubShot.Core.Geometry;
using ScrubShot.Core.Hotkeys;
using ScrubShot.Core.Settings;

namespace ScrubShot.Tests.Capture;

/// <summary>Drives the coordinator with a fake capturer (real DIB sections on a synthetic layout), a scripted selector and a recording sink.</summary>
public class CaptureCoordinatorTests
{
    private static readonly DateTimeOffset Stamp = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Monitor 0 (primary) at (0,0) 6x3; monitor 1 at (-2,-1) 2x1. Virtual screen (-2,-1) 8x4.</summary>
    private static MonitorLayout Layout() => new(
    [
        new MonitorInfo(0, "A", new PixelRect(0, 0, 6, 3), new PixelRect(0, 0, 6, 3), true, 96),
        new MonitorInfo(1, "B", new PixelRect(-2, -1, 2, 1), new PixelRect(-2, -1, 2, 1), false, 144),
    ]);

    [Fact]
    public async Task Full_screen_crops_the_whole_virtual_screen_without_the_overlay()
    {
        var harness = new Harness();

        var outcome = await harness.CaptureAsync(CaptureRequest.FullScreen);

        Assert.Equal(CaptureOutcome.Completed, outcome);
        Assert.Equal(0, harness.Selector.Calls);
        var image = Assert.Single(harness.Sink.Images);
        Assert.Equal(new PixelRect(-2, -1, 8, 4), image.ScreenRect);
        Assert.True(harness.Capturer.LastFrozen!.IsDisposed);
        Assert.Null(harness.Coordinator.LastRegion);
    }

    [Fact]
    public async Task Monitor_capture_crops_that_monitor_only()
    {
        var harness = new Harness();

        var outcome = await harness.CaptureAsync(CaptureRequest.ForMonitor(1));

        Assert.Equal(CaptureOutcome.Completed, outcome);
        var image = Assert.Single(harness.Sink.Images);
        Assert.Equal(new PixelRect(-2, -1, 2, 1), image.ScreenRect);
        Assert.Null(harness.Coordinator.LastRegion);
    }

    [Fact]
    public async Task Unknown_monitor_index_is_reported_before_anything_is_captured()
    {
        var harness = new Harness();

        var outcome = await harness.CaptureAsync(CaptureRequest.ForMonitor(5));

        Assert.Equal(CaptureOutcome.NoMonitor, outcome);
        Assert.Equal(0, harness.Capturer.Captures);
        Assert.Empty(harness.Sink.Images);
    }

    [Fact]
    public async Task Region_capture_uses_the_overlay_and_remembers_the_region()
    {
        var harness = new Harness { Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(1, 1, 3, 2)) } };

        var outcome = await harness.CaptureAsync(CaptureRequest.Region);

        Assert.Equal(CaptureOutcome.Completed, outcome);
        Assert.Equal(OverlayMode.Region, harness.Selector.LastMode);
        var image = Assert.Single(harness.Sink.Images);
        Assert.Equal(new PixelRect(1, 1, 3, 2), image.ScreenRect);
        Assert.Equal(3 * 2 * FrozenScreen.BytesPerPixel, image.Pixels.Length);
        Assert.Equal(new PixelRect(1, 1, 3, 2), harness.Coordinator.LastRegion);
    }

    [Fact]
    public async Task Window_capture_asks_the_overlay_for_a_window()
    {
        var harness = new Harness { Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(0, 0, 6, 3), 42) } };

        await harness.CaptureAsync(CaptureRequest.Window);

        Assert.Equal(OverlayMode.Window, harness.Selector.LastMode);
    }

    [Fact]
    public async Task Cancelled_overlay_reaches_no_sink_and_disposes_the_frozen_screen()
    {
        var harness = new Harness { Selector = { Result = OverlayResult.Cancelled } };

        var outcome = await harness.CaptureAsync(CaptureRequest.Region);

        Assert.Equal(CaptureOutcome.Cancelled, outcome);
        Assert.Empty(harness.Sink.Images);
        Assert.True(harness.Capturer.LastFrozen!.IsDisposed);
        Assert.Null(harness.Coordinator.LastRegion);
    }

    [Fact]
    public async Task Repeat_without_a_remembered_region_falls_back_to_region_select()
    {
        var harness = new Harness { Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(2, 0, 2, 2)) } };

        var outcome = await harness.CaptureAsync(CaptureRequest.RepeatLastRegion);

        Assert.Equal(CaptureOutcome.Completed, outcome);
        Assert.Equal(1, harness.Selector.Calls);
        Assert.Equal(OverlayMode.Region, harness.Selector.LastMode);
        Assert.Equal(new PixelRect(2, 0, 2, 2), harness.Coordinator.LastRegion);
    }

    [Fact]
    public async Task Repeat_reuses_the_remembered_region_without_the_overlay()
    {
        var harness = new Harness { Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(1, 1, 3, 2)) } };
        await harness.CaptureAsync(CaptureRequest.Region);

        var outcome = await harness.CaptureAsync(CaptureRequest.RepeatLastRegion);

        Assert.Equal(CaptureOutcome.Completed, outcome);
        Assert.Equal(1, harness.Selector.Calls);
        Assert.Equal(2, harness.Sink.Images.Count);
        Assert.Equal(new PixelRect(1, 1, 3, 2), harness.Sink.Images[1].ScreenRect);
    }

    [Fact]
    public async Task Repeat_clamps_the_remembered_region_when_the_layout_shrank()
    {
        var harness = new Harness { Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(-2, -1, 4, 3)) } };
        await harness.CaptureAsync(CaptureRequest.Region);
        harness.Layout = new MonitorLayout([new MonitorInfo(0, "A", new PixelRect(0, 0, 6, 3), new PixelRect(0, 0, 6, 3), true, 96)]);

        var outcome = await harness.CaptureAsync(CaptureRequest.RepeatLastRegion);

        Assert.Equal(CaptureOutcome.Completed, outcome);
        Assert.Equal(1, harness.Selector.Calls);
        Assert.Equal(new PixelRect(0, 0, 2, 2), harness.Sink.Images[1].ScreenRect);
    }

    [Fact]
    public async Task Repeat_falls_back_when_the_remembered_region_is_entirely_off_screen()
    {
        var harness = new Harness { Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(-2, -1, 2, 1)) } };
        await harness.CaptureAsync(CaptureRequest.Region);
        harness.Layout = new MonitorLayout([new MonitorInfo(0, "A", new PixelRect(0, 0, 6, 3), new PixelRect(0, 0, 6, 3), true, 96)]);
        harness.Selector.Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(0, 0, 1, 1));

        await harness.CaptureAsync(CaptureRequest.RepeatLastRegion);

        Assert.Equal(2, harness.Selector.Calls);
        Assert.Equal(new PixelRect(0, 0, 1, 1), harness.Sink.Images[1].ScreenRect);
    }

    [Fact]
    public async Task A_second_request_while_one_is_in_flight_is_dropped_as_busy()
    {
        var harness = new Harness();
        harness.Selector.Gate = new TaskCompletionSource<OverlayResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = harness.CaptureAsync(CaptureRequest.Region);
        Assert.True(harness.Coordinator.IsBusy);
        var second = await harness.CaptureAsync(CaptureRequest.FullScreen);
        harness.Selector.Gate.SetResult(new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(0, 0, 1, 1)));

        Assert.Equal(CaptureOutcome.Busy, second);
        Assert.Equal(CaptureOutcome.Completed, await first);
        Assert.False(harness.Coordinator.IsBusy);
        Assert.Single(harness.Sink.Images);
    }

    [Fact]
    public async Task Sink_failure_clears_the_image_and_reports_failed()
    {
        var harness = new Harness { Sink = { Throw = true } };

        var outcome = await harness.CaptureAsync(CaptureRequest.FullScreen);

        Assert.Equal(CaptureOutcome.Failed, outcome);
        var image = Assert.Single(harness.Sink.Images);
        Assert.All(image.Pixels, b => Assert.Equal(0, b));
        Assert.False(harness.Coordinator.IsBusy);
    }

    [Fact]
    public async Task Capturer_failure_reports_failed_and_frees_the_coordinator()
    {
        var harness = new Harness { Capturer = { Throw = true } };

        var outcome = await harness.CaptureAsync(CaptureRequest.FullScreen);

        Assert.Equal(CaptureOutcome.Failed, outcome);
        Assert.Empty(harness.Sink.Images);
        Assert.False(harness.Coordinator.IsBusy);
    }

    [Fact]
    public async Task Delay_setting_waits_before_capturing()
    {
        var time = new FakeTimeProvider();
        var harness = new Harness(time) { Settings = new CaptureSettings { DelayMilliseconds = 500 } };

        var task = harness.CaptureAsync(CaptureRequest.FullScreen);
        await Task.Yield();
        Assert.Equal(0, harness.Capturer.Captures);
        time.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Equal(CaptureOutcome.Completed, await task);
        Assert.Equal(1, harness.Capturer.Captures);
    }

    [Fact]
    public async Task Overlay_options_follow_the_capture_settings()
    {
        var harness = new Harness
        {
            Settings = new CaptureSettings { ShowMagnifier = false, ShowCoordinates = false, CaptureCursor = true },
            Selector = { Result = new OverlayResult(OverlayOutcome.Confirmed, new PixelRect(0, 0, 1, 1)) },
        };

        await harness.CaptureAsync(CaptureRequest.Region);

        Assert.False(harness.Selector.LastOptions!.ShowMagnifier);
        Assert.False(harness.Selector.LastOptions.ShowCoordinates);
        Assert.True(harness.Capturer.LastOptions!.IncludeCursor);
    }

    [Theory]
    [InlineData(HotkeyAction.Region, CaptureMode.Region)]
    [InlineData(HotkeyAction.Window, CaptureMode.Window)]
    [InlineData(HotkeyAction.FullScreen, CaptureMode.FullScreen)]
    [InlineData(HotkeyAction.RepeatLastRegion, CaptureMode.RepeatLastRegion)]
    public void Hotkey_actions_map_to_capture_modes(HotkeyAction action, CaptureMode expected)
    {
        Assert.Equal(expected, CaptureRequest.FromHotkey(action).Mode);
    }

    private sealed class Harness
    {
        public Harness(TimeProvider? timeProvider = null)
        {
            Coordinator = new CaptureCoordinator(Capturer, () => Layout, Selector, Sink, () => Settings, timeProvider: timeProvider);
        }

        public MonitorLayout Layout { get; set; } = CaptureCoordinatorTests.Layout();

        public CaptureSettings Settings { get; set; } = new();

        public FakeCapturer Capturer { get; } = new();

        public FakeSelector Selector { get; } = new();

        public RecordingSink Sink { get; } = new();

        public CaptureCoordinator Coordinator { get; }

        public Task<CaptureOutcome> CaptureAsync(CaptureRequest request) =>
            Coordinator.CaptureAsync(request, TestContext.Current.CancellationToken);
    }

    private sealed class FakeCapturer : IScreenCapturer
    {
        public int Captures { get; private set; }

        public bool Throw { get; set; }

        public FrozenScreen? LastFrozen { get; private set; }

        public CaptureOptions? LastOptions { get; private set; }

        public FrozenScreen Capture(MonitorLayout layout, CaptureOptions options)
        {
            Captures++;
            LastOptions = options;
            if (Throw)
            {
                throw new InvalidOperationException("capture failed");
            }

            LastFrozen = FrozenScreen.Allocate(layout, Stamp, options.IncludeCursor);
            LastFrozen.WritablePixels.Fill(0x5A);
            return LastFrozen;
        }
    }

    private sealed class FakeSelector : IRegionSelector
    {
        public OverlayResult Result { get; set; } = OverlayResult.Cancelled;

        public TaskCompletionSource<OverlayResult>? Gate { get; set; }

        public int Calls { get; private set; }

        public OverlayMode? LastMode { get; private set; }

        public OverlayOptions? LastOptions { get; private set; }

        public Task<OverlayResult> SelectAsync(FrozenScreen frozen, OverlayMode mode, OverlayOptions options, CancellationToken cancellationToken)
        {
            Calls++;
            LastMode = mode;
            LastOptions = options;
            Assert.False(frozen.IsDisposed);
            return Gate?.Task ?? Task.FromResult(Result);
        }
    }

    private sealed class RecordingSink : ICaptureSink
    {
        public List<CapturedImage> Images { get; } = [];

        public bool Throw { get; set; }

        public Task HandleAsync(CapturedImage image, CaptureRequest request, CancellationToken cancellationToken)
        {
            Images.Add(image);
            return Throw ? throw new IOException("sink failed") : Task.CompletedTask;
        }
    }

    /// <summary>Minimal manual clock for Task.Delay(TimeProvider).</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly List<FakeTimer> _timers = [];
        private DateTimeOffset _now = Stamp;

        public override DateTimeOffset GetUtcNow() => _now;

        public override long GetTimestamp() => _now.UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan by)
        {
            _now += by;
            FakeTimer[] due;
            lock (_timers)
            {
                due = _timers.Where(t => t.DueAt <= _now).ToArray();
                _timers.RemoveAll(t => t.DueAt <= _now);
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        private sealed class FakeTimer(FakeTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset DueAt { get; private set; } = DateTimeOffset.MaxValue;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._timers)
                {
                    owner._timers.Remove(this);
                    if (dueTime != Timeout.InfiniteTimeSpan)
                    {
                        DueAt = owner._now + dueTime;
                        owner._timers.Add(this);
                    }
                }

                return true;
            }

            public void Fire() => callback(state);

            public void Dispose()
            {
                lock (owner._timers)
                {
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
