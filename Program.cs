using Microsoft.Extensions.AI;
using RoutingChat;
using Spectre.Console;

// Each simulated model reports when it is invoked so the sample can print a routing trace.
List<(string Name, bool Failed)> trace = [];
void Record(SimulatedChatClient client) => trace.Add((client.Name, client.IsDown));

SimulatedChatClient code = new("code", "I handle programming questions.", TimeSpan.FromMilliseconds(260), Record);
SimulatedChatClient creative = new("creative", "I handle writing and ideation.", TimeSpan.FromMilliseconds(300), Record);
SimulatedChatClient math = new("math", "I handle calculation and analysis.", TimeSpan.FromMilliseconds(220), Record);
SimulatedChatClient general = new("general", "I am the catch-all backstop.", TimeSpan.FromMilliseconds(180), Record);

SimulatedChatClient[] all = [code, creative, math, general];

using var embeddings = new KeywordEmbeddingGenerator();

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
    scoreThreshold: 0.25f,
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
    AnsiConsole.MarkupLine("[grey]Microsoft.Extensions.AI routing, with simulated models.[/]");
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
            foreach (SimulatedChatClient client in all)
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
            SimulatedChatClient? target = all.FirstOrDefault(c => c.Name == argument);
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
