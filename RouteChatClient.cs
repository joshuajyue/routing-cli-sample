using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace RoutingChat;

/// <summary>
/// Names a route, reports when it is invoked, and can be forced to fail on demand.
/// </summary>
/// <remarks>
/// Wrapping the inner client rather than building this into the model itself means the sample
/// behaves the same whether the route is a real OpenAI client or a simulated one.
/// </remarks>
internal sealed class RouteChatClient : DelegatingChatClient
{
    private readonly Action<RouteChatClient>? _onInvoked;

    public RouteChatClient(string name, IChatClient innerClient, Action<RouteChatClient>? onInvoked = null)
        : base(innerClient)
    {
        Name = name;
        _onInvoked = onInvoked;
    }

    public string Name { get; }

    /// <summary>Gets or sets a value indicating whether every call fails, simulating an outage.</summary>
    public bool IsDown { get; set; }

    public int Invocations { get; private set; }

    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Enter();
        return base.GetResponseAsync(messages, options, cancellationToken);
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Enter();

        await foreach (ChatResponseUpdate update in
            base.GetStreamingResponseAsync(messages, options, cancellationToken).ConfigureAwait(false))
        {
            yield return update;
        }
    }

    private void Enter()
    {
        Invocations++;
        _onInvoked?.Invoke(this);

        if (IsDown)
        {
            // Thrown before any output is produced, which is what lets failover reselect.
            throw new HttpRequestException($"{Name} is unavailable (503).");
        }
    }
}
