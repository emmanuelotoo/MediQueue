namespace MediQueue.Api.Tests;

/// <summary>
/// Reads responses with the system's own JSON settings rather than the client
/// defaults, so the tests parse exactly what a real caller would.
/// </summary>
internal static class HttpJsonExtensions
{
    public static Task<T?> ReadJsonAsync<T>(this HttpContent content) =>
        content.ReadFromJsonAsync<T>(MediQueueJson.Options);

    public static Task<T?> GetJsonAsync<T>(this HttpClient client, string requestUri) =>
        client.GetFromJsonAsync<T>(requestUri, MediQueueJson.Options);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string requestUri, T value) =>
        client.PostAsJsonAsync(requestUri, value, MediQueueJson.Options);
}
