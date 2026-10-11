using System.IO;
using FoxyVPN.Windows.Services;
using Xunit;

namespace FoxyVPN.Tests;

public sealed class WindowsTunnelEngineTests
{
    private sealed class FakeRuntime(bool confirmConnected = true) : IWindowsTunnelRuntime
    {
        public bool IsAvailable { get; set; } = true;
        public bool IsConnected { get; private set; }
        public bool IsSafetyHold { get; set; }
        public bool ConfirmConnected { get; set; } = confirmConnected;
        public bool FailStop { get; set; }
        public bool ThrowOnStart { get; set; }
        public bool WaitUntilCancelled { get; set; }
        public bool BlockDispose { get; set; }
        public TaskCompletionSource<bool> StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FinishDispose { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Starts { get; private set; }
        public int Stops { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Starts++;
            if (WaitUntilCancelled)
            {
                StartEntered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            if (ThrowOnStart)
            {
                IsSafetyHold = true;
                throw new IOException("simulated startup failure with incomplete cleanup");
            }
            IsConnected = ConfirmConnected;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Stops++;
            if (FailStop)
            {
                IsSafetyHold = true;
                throw new IOException("simulated route rollback failure");
            }
            IsConnected = false;
            IsSafetyHold = false;
            return Task.CompletedTask;
        }

        public void SimulateUnexpectedDrop() => IsConnected = false;

        public async ValueTask DisposeAsync()
        {
            if (!BlockDispose) return;
            DisposeEntered.TrySetResult(true);
            await FinishDispose.Task.ConfigureAwait(false);
        }
    }

    [Fact]
    public async Task MissingRuntimeFailsClosed()
    {
        await using var engine = new WindowsTunnelEngine();
        var error = await Assert.ThrowsAsync<TunnelEngineUnavailableException>(() => engine.ConnectAsync());
        Assert.Contains("No connection was started", error.Message, StringComparison.Ordinal);
        Assert.False(engine.IsConnected);
        Assert.False(engine.IsSupported);
        Assert.Equal(WindowsTunnelState.Disconnected, engine.State);
    }

    [Fact]
    public async Task ConnectedIsReportedOnlyAfterRuntimeConfirmsIt()
    {
        var runtime = new FakeRuntime(confirmConnected: true);
        await using var engine = new WindowsTunnelEngine(runtime);

        await engine.ConnectAsync();

        Assert.True(engine.IsConnected);
        Assert.Equal(WindowsTunnelState.Connected, engine.State);
        Assert.Equal(1, runtime.Starts);
    }

    [Fact]
    public async Task EngineRejectsRuntimeThatDoesNotConfirmConnection()
    {
        var runtime = new FakeRuntime(confirmConnected: false);
        await using var engine = new WindowsTunnelEngine(runtime);

        await Assert.ThrowsAsync<TunnelEngineUnavailableException>(() => engine.ConnectAsync());

        Assert.False(engine.IsConnected);
        Assert.Equal(WindowsTunnelState.Faulted, engine.State);
        Assert.Equal(1, runtime.Stops);
    }

    [Fact]
    public async Task DisconnectReturnsRuntimeToDisconnectedState()
    {
        var runtime = new FakeRuntime();
        await using var engine = new WindowsTunnelEngine(runtime);

        await engine.ConnectAsync();
        await engine.DisconnectAsync();

        Assert.False(engine.IsConnected);
        Assert.Equal(WindowsTunnelState.Disconnected, engine.State);
        Assert.Equal(1, runtime.Stops);
    }

    [Fact]
    public async Task StartupFailureIsNotReportedDisconnectedWhenCleanupCannotBeVerified()
    {
        var runtime = new FakeRuntime
        {
            ThrowOnStart = true,
            FailStop = true
        };
        await using var engine = new WindowsTunnelEngine(runtime);

        await Assert.ThrowsAsync<IOException>(() => engine.ConnectAsync());

        Assert.Equal(WindowsTunnelState.Faulted, engine.State);
        Assert.True(runtime.IsSafetyHold);
    }

    [Fact]
    public async Task ExplicitDisconnectAttemptsRecoveryEvenWhenEngineRawStateIsDisconnected()
    {
        var runtime = new FakeRuntime { IsSafetyHold = true };
        await using var engine = new WindowsTunnelEngine(runtime);

        Assert.Equal(WindowsTunnelState.Faulted, engine.State);
        await engine.DisconnectAsync();

        Assert.Equal(1, runtime.Stops);
        Assert.False(runtime.IsSafetyHold);
        Assert.Equal(WindowsTunnelState.Disconnected, engine.State);
    }

    [Fact]
    public async Task NewConnectIsRejectedWhileRuntimeIsInSafetyHold()
    {
        var runtime = new FakeRuntime { IsSafetyHold = true };
        await using var engine = new WindowsTunnelEngine(runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ConnectAsync());

        Assert.Equal(0, runtime.Starts);
        Assert.Equal(WindowsTunnelState.Faulted, engine.State);
    }


    [Fact]
    public async Task CancelledStartupIsDisconnectedOnlyAfterCleanupIsVerified()
    {
        var runtime = new FakeRuntime { WaitUntilCancelled = true };
        await using var engine = new WindowsTunnelEngine(runtime);
        using var cancellation = new CancellationTokenSource();
        var connect = engine.ConnectAsync(cancellation.Token);
        await runtime.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);

        Assert.Equal(1, runtime.Stops);
        Assert.False(runtime.IsSafetyHold);
        Assert.Equal(WindowsTunnelState.Disconnected, engine.State);
    }

    [Fact]
    public async Task ConcurrentDisposeCallsWaitForTheSameCleanupTask()
    {
        var runtime = new FakeRuntime { BlockDispose = true };
        var engine = new WindowsTunnelEngine(runtime);

        var first = engine.DisposeAsync().AsTask();
        await runtime.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = engine.DisposeAsync().AsTask();
        Assert.False(second.IsCompleted);

        runtime.FinishDispose.TrySetResult(true);
        await Task.WhenAll(first, second);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => engine.ConnectAsync());
    }

    [Fact]
    public async Task UnexpectedRuntimeLossIsReportedAsFaultedRatherThanConnected()
    {
        var runtime = new FakeRuntime(confirmConnected: true);
        await using var engine = new WindowsTunnelEngine(runtime);
        await engine.ConnectAsync();
        Assert.True(engine.IsConnected);

        runtime.SimulateUnexpectedDrop();

        Assert.False(engine.IsConnected);
        Assert.Equal(WindowsTunnelState.Faulted, engine.State);
    }

}
