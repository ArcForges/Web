// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.JSInterop;

namespace ArcForges.Web.App.Dashboard;

public interface IDashboardActivity : IAsyncDisposable
{
    event Action? Resumed;
    Task StartAsync(CancellationToken cancellationToken);
}

/// <summary>Browser lifecycle notifications for a dashboard layout. Authentication stays in the typed client.</summary>
internal sealed class DashboardActivity(IJSRuntime browser) : IDashboardActivity
{
    internal const string ModulePath = "./_framework/arcforges-dashboard-activity.43e6e05b58.js";
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private DotNetObjectReference<DashboardActivity>? _callback;
    private IJSObjectReference? _module;
    private IJSObjectReference? _subscription;
    private bool _disposed;

    public event Action? Resumed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_disposed || _subscription is not null)
                return;

            _module = await browser.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
            cancellationToken.ThrowIfCancellationRequested();
            _callback = DotNetObjectReference.Create(this);
            // Keep the returned listener handle even if the caller cancels while JavaScript attaches it.
            _subscription = await _module.InvokeAsync<IJSObjectReference>("subscribe", _callback);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (JSException)
        {
            await ReleaseAsync();
        }
        catch
        {
            await ReleaseAsync();
            throw;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    [JSInvokable]
    public void NotifyResumed()
    {
        if (!_disposed && _subscription is not null)
            Resumed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            Resumed = null;
            await ReleaseAsync();
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async ValueTask ReleaseAsync()
    {
        var subscription = _subscription;
        var module = _module;
        _subscription = null;
        _module = null;
        try
        {
            if (subscription is not null)
            {
                try
                {
                    await subscription.InvokeVoidAsync("dispose");
                }
                finally
                {
                    await subscription.DisposeAsync();
                }
            }
        }
        catch (JSException)
        {
            // A closed browser context has already discarded its listeners.
        }
        finally
        {
            _callback?.Dispose();
            _callback = null;
            if (module is not null)
            {
                try
                {
                    await module.DisposeAsync();
                }
                catch (JSException)
                {
                    // The browser context can close during layout disposal.
                }
            }
        }
    }
}
