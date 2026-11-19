using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FlyyLink.Shell;

/// An error from the licence server, such as "bad_code" or "computer_limit".
internal sealed class LicenceServerException(string code, int? limit = null) : Exception(code)
{
    public string Code { get; } = code;

    /// How many computers the licence covers, sent with "computer_limit".
    public int? Limit { get; } = limit;
}

/// Talks to the licence API on the FlyyLink website.
internal sealed class LicenceServer
{
    public const string Site = "https://flyylink.toneflix.net";

    private static readonly HttpClient Http = new() { BaseAddress = new Uri(Site), Timeout = TimeSpan.FromSeconds(20) };

    public sealed record Activation([property: JsonPropertyName("token")] string Token, [property: JsonPropertyName("secret")] string Secret);

    public sealed record Notice(
        [property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("linkText")] string? LinkText);

    public sealed record Notices([property: JsonPropertyName("notices")] Notice[] Items, [property: JsonPropertyName("latest")] int Latest);

    private sealed record TokenReply([property: JsonPropertyName("token")] string Token);

    private sealed record ErrorReply([property: JsonPropertyName("error")] string? Error, [property: JsonPropertyName("limit")] int? Limit);


    public async Task SendCodeAsync(string email) => await PostAsync<object>("/api/code", new { email });

    public Task<Activation> ActivateAsync(string email, string code, string machine, string name) =>
        PostAsync<Activation>("/api/activate", new { email, code, machine = new { id = machine, name, platform = "windows" } });

    public async Task<string> RefreshAsync(string machine, string secret) =>
        (await PostAsync<TokenReply>("/api/refresh", new { machine, secret })).Token;

    /// App notices newer than `after`. With no `after`, only the newest id, to start from.
    public Task<Notices> NoticesAsync(string machine, string? secret, int? after) =>
        PostAsync<Notices>("/api/notices", new { machine, secret, after });

    private static async Task<T> PostAsync<T>(string path, object body)
    {
        using var response = await Http.PostAsJsonAsync(path, body);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ErrorReply>().ConfigureAwait(false);
            throw new LicenceServerException(error?.Error ?? "server_error", error?.Limit);
        }

        return response.StatusCode == System.Net.HttpStatusCode.NoContent ? default! : (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
