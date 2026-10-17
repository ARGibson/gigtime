using Gigtime.Platform;

namespace Gigtime.Cli.Tests;

internal sealed record FakePaths(string DataDirectory, string RuntimeDirectory, string ConfigDirectory) : IPlatformPaths;
