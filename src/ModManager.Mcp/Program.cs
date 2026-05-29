using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The 626 Mod Launcher agent-access MCP server (stdio). Phase 1 is READ-ONLY: it exposes the
// launcher's state to a local agent over the Model Context Protocol, running headless against
// ModManager.Core (the app need not be open). Write tools + the live-app channel come later.
var builder = Host.CreateApplicationBuilder(args);

// stdout is the JSON-RPC channel — route ALL logging to stderr so it never corrupts the protocol.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
