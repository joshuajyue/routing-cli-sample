# routing-chat

A small terminal chat app that shows content-based routing and failover in
[`Microsoft.Extensions.AI`](https://www.nuget.org/packages/Microsoft.Extensions.AI), using
[Spectre.Console](https://spectreconsole.net) for the UI.

The models are simulated, so it runs with no API key — and you can take one down mid-conversation
to watch failover pick up.

```
you> why does my code throw a null reference exception
route: code ok
[code] I handle programming questions. ...

you> /kill code
code is down

you> why does my code throw a null reference exception
route: code x -> general ok
[general] I am the catch-all backstop. ...
```

## Run it

```bash
dotnet run
```

Then try:

| | |
| --- | --- |
| `why does my code throw a null reference exception` | routes to **code** |
| `write me a short poem about the sea` | routes to **creative** |
| `what is the derivative of x squared` | routes to **math** |
| `hey, how's it going` | scores below the threshold, so **general** |
| `/kill code` then ask a coding question again | **code** fails, failover reselects **general** |

Commands: `/kill <model>`, `/revive <model>`, `/status`, `/reset`, `/help`, `/quit`.

## The pipeline

```
OrderedFailoverChatClient
├── SemanticRoutingChatClient      picks a specialist by content
│   ├── code                       programming
│   ├── creative                   writing
│   ├── math                       calculation
│   └── general                    default, when no profile scores high enough
└── general                        backstop, when the specialist fails
```

Both are plain `IChatClient` implementations, so they nest: the semantic router is just another
client from the failover client's point of view. `SemanticRoutingChatClient` embeds the last user
message, compares it against the example utterances registered for each specialist, and picks the
best match above `scoreThreshold`. If that specialist then throws before producing output,
`OrderedFailoverChatClient` moves to the next entry in its list.

## Files

| | |
| --- | --- |
| `Program.cs` | pipeline wiring and the Spectre.Console UI |
| `SimulatedChatClient.cs` | a fake model that streams a canned reply and can be forced to fail |
| `KeywordEmbeddingGenerator.cs` | an offline `IEmbeddingGenerator` so semantic routing works without a network call |

## Using real models

Swap the `SimulatedChatClient` instances in `Program.cs` for real ones — nothing else changes:

```csharp
IChatClient code = new OpenAIClient(apiKey)
    .GetChatClient("gpt-4o-mini")
    .AsIChatClient();
```

Do the same for the embedding generator, which is the part that decides where a message routes:

```csharp
IEmbeddingGenerator<string, Embedding<float>> embeddings = new OpenAIClient(apiKey)
    .GetEmbeddingClient("text-embedding-3-small")
    .AsIEmbeddingGenerator();
```

`KeywordEmbeddingGenerator` hashes words into a vector and normalizes, so cosine similarity
tracks vocabulary overlap. That is enough to route convincingly offline, but a real embedding
model will match on meaning rather than on shared words.

## Package version

The routing types ship in `Microsoft.Extensions.AI` **10.9.0**. Until that is on NuGet.org, this
sample restores them from `./local-packages`, built from a clone of
[dotnet/extensions](https://github.com/dotnet/extensions):

```bash
git clone https://github.com/dotnet/extensions
cd extensions
dotnet pack src/Libraries/Microsoft.Extensions.AI.Abstractions/Microsoft.Extensions.AI.Abstractions.csproj -c Release -o <sample>/local-packages
dotnet pack src/Libraries/Microsoft.Extensions.AI/Microsoft.Extensions.AI.csproj -c Release -o <sample>/local-packages
```

Once 10.9.0 is published, set `MeaiVersion` in `RoutingChat.csproj` to `10.9.0` and delete the
`local` source from `NuGet.config`.

These types are `[Experimental]` under diagnostic ID `MEAI001`, which `RoutingChat.csproj`
suppresses with `<NoWarn>`.
