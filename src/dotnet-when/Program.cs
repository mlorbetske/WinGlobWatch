using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.CommandLineUtils;
using WinGlobWatch;

namespace dotnet_when
{
    public class Program
    {
        public static int Main(string[] args)
        {
            CommandLineApplication app = new CommandLineApplication();

            CommandOption includes = app.Option("-i|--include", "Include items matching the pattern", CommandOptionType.MultipleValue);
            CommandOption excludes = app.Option("-x|--exclude", "Exclude items matching the pattern", CommandOptionType.MultipleValue);
            CommandOption rateLimit = app.Option("-r|--rate-limit", "The minimum number of milliseconds that must pass between processing dirty or include events", CommandOptionType.SingleValue);
            CommandOption help = app.Option("-h|--help", "Shows help", CommandOptionType.NoValue);
            CommandArgument run = app.Argument("run", "The task to run when an included file changes");
            CommandArgument arguments = app.Argument("arguments", "The task to run when an included file changes");

            app.OnExecute(async () =>
            {
                if (help.HasValue())
                {
                    app.ShowHelp();
                    return 0;
                }

                int limit = int.Parse(rateLimit.Value() ?? "250");
                string currentDir = Directory.GetCurrentDirectory();
                Watcher<GlobbingPattern> watcher = await Watcher<GlobbingPattern>.For(currentDir).ConfigureAwait(false);
                Task currentTask = null;

                using (watcher)
                {
                    foreach (string value in includes.Values)
                    {
                        watcher.AddPattern(EntryKind.Include, new GlobbingPattern(value));
                    }

                    foreach (string value in excludes.Values)
                    {
                        watcher.AddPattern(EntryKind.Exclude, new GlobbingPattern(value));
                    }

                    while (!watcher.Ready.IsCompleted)
                    {
                        await watcher.Ready;
                    }

                    watcher.Dirty += (sender, e) =>
                    {
                        Task t = null;

                        t = Task.Run(async () =>
                        {
                            await Task.Delay(limit);

                            if (currentTask == t)
                            {
                                CommandResult execResult = Command.Create(new CommandSpec(run.Value, arguments.Value, CommandResolutionStrategy.Path)).ForwardStdErr().ForwardStdOut().Execute();
                                if (execResult.ExitCode != 0)
                                {
                                    Console.WriteLine($"{run.Value} {arguments.Value} exited with code {execResult.ExitCode}".Red().Bold());
                                }
                                watcher.Clean();
                            }
                        });

                        currentTask = t;
                    };

                    watcher.FilteredEntriesChanged += (sender, e) =>
                    {
                        Task t = null;

                        t = Task.Run(async () =>
                        {
                            await Task.Delay(limit);

                            if (currentTask == t)
                            {
                                CommandResult execResult = Command.Create(new CommandSpec(run.Value, arguments.Value, CommandResolutionStrategy.Path)).ForwardStdErr().ForwardStdOut().Execute();
                                if (execResult.ExitCode != 0)
                                {
                                    Console.WriteLine($"{run.Value} {arguments.Value} exited with code {execResult.ExitCode}".Red().Bold());
                                }
                                watcher.Clean();
                            }
                        });

                        currentTask = t;
                    };

                    Console.WriteLine($"Now watching for changes in {currentDir}");
                    Console.WriteLine("Press [Enter] to stop...");
                    Console.ReadLine();
                }
                return 0;
            });

            int result;
            try
            {
                result = app.Execute(args);
            }
            catch (Exception ex)
            {
                AggregateException ax = ex as AggregateException;

                while (ax != null && ax.InnerExceptions.Count == 1)
                {
                    ex = ax.InnerException;
                    ax = ex as AggregateException;
                }

                Reporter.Error.WriteLine(ex.Message.Bold().Red());

                while (ex.InnerException != null)
                {
                    ex = ex.InnerException;
                    ax = ex as AggregateException;

                    while (ax != null && ax.InnerExceptions.Count == 1)
                    {
                        ex = ax.InnerException;
                        ax = ex as AggregateException;
                    }

                    Reporter.Error.WriteLine(ex.Message.Bold().Red());
                }

                Reporter.Error.WriteLine(ex.StackTrace.Bold().Red());
                result = 1;
            }

            return result;
        }
    }
}
