using MediQueue.Shared.Contracts;
using MediQueue.Shared.Realtime;
using MediQueue.Shared.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace MediQueue.Client.Services;

public enum LiveState
{
    Idle,
    Connecting,
    Live,
    Reconnecting,
    Lost
}

/// <summary>
/// The only place the client touches SignalR. Components subscribe to plain C#
/// events and never see a <see cref="HubConnection"/>, so reconnect handling
/// and group management live in one place instead of being repeated on every
/// screen.
/// </summary>
public class QueueHubClient : IAsyncDisposable
{
    private readonly NavigationManager _navigation;
    private readonly SessionStore _session;

    private HubConnection? _connection;
    private Task? _starting;

    /// <summary>
    /// What this client is watching. Replayed after a reconnect, because
    /// SignalR does not restore group membership itself.
    /// </summary>
    private readonly HashSet<int> _departments = [];
    private readonly HashSet<int> _boards = [];
    private readonly HashSet<string> _tickets = [];
    private bool _reception;

    public QueueHubClient(NavigationManager navigation, SessionStore session)
    {
        _navigation = navigation;
        _session = session;
    }

    public LiveState State { get; private set; } = LiveState.Idle;

    public event Action<DepartmentQueueDto>? QueueUpdated;
    public event Action<BoardDto>? BoardUpdated;
    public event Action<TicketStatusDto>? TicketUpdated;
    public event Action<TicketCalledDto>? TicketCalled;
    public event Action<EmergencyAlertDto>? EmergencyFlagged;
    public event Action? StateChanged;

    public async Task EnsureConnectedAsync()
    {
        if (_connection is not null)
        {
            if (_starting is not null)
            {
                await _starting;
            }

            return;
        }

        var stored = await _session.GetSessionAsync();

        _connection = new HubConnectionBuilder()
            .WithUrl(_navigation.ToAbsoluteUri("/hubs/queue"), options =>
            {
                // Staff connections carry their token; patient devices do not.
                options.AccessTokenProvider = () => Task.FromResult(stored?.AccessToken);
            })
            .AddJsonProtocol(options => MediQueueJson.Configure(options.PayloadSerializerOptions))
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10)
            ])
            .Build();

        _connection.On<DepartmentQueueDto>(QueueEvents.QueueUpdated, dto => QueueUpdated?.Invoke(dto));
        _connection.On<BoardDto>(QueueEvents.BoardUpdated, dto => BoardUpdated?.Invoke(dto));
        _connection.On<TicketStatusDto>(QueueEvents.TicketUpdated, dto => TicketUpdated?.Invoke(dto));
        _connection.On<TicketCalledDto>(QueueEvents.TicketCalled, dto => TicketCalled?.Invoke(dto));
        _connection.On<EmergencyAlertDto>(QueueEvents.EmergencyFlagged, dto => EmergencyFlagged?.Invoke(dto));

        _connection.Reconnecting += _ =>
        {
            SetState(LiveState.Reconnecting);
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            await RejoinAsync();
            SetState(LiveState.Live);
        };

        _connection.Closed += _ =>
        {
            SetState(LiveState.Lost);
            return Task.CompletedTask;
        };

        SetState(LiveState.Connecting);

        _starting = _connection.StartAsync();

        try
        {
            await _starting;
            SetState(LiveState.Live);
        }
        catch (Exception)
        {
            // The screen still works from its last REST read; it simply stops
            // updating by itself, and the indicator says so.
            SetState(LiveState.Lost);
        }
        finally
        {
            _starting = null;
        }
    }

    public async Task WatchDepartmentAsync(int departmentId)
    {
        _departments.Add(departmentId);
        await EnsureConnectedAsync();
        await InvokeAsync(QueueHubMethods.WatchDepartment, departmentId);
    }

    public async Task WatchBoardAsync(int departmentId)
    {
        _boards.Add(departmentId);
        await EnsureConnectedAsync();
        await InvokeAsync(QueueHubMethods.WatchBoard, departmentId);
    }

    public async Task WatchTicketAsync(string ticketCode)
    {
        _tickets.Add(ticketCode);
        await EnsureConnectedAsync();
        await InvokeAsync(QueueHubMethods.WatchTicket, ticketCode);
    }

    public async Task WatchReceptionAsync()
    {
        _reception = true;
        await EnsureConnectedAsync();
        await InvokeAsync(QueueHubMethods.WatchReception);
    }

    public async Task StopWatchingDepartmentAsync(int departmentId)
    {
        _departments.Remove(departmentId);

        if (_connection?.State == HubConnectionState.Connected)
        {
            await InvokeAsync(QueueHubMethods.StopWatchingDepartment, departmentId);
        }
    }

    private async Task RejoinAsync()
    {
        foreach (var id in _departments)
        {
            await InvokeAsync(QueueHubMethods.WatchDepartment, id);
        }

        foreach (var id in _boards)
        {
            await InvokeAsync(QueueHubMethods.WatchBoard, id);
        }

        foreach (var code in _tickets)
        {
            await InvokeAsync(QueueHubMethods.WatchTicket, code);
        }

        if (_reception)
        {
            await InvokeAsync(QueueHubMethods.WatchReception);
        }
    }

    private async Task InvokeAsync(string method, object? argument = null)
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            if (argument is null)
            {
                await _connection.InvokeAsync(method);
            }
            else
            {
                await _connection.InvokeAsync(method, argument);
            }
        }
        catch (Exception)
        {
            // A refused subscription must not take the page down with it.
            SetState(LiveState.Lost);
        }
    }

    private void SetState(LiveState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
