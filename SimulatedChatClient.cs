using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RoutingChat;

/// <summary>
/// A fake model that streams a canned reply, so the sample runs with no API key and lets you
/// force outages on demand to watch failover happen.
/// </summary>
/// <remarks>
/// Replace instances of this with real clients — <c>OpenAIClient.GetChatClient(...).AsIChatClient()</c>,
/// for example — and the routing pipeline in <c>Program</c> is unchanged.
/// </remarks>
internal sealed class SimulatedChatClient : IChatClient
{
    private readonly string _persona;
    private readonly TimeSpan _latency;
    private readonly Action<SimulatedChatClient>? _onInvoked;

    public SimulatedChatClient(
        string name,
        string persona,
        TimeSpan latency,
        Action<SimulatedChatClient>? onInvoked = null)
    {
        Name = name;
        _persona = persona;
        _latency = latency;
        _onInvoked = onInvoked;
    }

    public string Name { get; }

    /// <summary>Gets or sets a value indicating whether every call fails, simulating an outage.</summary>
    public bool IsDown { get; set; }

    public int Invocations { get; private set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = new System.Text.StringBuilder();
        await foreach (ChatResponseUpdate update in
            GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            text.Append(update.Text);
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, text.ToString()));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Invocations++;
        _onInvoked?.Invoke(this);

        await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);

        if (IsDown)
        {
            // Thrown before anything is yielded, which is what lets failover reselect.
            throw new HttpRequestException($"{Name} is unavailable (503).");
        }

        string prompt = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        foreach (string word in Reply(prompt).Split(' '))
        {
            await Task.Delay(18, cancellationToken).ConfigureAwait(false);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
        // Nothing to release.
    }

    private string Reply(string prompt)
    {
        string trimmed = prompt.Length > 60 ? prompt[..57] + "..." : prompt;
        return $"[{Name}] {_persona} You asked: \"{trimmed}\". "
            + "This is a simulated reply, so the interesting part is the routing trace above.";
    }
}
