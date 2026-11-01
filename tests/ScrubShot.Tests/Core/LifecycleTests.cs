// SPDX-License-Identifier: GPL-3.0-only
// Copyright (C) 2026 ScrubShot contributors. Ownership entity to be decided (docs/decisions.md D-015).

using ScrubShot.Core.Lifecycle;

namespace ScrubShot.Tests.Core;

public class SingleInstanceTests
{
    [Fact]
    public async Task Second_acquire_fails_and_signals_the_first()
    {
        var name = $"ScrubShotTest-{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim(false);
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        first.ActivationRequested += activated.Set;
        first.Start();

        // A mutex is re-entrant for its owning thread, so the "second process" must come from another thread.
        using var second = await Task.Run(() => SingleInstance.TryAcquire(name), TestContext.Current.CancellationToken);

        Assert.Null(second);
        Assert.True(activated.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the first instance was not signalled");
    }

    [Fact]
    public async Task Signal_before_start_is_delivered_once_started()
    {
        var name = $"ScrubShotTest-{Guid.NewGuid():N}";
        using var activated = new ManualResetEventSlim(false);
        using var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        using var second = await Task.Run(() => SingleInstance.TryAcquire(name), TestContext.Current.CancellationToken);
        Assert.Null(second);

        first.ActivationRequested += activated.Set;
        first.Start();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken), "the early signal was lost");
    }

    [Fact]
    public void Dispose_without_start_is_clean()
    {
        var instance = SingleInstance.TryAcquire($"ScrubShotTest-{Guid.NewGuid():N}");
        Assert.NotNull(instance);

        instance.Dispose();
        Assert.Throws<ObjectDisposedException>(instance.Start);
    }

    [Fact]
    public void Disposing_the_first_lets_a_new_instance_acquire()
    {
        var name = $"ScrubShotTest-{Guid.NewGuid():N}";
        var first = SingleInstance.TryAcquire(name);
        Assert.NotNull(first);
        first.Dispose();

        using var next = SingleInstance.TryAcquire(name);

        Assert.NotNull(next);
    }

    [Fact]
    public void Different_names_do_not_collide()
    {
        using var a = SingleInstance.TryAcquire($"ScrubShotTest-{Guid.NewGuid():N}");
        using var b = SingleInstance.TryAcquire($"ScrubShotTest-{Guid.NewGuid():N}");

        Assert.NotNull(a);
        Assert.NotNull(b);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var instance = SingleInstance.TryAcquire($"ScrubShotTest-{Guid.NewGuid():N}");
        Assert.NotNull(instance);

        instance.Dispose();
        instance.Dispose();
    }
}

public class StartupRegistrationTests
{
    private const string Exe = @"C:\Program Files\ScrubShot\ScrubShot.exe";

    [Fact]
    public void Command_line_quotes_the_path()
    {
        Assert.Equal("\"" + Exe + "\"", StartupRegistration.CommandLineFor(Exe));
    }

    [Fact]
    public void Enabled_with_no_entry_registers()
    {
        var store = new InMemoryStartupStore();

        var result = StartupRegistration.Sync(true, Exe, store);

        Assert.Equal(StartupSyncResult.Registered, result);
        Assert.Equal("\"" + Exe + "\"", store.CommandLine);
    }

    [Fact]
    public void Enabled_with_matching_entry_is_unchanged_and_writes_nothing()
    {
        var store = new InMemoryStartupStore { CommandLine = "\"" + Exe.ToUpperInvariant() + "\"" };

        var result = StartupRegistration.Sync(true, Exe, store);

        Assert.Equal(StartupSyncResult.Unchanged, result);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Enabled_with_stale_path_updates()
    {
        var store = new InMemoryStartupStore { CommandLine = @"""D:\old\ScrubShot.exe""" };

        var result = StartupRegistration.Sync(true, Exe, store);

        Assert.Equal(StartupSyncResult.Updated, result);
        Assert.Equal("\"" + Exe + "\"", store.CommandLine);
    }

    [Fact]
    public void Disabled_removes_an_existing_entry()
    {
        var store = new InMemoryStartupStore { CommandLine = "\"" + Exe + "\"" };

        var result = StartupRegistration.Sync(false, Exe, store);

        Assert.Equal(StartupSyncResult.Removed, result);
        Assert.Null(store.CommandLine);
        Assert.Equal(1, store.Removes);
    }

    [Fact]
    public void Disabled_with_no_entry_touches_nothing()
    {
        var store = new InMemoryStartupStore();

        var result = StartupRegistration.Sync(false, Exe, store);

        Assert.Equal(StartupSyncResult.Unchanged, result);
        Assert.Equal(0, store.Removes);
    }

    [Fact]
    public void Over_long_command_line_is_refused_without_writing()
    {
        var store = new InMemoryStartupStore();
        var longPath = @"C:\" + new string('x', 300) + @"\ScrubShot.exe";

        var result = StartupRegistration.Sync(true, longPath, store);

        Assert.Equal(StartupSyncResult.CommandLineTooLong, result);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public void Store_errors_are_reported_not_thrown()
    {
        var store = new ThrowingStore();

        var result = StartupRegistration.Sync(true, Exe, store);

        Assert.Equal(StartupSyncResult.Failed, result);
    }

    private sealed class ThrowingStore : IStartupStore
    {
        public string? Read() => throw new UnauthorizedAccessException();

        public void Write(string commandLine) => throw new UnauthorizedAccessException();

        public void Remove() => throw new UnauthorizedAccessException();
    }
}
