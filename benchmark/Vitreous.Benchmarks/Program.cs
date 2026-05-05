using BenchmarkDotNet.Running;

// Default: run all benchmark classes when no arguments are given.
// Pass --filter <pattern> to run a subset, e.g.:
//   dotnet run -c Release -- --filter "*Dispatch*"
//   dotnet run -c Release -- --filter "*Startup*"
var effectiveArgs = args.Length == 0
    ? ["--filter", "*"]
    : args;

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(effectiveArgs);
