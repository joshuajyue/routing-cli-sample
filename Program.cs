using Microsoft.Extensions.AI;
using OpenAI;
using RoutingChat;
using Spectre.Console;

// Set OPENAI_API_KEY to route to real models. Without it, the sample uses simulated ones so it
// still runs, and so outages can be forced with /kill.
string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
bool live = !string.IsNullOrWhiteSpace(apiKey);

string chatModel = Environment.GetEnvironmentVariable("OPENAI_CHAT_MODEL") ?? "gpt-4o-mini";
string embeddingModel = Environment.GetEnvironmentVariable("OPENAI_EMBEDDING_MODEL") ?? "text-embedding-3-small";
OpenAIClient? openAI = live ? new OpenAIClient(apiKey) : null;

// Each route reports when it is invoked so the sample can print a routing trace.
List<(string Name, bool Failed)> trace = [];
void Record(RouteChatClient client) => trace.Add((client.Name, client.IsDown));

RouteChatClient Route(string name, string persona, int latencyMs)
{
    IChatClient inner;
    if (openAI is not null)
    {
        // The route's own options belong on the route's client, layered over the request options.
        string model = Environment.GetEnvironmentVariable($"OPENAI_CHAT_MODEL_{name.ToUpperInvariant()}") ?? chatModel;
        inner = openAI.GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .ConfigureOptions(options => options.Instructions = persona)
            .Build();
    }
    else
    {
        inner = new SimulatedChatClient(name, persona, TimeSpan.FromMilliseconds(latencyMs));
    }

    return new RouteChatClient(name, inner, Record);
}

using RouteChatClient code = Route("code", "You are a precise programming assistant. Answer with code where it helps.", 260);
using RouteChatClient creative = Route("creative", "You are a vivid, imaginative writing assistant.", 300);
using RouteChatClient math = Route("math", "You are a careful quantitative assistant. Show your reasoning.", 220);
using RouteChatClient general = Route("general", "You are a helpful general-purpose assistant.", 180);

RouteChatClient[] all = [code, creative, math, general];

using IEmbeddingGenerator<string, Embedding<float>> embeddings = openAI is not null
    ? openAI.GetEmbeddingClient(embeddingModel).AsIEmbeddingGenerator()
    : new KeywordEmbeddingGenerator();

// Route by what the message is about. Below the threshold, the default client wins.
using var semantic = new SemanticRoutingChatClient(
    embeddings,
    new Dictionary<IChatClient, IReadOnlyList<string>>
    {
        [code] =
        [
            "write a function that sorts a list",
            "why does my code throw a null reference exception",
            "refactor this class to use dependency injection",
            "explain async await in csharp",
        ],
        [creative] =
        [
            "write me a short poem about the sea",
            "brainstorm names for a coffee shop",
            "draft a friendly announcement email",
            "give me story ideas about time travel",
        ],
        [math] =
        [
            "what is the derivative of x squared",
            "calculate compound interest over ten years",
            "solve this system of linear equations",
            "what is the probability of rolling two sixes",
        ],
    },
    defaultClient: general,
    scoreThreshold: live ? 0.35f : 0.25f,
    leaveOpen: true);

// If the specialist that semantic routing picked is down, fall back to the general model.
using var router = new OrderedFailoverChatClient([semantic, general], leaveOpen: true);

List<ChatMessage> history = [];

WriteHeader();

while (true)
{
    AnsiConsole.Markup("[bold cyan]you[/][grey]>[/] ");
    string? input = Console.ReadLine();

    if (input is null)
    {
        // Redirected input ran out, or the user pressed Ctrl+D.
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.StartsWith('/'))
    {
        if (HandleCommand(input))
        {
            break;
        }

        continue;
    }

    history.Add(new ChatMessage(ChatRole.User, input));
    trace.Clear();

    try
    {
        var reply = new System.Text.StringBuilder();
        bool routePrinted = false;

        await foreach (ChatResponseUpdate update in router.GetStreamingResponseAsync(history))
        {
            if (!routePrinted)
            {
                WriteTrace();
                routePrinted = true;
            }

            reply.Append(update.Text);
            AnsiConsole.Write(new Markup($"[white]{Markup.Escape(update.Text ?? string.Empty)}[/]"));
        }

        history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }
    catch (Exception ex)
    {
        WriteTrace();
        AnsiConsole.MarkupLine($"[red]all routes failed:[/] [grey]{Markup.Escape(ex.Message)}[/]");
        AnsiConsole.WriteLine();
        history.RemoveAt(history.Count - 1);
    }
}

void WriteHeader()
{
    AnsiConsole.Write(new Rule("[bold]routing-chat[/]").LeftJustified().RuleStyle("grey"));
    AnsiConsole.MarkupLine(live
        ? $"[grey]Microsoft.Extensions.AI routing, live on OpenAI ([white]{Markup.Escape(chatModel)}[/], [white]{Markup.Escape(embeddingModel)}[/]).[/]"
        : "[grey]Microsoft.Extensions.AI routing, with simulated models. Set [white]OPENAI_API_KEY[/] to go live.[/]");
    AnsiConsole.WriteLine();

    var tree = new Tree("[bold]OrderedFailoverChatClient[/]").Style("grey");
    TreeNode semanticNode = tree.AddNode("[bold]SemanticRoutingChatClient[/] [grey]routes by content[/]");
    semanticNode.AddNode("[cyan]code[/] [grey]programming[/]");
    semanticNode.AddNode("[magenta]creative[/] [grey]writing[/]");
    semanticNode.AddNode("[yellow]math[/] [grey]calculation[/]");
    semanticNode.AddNode("[green]general[/] [grey]default below threshold[/]");
    tree.AddNode("[green]general[/] [grey]backstop if the specialist fails[/]");
    AnsiConsole.Write(tree);

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine("[grey]Try \"why does my code throw a null reference\", then [white]/kill code[/] and ask again.[/]");
    AnsiConsole.MarkupLine("[grey]Commands: [white]/kill[/] [white]/revive[/] [white]/status[/] [white]/reset[/] [white]/help[/] [white]/quit[/][/]");
    AnsiConsole.WriteLine();
}

void WriteTrace()
{
    if (trace.Count == 0)
    {
        return;
    }

    IEnumerable<string> hops = trace.Select(hop => hop.Failed
        ? $"[red]{hop.Name} x[/]"
        : $"[{ColorFor(hop.Name)}]{hop.Name}[/] [green]ok[/]");

    AnsiConsole.MarkupLine($"[grey]route:[/] {string.Join(" [grey]->[/] ", hops)}");
}

static string ColorFor(string name) => name switch
{
    "code" => "cyan",
    "creative" => "magenta",
    "math" => "yellow",
    _ => "green",
};

bool HandleCommand(string input)
{
    string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    string command = parts[0].ToLowerInvariant();
    string? argument = parts.Length > 1 ? parts[1].ToLowerInvariant() : null;

    switch (command)
    {
        case "/quit":
        case "/exit":
            return true;

        case "/help":
            AnsiConsole.MarkupLine("[grey]/kill <model>    take a model down so failover kicks in[/]");
            AnsiConsole.MarkupLine("[grey]/revive <model>  bring it back[/]");
            AnsiConsole.MarkupLine("[grey]/status          show health and invocation counts[/]");
            AnsiConsole.MarkupLine("[grey]/reset           clear the conversation[/]");
            AnsiConsole.MarkupLine("[grey]/quit            exit[/]");
            break;

        case "/status":
            var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
            table.AddColumn("model");
            table.AddColumn("state");
            table.AddColumn(new TableColumn("calls").RightAligned());
            foreach (RouteChatClient client in all)
            {
                table.AddRow(
                    $"[{ColorFor(client.Name)}]{client.Name}[/]",
                    client.IsDown ? "[red]down[/]" : "[green]up[/]",
                    client.Invocations.ToString());
            }

            AnsiConsole.Write(table);
            break;

        case "/reset":
            history.Clear();
            AnsiConsole.MarkupLine("[grey]conversation cleared[/]");
            break;

        case "/kill":
        case "/revive":
            RouteChatClient? target = all.FirstOrDefault(c => c.Name == argument);
            if (target is null)
            {
                AnsiConsole.MarkupLine($"[red]unknown model.[/] [grey]try: {string.Join(", ", all.Select(c => c.Name))}[/]");
                break;
            }

            target.IsDown = command == "/kill";
            AnsiConsole.MarkupLine(target.IsDown
                ? $"[red]{target.Name} is down[/]"
                : $"[green]{target.Name} is back[/]");
            break;

        default:
            AnsiConsole.MarkupLine("[grey]unknown command, try /help[/]");
            break;
    }

    AnsiConsole.WriteLine();
    return false;
}
