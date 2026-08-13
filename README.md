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

With no configuration the models are simulated, so it starts instantly and you can force outages.

### Against real OpenAI models

Set an API key and the same pipeline routes to real models:

```bash
# PowerShell
$env:OPENAI_API_KEY = "sk-..."
dotnet run

# bash
export OPENAI_API_KEY=sk-...
dotnet run
```

| variable | default | |
| --- | --- | --- |
| `OPENAI_API_KEY` | — | set it to go live; unset means simulated |
| `OPENAI_CHAT_MODEL` | `gpt-4o-mini` | model for any route without an override |
| `OPENAI_EMBEDDING_MODEL` | `text-embedding-3-small` | model that decides where a message routes |
| `OPENAI_CHAT_MODEL_<ROUTE>` | — | model for one route |
| `OPENAI_REASONING_<ROUTE>` | — | `none`, `low`, `medium`, or `high` |
| `OPENAI_TEMPERATURE_<ROUTE>` | — | e.g. `0.8` |

`<ROUTE>` is `CODE`, `CREATIVE`, `MATH`, or `GENERAL`. Whatever you set shows up in the tree the
app prints at startup, so you can confirm it took effect.

Per-route settings are where routing earns its keep — send hard problems to a stronger model and
everything else to a cheap one:

```bash
export OPENAI_CHAT_MODEL=gpt-5.4            # default for every route
export OPENAI_CHAT_MODEL_CODE=gpt-5.5       # code gets the stronger model...
export OPENAI_REASONING_CODE=high           # ...thinking hard
export OPENAI_CHAT_MODEL_MATH=gpt-5.5       # math too...
export OPENAI_REASONING_MATH=medium         # ...but less of it
export OPENAI_TEMPERATURE_CREATIVE=0.8      # creative gets more variety
```

`run-gpt5.ps1` and `run-gpt5.sh` in this repo apply exactly that mix:

```powershell
$env:OPENAI_API_KEY = "sk-..."
. .\run-gpt5.ps1
dotnet run
```

```bash
export OPENAI_API_KEY=sk-...
source ./run-gpt5.sh
dotnet run
```

Dot-source or `source` them so the variables survive into `dotnet run`.

These per-route settings are applied with `ConfigureOptions` on the route's own client, not by the
router — routing decides *which* client to invoke, and the client carries its own configuration.

`/kill` still works in live mode: `RouteChatClient` wraps each route and fails before calling the
real model, so you can demonstrate failover without an actual outage.

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
| `RouteChatClient.cs` | names a route, records invocations, and can be forced to fail |
| `SimulatedChatClient.cs` | a fake model that streams a canned reply, used when no API key is set |
| `KeywordEmbeddingGenerator.cs` | an offline `IEmbeddingGenerator` so semantic routing works without a network call |

## How the two modes differ

Only the construction of the inner clients changes. Everything downstream — the semantic router,
the failover client, the trace, `/kill` — is identical:

```csharp
IChatClient inner = openAI is not null
    ? openAI.GetChatClient(model)
        .AsIChatClient()
        .AsBuilder()
        .ConfigureOptions(options => options.Instructions = persona)
        .Build()
    : new SimulatedChatClient(name, persona, latency);

return new RouteChatClient(name, inner, Record);
```

The `ConfigureOptions` call is worth noting: the route's own options belong on the route's client,
layered over whatever options the request carried. Routing chooses *which* client to invoke, and
the client supplies its own configuration.

`KeywordEmbeddingGenerator` hashes words into a vector and normalizes, so cosine similarity tracks
vocabulary overlap. That is enough to route convincingly offline, but a real embedding model
matches on meaning rather than on shared words — which is why the score threshold is slightly
higher in live mode.

## Package version

The routing types ship in `Microsoft.Extensions.AI` **10.9.0**, so `dotnet run` restores them from
NuGet.org like any other package.

These types are `[Experimental]` under diagnostic ID `MEAI001`, which `RoutingChat.csproj`
suppresses with `<NoWarn>`.

