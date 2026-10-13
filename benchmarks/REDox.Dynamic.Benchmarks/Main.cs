using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

var config = DefaultConfig.Instance
    .WithOptions(ConfigOptions.JoinSummary);

var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);

switcher.Run(args, config);