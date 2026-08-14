using Blazored.LocalStorage;
using MediQueue.Shared.Contracts;

namespace MediQueue.Client.Services;

/// <summary>
/// Keeps the signed-in session across a page refresh, and remembers the ticket
/// a patient is holding so returning to the site puts them straight back on
/// their own status screen rather than asking for the code again.
/// </summary>
public class SessionStore
{
    private const string SessionKey = "mediqueue.session";
    private const string TicketKey = "mediqueue.ticket";

    private readonly ILocalStorageService _storage;

    public SessionStore(ILocalStorageService storage) => _storage = storage;

    public async Task<LoginResponse?> GetSessionAsync()
    {
        var session = await _storage.GetItemAsync<LoginResponse>(SessionKey);

        // An expired token would only produce 401s; drop it and ask again.
        if (session is not null && session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await ClearSessionAsync();
            return null;
        }

        return session;
    }

    public Task SaveSessionAsync(LoginResponse session) =>
        _storage.SetItemAsync(SessionKey, session).AsTask();

    public Task ClearSessionAsync() => _storage.RemoveItemAsync(SessionKey).AsTask();

    public Task<string?> GetTicketCodeAsync() => _storage.GetItemAsync<string>(TicketKey).AsTask();

    public Task RememberTicketAsync(string ticketCode) =>
        _storage.SetItemAsync(TicketKey, ticketCode).AsTask();

    public Task ForgetTicketAsync() => _storage.RemoveItemAsync(TicketKey).AsTask();
}
