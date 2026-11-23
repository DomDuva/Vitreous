using Vitreous.Abstractions;
using VitreousConsole._1.Common;

namespace VitreousConsole._1.Features.Todos;

/// <summary>Maps the command line to the verb of each slice. A new feature adds a line here.</summary>
public static class TodoCli
{
    private const string Help = """
        Usage:
          add <title>   Add a todo
          list          List every todo
          show <id>     Show a todo
          done <id>     Mark a todo as done
        """;

    public static Task<int> Run(string[] args, ISender sender, CliOutput output, CancellationToken ct) => args switch
    {
        ["add", .. var words]                                => CreateTodoCli.Run(string.Join(' ', words), sender, output, ct),
        ["list"]                                             => ListTodosCli.Run(sender, output, ct),
        ["show", var arg] when int.TryParse(arg, out var id) => GetTodoCli.Run(id, sender, output, ct),
        ["done", var arg] when int.TryParse(arg, out var id) => CompleteTodoCli.Run(id, sender, output, ct),
        [] or ["help" or "-h" or "--help"]                   => Print(output.Out, ExitCodes.Success),
        _                                                    => Print(output.Error, ExitCodes.Usage),
    };

    private static Task<int> Print(TextWriter writer, int exitCode)
    {
        writer.WriteLine(Help);
        return Task.FromResult(exitCode);
    }
}
