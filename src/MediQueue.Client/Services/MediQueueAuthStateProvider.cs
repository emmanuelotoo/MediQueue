using System.Net.Http.Headers;
using System.Security.Claims;
using MediQueue.Shared.Contracts;
using Microsoft.AspNetCore.Components.Authorization;

namespace MediQueue.Client.Services;

/// <summary>
/// Holds who is signed in. Claims are rebuilt from the stored session rather
/// than parsed out of the token: the client only needs them to decide what to
/// show, and the API re-validates the token on every call regardless.
/// </summary>
public class MediQueueAuthStateProvider : AuthenticationStateProvider
{
    private static readonly AuthenticationState SignedOut =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly SessionStore _store;
    private readonly HttpClient _http;

    private LoginResponse? _session;

    public MediQueueAuthStateProvider(SessionStore store, HttpClient http)
    {
        _store = store;
        _http = http;
    }

    public LoginResponse? Session => _session;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _session ??= await _store.GetSessionAsync();

        if (_session is null)
        {
            return SignedOut;
        }

        ApplyToken(_session.AccessToken);
        return new AuthenticationState(Principal(_session));
    }

    public async Task SignInAsync(LoginResponse session)
    {
        _session = session;
        await _store.SaveSessionAsync(session);
        ApplyToken(session.AccessToken);

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Principal(session))));
    }

    public async Task SignOutAsync()
    {
        _session = null;
        await _store.ClearSessionAsync();
        _http.DefaultRequestHeaders.Authorization = null;

        NotifyAuthenticationStateChanged(Task.FromResult(SignedOut));
    }

    private void ApplyToken(string token) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static ClaimsPrincipal Principal(LoginResponse session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, session.FullName),
            new(ClaimTypes.Email, session.Email),
            new(ClaimTypes.Role, session.Role)
        };

        if (session.DepartmentId is not null)
        {
            claims.Add(new Claim("mediqueue:department", session.DepartmentId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "mediqueue"));
    }
}
