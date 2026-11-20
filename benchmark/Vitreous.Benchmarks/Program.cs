using BenchmarkDotNet.Running;

// Default: run all benchmark classes when no filter is given (other options such as
// --runtimes still apply). Pass --filter <pattern> to run a subset, e.g.:
//   dotnet run -c Release -f net11.0 -- --filter "*Dispatch*"
//   dotnet run -c Release -f net11.0 -- --filter "*Startup*"
var effectiveArgs = args.Contains("--filter") || args.Contains("-f")
    ? args
    : [.. args, "--filter", "*"];

BenchmarkSwitcher
    .FromAssembly(typeof(Program).Assembly)
    .Run(effectiveArgs);
