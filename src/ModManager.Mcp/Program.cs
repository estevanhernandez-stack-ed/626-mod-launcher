using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The 626 Mod Launcher agent-access MCP server (stdio). Phase 1 is READ-ONLY: it exposes the
// launcher's state to a local agent over the Model Context Protocol, running headless against
// ModManager.Core (the app need not be open). Write tools + the live-app channel come later.
// Apply the launcher's cached game-definition feed BEFORE the first tool answers anything. Without
// this the MCP reads the embedded snapshot while the app reads embedded + feed, and the two report
// different mod folders for the same game (A18). Best-effort: no cache or a failed gate simply leaves
// the embedded snapshot in place.
var feedApplied = ModManager.Mcp.McpManifest.Apply();

var builder = Host.CreateApplicationBuilder(args);

// stdout is the JSON-RPC channel — route ALL logging to stderr so it never corrupts the protocol.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var host = builder.Build();
// stderr, never stdout - stdout is the JSON-RPC channel.
host.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("626.mcp")
    .LogInformation("game-definition feed: {State}", feedApplied ? "cached remote applied" : "embedded snapshot");

await host.RunAsync();
