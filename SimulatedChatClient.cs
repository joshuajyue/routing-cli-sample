using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RoutingChat;

/// <summary>
/// A fake model that streams a canned reply, so the sample runs with no API key.
/// </summary>
/// <remarks>
/// Set <c>OPENAI_API_KEY</c> to replace these with real OpenAI clients. Nothing about the routing
/// pipeline in <c>Program</c> changes either way.
/// </remarks>
internal sealed class SimulatedChatClient : IChatClient
{
    private readonly string _name;
    private readonly string _persona;
    private readonly TimeSpan _latency;

    public SimulatedChatClient(string name, string persona, TimeSpan latency)
    {
        _name = name;
        _persona = persona;
        _latency = latency;
    }

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
        await Task.Delay(_latency, cancellationToken).ConfigureAwait(false);

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
        return $"[{_name}] {_persona} You asked: \"{trimmed}\". "
            + "This is a simulated reply, so the interesting part is the routing trace above.";
    }
}
