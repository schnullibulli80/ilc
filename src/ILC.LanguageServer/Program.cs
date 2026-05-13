using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ILC.Compiler.Binding;
using ILC.Compiler.Core;
using ILC.Compiler.Syntax;

var workspaceRoot = Directory.GetCurrentDirectory();
var debugEnabled = false;
for (var index = 0; index < args.Length; index++)
{
    if (args[index] == "--workspace" && index + 1 < args.Length)
    {
        workspaceRoot = Path.GetFullPath(args[++index]);
        continue;
    }

    if (args[index] == "--debug")
    {
        debugEnabled = true;
    }
}

var server = new LanguageServer(workspaceRoot, debugEnabled);
await server.RunAsync();

internal sealed class LanguageServer(string workspaceRoot, bool debugEnabled)
{
    private static readonly TimeSpan DiagnosticsDebounceDelay = TimeSpan.FromMilliseconds(350);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, string> documents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> documentRevisions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> scheduledDiagnostics = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedModelSnapshot> modelSnapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ModelSnapshot> importedWorkspaceSnapshots = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly Stream input = Console.OpenStandardInput();
    private readonly Stream output = Console.OpenStandardOutput();
    private readonly LanguageModel model = new(workspaceRoot);
    private long modelRevision;

    public async Task RunAsync()
    {
        while (true)
        {
            var payload = await ReadPayloadAsync();
            if (payload is null)
            {
                return;
            }

            try
            {
                await HandleMessageAsync(payload);
            }
            catch (Exception ex)
            {
                await LogAsync($"unhandled {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private async Task<string?> ReadPayloadAsync()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadAsciiLineAsync();
            if (line is null)
            {
                return null;
            }

            if (line.Length == 0)
            {
                break;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        if (!headers.TryGetValue("Content-Length", out var lengthText) || !int.TryParse(lengthText, out var length))
        {
            return null;
        }

        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await input.ReadAsync(buffer.AsMemory(offset, length - offset));
            if (read == 0)
            {
                return null;
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private async Task<string?> ReadAsciiLineAsync()
    {
        var buffer = new List<byte>();
        while (true)
        {
            var value = input.ReadByte();
            if (value < 0)
            {
                return buffer.Count == 0 ? null : Encoding.ASCII.GetString(buffer.ToArray());
            }

            if (value == '\n')
            {
                if (buffer.Count > 0 && buffer[^1] == '\r')
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                return Encoding.ASCII.GetString(buffer.ToArray());
            }

            buffer.Add((byte)value);
            await Task.Yield();
        }
    }

    private async Task HandleMessageAsync(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
        var hasId = root.TryGetProperty("id", out var idElement);

        switch (method)
        {
            case "initialize":
                await RespondAsync(idElement, new
                {
                    capabilities = new
                    {
                        textDocumentSync = 1,
                        completionProvider = new { triggerCharacters = new[] { ".", ":" } },
                        hoverProvider = true,
                        signatureHelpProvider = new { triggerCharacters = new[] { "(", ",", ";" } },
                        documentSymbolProvider = true,
                        definitionProvider = true,
                        codeActionProvider = true,
                        workspaceSymbolProvider = true
                    },
                    serverInfo = new { name = "ILC Language Server", version = "0.1.0" }
                });
                break;
            case "initialized":
                await LogAsync("initialized");
                await LogDebugAsync($"workspace={workspaceRoot}");
                break;
            case "shutdown" when hasId:
                await RespondAsync(idElement, null);
                break;
            case "exit":
                Environment.Exit(0);
                break;
            case "textDocument/didOpen":
                HandleDidOpen(root);
                break;
            case "textDocument/didChange":
                HandleDidChange(root);
                break;
            case "textDocument/didSave":
                await PublishDiagnosticsNowAsync(GetDocumentUri(root), "save");
                break;
            case "textDocument/completion" when hasId:
                await RespondAsync(idElement, HandleCompletion(root));
                break;
            case "textDocument/hover" when hasId:
                await RespondAsync(idElement, HandleHover(root));
                break;
            case "textDocument/signatureHelp" when hasId:
                await RespondAsync(idElement, await HandleSignatureHelpAsync(root));
                break;
            case "textDocument/documentSymbol" when hasId:
                await RespondAsync(idElement, HandleDocumentSymbols(root));
                break;
            case "textDocument/definition" when hasId:
                await RespondAsync(idElement, HandleDefinition(root));
                break;
            case "textDocument/codeAction" when hasId:
                await RespondAsync(idElement, HandleCodeAction(root));
                break;
            case "workspace/symbol" when hasId:
                await RespondAsync(idElement, HandleWorkspaceSymbols(root));
                break;
            default:
                if (hasId)
                {
                    await RespondAsync(idElement, null);
                }
                break;
        }
    }

    private void HandleDidOpen(JsonElement root)
    {
        var textDocument = root.GetProperty("params").GetProperty("textDocument");
        var uri = textDocument.GetProperty("uri").GetString() ?? string.Empty;
        var text = textDocument.GetProperty("text").GetString() ?? string.Empty;
        UpdateDocument(uri, text);
        _ = PublishDiagnosticsAsync(uri);
    }

    private void HandleDidChange(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        var uri = parameters.GetProperty("textDocument").GetProperty("uri").GetString() ?? string.Empty;
        var changes = parameters.GetProperty("contentChanges");
        if (changes.GetArrayLength() > 0)
        {
            UpdateDocument(uri, changes[0].GetProperty("text").GetString() ?? string.Empty);
            ScheduleDiagnostics(uri);
        }
    }

    private void UpdateDocument(string uri, string text)
    {
        documents[uri] = text;
        var revision = Interlocked.Increment(ref modelRevision);
        documentRevisions[uri] = revision;
        modelSnapshots.TryRemove(uri, out _);
        modelSnapshots.TryRemove(WorkspaceCacheKey, out _);
        _ = LogDebugAsync($"model cache invalidated uri={uri} revision={revision}");
    }

    private void ScheduleDiagnostics(string uri)
    {
        CancelScheduledDiagnostics(uri);

        var cancellation = new CancellationTokenSource();
        scheduledDiagnostics[uri] = cancellation;
        _ = PublishDiagnosticsAfterDelayAsync(uri, cancellation);
    }

    private async Task PublishDiagnosticsAfterDelayAsync(string uri, CancellationTokenSource cancellation)
    {
        try
        {
            await LogDebugAsync($"diagnostics scheduled uri={uri} delayMs={(int)DiagnosticsDebounceDelay.TotalMilliseconds}");
            await Task.Delay(DiagnosticsDebounceDelay, cancellation.Token);
            await PublishDiagnosticsAsync(uri, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            await LogDebugAsync($"diagnostics canceled uri={uri}");
        }
        finally
        {
            if (scheduledDiagnostics.TryGetValue(uri, out var current) && ReferenceEquals(current, cancellation))
            {
                scheduledDiagnostics.TryRemove(uri, out _);
            }

            cancellation.Dispose();
        }
    }

    private async Task PublishDiagnosticsNowAsync(string uri, string reason)
    {
        CancelScheduledDiagnostics(uri);
        await LogDebugAsync($"diagnostics immediate uri={uri} reason={reason}");
        await PublishDiagnosticsAsync(uri);
    }

    private void CancelScheduledDiagnostics(string uri)
    {
        if (scheduledDiagnostics.TryRemove(uri, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    private object HandleCompletion(JsonElement root)
    {
        var uri = GetDocumentUri(root);
        var position = GetPosition(root);
        _ = LogDebugAsync($"completion request uri={uri} line={position.Line + 1} char={position.Character + 1}");
        var snapshot = BuildModel(uri);
        var context = model.GetCompletionContext(snapshot, position.Line, position.Character);
        if (context.DebugContext.Length > 0)
        {
            _ = LogDebugAsync($"completion context uri={uri} line={position.Line + 1} char={position.Character + 1} {context.DebugContext} symbols={context.Symbols.Count}");
        }

        var keywordItems = context.IncludeKeywords
            ? model.Keywords.Select(keyword => new LanguageCompletionItem(keyword, 14, "ILC keyword", null, $"90_{keyword}"))
            : Enumerable.Empty<LanguageCompletionItem>();
        var builtInItems = context.IncludeBuiltInTypes
            ? model.BuiltInTypes.Select(type => new LanguageCompletionItem(type, 7, "ILC built-in type", null, $"30_{type}"))
            : Enumerable.Empty<LanguageCompletionItem>();
        var symbolItems = context.Symbols.Select(symbol => new
            LanguageCompletionItem(
                symbol.Name,
                ToCompletionKind(symbol.Kind),
                symbol.Signature,
                symbol.Documentation.Length > 0 || symbol.Parameters.Count > 0 || symbol.ReturnsDocumentation.Length > 0
                    ? (object)new { kind = "markdown", value = symbol.ToMarkdown() }
                    : null,
                CompletionSortText(symbol)));
        var items = keywordItems
            .Concat(builtInItems)
            .Concat(symbolItems)
            .GroupBy(item => $"{item.Label}:{item.Detail}", StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(item => new { label = item.Label, kind = item.Kind, detail = item.Detail, documentation = item.Documentation, sortText = item.SortText })
            .Take(1000)
            .ToArray();
        return new { isIncomplete = false, items };
    }

    private static string CompletionSortText(LanguageSymbol symbol)
    {
        var kindRank = symbol.Kind switch
        {
            "local" or "parameter" => "00",
            "property" or "field" or "enumMember" => "10",
            "method" or "function" or "constructor" => "20",
            "class" or "interface" or "enum" => "30",
            "constant" => "40",
            _ => "80"
        };
        var sourceRank = symbol.Uri.Contains("/libs/shipped/", StringComparison.Ordinal) ? "2" : "1";
        return $"{kindRank}_{sourceRank}_{symbol.Name}";
    }

    private object? HandleHover(JsonElement root)
    {
        var uri = GetDocumentUri(root);
        var position = GetPosition(root);
        _ = LogDebugAsync($"hover request uri={uri} line={position.Line + 1} char={position.Character + 1}");
        var snapshot = BuildModel(uri);
        var indexerSymbol = model.ResolveIndexSymbolAt(snapshot, position.Line, position.Character);
        if (indexerSymbol is not null)
        {
            return new
            {
                contents = new
                {
                    kind = "markdown",
                    value = indexerSymbol.ToMarkdown()
                },
                range = ToRange(indexerSymbol.Range)
            };
        }

        var word = LanguageModel.GetWordAt(snapshot.Text, position.Line, position.Character);
        if (string.IsNullOrWhiteSpace(word))
        {
            return null;
        }

        var symbol = model.ResolveSymbolAt(snapshot, position.Line, position.Character, word);
        if (symbol is null)
        {
            _ = LogDebugAsync($"hover none uri={uri} line={position.Line + 1} char={position.Character + 1} word='{word}'");
            return null;
        }

        _ = LogDebugAsync($"hover symbol uri={uri} line={position.Line + 1} char={position.Character + 1} word='{word}' kind={symbol.Kind} signature='{symbol.Signature}' type='{symbol.TypeName}' owner='{symbol.OwnerType}'");
        return new
        {
            contents = new
            {
                kind = "markdown",
                value = symbol.ToMarkdown()
            },
            range = ToRange(symbol.Range)
        };
    }

    private async Task<object?> HandleSignatureHelpAsync(JsonElement root)
    {
        var uri = GetDocumentUri(root);
        var position = GetPosition(root);
        await LogDebugAsync($"signature request uri={uri} line={position.Line + 1} char={position.Character + 1}");
        var text = documents.TryGetValue(uri, out var openText)
            ? openText
            : BuildModel(uri).Text;
        var invocation = LanguageModel.FindInvocation(text, position.Line, position.Character);
        if (invocation is null)
        {
            var indexerCandidates = model.ResolveIndexSymbolsAt(BuildModel(uri), position.Line, position.Character).ToArray();
            if (indexerCandidates.Length == 0)
            {
                await LogDebugAsync($"signature none uri={uri} line={position.Line + 1} char={position.Character + 1}");
                return null;
            }

            await LogDebugAsync($"signature indexer uri={uri} line={position.Line + 1} char={position.Character + 1} candidates={indexerCandidates.Length}");
            return new
            {
                signatures = indexerCandidates.Select(symbol => new
                {
                    label = symbol.Signature,
                    documentation = new { kind = "markdown", value = symbol.ToDocumentationMarkdown() },
                    parameters = symbol.Parameters.Select(parameter => new
                    {
                        label = $"{(parameter.Modifier.Length > 0 ? parameter.Modifier + " " : string.Empty)}{parameter.Name}: {parameter.Type}",
                        documentation = parameter.Documentation
                    }).ToArray()
                }).ToArray(),
                activeSignature = 0,
                activeParameter = 0
            };
        }

        var snapshot = BuildModel(uri);
        var constructorMatches = LanguageModel.DeduplicateSymbols(snapshot.Symbols
            .Where(symbol => symbol.Kind == "constructor" && symbol.OwnerType == invocation.Name)
            .ToArray()).ToArray();
        var candidates = LanguageModel.DeduplicateSymbols(model.ResolveInvocationSymbols(snapshot, position.Line, invocation)
            .Where(symbol => symbol.Kind is "method" or "function" or "constructor"))
            .ToArray();
        await LogDebugAsync(
            $"signature invocation uri={uri} line={position.Line + 1} char={position.Character + 1} receiver='{invocation.Receiver}' name='{invocation.Name}' param={invocation.ParameterIndex} constructors={constructorMatches.Length} candidates={candidates.Length}");
        if (candidates.Length > 0)
        {
            await LogDebugAsync("signature candidates " + string.Join(", ", candidates.Select(symbol => $"{symbol.Kind} {symbol.OwnerType}.{symbol.Name} {symbol.Signature} [{Path.GetFileName(UriToPath(symbol.Uri) ?? symbol.Uri)}:{symbol.Range.Start.Line + 1}]")));
        }

        if (candidates.Length == 0)
        {
            return null;
        }

        return new
        {
            signatures = candidates.Select(symbol => new
            {
                label = symbol.Signature,
                documentation = new { kind = "markdown", value = symbol.ToDocumentationMarkdown() },
                parameters = symbol.Parameters.Select(parameter => new
                {
                    label = $"{(parameter.Modifier.Length > 0 ? parameter.Modifier + " " : string.Empty)}{parameter.Name}: {parameter.Type}",
                    documentation = parameter.Documentation
                }).ToArray()
            }).ToArray(),
            activeSignature = 0,
            activeParameter = invocation.ParameterIndex
        };
    }

    private object[] HandleDocumentSymbols(JsonElement root)
    {
        var uri = GetDocumentUri(root);
        if (documents.TryGetValue(uri, out var text))
        {
            return model.BuildDocumentSymbols(uri, text).Select(ToDocumentSymbol).ToArray();
        }

        var path = UriToPath(uri);
        if (path is not null && File.Exists(path))
        {
            return model.BuildDocumentSymbols(uri, File.ReadAllText(path)).Select(ToDocumentSymbol).ToArray();
        }

        return [];
    }

    private object[] HandleDefinition(JsonElement root)
    {
        var uri = GetDocumentUri(root);
        var position = GetPosition(root);
        _ = LogDebugAsync($"definition request uri={uri} line={position.Line + 1} char={position.Character + 1}");
        var snapshot = BuildModel(uri);
        var word = LanguageModel.GetWordAt(snapshot.Text, position.Line, position.Character);
        if (string.IsNullOrWhiteSpace(word))
        {
            return [];
        }

        return model.ResolveSymbolsAt(snapshot, position.Line, position.Character, word)
            .Select(symbol => new
            {
                uri = symbol.Uri,
                range = ToRange(symbol.Range)
            })
            .ToArray();
    }

    private object[] HandleWorkspaceSymbols(JsonElement root)
    {
        var query = root.GetProperty("params").TryGetProperty("query", out var queryElement)
            ? queryElement.GetString() ?? string.Empty
            : string.Empty;
        var snapshot = BuildWorkspaceModel();
        return snapshot.Symbols
            .Where(symbol => query.Length == 0 || symbol.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(500)
            .Select(symbol => new
            {
                name = symbol.Name,
                kind = ToSymbolKind(symbol.Kind),
                location = new { uri = symbol.Uri, range = ToRange(symbol.Range) }
            })
            .ToArray();
    }

    private object[] HandleCodeAction(JsonElement root)
    {
        var uri = GetDocumentUri(root);
        var position = GetRangeStart(root);
        if (!HasCodeActionDiagnostics(root))
        {
            _ = LogDebugAsync($"codeAction skipped uri={uri} line={position.Line + 1} char={position.Character + 1} reason=no-diagnostics");
            return [];
        }

        _ = LogDebugAsync($"codeAction request uri={uri} line={position.Line + 1} char={position.Character + 1}");
        var snapshot = BuildModel(uri);
        var word = LanguageModel.GetWordAt(snapshot.Text, position.Line, position.Character);
        if (string.IsNullOrWhiteSpace(word))
        {
            return [];
        }

        return model.FindMissingUsesActions(snapshot, word)
            .Select(namespaceName =>
            {
                var edit = model.GetUsesInsertEdit(snapshot.Text, namespaceName);
                return new
                {
                    title = $"Add uses {namespaceName}",
                    kind = "quickfix",
                    edit = new
                    {
                        changes = new Dictionary<string, object[]>
                        {
                            [uri] =
                            [
                                new
                                {
                                    range = ToRange(edit.Range),
                                    newText = edit.NewText
                                }
                            ]
                        }
                    }
                };
            })
            .ToArray();
    }

    private static bool HasCodeActionDiagnostics(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        if (!parameters.TryGetProperty("context", out var context) ||
            !context.TryGetProperty("diagnostics", out var diagnostics) ||
            diagnostics.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return diagnostics.GetArrayLength() > 0;
    }

    private async Task PublishDiagnosticsAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!documents.TryGetValue(uri, out var text))
        {
            return;
        }

        var snapshot = model.BuildDocument(uri, text);
        cancellationToken.ThrowIfCancellationRequested();
        var syntaxDiagnostics = snapshot.Diagnostics.Select(diagnostic => new
        {
            range = ToRange(snapshot.ToRange(diagnostic.Span)),
            severity = diagnostic.Severity == DiagnosticSeverity.Error ? 1 : diagnostic.Severity == DiagnosticSeverity.Warning ? 2 : 3,
            code = diagnostic.Id,
            source = "ilc",
            message = diagnostic.Message
        });
        var rawBindingDiagnostics = model.GetBindingDiagnostics(uri, snapshot.Text, documents);
        cancellationToken.ThrowIfCancellationRequested();
        var bindingDiagnostics = rawBindingDiagnostics
            .Where(diagnostic => !snapshot.Diagnostics.Any(existing => existing.Id == diagnostic.Id && existing.Span == diagnostic.Span && existing.Message == diagnostic.Message))
            .Where(diagnostic => !LanguageModel.IsSuppressedLanguageServerDiagnostic(snapshot.Text, diagnostic))
            .Select(diagnostic => new
            {
                range = ToRange(snapshot.ToRange(diagnostic.Span)),
                severity = diagnostic.Severity == DiagnosticSeverity.Error ? 1 : diagnostic.Severity == DiagnosticSeverity.Warning ? 2 : 3,
                code = diagnostic.Id,
                source = "ilc",
                message = diagnostic.Message
            });
        var missingUsesDiagnostics = model.FindMissingUsesDiagnostics(snapshot).Select(diagnostic => new
        {
            range = ToRange(diagnostic.Range),
            severity = 2,
            code = "ILC1001",
            source = "ilc",
            message = $"'{diagnostic.TypeName}' is available in namespace '{diagnostic.NamespaceName}'. Add a uses clause."
        });
        var diagnostics = syntaxDiagnostics.Concat(bindingDiagnostics).Concat(missingUsesDiagnostics).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        await LogDebugAsync($"diagnostics uri={uri} syntax={snapshot.Diagnostics.Count} binding={rawBindingDiagnostics.Count} total={diagnostics.Length}");

        await NotifyAsync("textDocument/publishDiagnostics", new { uri, diagnostics });
    }

    private ModelSnapshot BuildModel(string uri)
    {
        var revision = Volatile.Read(ref modelRevision);
        if (documents.TryGetValue(uri, out var text))
        {
            if (modelSnapshots.TryGetValue(uri, out var cached) &&
                cached.Revision == revision &&
                string.Equals(cached.Text, text, StringComparison.Ordinal))
            {
                _ = LogDebugAsync($"model cache hit uri={uri} revision={revision}");
                return cached.Snapshot;
            }

            var visibleNamespaces = model.CollectVisibleNamespaces(text);
            var workspace = BuildImportedWorkspaceModel(uri, visibleNamespaces);
            var snapshot = model.BuildDocumentWithWorkspace(uri, text, workspace);
            modelSnapshots[uri] = new CachedModelSnapshot(revision, text, snapshot);
            var parameterCount = snapshot.Symbols.Count(symbol => symbol.Kind == "parameter" && symbol.Uri == uri);
            var localCount = snapshot.Symbols.Count(symbol => symbol.Kind == "local" && symbol.Uri == uri);
            var inferredLocalCount = snapshot.Symbols.Count(symbol => symbol.Kind == "local" && symbol.Uri == uri && symbol.TypeName == "inferred");
            _ = LogDebugAsync($"model cache miss uri={uri} revision={revision} symbols={snapshot.Symbols.Count} parameters={parameterCount} locals={localCount} inferredLocals={inferredLocalCount}");
            if (inferredLocalCount > 0)
            {
                var inferredNames = snapshot.Symbols
                    .Where(symbol => symbol.Kind == "local" && symbol.Uri == uri && symbol.TypeName == "inferred")
                    .OrderBy(symbol => symbol.Range.Start.Line)
                    .ThenBy(symbol => symbol.Range.Start.Character)
                    .Take(40)
                    .Select(symbol => $"{symbol.Name}@{symbol.Range.Start.Line + 1}:{symbol.Range.Start.Character + 1}");
                _ = LogDebugAsync($"model inferred locals uri={uri} {string.Join(", ", inferredNames)}");
            }
            return snapshot;
        }

        return BuildWorkspaceModel();
    }

    private ModelSnapshot BuildWorkspaceModel()
    {
        var revision = Volatile.Read(ref modelRevision);
        if (modelSnapshots.TryGetValue(WorkspaceCacheKey, out var cached) && cached.Revision == revision)
        {
            _ = LogDebugAsync($"model cache hit uri={WorkspaceCacheKey} revision={revision}");
            return cached.Snapshot;
        }

        var snapshot = model.Build(workspaceRoot, documents);
        modelSnapshots[WorkspaceCacheKey] = new CachedModelSnapshot(revision, string.Empty, snapshot);
        _ = LogDebugAsync($"model cache miss uri={WorkspaceCacheKey} revision={revision} symbols={snapshot.Symbols.Count}");
        return snapshot;
    }

    private ModelSnapshot BuildImportedWorkspaceModel(string primaryUri, IReadOnlySet<string> visibleNamespaces)
    {
        var namespaceKey = string.Join("|", visibleNamespaces.OrderBy(namespaceName => namespaceName, StringComparer.Ordinal));
        var openDependencyKey = string.Join(
            "|",
            documentRevisions
                .Where(pair => !string.Equals(pair.Key, primaryUri, StringComparison.Ordinal))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}@{pair.Value}"));
        var cacheKey = $"{namespaceKey}::{openDependencyKey}";
        if (importedWorkspaceSnapshots.TryGetValue(cacheKey, out var cached))
        {
            _ = LogDebugAsync($"import cache hit uri={primaryUri} key={cacheKey} symbols={cached.Symbols.Count}");
            return cached;
        }

        var workspace = model.Build(workspaceRoot, documents, visibleNamespaces);
        importedWorkspaceSnapshots[cacheKey] = workspace;
        _ = LogDebugAsync($"import cache miss uri={primaryUri} key={cacheKey} symbols={workspace.Symbols.Count}");
        if (importedWorkspaceSnapshots.Count > 32)
        {
            importedWorkspaceSnapshots.Clear();
            _ = LogDebugAsync("import cache cleared reason=size-limit");
        }

        return workspace;
    }

    private const string WorkspaceCacheKey = "<workspace>";

    private static string GetDocumentUri(JsonElement root)
    {
        var parameters = root.GetProperty("params");
        if (parameters.TryGetProperty("textDocument", out var textDocument))
        {
            return textDocument.GetProperty("uri").GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static LspPosition GetPosition(JsonElement root)
    {
        var position = root.GetProperty("params").GetProperty("position");
        return new LspPosition(position.GetProperty("line").GetInt32(), position.GetProperty("character").GetInt32());
    }

    private static LspPosition GetRangeStart(JsonElement root)
    {
        var start = root.GetProperty("params").GetProperty("range").GetProperty("start");
        return new LspPosition(start.GetProperty("line").GetInt32(), start.GetProperty("character").GetInt32());
    }

    private async Task RespondAsync(JsonElement id, object? result)
    {
        await WriteJsonAsync(new { jsonrpc = "2.0", id, result });
    }

    private async Task NotifyAsync(string method, object parameters)
    {
        await WriteJsonAsync(new { jsonrpc = "2.0", method, @params = parameters });
    }

    private async Task LogAsync(string message)
    {
        await NotifyAsync("window/logMessage", new { type = 4, message = $"[ilc-lsp] {message}" });
    }

    private async Task LogDebugAsync(string message)
    {
        if (debugEnabled)
        {
            await LogAsync(message);
        }
    }

    private async Task WriteJsonAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n");
        await writeLock.WaitAsync();
        try
        {
            await output.WriteAsync(header);
            await output.WriteAsync(bytes);
            await output.FlushAsync();
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static object ToRange(LspRange range) => new
    {
        start = new { line = range.Start.Line, character = range.Start.Character },
        end = new { line = range.End.Line, character = range.End.Character }
    };

    private static int ToSymbolKind(string kind) => kind switch
    {
        "class" => 5,
        "interface" => 11,
        "enum" => 10,
        "method" => 6,
        "function" => 12,
        "constructor" => 9,
        "property" => 7,
        "field" => 8,
        "parameter" => 13,
        "local" => 13,
        "constant" => 14,
        "enumMember" => 22,
        _ => 13
    };

    private static int ToCompletionKind(string kind) => kind switch
    {
        "class" or "interface" or "enum" => 7,
        "method" => 2,
        "function" or "constructor" => 3,
        "property" => 10,
        "field" => 5,
        "parameter" => 6,
        "local" => 6,
        "constant" => 21,
        "enumMember" => 20,
        _ => 1
    };

    private static object ToDocumentSymbol(LanguageDocumentSymbol symbol) => new
    {
        name = symbol.Name,
        detail = symbol.Signature,
        kind = ToSymbolKind(symbol.Kind),
        range = ToRange(symbol.Range),
        selectionRange = ToRange(symbol.SelectionRange),
        children = symbol.Children.Select(ToDocumentSymbol).ToArray()
    };

    private static string? UriToPath(string uri)
    {
        try
        {
            return new Uri(uri).LocalPath;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class LanguageModel(string workspaceRoot)
{
    public IReadOnlyList<string> Keywords { get; } =
    [
        "namespace", "uses", "public", "private", "protected", "internal", "static", "extern",
        "class", "record", "interface", "enum", "delegate", "constructor", "method", "function",
        "procedure", "property", "var", "const", "begin", "end", "if", "then", "else", "while",
        "do", "for", "foreach", "in", "repeat", "until", "case", "of", "match", "with", "try",
        "except", "finally", "raise", "return", "new", "nil", "true", "false", "out", "ref"
    ];

    public IReadOnlyList<string> BuiltInTypes { get; } = TypeSymbol.BuiltInTypes.Select(type => type.Name).ToArray();

    public ModelSnapshot BuildDocument(string uri, string text)
    {
        var tree = SyntaxTree.Parse(text);
        var typeBases = CollectTypeBases(tree.Root);
        var typeParameters = CollectTypeParameters(tree.Root);
        var declaredSymbols = CollectSymbols(uri, text, tree.Root).ToArray();
        var declaredSnapshot = new ModelSnapshot(uri, text, tree.Diagnostics, declaredSymbols, typeBases, typeParameters);
        var symbols = declaredSymbols.Concat(CollectLocalSymbols(uri, text, declaredSnapshot)).ToArray();
        return new ModelSnapshot(uri, text, tree.Diagnostics, symbols, typeBases, typeParameters);
    }

    public IReadOnlyList<LanguageDocumentSymbol> BuildDocumentSymbols(string uri, string text)
    {
        var tree = SyntaxTree.Parse(text);
        return CollectDocumentSymbols(uri, text, tree.Root).ToArray();
    }

    public ModelSnapshot BuildDocumentWithWorkspace(string uri, string text, IReadOnlyDictionary<string, string> openDocuments)
    {
        var primaryTree = SyntaxTree.Parse(text);
        var importedNamespaces = CollectVisibleNamespaces(primaryTree.Root);
        var workspace = Build(workspaceRoot, openDocuments, importedNamespaces);
        return BuildDocumentWithWorkspace(uri, text, workspace);
    }

    public ModelSnapshot BuildDocumentWithWorkspace(string uri, string text, ModelSnapshot workspace)
    {
        var tree = SyntaxTree.Parse(text);
        var primarySymbols = CollectSymbols(uri, text, tree.Root).ToArray();
        var workspaceSymbols = workspace.Symbols.Where(symbol => symbol.Uri != uri).ToArray();
        var typeBases = CollectTypeBases(tree.Root)
            .Concat(workspace.TypeBases)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.First().Value, StringComparer.Ordinal);
        var typeParameters = CollectTypeParameters(tree.Root)
            .Concat(workspace.TypeParameters)
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.First().Value, StringComparer.Ordinal);
        var declaredWithWorkspace = new ModelSnapshot(
            uri,
            text,
            tree.Diagnostics,
            primarySymbols.Concat(workspaceSymbols).ToArray(),
            typeBases,
            typeParameters);
        var localSymbols = CollectLocalSymbols(uri, text, declaredWithWorkspace).ToArray();

        return new ModelSnapshot(
            uri,
            text,
            tree.Diagnostics,
            primarySymbols.Concat(localSymbols).Concat(workspaceSymbols).ToArray(),
            typeBases,
            typeParameters);
    }

    public IReadOnlySet<string> CollectVisibleNamespaces(string text)
    {
        var tree = SyntaxTree.Parse(text);
        return CollectVisibleNamespaces(tree.Root);
    }

    public ModelSnapshot Build(string root, IReadOnlyDictionary<string, string> openDocuments) =>
        Build(root, openDocuments, null);

    public ModelSnapshot Build(string root, IReadOnlyDictionary<string, string> openDocuments, IReadOnlySet<string>? visibleNamespaces)
    {
        var symbols = new List<LanguageSymbol>();
        var typeBases = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeParameters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var file in EnumerateIlcFiles(root))
        {
            var uri = PathToUri(file);
            var text = openDocuments.TryGetValue(uri, out var openText) ? openText : File.ReadAllText(file);
            var tree = SyntaxTree.Parse(text);
            if (IsVisible(tree.Root, visibleNamespaces))
            {
                symbols.AddRange(CollectSymbols(uri, text, tree.Root));
                foreach (var pair in CollectTypeBases(tree.Root))
                {
                    typeBases[pair.Key] = pair.Value;
                }

                foreach (var pair in CollectTypeParameters(tree.Root))
                {
                    typeParameters[pair.Key] = pair.Value;
                }
            }
        }

        foreach (var (uri, text) in openDocuments)
        {
            if (UriToPath(uri) is { } path && File.Exists(path))
            {
                continue;
            }

            var tree = SyntaxTree.Parse(text);
            if (IsVisible(tree.Root, visibleNamespaces))
            {
                symbols.AddRange(CollectSymbols(uri, text, tree.Root));
                foreach (var pair in CollectTypeBases(tree.Root))
                {
                    typeBases[pair.Key] = pair.Value;
                }

                foreach (var pair in CollectTypeParameters(tree.Root))
                {
                    typeParameters[pair.Key] = pair.Value;
                }
            }
        }

        return new ModelSnapshot(string.Empty, string.Empty, [], symbols, typeBases, typeParameters);
    }

    public IReadOnlyList<Diagnostic> GetBindingDiagnostics(string uri, string text, IReadOnlyDictionary<string, string> openDocuments)
    {
        var primaryTree = SyntaxTree.Parse(text);
        var importedNamespaces = CollectImportedNamespaces(text).ToHashSet(StringComparer.Ordinal);
        var importedTrees = new List<SyntaxTree>();
        var seenUris = new HashSet<string>(StringComparer.Ordinal) { uri };
        var workspaceDocuments = new List<(string Uri, string Text, SyntaxTree Tree, string? NamespaceName)>();
        foreach (var file in EnumerateIlcFiles(workspaceRoot))
        {
            var fileUri = PathToUri(file);
            if (string.Equals(fileUri, uri, StringComparison.Ordinal))
            {
                continue;
            }

            var importedText = openDocuments.TryGetValue(fileUri, out var openText) ? openText : File.ReadAllText(file);
            var importedTree = SyntaxTree.Parse(importedText);
            workspaceDocuments.Add((fileUri, importedText, importedTree, importedTree.Root.Namespace?.Name.ToDisplayString()));
        }

        foreach (var (openUri, openText) in openDocuments)
        {
            if (string.Equals(openUri, uri, StringComparison.Ordinal) ||
                workspaceDocuments.Any(document => string.Equals(document.Uri, openUri, StringComparison.Ordinal)))
            {
                continue;
            }

            var importedTree = SyntaxTree.Parse(openText);
            workspaceDocuments.Add((openUri, openText, importedTree, importedTree.Root.Namespace?.Name.ToDisplayString()));
        }

        var addedImport = true;
        while (addedImport)
        {
            addedImport = false;
            foreach (var document in workspaceDocuments)
            {
                if (document.NamespaceName is null ||
                    !importedNamespaces.Contains(document.NamespaceName) ||
                    !seenUris.Add(document.Uri))
                {
                    continue;
                }

                importedTrees.Add(document.Tree);
                foreach (var namespaceName in CollectImportedNamespaces(document.Text))
                {
                    importedNamespaces.Add(namespaceName);
                }

                addedImport = true;
            }
        }

        var mergedTree = SyntaxTree.Merge(primaryTree, importedTrees);
        var binding = new Binder().Bind(mergedTree);
        return binding.Diagnostics
            .Where(diagnostic => IsPrimaryDocumentDiagnostic(text, diagnostic))
            .ToArray();
    }

    private IEnumerable<string> EnumerateIlcFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(root, "*.ilc", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}tmp{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            yield return file;
        }
    }

    private IEnumerable<LanguageSymbol> CollectSymbols(string uri, string text, CompilationUnitSyntax root)
    {
        foreach (var member in root.Members)
        {
            foreach (var symbol in CollectMemberSymbols(uri, text, member))
            {
                yield return symbol;
            }
        }
    }

    private static IReadOnlySet<string> CollectVisibleNamespaces(CompilationUnitSyntax root)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        if (root.Namespace is not null)
        {
            namespaces.Add(root.Namespace.Name.ToDisplayString());
        }

        if (root.Uses is not null)
        {
            foreach (var import in root.Uses.Imports)
            {
                namespaces.Add(import.NamespaceName.ToDisplayString());
            }
        }

        return namespaces;
    }

    private static IReadOnlySet<string> CollectImportedNamespaces(string text)
    {
        var tree = SyntaxTree.Parse(text);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        if (tree.Root.Namespace is not null)
        {
            namespaces.Add(tree.Root.Namespace.Name.ToDisplayString());
        }

        if (tree.Root.Uses is not null)
        {
            foreach (var import in tree.Root.Uses.Imports)
            {
                namespaces.Add(import.NamespaceName.ToDisplayString());
            }
        }

        return namespaces;
    }

    private static string GetNamespaceForSymbol(LanguageSymbol symbol)
    {
        if (UriToPath(symbol.Uri) is not { } path || !File.Exists(path))
        {
            return string.Empty;
        }

        var tree = SyntaxTree.Parse(File.ReadAllText(path));
        return tree.Root.Namespace?.Name.ToDisplayString() ?? string.Empty;
    }

    private static bool IsVisible(CompilationUnitSyntax root, IReadOnlySet<string>? visibleNamespaces)
    {
        if (visibleNamespaces is null)
        {
            return true;
        }

        var namespaceName = root.Namespace?.Name.ToDisplayString();
        return namespaceName is not null && visibleNamespaces.Contains(namespaceName);
    }

    private static bool IsPrimaryDocumentDiagnostic(string text, Diagnostic diagnostic)
    {
        if (diagnostic.Span.Start < 0 || diagnostic.Span.Start > text.Length)
        {
            return false;
        }

        if (diagnostic.Span.Length <= 0)
        {
            return true;
        }

        return diagnostic.Span.End <= text.Length;
    }

    public static bool IsSuppressedLanguageServerDiagnostic(string text, Diagnostic diagnostic)
    {
        if (diagnostic.Id == "ILC2102" &&
            diagnostic.Message.Contains("'self'", StringComparison.Ordinal) &&
            diagnostic.Span.Start >= 0 &&
            diagnostic.Span.End <= text.Length &&
            string.Equals(text.Substring(diagnostic.Span.Start, diagnostic.Span.Length), "self", StringComparison.Ordinal))
        {
            return true;
        }

        if (diagnostic.Id == "ILC2111" &&
            diagnostic.Message.Contains("'self.", StringComparison.Ordinal) &&
            diagnostic.Message.Contains("cannot be accessed through self", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> CollectTypeBases(CompilationUnitSyntax root)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax declaration when declaration.BaseType is not null:
                    result[declaration.Identifier.Text] = declaration.BaseType.ToDisplayString();
                    break;
                case ClassDeclarationSyntax declaration:
                    result[declaration.Identifier.Text] = string.Empty;
                    break;
                case InterfaceDeclarationSyntax declaration when declaration.BaseInterfaces.Count > 0:
                    result[declaration.Identifier.Text] = declaration.BaseInterfaces[0].ToDisplayString();
                    break;
            }
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CollectTypeParameters(CompilationUnitSyntax root)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var member in root.Members)
        {
            switch (member)
            {
                case ClassDeclarationSyntax declaration:
                    result[declaration.Identifier.Text] = declaration.TypeParameters?.GetParameterNames() ?? [];
                    break;
                case InterfaceDeclarationSyntax declaration:
                    result[declaration.Identifier.Text] = declaration.TypeParameters?.GetParameterNames() ?? [];
                    break;
            }
        }

        return result;
    }

    private IEnumerable<LanguageSymbol> CollectLocalSymbols(string uri, string text, ModelSnapshot declaredSnapshot)
    {
        var tree = SyntaxTree.Parse(text);
        var semanticSymbols = BuildSemanticSymbols(declaredSnapshot);
        foreach (var (method, ownerType) in EnumerateMethods(tree.Root))
        {
            var methodSymbols = new List<LanguageSymbol>();
            var currentMethod = CreateMethodSymbol(method, ownerType);
            foreach (var parameter in method.Parameters)
            {
                var modifier = ParameterModifier(parameter);
                var name = parameter.Identifier.Text;
                var typeName = NormalizeLocalTypeName(parameter.TypeName.ToDisplayString());
                var symbol = CreateSymbol(uri, text, name, "parameter", parameter.Identifier.Span, $"{modifier}{name}: {typeName}", typeName: typeName);
                methodSymbols.Add(symbol);
                yield return symbol;
            }

            foreach (var declaration in EnumerateLocalDeclarations(method))
            {
                var line = ToPosition(text, declaration.Identifier.Span.Start).Line;
                var localSnapshot = declaredSnapshot with { Symbols = declaredSnapshot.Symbols.Concat(methodSymbols).ToArray() };
                var inferredType = declaration.IsForeachElement
                    ? InferForeachElementType(localSnapshot, semanticSymbols, declaration.Initializer, line, currentMethod)
                    : InferExpressionType(localSnapshot, semanticSymbols, declaration.Initializer, line, currentMethod);
                var typeName = declaration.ExplicitType.Length > 0 ? declaration.ExplicitType : inferredType.Length > 0 ? inferredType : "inferred";
                var symbol = CreateSymbol(uri, text, declaration.Identifier.Text, "local", declaration.Identifier.Span, $"{declaration.Identifier.Text}: {typeName}", typeName: typeName);
                methodSymbols.Add(symbol);
                yield return symbol;

                if (declaration.Initializer is QueryExpressionSyntax query)
                {
                    foreach (var querySymbol in CollectQueryLocalSymbols(uri, text, localSnapshot, semanticSymbols, query, line, currentMethod))
                    {
                        yield return querySymbol;
                    }
                }
            }
        }
    }

    private static IEnumerable<(MethodDeclarationSyntax Method, string OwnerType)> EnumerateMethods(CompilationUnitSyntax root)
    {
        foreach (var member in root.Members)
        {
            if (member is not ClassDeclarationSyntax and not InterfaceDeclarationSyntax)
            {
                continue;
            }

            var ownerType = member switch
            {
                ClassDeclarationSyntax declaration => declaration.Identifier.Text,
                InterfaceDeclarationSyntax declaration => declaration.Identifier.Text,
                _ => string.Empty
            };

            var members = member switch
            {
                ClassDeclarationSyntax declaration => declaration.Members,
                InterfaceDeclarationSyntax declaration => declaration.Members,
                _ => []
            };

            foreach (var method in members.OfType<MethodDeclarationSyntax>())
            {
                yield return (method, ownerType);
            }
        }
    }

    private static IEnumerable<LocalDeclarationInfo> EnumerateLocalDeclarations(MethodDeclarationSyntax method)
    {
        if (method.Body is null)
        {
            yield break;
        }

        foreach (var declaration in EnumerateLocalDeclarations(method.Body))
        {
            yield return declaration;
        }
    }

    private static IEnumerable<LocalDeclarationInfo> EnumerateLocalDeclarations(StatementSyntax statement)
    {
        switch (statement)
        {
            case LocalVariableDeclarationStatementSyntax local:
                foreach (var declarator in local.Declarators)
                {
                    yield return new LocalDeclarationInfo(
                        declarator.Identifier,
                        declarator.TypeName is null ? string.Empty : NormalizeLocalTypeName(declarator.TypeName.ToDisplayString()),
                        declarator.Initializer,
                        false);
                }

                break;
            case BlockStatementSyntax block:
                foreach (var nested in block.Statements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                break;
            case IfStatementSyntax ifStatement:
                foreach (var nested in EnumerateLocalDeclarations(ifStatement.ThenStatement))
                {
                    yield return nested;
                }

                if (ifStatement.ElseStatement is not null)
                {
                    foreach (var nested in EnumerateLocalDeclarations(ifStatement.ElseStatement))
                    {
                        yield return nested;
                    }
                }

                break;
            case WhileStatementSyntax whileStatement:
                foreach (var nested in EnumerateLocalDeclarations(whileStatement.Body))
                {
                    yield return nested;
                }

                break;
            case RepeatStatementSyntax repeat:
                foreach (var nested in repeat.Statements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                break;
            case ForStatementSyntax forStatement:
                if (forStatement.VarKeyword is not null)
                {
                    yield return new LocalDeclarationInfo(forStatement.Identifier, TypeSymbol.Integer.Name, null, false);
                }

                foreach (var nested in EnumerateLocalDeclarations(forStatement.Body))
                {
                    yield return nested;
                }

                break;
            case ForeachStatementSyntax foreachStatement:
                if (foreachStatement.VarKeyword is not null)
                {
                    yield return new LocalDeclarationInfo(foreachStatement.Identifier, string.Empty, foreachStatement.Collection, true);
                }

                foreach (var nested in EnumerateLocalDeclarations(foreachStatement.Body))
                {
                    yield return nested;
                }

                break;
            case WithStatementSyntax withStatement:
                foreach (var nested in EnumerateLocalDeclarations(withStatement.Body))
                {
                    yield return nested;
                }

                break;
            case CaseStatementSyntax caseStatement:
                foreach (var nested in caseStatement.Clauses.SelectMany(clause => EnumerateLocalDeclarations(clause.Body)))
                {
                    yield return nested;
                }

                foreach (var nested in caseStatement.ElseStatements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                break;
            case MatchStatementSyntax matchStatement:
                foreach (var nested in matchStatement.Arms.SelectMany(arm => EnumerateLocalDeclarations(arm.Body)))
                {
                    yield return nested;
                }

                foreach (var nested in matchStatement.ElseStatements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                break;
            case TryStatementSyntax tryStatement:
                foreach (var nested in tryStatement.TryStatements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                foreach (var nested in tryStatement.ExceptionClauses.SelectMany(clause => EnumerateLocalDeclarations(clause.Body)))
                {
                    yield return nested;
                }

                foreach (var nested in tryStatement.ExceptStatements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                foreach (var nested in tryStatement.FinallyStatements.SelectMany(EnumerateLocalDeclarations))
                {
                    yield return nested;
                }

                break;
        }
    }

    private IEnumerable<LanguageDocumentSymbol> CollectDocumentSymbols(string uri, string text, CompilationUnitSyntax root)
    {
        foreach (var member in root.Members)
        {
            var symbol = CreateDocumentSymbol(uri, text, member);
            if (symbol is not null)
            {
                yield return symbol;
            }
        }
    }

    private LanguageDocumentSymbol? CreateDocumentSymbol(string uri, string text, MemberSyntax member)
    {
        switch (member)
        {
            case ClassDeclarationSyntax declaration:
                return CreateDocumentSymbol(
                    uri,
                    text,
                    declaration.Identifier.Text,
                    "class",
                    SpanFrom(declaration.ClassKeyword.Span, declaration.SemicolonToken.Span),
                    declaration.Identifier.Span,
                    $"class {declaration.Identifier.Text}",
                    declaration.Members.Select(member => CreateDocumentSymbol(uri, text, member)).Where(symbol => symbol is not null).Cast<LanguageDocumentSymbol>().ToArray());
            case InterfaceDeclarationSyntax declaration:
                return CreateDocumentSymbol(
                    uri,
                    text,
                    declaration.Identifier.Text,
                    "interface",
                    SpanFrom(declaration.InterfaceKeyword.Span, declaration.SemicolonToken.Span),
                    declaration.Identifier.Span,
                    $"interface {declaration.Identifier.Text}",
                    declaration.Members.Select(member => CreateDocumentSymbol(uri, text, member)).Where(symbol => symbol is not null).Cast<LanguageDocumentSymbol>().ToArray());
            case EnumDeclarationSyntax declaration:
                return CreateDocumentSymbol(
                    uri,
                    text,
                    declaration.Identifier.Text,
                    "enum",
                    SpanFrom(declaration.EnumKeyword.Span, declaration.SemicolonToken.Span),
                    declaration.Identifier.Span,
                    $"enum {declaration.Identifier.Text}",
                    declaration.Members.Select(member => CreateDocumentSymbol(uri, text, member)).ToArray());
            case DelegateDeclarationSyntax declaration:
                return CreateDocumentSymbol(uri, text, declaration.Identifier.Text, "function", declaration.Identifier.Span, declaration.Identifier.Span, FormatDelegate(declaration), []);
            case TopLevelVariableDeclarationSyntax declaration when declaration.Declarators.Count > 0:
                return CreateDocumentSymbol(uri, text, declaration.Declarators[0].Identifier.Text, "field", declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Text, []);
            case TopLevelConstantDeclarationSyntax declaration when declaration.Declarators.Count > 0:
                return CreateDocumentSymbol(uri, text, declaration.Declarators[0].Identifier.Text, "constant", declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Text, []);
            default:
                return null;
        }
    }

    private LanguageDocumentSymbol? CreateDocumentSymbol(string uri, string text, TypeMemberSyntax member)
    {
        switch (member)
        {
            case MethodDeclarationSyntax declaration:
                var methodKind = declaration.Keyword.Kind == SyntaxKind.ConstructorKeyword
                    ? "constructor"
                    : declaration.Keyword.Kind == SyntaxKind.FunctionKeyword
                        ? "function"
                        : "method";
                return CreateDocumentSymbol(uri, text, declaration.Identifier.Text, methodKind, SpanFrom(declaration.Keyword.Span, declaration.TerminatorToken.Span), declaration.Identifier.Span, FormatMethod(declaration), []);
            case PropertyDeclarationSyntax declaration:
                return CreateDocumentSymbol(uri, text, declaration.Identifier.Text, "property", declaration.Identifier.Span, declaration.Identifier.Span, FormatProperty(declaration), []);
            case FieldDeclarationSyntax declaration when declaration.Declarators.Count > 0:
                return CreateDocumentSymbol(uri, text, declaration.Declarators[0].Identifier.Text, "field", declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Text, []);
            case ConstantDeclarationSyntax declaration when declaration.Declarators.Count > 0:
                return CreateDocumentSymbol(uri, text, declaration.Declarators[0].Identifier.Text, "constant", declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Span, declaration.Declarators[0].Identifier.Text, []);
            default:
                return null;
        }
    }

    private LanguageDocumentSymbol CreateDocumentSymbol(
        string uri,
        string text,
        string name,
        string kind,
        TextSpan rangeSpan,
        TextSpan selectionSpan,
        string signature,
        IReadOnlyList<LanguageDocumentSymbol> children) =>
        new(uri, name, kind, signature, ToRange(text, rangeSpan), ToRange(text, selectionSpan), children);

    private LanguageDocumentSymbol CreateDocumentSymbol(string uri, string text, EnumMemberSyntax member) =>
        CreateDocumentSymbol(uri, text, member.Identifier.Text, "enumMember", member.Identifier.Span, member.Identifier.Span, member.Identifier.Text, []);

    private IEnumerable<LanguageSymbol> CollectMemberSymbols(string uri, string text, MemberSyntax member)
    {
        switch (member)
        {
            case ClassDeclarationSyntax declaration:
                yield return CreateSymbol(uri, text, declaration.Identifier.Text, "class", declaration.Identifier.Span, $"class {declaration.Identifier.Text}");
                foreach (var child in declaration.Members.SelectMany(child => CollectTypeMemberSymbols(uri, text, child, declaration.Identifier.Text)))
                {
                    yield return child;
                }
                break;
            case InterfaceDeclarationSyntax declaration:
                yield return CreateSymbol(uri, text, declaration.Identifier.Text, "interface", declaration.Identifier.Span, $"interface {declaration.Identifier.Text}");
                foreach (var child in declaration.Members.SelectMany(child => CollectTypeMemberSymbols(uri, text, child, declaration.Identifier.Text)))
                {
                    yield return child;
                }
                break;
            case EnumDeclarationSyntax declaration:
                yield return CreateSymbol(uri, text, declaration.Identifier.Text, "enum", declaration.Identifier.Span, $"enum {declaration.Identifier.Text}");
                foreach (var enumMember in declaration.Members)
                {
                    yield return CreateSymbol(uri, text, enumMember.Identifier.Text, "enumMember", enumMember.Identifier.Span, $"{declaration.Identifier.Text}.{enumMember.Identifier.Text}", ownerType: declaration.Identifier.Text);
                }
                break;
            case DelegateDeclarationSyntax declaration:
                yield return CreateSymbol(uri, text, declaration.Identifier.Text, "function", declaration.Identifier.Span, FormatDelegate(declaration));
                break;
            case TopLevelVariableDeclarationSyntax declaration:
                foreach (var declarator in declaration.Declarators)
                {
                    yield return CreateSymbol(uri, text, declarator.Identifier.Text, "field", declarator.Identifier.Span, declarator.Identifier.Text);
                }
                break;
            case TopLevelConstantDeclarationSyntax declaration:
                foreach (var declarator in declaration.Declarators)
                {
                    yield return CreateSymbol(uri, text, declarator.Identifier.Text, "constant", declarator.Identifier.Span, declarator.Identifier.Text);
                }
                break;
        }
    }

    private IEnumerable<LanguageSymbol> CollectTypeMemberSymbols(string uri, string text, TypeMemberSyntax member, string ownerType)
    {
        switch (member)
        {
            case MethodDeclarationSyntax declaration:
                var methodKind = declaration.Keyword.Kind == SyntaxKind.ConstructorKeyword
                    ? "constructor"
                    : declaration.Keyword.Kind == SyntaxKind.FunctionKeyword
                        ? "function"
                        : "method";
                yield return CreateSymbol(
                    uri,
                    text,
                    declaration.Identifier.Text,
                    methodKind,
                    methodKind == "constructor" ? declaration.Keyword.Span : declaration.Identifier.Span,
                    FormatMethod(declaration),
                    declaration.Parameters.Select(FormatParameter).ToArray(),
                    ownerType: ownerType,
                    typeName: declaration.ReturnType?.ToDisplayString() ?? string.Empty);
                break;
            case PropertyDeclarationSyntax declaration:
                yield return CreateSymbol(
                    uri,
                    text,
                    declaration.Identifier.Text,
                    "property",
                    declaration.Identifier.Span,
                    FormatProperty(declaration),
                    declaration.IndexParameter is null ? [] : [FormatParameter(declaration.IndexParameter)],
                    ownerType: ownerType,
                    typeName: declaration.TypeName.ToDisplayString());
                break;
            case FieldDeclarationSyntax declaration:
                foreach (var declarator in declaration.Declarators)
                {
                    yield return CreateSymbol(uri, text, declarator.Identifier.Text, "field", declarator.Identifier.Span, declarator.TypeName?.ToDisplayString() is { } fieldType ? $"{declarator.Identifier.Text}: {fieldType}" : declarator.Identifier.Text, ownerType: ownerType, typeName: declarator.TypeName?.ToDisplayString() ?? string.Empty);
                }
                break;
            case ConstantDeclarationSyntax declaration:
                foreach (var declarator in declaration.Declarators)
                {
                    yield return CreateSymbol(uri, text, declarator.Identifier.Text, "constant", declarator.Identifier.Span, declarator.Identifier.Text);
                }
                break;
        }
    }

    private LanguageSymbol CreateSymbol(
        string uri,
        string text,
        string name,
        string kind,
        TextSpan span,
        string signature,
        IReadOnlyList<LanguageParameter>? parameters = null,
        string ownerType = "",
        string typeName = "")
    {
        var range = ToRange(text, span);
        var docs = DocumentationReader.FindDocumentationBefore(text, span.Start);
        var resolvedParameters = (parameters ?? [])
            .Select(parameter => parameter with { Documentation = docs.Params.GetValueOrDefault(parameter.Name, parameter.Documentation) })
            .ToArray();
        return new LanguageSymbol(uri, name, kind, signature, docs.Summary, docs.Returns, resolvedParameters, range, ownerType, typeName);
    }

    private static LanguageSymbol CreateSyntheticSymbol(
        ModelSnapshot snapshot,
        string name,
        string kind,
        string signature,
        IReadOnlyList<LanguageParameter>? parameters = null,
        string documentation = "",
        string typeName = "") =>
        new(
            snapshot.Uri,
            name,
            kind,
            signature,
            documentation,
            string.Empty,
            parameters ?? [],
            ToRange(snapshot.Text, new TextSpan(0, Math.Min(1, snapshot.Text.Length))),
            string.Empty,
            typeName);

    public static LspRange ToRange(string text, TextSpan span)
    {
        var start = ToPosition(text, span.Start);
        var end = ToPosition(text, span.End);
        return new LspRange(start, end);
    }

    private static LspPosition ToPosition(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        var limit = Math.Clamp(offset, 0, text.Length);
        for (var index = 0; index < limit; index++)
        {
            if (text[index] == '\n')
            {
                line++;
                lineStart = index + 1;
            }
        }

        return new LspPosition(line, limit - lineStart);
    }

    public static string GetWordAt(string text, int line, int character)
    {
        var offset = OffsetAt(text, line, character);
        if (offset < 0 || offset > text.Length)
        {
            return string.Empty;
        }

        var start = offset;
        while (start > 0 && IsIdentifierChar(text[start - 1]))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && IsIdentifierChar(text[end]))
        {
            end++;
        }

        return end > start ? text[start..end] : string.Empty;
    }

    public LanguageCompletionContext GetCompletionContext(ModelSnapshot snapshot, int line, int character)
    {
        var prefix = GetWordAt(snapshot.Text, line, character);
        var receiver = GetMemberCompletionReceiverAt(snapshot.Text, line, character);
        if (!string.IsNullOrWhiteSpace(receiver))
        {
            var receiverType = ResolveReceiverType(snapshot, receiver, line);
            if (receiverType.Length > 0)
            {
                return new LanguageCompletionContext(
                    FilterByPrefix(ResolveMemberSymbols(snapshot, receiverType, string.Empty).Where(symbol => symbol.Kind != "constructor"), prefix).ToArray(),
                    IncludeKeywords: false,
                    IncludeBuiltInTypes: false,
                    DebugContext: $"receiver='{receiver}' receiverType='{receiverType}' prefix='{prefix}'");
            }
        }

        if (IsNewExpressionContext(snapshot.Text, line, character))
        {
            return new LanguageCompletionContext(
                FilterByPrefix(snapshot.Symbols.Where(symbol => symbol.Kind == "class"), prefix).ToArray(),
                IncludeKeywords: false,
                IncludeBuiltInTypes: false,
                DebugContext: $"newExpression prefix='{prefix}'");
        }

        if (IsTypeNameContext(snapshot.Text, line, character))
        {
            return new LanguageCompletionContext(
                FilterByPrefix(snapshot.Symbols.Where(symbol => symbol.Kind is "class" or "interface" or "enum" or "function"), prefix).ToArray(),
                IncludeKeywords: false,
                IncludeBuiltInTypes: true,
                DebugContext: $"typeName prefix='{prefix}'");
        }

        var localSymbols = snapshot.Symbols
            .Where(symbol => symbol.Uri == snapshot.Uri && (symbol.Kind is "local" or "parameter") && symbol.Range.Start.Line <= line);
        var globalSymbols = snapshot.Symbols
            .Where(symbol => symbol.Kind is not "local" and not "parameter" and not "constructor");
        return new LanguageCompletionContext(
            FilterByPrefix(localSymbols.Concat(globalSymbols), prefix)
                .OrderBy(symbol => symbol.Kind is "local" or "parameter" ? 0 : symbol.Uri == snapshot.Uri ? 1 : symbol.Uri.Contains("/libs/shipped/", StringComparison.Ordinal) ? 3 : 2)
                .ToArray(),
            IncludeKeywords: true,
            IncludeBuiltInTypes: true);
    }

    public IEnumerable<LanguageSymbol> ResolveSymbolsAt(ModelSnapshot snapshot, int line, int character, string word)
    {
        if (string.Equals(word, "self", StringComparison.Ordinal))
        {
            var selfType = GetEnclosingTypeName(snapshot.Text, line);
            if (selfType.Length > 0)
            {
                var offset = OffsetAt(snapshot.Text, line, character);
                var start = offset;
                while (start > 0 && IsIdentifierChar(snapshot.Text[start - 1]))
                {
                    start--;
                }

                return
                [
                    new LanguageSymbol(
                        snapshot.Uri,
                        "self",
                        "parameter",
                        $"self: {selfType}",
                        string.Empty,
                        string.Empty,
                        [],
                        ToRange(snapshot.Text, new TextSpan(start, word.Length)),
                        string.Empty,
                        selfType)
                ];
            }
        }

        var memberAccess = GetMemberAccessAt(snapshot.Text, line, character);
        if (memberAccess is not null)
        {
            var receiverType = ResolveReceiverType(snapshot, memberAccess.Value.Receiver, line);
            if (receiverType.Length > 0)
            {
                return ResolveMemberSymbols(snapshot, receiverType, memberAccess.Value.Member)
                    .OrderBy(symbol => symbol.Uri == snapshot.Uri ? 0 : 1)
                    .ToArray();
            }
        }

        var scoped = snapshot.Symbols
            .Where(symbol => symbol.Name == word && symbol.Uri == snapshot.Uri && (symbol.Kind is "local" or "parameter") && symbol.Range.Start.Line <= line)
            .OrderByDescending(symbol => symbol.Range.Start.Line)
            .ToArray();
        if (scoped.Length > 0)
        {
            return scoped;
        }

        var directSymbols = snapshot.Symbols
            .Where(symbol => symbol.Name == word && symbol.Kind is not "local" and not "parameter")
            .OrderBy(symbol => symbol.Uri == snapshot.Uri ? 0 : symbol.Uri.Contains("/libs/shipped/", StringComparison.Ordinal) ? 2 : 1)
            .ToArray();
        if (directSymbols.Length > 0)
        {
            return directSymbols;
        }

        if (TryResolveNamespaceHover(snapshot, line, character, word, out var namespaceSymbol))
        {
            return [namespaceSymbol];
        }

        return [];
    }

    public LanguageSymbol? ResolveSymbolAt(ModelSnapshot snapshot, int line, int character, string word) =>
        ResolveSymbolsAt(snapshot, line, character, word).FirstOrDefault();

    public LanguageSymbol? ResolveIndexSymbolAt(ModelSnapshot snapshot, int line, int character) =>
        ResolveIndexSymbolsAt(snapshot, line, character).FirstOrDefault();

    public IEnumerable<LanguageSymbol> ResolveIndexSymbolsAt(ModelSnapshot snapshot, int line, int character)
    {
        if (!TryGetIndexExpressionAt(snapshot.Text, line, character, out var targetExpression))
        {
            return [];
        }

        var targetType = ResolveReceiverType(snapshot, targetExpression, line);
        if (targetType.Length == 0)
        {
            return [];
        }

        if (TryGetArrayElementType(targetType, out var elementType))
        {
            var rank = GetArrayRank(targetType);
            var parameters = Enumerable.Range(0, rank)
                .Select(index => new LanguageParameter(
                    rank == 1 ? "index" : $"index{index}",
                    "Integer",
                    string.Empty,
                    rank == 1 ? "The zero-based array index." : $"The zero-based array index for dimension {index + 1}."))
                .ToArray();
            var parameterText = string.Join("; ", parameters.Select(parameter => $"{parameter.Name}: {parameter.Type}"));
            return
            [
                CreateSyntheticSymbol(
                    snapshot,
                    "index",
                    "property",
                    $"index[{parameterText}]: {elementType}",
                    parameters,
                    typeName: elementType)
            ];
        }

        return ResolveMemberSymbols(snapshot, targetType, "Item")
            .Where(symbol => symbol.Kind == "property")
            .ToArray();
    }

    public IEnumerable<LanguageSymbol> ResolveInvocationSymbols(ModelSnapshot snapshot, int line, InvocationInfo invocation)
    {
        if (!string.IsNullOrWhiteSpace(invocation.Receiver))
        {
            var receiverType = ResolveReceiverType(snapshot, invocation.Receiver, line);
            if (receiverType.Length > 0)
            {
                return DeduplicateSymbols(ResolveMemberSymbols(snapshot, receiverType, invocation.Name)
                    .Where(symbol => symbol.Kind is "method" or "function" or "constructor")
                    .OrderBy(symbol => symbol.Uri == snapshot.Uri ? 0 : 1))
                    .ToArray();
            }
        }

        var constructors = DeduplicateSymbols(snapshot.Symbols
            .Where(symbol => symbol.OwnerType == invocation.Name && symbol.Kind == "constructor")
            .OrderBy(symbol => symbol.Uri == snapshot.Uri ? 0 : 1))
            .ToArray();
        if (constructors.Length > 0)
        {
            return constructors;
        }

        return DeduplicateSymbols(snapshot.Symbols
            .Where(symbol => symbol.Name == invocation.Name && symbol.Kind is "method" or "function" or "constructor")
            .OrderBy(symbol => symbol.Uri == snapshot.Uri ? 0 : symbol.Uri.Contains("/libs/shipped/", StringComparison.Ordinal) ? 2 : 1))
            .ToArray();
    }

    public static IEnumerable<LanguageSymbol> DeduplicateSymbols(IEnumerable<LanguageSymbol> symbols) =>
        symbols
            .GroupBy(symbol => $"{symbol.Kind}:{symbol.OwnerType}:{symbol.Name}:{symbol.Signature}", StringComparer.Ordinal)
            .Select(group => group.First());

    public IEnumerable<string> FindMissingUsesActions(ModelSnapshot snapshot, string typeName)
    {
        if (snapshot.Symbols.Any(symbol => symbol.Name == typeName))
        {
            yield break;
        }

        var importedNamespaces = CollectImportedNamespaces(snapshot.Text);
        var workspace = Build(workspaceRoot, new Dictionary<string, string>(StringComparer.Ordinal));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in workspace.Symbols.Where(symbol => symbol.Name == typeName && symbol.Kind is "class" or "interface" or "enum" or "function"))
        {
            var namespaceName = GetNamespaceForSymbol(symbol);
            if (namespaceName.Length > 0 && !importedNamespaces.Contains(namespaceName) && seen.Add(namespaceName))
            {
                yield return namespaceName;
            }
        }
    }

    public IEnumerable<MissingUsesDiagnostic> FindMissingUsesDiagnostics(ModelSnapshot snapshot)
    {
        var importedNamespaces = CollectImportedNamespaces(snapshot.Text);
        var workspace = Build(workspaceRoot, new Dictionary<string, string>(StringComparer.Ordinal));
        var visibleNames = snapshot.Symbols.Select(symbol => symbol.Name).ToHashSet(StringComparer.Ordinal);
        var ignoredSpans = SyntaxTree.Parse(snapshot.Text).Root.Tokens
            .Where(token => token.Kind == SyntaxKind.StringToken)
            .Select(token => token.Span)
            .ToArray();
        var candidates = workspace.Symbols
            .Where(symbol => symbol.Kind is "class" or "interface" or "enum" or "function")
            .Select(symbol => new { Symbol = symbol, NamespaceName = GetNamespaceForSymbol(symbol) })
            .Where(item => item.NamespaceName.Length > 0 && !importedNamespaces.Contains(item.NamespaceName) && !visibleNames.Contains(item.Symbol.Name))
            .GroupBy(item => item.Symbol.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().NamespaceName, StringComparer.Ordinal);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var identifierPattern = new System.Text.RegularExpressions.Regex(@"\b[A-Z][A-Za-z0-9_]*\b");
        foreach (System.Text.RegularExpressions.Match match in identifierPattern.Matches(snapshot.Text))
        {
            var name = match.Value;
            if (IsOffsetInSpans(match.Index, ignoredSpans) ||
                IsMemberAccessName(snapshot.Text, match.Index) ||
                !reported.Add(name) ||
                !candidates.TryGetValue(name, out var namespaceName))
            {
                continue;
            }

            yield return new MissingUsesDiagnostic(ToRange(snapshot.Text, new TextSpan(match.Index, match.Length)), name, namespaceName);
        }
    }

    private static bool IsOffsetInSpans(int offset, IReadOnlyList<TextSpan> spans) =>
        spans.Any(span => offset >= span.Start && offset < span.End);

    private static bool IsMemberAccessName(string text, int offset)
    {
        var index = offset - 1;
        while (index >= 0 && char.IsWhiteSpace(text[index]))
        {
            index--;
        }

        return index >= 0 && text[index] == '.';
    }

    public TextEditInfo GetUsesInsertEdit(string text, string namespaceName)
    {
        var tree = SyntaxTree.Parse(text);
        var offset = 0;
        if (tree.Root.Uses is not null)
        {
            offset = tree.Root.Uses.SemicolonToken.Span.Start;
            var position = ToPosition(text, offset);
            return new TextEditInfo(new LspRange(position, position), $", {namespaceName}");
        }
        else if (tree.Root.Namespace is not null)
        {
            offset = tree.Root.Namespace.SemicolonToken.Span.End;
            if (offset < text.Length && text[offset] == '\r')
            {
                offset++;
            }

            if (offset < text.Length && text[offset] == '\n')
            {
                offset++;
            }
        }

        var insertPosition = ToPosition(text, offset);
        return new TextEditInfo(new LspRange(insertPosition, insertPosition), $"uses {namespaceName};\n");
    }

    private IEnumerable<LanguageSymbol> ResolveMemberSymbols(ModelSnapshot snapshot, string receiverType, string memberName)
    {
        if (SemanticFacts.HasLengthProperty(CreateTypeSymbol(receiverType)) &&
            (memberName.Length == 0 || string.Equals(memberName, "Length", StringComparison.Ordinal)))
        {
            yield return CreateSyntheticSymbol(
                snapshot,
                "Length",
                "property",
                "Length: Integer",
                typeName: "Integer");
        }

        var currentType = receiverType;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (currentType.Length > 0 && visited.Add(GetGenericTypeDefinitionName(currentType)))
        {
            var ownerType = GetGenericTypeDefinitionName(currentType);
            foreach (var symbol in snapshot.Symbols.Where(symbol => (memberName.Length == 0 || symbol.Name == memberName) && symbol.OwnerType == ownerType))
            {
                yield return SubstituteGenericMemberType(snapshot, currentType, symbol);
            }

            currentType = SubstituteGenericTypeName(snapshot, currentType, snapshot.TypeBases.GetValueOrDefault(ownerType, string.Empty));
        }
    }

    private string ResolveReceiverType(ModelSnapshot snapshot, string receiver, int line)
    {
        receiver = TrimOuterParentheses(receiver.Trim());
        if (TryResolveCastReceiverType(receiver, out var castType))
        {
            return castType;
        }

        if (TryResolveIndexedReceiverType(snapshot, receiver, line, out var indexedType))
        {
            return indexedType;
        }

        if (receiver.Contains('.', StringComparison.Ordinal))
        {
            var chainType = ResolveReceiverChainType(snapshot, receiver, line);
            if (chainType.Length > 0)
            {
                return chainType;
            }
        }

        return ResolveSimpleReceiverType(snapshot, receiver, line);
    }

    private string ResolveReceiverChainType(ModelSnapshot snapshot, string receiver, int line)
    {
        var parts = receiver
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var currentType = ResolveSimpleReceiverType(snapshot, parts[0], line);
        for (var index = 1; index < parts.Length && currentType.Length > 0; index++)
        {
            var member = ResolveMemberSymbols(snapshot, currentType, parts[index])
                .Where(symbol => symbol.TypeName.Length > 0)
                .OrderBy(symbol => symbol.Kind is "property" or "field" ? 0 : 1)
                .FirstOrDefault();
            currentType = member?.TypeName ?? string.Empty;
        }

        return currentType;
    }

    private string ResolveSimpleReceiverType(ModelSnapshot snapshot, string receiver, int line)
    {
        if (string.Equals(receiver, "self", StringComparison.Ordinal))
        {
            return GetEnclosingTypeName(snapshot.Text, line);
        }

        var local = snapshot.Symbols
            .Where(symbol => symbol.Uri == snapshot.Uri && (symbol.Kind is "local" or "parameter") && symbol.Name == receiver && symbol.TypeName.Length > 0 && symbol.Range.Start.Line <= line)
            .OrderByDescending(symbol => symbol.Range.Start.Line)
            .FirstOrDefault();
        if (local is not null)
        {
            return local.TypeName;
        }

        var receiverDefinition = GetGenericTypeDefinitionName(receiver);
        if (snapshot.Symbols.Any(symbol => (symbol.Kind is "class" or "interface" or "enum") && symbol.Name == receiverDefinition))
        {
            return receiver;
        }

        var field = snapshot.Symbols.FirstOrDefault(symbol => symbol.Name == receiver && symbol.TypeName.Length > 0);
        return field?.TypeName ?? string.Empty;
    }

    private string InferExpressionType(ModelSnapshot snapshot, string expression, int line)
    {
        expression = TrimOuterParentheses(expression.Trim());
        if (expression.Length == 0)
        {
            return string.Empty;
        }

        if (TrySplitNullCoalescingExpression(expression, out var coalesceLeft, out var coalesceRight))
        {
            var leftType = InferExpressionType(snapshot, coalesceLeft, line);
            if (leftType.Length > 0 && leftType != "inferred")
            {
                return leftType;
            }

            return InferExpressionType(snapshot, coalesceRight, line);
        }

        if (expression.StartsWith('\'') || expression.StartsWith('"'))
        {
            return "String";
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(expression, @"^[0-9]+$"))
        {
            return "Integer";
        }

        if (string.Equals(expression, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expression, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "Boolean";
        }

        if (expression.StartsWith("not ", StringComparison.OrdinalIgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(expression, @"\s+(?:is|in|not\s+in|and|or|xor)\s+", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ||
            System.Text.RegularExpressions.Regex.IsMatch(expression, @"\s(?:=|<>|<=|>=|<|>)\s"))
        {
            return "Boolean";
        }

        var newArrayMatch = System.Text.RegularExpressions.Regex.Match(
            expression,
            @"^new\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?(?:<[^;\r\n()]+>)?)\s*\[([^\]]*)\]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (newArrayMatch.Success)
        {
            var rank = newArrayMatch.Groups[2].Value.Count(ch => ch == ',') + 1;
            return NormalizeLocalTypeName(newArrayMatch.Groups[1].Value) + FormatArraySuffix(rank);
        }

        var newMatch = System.Text.RegularExpressions.Regex.Match(
            expression,
            @"^new\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?(?:<[^;\r\n()]+>)?)\s*\(",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (newMatch.Success)
        {
            return NormalizeLocalTypeName(newMatch.Groups[1].Value);
        }

        if (TryResolveCastReceiverType(expression, out var castType))
        {
            return castType;
        }

        if (TryResolveIndexedReceiverType(snapshot, expression, line, out var indexedType))
        {
            return indexedType;
        }

        if (TryInferCallExpressionType(snapshot, expression, line, out var callType))
        {
            return callType;
        }

        if (expression.Contains('.', StringComparison.Ordinal))
        {
            var lastDot = expression.LastIndexOf('.');
            var receiver = expression[..lastDot];
            var memberName = expression[(lastDot + 1)..];
            var receiverType = ResolveReceiverType(snapshot, receiver, line);
            if (receiverType.Length > 0)
            {
                var member = ResolveMemberSymbols(snapshot, receiverType, memberName)
                    .Where(symbol => symbol.TypeName.Length > 0)
                    .OrderBy(symbol => symbol.Kind is "property" or "field" ? 0 : 1)
                    .FirstOrDefault();
                if (member is not null)
                {
                    return member.TypeName;
                }
            }
        }

        return ResolveSimpleReceiverType(snapshot, expression, line);
    }

    private string InferExpressionType(
        ModelSnapshot snapshot,
        SemanticSymbolSet semanticSymbols,
        ExpressionSyntax? expression,
        int line,
        MethodSymbol? currentMethod)
    {
        if (expression is null)
        {
            return string.Empty;
        }

        var inferredType = InferSemanticExpressionType(snapshot, semanticSymbols, expression, line, currentMethod);
        if (inferredType == TypeSymbol.Unknown || inferredType.Name == TypeSymbol.Unknown.Name)
        {
            if (expression is QueryExpressionSyntax query &&
                TryInferQueryExpressionType(snapshot, semanticSymbols, query, line, currentMethod, out var queryType))
            {
                return queryType;
            }

            return InferExpressionType(snapshot, SemanticFacts.GetExpressionDisplayName(expression), line);
        }

        return NormalizeLocalTypeName(inferredType.Name);
    }

    private bool TryInferQueryExpressionType(
        ModelSnapshot snapshot,
        SemanticSymbolSet semanticSymbols,
        QueryExpressionSyntax query,
        int line,
        MethodSymbol? currentMethod,
        out string typeName)
    {
        typeName = string.Empty;
        var locals = CollectLocalTypes(snapshot, line);
        if (!SemanticFacts.TryTranslateQueryExpression(
                query,
                locals,
                semanticSymbols.Types,
                semanticSymbols.Methods,
                semanticSymbols.Fields,
                semanticSymbols.Constants,
                semanticSymbols.Properties,
                currentMethod,
                out var translated))
        {
            return false;
        }

        var translatedType = InferSemanticExpressionType(snapshot, semanticSymbols, translated, line, currentMethod);
        if (translatedType != TypeSymbol.Unknown && translatedType.Name != TypeSymbol.Unknown.Name)
        {
            typeName = NormalizeLocalTypeName(translatedType.Name);
            return typeName.Length > 0;
        }

        typeName = InferExpressionType(snapshot, SemanticFacts.GetExpressionDisplayName(translated), line);
        return typeName.Length > 0;
    }

    private string InferForeachElementType(
        ModelSnapshot snapshot,
        SemanticSymbolSet semanticSymbols,
        ExpressionSyntax? collection,
        int line,
        MethodSymbol? currentMethod)
    {
        var collectionType = InferSemanticExpressionType(snapshot, semanticSymbols, collection, line, currentMethod);
        return TryResolveEnumerableElementType(collectionType, semanticSymbols, out var elementType)
            ? NormalizeLocalTypeName(elementType.Name)
            : string.Empty;
    }

    private IEnumerable<LanguageSymbol> CollectQueryLocalSymbols(
        string uri,
        string text,
        ModelSnapshot snapshot,
        SemanticSymbolSet semanticSymbols,
        QueryExpressionSyntax query,
        int line,
        MethodSymbol? currentMethod)
    {
        var queryLocals = new Dictionary<string, TypeSymbol>(CollectLocalTypes(snapshot, line), StringComparer.Ordinal);
        if (TryResolveEnumerableElementType(InferSemanticExpressionType(query.SourceExpression, queryLocals, semanticSymbols, currentMethod), semanticSymbols, out var rangeVariableType))
        {
            queryLocals[query.Identifier.Text] = rangeVariableType;
            yield return CreateQueryLocalSymbol(uri, text, query.Identifier, rangeVariableType);
        }

        TypeSymbol? joinRangeVariableType = null;
        if (query.JoinSourceExpression is not null &&
            query.JoinIdentifier is not null &&
            TryResolveEnumerableElementType(InferSemanticExpressionType(query.JoinSourceExpression, queryLocals, semanticSymbols, currentMethod), semanticSymbols, out joinRangeVariableType))
        {
            queryLocals[query.JoinIdentifier.Text] = joinRangeVariableType;
            yield return CreateQueryLocalSymbol(uri, text, query.JoinIdentifier, joinRangeVariableType);

            if (query.JoinIntoIdentifier is not null)
            {
                var groupedJoinRangeType = SemanticFacts.ResolveTypeReference($"IEnumerable<{joinRangeVariableType.Name}>", semanticSymbols.Types)
                    ?? new TypeSymbol($"IEnumerable<{joinRangeVariableType.Name}>", true);
                queryLocals[query.JoinIntoIdentifier.Text] = groupedJoinRangeType;
                yield return CreateQueryLocalSymbol(uri, text, query.JoinIntoIdentifier, groupedJoinRangeType);
            }
        }

        if (query.SecondSourceExpression is not null &&
            query.SecondIdentifier is not null &&
            TryResolveEnumerableElementType(InferSemanticExpressionType(query.SecondSourceExpression, queryLocals, semanticSymbols, currentMethod), semanticSymbols, out var secondRangeVariableType))
        {
            queryLocals[query.SecondIdentifier.Text] = secondRangeVariableType;
            yield return CreateQueryLocalSymbol(uri, text, query.SecondIdentifier, secondRangeVariableType);
        }

        if (query.LetExpression is not null && query.LetIdentifier is not null)
        {
            var letType = InferQueryLocalExpressionType(query.LetExpression, queryLocals, semanticSymbols, currentMethod);
            if (letType != TypeSymbol.Unknown)
            {
                queryLocals[query.LetIdentifier.Text] = letType;
                yield return CreateQueryLocalSymbol(uri, text, query.LetIdentifier, letType);
            }
        }

        var projectedType = query.GroupExpression is not null
            ? InferSemanticExpressionType(query.GroupExpression, queryLocals, semanticSymbols, currentMethod)
            : InferSemanticExpressionType(query.SelectExpression, queryLocals, semanticSymbols, currentMethod);
        var continuationRangeType = projectedType;
        if (query.GroupExpression is not null && query.GroupByExpression is not null)
        {
            var groupKeyType = InferSemanticExpressionType(query.GroupByExpression, queryLocals, semanticSymbols, currentMethod);
            continuationRangeType = SemanticFacts.ResolveTypeReference($"Grouping<{groupKeyType.Name}, {projectedType.Name}>", semanticSymbols.Types)
                ?? new TypeSymbol($"Grouping<{groupKeyType.Name}, {projectedType.Name}>", true);
        }

        if (query.IntoIdentifier is not null && continuationRangeType != TypeSymbol.Unknown)
        {
            var continuationLocals = new Dictionary<string, TypeSymbol>(CollectLocalTypes(snapshot, line), StringComparer.Ordinal)
            {
                [query.IntoIdentifier.Text] = continuationRangeType
            };
            yield return CreateQueryLocalSymbol(uri, text, query.IntoIdentifier, continuationRangeType);

            if (query.ContinuationLetExpression is not null && query.ContinuationLetIdentifier is not null)
            {
                var continuationLetType = InferQueryLocalExpressionType(query.ContinuationLetExpression, continuationLocals, semanticSymbols, currentMethod);
                if (continuationLetType != TypeSymbol.Unknown)
                {
                    yield return CreateQueryLocalSymbol(uri, text, query.ContinuationLetIdentifier, continuationLetType);
                }
            }
        }
    }

    private LanguageSymbol CreateQueryLocalSymbol(string uri, string text, SyntaxToken identifier, TypeSymbol type)
    {
        var typeName = NormalizeLocalTypeName(type.Name);
        return CreateSymbol(uri, text, identifier.Text, "local", identifier.Span, $"{identifier.Text}: {typeName}", typeName: typeName);
    }

    private static TypeSymbol InferQueryLocalExpressionType(
        ExpressionSyntax? expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        SemanticSymbolSet semanticSymbols,
        MethodSymbol? currentMethod)
    {
        var type = InferSemanticExpressionType(expression, locals, semanticSymbols, currentMethod);
        if (type != TypeSymbol.Unknown && type.Name != TypeSymbol.Unknown.Name)
        {
            return type;
        }

        return TryInferKnownEnumerableCallType(expression, out var enumerableCallType)
            ? enumerableCallType
            : TypeSymbol.Unknown;
    }

    private static bool TryInferKnownEnumerableCallType(ExpressionSyntax? expression, out TypeSymbol type)
    {
        type = TypeSymbol.Unknown;
        if (expression is not CallExpressionSyntax call)
        {
            return false;
        }

        var targetName = SemanticFacts.GetExpressionDisplayName(call.Target);
        if (!targetName.StartsWith("Enumerable", StringComparison.Ordinal))
        {
            return false;
        }

        var lastDot = targetName.LastIndexOf('.');
        if (lastDot < 0)
        {
            return false;
        }

        type = targetName[(lastDot + 1)..] switch
        {
            "Any" or "Contains" => TypeSymbol.Boolean,
            "Count" => TypeSymbol.Integer,
            _ => TypeSymbol.Unknown
        };

        return type != TypeSymbol.Unknown;
    }

    private static TypeSymbol InferSemanticExpressionType(
        ExpressionSyntax? expression,
        IReadOnlyDictionary<string, TypeSymbol> locals,
        SemanticSymbolSet semanticSymbols,
        MethodSymbol? currentMethod) =>
        expression is null
            ? TypeSymbol.Unknown
            : SemanticFacts.InferExpressionType(
                expression,
                locals,
                semanticSymbols.Methods,
                semanticSymbols.Fields,
                semanticSymbols.Constants,
                semanticSymbols.Properties,
                currentMethod,
                semanticSymbols.Types);

    private static bool TryResolveEnumerableElementType(TypeSymbol collectionType, SemanticSymbolSet semanticSymbols, out TypeSymbol elementType)
    {
        if (collectionType == TypeSymbol.String)
        {
            elementType = TypeSymbol.Char;
            return true;
        }

        elementType = SemanticFacts.GetElementType(collectionType)
            ?? SemanticFacts.GetSetElementType(collectionType)
            ?? TypeSymbol.Unknown;
        if (elementType != TypeSymbol.Unknown && elementType.Name != TypeSymbol.Unknown.Name)
        {
            return true;
        }

        if (TryResolveEnumerableElementTypeName(collectionType.Name, out var directElementTypeName))
        {
            elementType = CreateTypeSymbol(directElementTypeName);
            return elementType != TypeSymbol.Unknown && elementType.Name != TypeSymbol.Unknown.Name;
        }

        elementType = SemanticFacts.ResolveEnumerablePattern(collectionType, semanticSymbols.Types)?.ElementType
            ?? TypeSymbol.Unknown;

        return elementType != TypeSymbol.Unknown && elementType.Name != TypeSymbol.Unknown.Name;
    }

    private static bool TryResolveEnumerableElementTypeName(string collectionTypeName, out string elementTypeName)
    {
        elementTypeName = string.Empty;
        var genericDefinition = GetGenericTypeDefinitionName(collectionTypeName);
        if (genericDefinition is not ("IEnumerable" or "IEnumerator" or "List" or "IReadOnlyList" or "Dictionary"))
        {
            return false;
        }

        var genericArguments = GetGenericTypeArguments(collectionTypeName);
        if (genericArguments.Count == 0)
        {
            return false;
        }

        elementTypeName = genericDefinition == "Dictionary" && genericArguments.Count >= 2
            ? $"KeyValuePair<{genericArguments[0]}, {genericArguments[1]}>"
            : genericArguments[0];
        return elementTypeName.Length > 0;
    }

    private TypeSymbol InferSemanticExpressionType(
        ModelSnapshot snapshot,
        SemanticSymbolSet semanticSymbols,
        ExpressionSyntax? expression,
        int line,
        MethodSymbol? currentMethod)
    {
        if (expression is null)
        {
            return TypeSymbol.Unknown;
        }

        return SemanticFacts.InferExpressionType(
            expression,
            CollectLocalTypes(snapshot, line),
            semanticSymbols.Methods,
            semanticSymbols.Fields,
            semanticSymbols.Constants,
            semanticSymbols.Properties,
            currentMethod,
            semanticSymbols.Types);
    }

    private static IReadOnlyDictionary<string, TypeSymbol> CollectLocalTypes(ModelSnapshot snapshot, int line) =>
        snapshot.Symbols
            .Where(symbol => symbol.Kind is "local" or "parameter" &&
                symbol.TypeName.Length > 0 &&
                symbol.TypeName != "inferred" &&
                symbol.Range.Start.Line <= line)
            .GroupBy(symbol => symbol.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => CreateTypeSymbol(group.Last().TypeName),
                StringComparer.Ordinal);

    private static SemanticSymbolSet BuildSemanticSymbols(ModelSnapshot snapshot)
    {
        var declaredMethods = snapshot.Symbols
            .Where(symbol => symbol.Kind is "method" or "function" or "constructor")
            .SelectMany(CreateMethodSymbols)
            .ToArray();
        var knownFields = snapshot.Symbols
            .Where(symbol => symbol.Kind == "field" && symbol.TypeName.Length > 0)
            .SelectMany(CreateFieldSymbols)
            .ToArray();
        var knownConstants = snapshot.Symbols
            .Where(symbol => symbol.Kind is "constant" or "enumMember")
            .SelectMany(CreateConstantSymbols)
            .ToArray();
        var knownProperties = snapshot.Symbols
            .Where(symbol => symbol.Kind == "property" && symbol.TypeName.Length > 0)
            .SelectMany(CreatePropertySymbols)
            .ToArray();
        var knownMethods = declaredMethods
            .Concat(knownProperties.SelectMany(property => new[] { property.GetterMethod, property.SetterMethod }.Where(method => method is not null).Cast<MethodSymbol>()))
            .ToArray();

        var types = new List<TypeSymbol>(TypeSymbol.BuiltInTypes);
        foreach (var typeSymbol in snapshot.Symbols.Where(symbol => symbol.Kind is "class" or "interface" or "enum"))
        {
            var ownerType = typeSymbol.Name;
            var typeParameters = snapshot.TypeParameters.GetValueOrDefault(ownerType, [])
                .Select(parameter => new TypeParameterSymbol(parameter))
                .ToArray();
            var baseType = snapshot.TypeBases.TryGetValue(ownerType, out var baseTypeName) && baseTypeName.Length > 0
                ? CreateTypeSymbol(baseTypeName)
                : typeSymbol.Kind == "class"
                    ? TypeSymbol.Object
                    : null;

            types.Add(new NamedTypeSymbol(
                ownerType,
                typeSymbol.Kind != "enum",
                false,
                typeSymbol.Kind == "interface",
                baseType,
                [],
                knownMethods.Where(method => method.DeclaringTypeName == ownerType).ToArray(),
                knownFields.Where(field => field.DeclaringTypeName == ownerType).ToArray(),
                knownConstants.Where(constant => constant.DeclaringTypeName == ownerType).ToArray(),
                knownProperties.Where(property => property.DeclaringTypeName == ownerType).ToArray(),
                typeParameters.Length,
                typeParameters));
        }

        return new SemanticSymbolSet(
            SymbolLists.CreateTypes(types),
            SymbolLists.CreateMethods(knownMethods),
            SymbolLists.CreateFields(knownFields),
            SymbolLists.CreateConstants(knownConstants),
            SymbolLists.CreateProperties(knownProperties));
    }

    private static MethodSymbol CreateMethodSymbol(MethodDeclarationSyntax method, string ownerType)
    {
        var parameters = method.Parameters.Select(ToParameterSymbol).ToArray();
        var returnType = method.ReturnType is null
            ? TypeSymbol.Void
            : CreateTypeSymbol(method.ReturnType.ToDisplayString());
        var isConstructor = method.Keyword.Kind == SyntaxKind.ConstructorKeyword;
        return new MethodSymbol(
            method.Identifier.Text,
            isConstructor ? CreateTypeSymbol(ownerType) : returnType,
            parameters,
            ownerType.Length == 0 ? null : ownerType,
            IsStatic(method.Modifiers),
            method,
            isConstructor);
    }

    private static IEnumerable<MethodSymbol> CreateMethodSymbols(LanguageSymbol symbol)
    {
        var method = new MethodSymbol(
            symbol.Name,
            symbol.TypeName.Length > 0 ? CreateTypeSymbol(symbol.TypeName) : TypeSymbol.Void,
            symbol.Parameters.Select(ToParameterSymbol).ToArray(),
            symbol.OwnerType.Length == 0 ? null : symbol.OwnerType,
            false,
            IsConstructor: symbol.Kind == "constructor");

        yield return method;
        if (symbol.OwnerType.Length > 0)
        {
            yield return method with { IsStatic = true };
        }
    }

    private static IEnumerable<FieldSymbol> CreateFieldSymbols(LanguageSymbol symbol)
    {
        var field = new FieldSymbol(
            symbol.Name,
            CreateTypeSymbol(symbol.TypeName),
            symbol.OwnerType.Length == 0 ? null : symbol.OwnerType,
            false);

        yield return field;
        if (symbol.OwnerType.Length > 0)
        {
            yield return field with { IsStatic = true };
        }
    }

    private static IEnumerable<ConstantSymbol> CreateConstantSymbols(LanguageSymbol symbol)
    {
        var type = symbol.TypeName.Length > 0 ? CreateTypeSymbol(symbol.TypeName) : CreateTypeSymbol(symbol.OwnerType.Length > 0 ? symbol.OwnerType : TypeSymbol.Object.Name);
        var constant = new ConstantSymbol(
            symbol.Name,
            type,
            null,
            symbol.OwnerType.Length == 0 ? null : symbol.OwnerType,
            symbol.OwnerType.Length > 0);

        yield return constant;
    }

    private static IEnumerable<PropertySymbol> CreatePropertySymbols(LanguageSymbol symbol)
    {
        var propertyType = CreateTypeSymbol(symbol.TypeName);
        var getterMethod = new MethodSymbol(
            $"get_{symbol.Name}",
            propertyType,
            [],
            symbol.OwnerType.Length == 0 ? null : symbol.OwnerType,
            false);
        var property = new PropertySymbol(
            symbol.Name,
            propertyType,
            null,
            null,
            symbol.Parameters.Count > 0 ? ToParameterSymbol(symbol.Parameters[0]) : null,
            getterMethod,
            DeclaringTypeName: symbol.OwnerType.Length == 0 ? null : symbol.OwnerType,
            IsStatic: false,
            IsIndexer: symbol.Parameters.Count > 0);

        yield return property;
        if (symbol.OwnerType.Length > 0)
        {
            yield return property with
            {
                IsStatic = true,
                GetterMethod = getterMethod with { IsStatic = true }
            };
        }
    }

    private static ParameterSymbol ToParameterSymbol(LanguageParameter parameter) =>
        new(parameter.Name, CreateTypeSymbol(parameter.Type), ToParameterPassingKind(parameter.Modifier));

    private static ParameterSymbol ToParameterSymbol(ParameterSyntax parameter) =>
        new(parameter.Identifier.Text, CreateTypeSymbol(parameter.TypeName.ToDisplayString()), ToParameterPassingKind(parameter.ModifierKeyword?.Text ?? string.Empty));

    private static ParameterPassingKind ToParameterPassingKind(string modifier) =>
        modifier.Trim() switch
        {
            "out" => ParameterPassingKind.Out,
            "ref" => ParameterPassingKind.Ref,
            "in" => ParameterPassingKind.In,
            "params" => ParameterPassingKind.Params,
            _ => ParameterPassingKind.Value
        };

    private static TypeSymbol CreateTypeSymbol(string typeName)
    {
        typeName = NormalizeLocalTypeName(typeName);
        if (typeName.Length == 0)
        {
            return TypeSymbol.Unknown;
        }

        return SemanticFacts.TryResolveBuiltInType(typeName) ?? new TypeSymbol(typeName, IsReferenceTypeName(typeName));
    }

    private static bool IsReferenceTypeName(string typeName) =>
        typeName.EndsWith("]", StringComparison.Ordinal) ||
        typeName.Contains('<', StringComparison.Ordinal) ||
        !TypeSymbol.BuiltInScalarTypes.Any(type => type.Name == typeName);

    private static bool IsStatic(IEnumerable<SyntaxToken> modifiers) =>
        modifiers.Any(modifier => modifier.Kind == SyntaxKind.StaticKeyword);

    private bool TryInferCallExpressionType(ModelSnapshot snapshot, string expression, int line, out string typeName)
    {
        typeName = string.Empty;
        var openParen = FindCallOpenParen(expression);
        if (openParen <= 0)
        {
            return false;
        }

        var target = expression[..openParen].Trim();
        if (target.Length == 0)
        {
            return false;
        }

        if (target.Contains('.', StringComparison.Ordinal))
        {
            var lastDot = target.LastIndexOf('.');
            var receiver = target[..lastDot];
            var memberName = target[(lastDot + 1)..];
            var receiverType = ResolveReceiverType(snapshot, receiver, line);
            if (receiverType.Length == 0)
            {
                return false;
            }

            var member = ResolveMemberSymbols(snapshot, receiverType, memberName)
                .Where(symbol => symbol.Kind is "method" or "function" && symbol.TypeName.Length > 0)
                .OrderBy(symbol => symbol.Kind == "function" ? 0 : 1)
                .FirstOrDefault();
            if (member is null)
            {
                return false;
            }

            typeName = member.TypeName;
            return true;
        }

        var function = snapshot.Symbols
            .Where(symbol => symbol.Name == target && symbol.Kind == "function" && symbol.TypeName.Length > 0)
            .OrderBy(symbol => symbol.Uri == snapshot.Uri ? 0 : symbol.Uri.Contains("/libs/shipped/", StringComparison.Ordinal) ? 2 : 1)
            .FirstOrDefault();
        if (function is null)
        {
            return false;
        }

        typeName = function.TypeName;
        return true;
    }

    private static int FindCallOpenParen(string expression)
    {
        var depth = 0;
        for (var index = expression.Length - 1; index >= 0; index--)
        {
            if (expression[index] == ')')
            {
                depth++;
            }
            else if (expression[index] == '(')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static bool TrySplitNullCoalescingExpression(string expression, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;
        var depth = 0;
        for (var index = 0; index < expression.Length - 1; index++)
        {
            if (expression[index] is '(' or '[')
            {
                depth++;
            }
            else if (expression[index] is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (expression[index] == '?' && expression[index + 1] == '?' && depth == 0)
            {
                left = expression[..index].Trim();
                right = expression[(index + 2)..].Trim();
                return left.Length > 0 && right.Length > 0;
            }
        }

        return false;
    }

    private static string NormalizeLocalTypeName(string typeName)
    {
        typeName = typeName.Trim();
        if (typeName.StartsWith("array of ", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeLocalTypeName(typeName["array of ".Length..]) + "[]";
        }

        var fixedArrayMatch = System.Text.RegularExpressions.Regex.Match(
            typeName,
            @"^array\s*\[([^\]]*)\]\s+of\s+(.+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (fixedArrayMatch.Success)
        {
            var rank = fixedArrayMatch.Groups[1].Value.Count(ch => ch == ',') + 1;
            return NormalizeLocalTypeName(fixedArrayMatch.Groups[2].Value) + FormatArraySuffix(rank);
        }

        return GetSimpleTypeReference(typeName);
    }

    private static string FormatArraySuffix(int rank) =>
        rank <= 1 ? "[]" : $"[{new string(',', rank - 1)}]";

    private bool TryResolveIndexedReceiverType(ModelSnapshot snapshot, string receiver, int line, out string typeName)
    {
        typeName = string.Empty;
        if (!receiver.EndsWith(']'))
        {
            return false;
        }

        var bracketStart = FindMatchingBracketStart(receiver, receiver.Length - 1);
        if (bracketStart <= 0)
        {
            return false;
        }

        var targetExpression = receiver[..bracketStart].Trim();
        var targetType = ResolveReceiverType(snapshot, targetExpression, line);
        if (targetType.Length == 0)
        {
            return false;
        }

        if (string.Equals(targetType, "String", StringComparison.Ordinal))
        {
            typeName = "String";
            return true;
        }

        if (TryGetArrayElementType(targetType, out var elementType))
        {
            typeName = elementType;
            return true;
        }

        var indexer = ResolveMemberSymbols(snapshot, targetType, "Item")
            .Where(symbol => symbol.Kind == "property" && symbol.TypeName.Length > 0)
            .FirstOrDefault();
        if (indexer is not null)
        {
            typeName = indexer.TypeName;
            return true;
        }

        var arguments = GetGenericTypeArguments(targetType);
        if (arguments.Count > 0)
        {
            typeName = arguments[^1];
            return true;
        }

        return false;
    }

    private static int FindMatchingBracketStart(string text, int closeBracket)
    {
        var depth = 0;
        for (var index = closeBracket; index >= 0; index--)
        {
            if (text[index] == ']')
            {
                depth++;
            }
            else if (text[index] == '[')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static LanguageSymbol SubstituteGenericMemberType(ModelSnapshot snapshot, string receiverType, LanguageSymbol symbol)
    {
        if (symbol.TypeName.Length == 0)
        {
            return symbol;
        }

        var typeName = SubstituteGenericTypeName(snapshot, receiverType, symbol.TypeName);
        var parameters = symbol.Parameters
            .Select(parameter => parameter with { Type = SubstituteGenericTypeName(snapshot, receiverType, parameter.Type) })
            .ToArray();
        var signature = symbol.Signature;
        foreach (var parameter in symbol.Parameters.Zip(parameters))
        {
            signature = signature.Replace($": {parameter.First.Type}", $": {parameter.Second.Type}", StringComparison.Ordinal);
        }

        if (symbol.TypeName.Length > 0)
        {
            signature = signature.Replace($": {symbol.TypeName}", $": {typeName}", StringComparison.Ordinal);
        }

        return typeName == symbol.TypeName && parameters.SequenceEqual(symbol.Parameters) && signature == symbol.Signature
            ? symbol
            : symbol with { TypeName = typeName, Parameters = parameters, Signature = signature };
    }

    private static string SubstituteGenericTypeName(ModelSnapshot snapshot, string receiverType, string typeName)
    {
        if (typeName.Length == 0)
        {
            return string.Empty;
        }

        var typeDefinition = GetGenericTypeDefinitionName(receiverType);
        var typeParameters = snapshot.TypeParameters.GetValueOrDefault(typeDefinition, []);
        var typeArguments = GetGenericTypeArguments(receiverType);
        if (typeParameters.Count == 0 || typeArguments.Count == 0)
        {
            return typeName;
        }

        for (var index = 0; index < Math.Min(typeParameters.Count, typeArguments.Count); index++)
        {
            typeName = ReplaceTypeParameter(typeName, typeParameters[index], typeArguments[index]);
        }

        return typeName;
    }

    private static string ReplaceTypeParameter(string typeName, string parameterName, string argumentName)
    {
        if (string.Equals(typeName, parameterName, StringComparison.Ordinal))
        {
            return argumentName;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            typeName,
            $@"\b{System.Text.RegularExpressions.Regex.Escape(parameterName)}\b",
            argumentName);
    }

    private static string GetGenericTypeDefinitionName(string typeName)
    {
        typeName = typeName.Trim();
        var genericStart = typeName.IndexOf('<', StringComparison.Ordinal);
        if (genericStart >= 0)
        {
            typeName = typeName[..genericStart];
        }

        while (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            typeName = typeName[..^2];
        }

        if (typeName.EndsWith(']'))
        {
            var bracketStart = typeName.LastIndexOf('[');
            if (bracketStart > 0)
            {
                typeName = typeName[..bracketStart];
            }
        }

        return typeName.Split('.').Last();
    }

    private static bool TryGetArrayElementType(string typeName, out string elementType)
    {
        typeName = typeName.Trim();
        elementType = string.Empty;
        if (!typeName.EndsWith(']'))
        {
            return false;
        }

        var bracketStart = typeName.LastIndexOf('[');
        if (bracketStart <= 0)
        {
            return false;
        }

        var rankPart = typeName[(bracketStart + 1)..^1];
        if (rankPart.Any(ch => ch != ','))
        {
            return false;
        }

        elementType = typeName[..bracketStart];
        return elementType.Length > 0;
    }

    private static int GetArrayRank(string typeName)
    {
        if (!TryGetArrayElementType(typeName, out _))
        {
            return 0;
        }

        var bracketStart = typeName.LastIndexOf('[');
        return typeName[(bracketStart + 1)..^1].Count(ch => ch == ',') + 1;
    }

    private static string GetSimpleTypeReference(string typeName)
    {
        typeName = typeName.Trim();
        var genericStart = typeName.IndexOf('<', StringComparison.Ordinal);
        if (genericStart < 0)
        {
            return typeName.Split('.').Last();
        }

        var definitionName = typeName[..genericStart].Split('.').Last();
        return definitionName + typeName[genericStart..];
    }

    private static IReadOnlyList<string> GetGenericTypeArguments(string typeName)
    {
        var genericStart = typeName.IndexOf('<', StringComparison.Ordinal);
        var genericEnd = typeName.LastIndexOf('>');
        if (genericStart < 0 || genericEnd <= genericStart)
        {
            return [];
        }

        var arguments = new List<string>();
        var depth = 0;
        var start = genericStart + 1;
        for (var index = genericStart + 1; index < genericEnd; index++)
        {
            if (typeName[index] == '<')
            {
                depth++;
            }
            else if (typeName[index] == '>')
            {
                depth--;
            }
            else if (typeName[index] == ',' && depth == 0)
            {
                arguments.Add(typeName[start..index].Trim());
                start = index + 1;
            }
        }

        arguments.Add(typeName[start..genericEnd].Trim());
        return arguments.Where(argument => argument.Length > 0).ToArray();
    }

    private static string GetEnclosingTypeName(string text, int line)
    {
        var tree = SyntaxTree.Parse(text);
        var offset = OffsetAt(text, line, 0);
        foreach (var declaration in tree.Root.Members.OfType<ClassDeclarationSyntax>())
        {
            if (offset >= declaration.ClassKeyword.Span.Start && offset <= declaration.SemicolonToken.Span.End)
            {
                return declaration.Identifier.Text;
            }
        }

        return string.Empty;
    }

    private static bool TryResolveCastReceiverType(string receiver, out string typeName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            receiver,
            @"\bas\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?(?:<[^;\r\n()]+>)?(?:\[\])?)\s*$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        typeName = match.Success ? GetSimpleTypeReference(match.Groups[1].Value) : string.Empty;
        return typeName.Length > 0;
    }

    private static string TrimOuterParentheses(string value)
    {
        while (value.Length >= 2 && value[0] == '(' && value[^1] == ')' && HasSingleOuterParentheses(value))
        {
            value = value[1..^1].Trim();
        }

        return value;
    }

    private static bool HasSingleOuterParentheses(string value)
    {
        var depth = 0;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '(')
            {
                depth++;
            }
            else if (value[index] == ')')
            {
                depth--;
                if (depth == 0 && index < value.Length - 1)
                {
                    return false;
                }
            }
        }

        return depth == 0;
    }

    private static IEnumerable<LanguageSymbol> FilterByPrefix(IEnumerable<LanguageSymbol> symbols, string prefix) =>
        string.IsNullOrWhiteSpace(prefix)
            ? symbols
            : symbols.Where(symbol => symbol.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsNewExpressionContext(string text, int line, int character)
    {
        var prefix = GetCurrentLinePrefix(text, line, character);
        return System.Text.RegularExpressions.Regex.IsMatch(prefix, @"\bnew\s+[A-Za-z_][A-Za-z0-9_]*$|\bnew\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsTypeNameContext(string text, int line, int character)
    {
        var prefix = GetCurrentLinePrefix(text, line, character);
        return System.Text.RegularExpressions.Regex.IsMatch(prefix, @"(:|\bas\b|\bis\b)\s*[A-Za-z_][A-Za-z0-9_]*$|(:|\bas\b|\bis\b)\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string GetCurrentLinePrefix(string text, int line, int character)
    {
        var offset = OffsetAt(text, line, character);
        var lineStart = Math.Clamp(offset, 0, text.Length);
        while (lineStart > 0 && text[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        return text[lineStart..Math.Clamp(offset, 0, text.Length)];
    }

    private static string GetMemberCompletionReceiverAt(string text, int line, int character)
    {
        var offset = OffsetAt(text, line, character);
        if (offset <= 0 || offset > text.Length)
        {
            return string.Empty;
        }

        var memberStart = offset;
        while (memberStart > 0 && IsIdentifierChar(text[memberStart - 1]))
        {
            memberStart--;
        }

        if (memberStart < 2 || text[memberStart - 1] != '.')
        {
            return string.Empty;
        }

        return ExtractReceiverBeforeDot(text, memberStart - 1);
    }

    private static (string Receiver, string Member)? GetMemberAccessAt(string text, int line, int character)
    {
        var offset = OffsetAt(text, line, character);
        if (offset < 0 || offset > text.Length)
        {
            return null;
        }

        var memberStart = offset;
        while (memberStart > 0 && IsIdentifierChar(text[memberStart - 1]))
        {
            memberStart--;
        }

        var memberEnd = offset;
        while (memberEnd < text.Length && IsIdentifierChar(text[memberEnd]))
        {
            memberEnd++;
        }

        if (memberStart == memberEnd || memberStart < 2 || text[memberStart - 1] != '.')
        {
            return null;
        }

        var receiver = ExtractReceiverBeforeDot(text, memberStart - 1);
        if (receiver.Length == 0)
        {
            return null;
        }

        return (receiver, text[memberStart..memberEnd]);
    }

    private static bool TryResolveNamespaceHover(ModelSnapshot snapshot, int line, int character, string word, out LanguageSymbol symbol)
    {
        symbol = default!;
        var chain = GetQualifiedNameChainAt(snapshot.Text, line, character);
        if (chain.Length == 0 || !chain.StartsWith(word, StringComparison.Ordinal))
        {
            return false;
        }

        var namespaceNames = snapshot.Symbols
            .Select(GetNamespaceForSymbol)
            .Where(namespaceName => namespaceName.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var matchingNamespace = namespaceNames
            .Where(namespaceName => namespaceName == word || namespaceName.StartsWith(word + ".", StringComparison.Ordinal))
            .OrderBy(namespaceName => namespaceName.Length)
            .FirstOrDefault();
        if (matchingNamespace is null)
        {
            return false;
        }

        var offset = OffsetAt(snapshot.Text, line, character);
        var start = offset;
        while (start > 0 && IsIdentifierChar(snapshot.Text[start - 1]))
        {
            start--;
        }

        symbol = new LanguageSymbol(
            snapshot.Uri,
            word,
            "namespace",
            $"namespace {matchingNamespace}",
            string.Empty,
            string.Empty,
            [],
            ToRange(snapshot.Text, new TextSpan(start, word.Length)),
            string.Empty,
            matchingNamespace);
        return true;
    }

    private static string GetQualifiedNameChainAt(string text, int line, int character)
    {
        var offset = OffsetAt(text, line, character);
        if (offset < 0 || offset > text.Length)
        {
            return string.Empty;
        }

        var start = offset;
        while (start > 0 && (IsIdentifierChar(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        var end = offset;
        while (end < text.Length && (IsIdentifierChar(text[end]) || text[end] == '.'))
        {
            end++;
        }

        return end > start ? text[start..end] : string.Empty;
    }

    private static bool TryGetIndexExpressionAt(string text, int line, int character, out string targetExpression)
    {
        targetExpression = string.Empty;
        var offset = OffsetAt(text, line, character);
        if (offset < 0 || offset > text.Length)
        {
            return false;
        }

        var openBracket = -1;
        var depth = 0;
        var scanStart = Math.Min(Math.Max(offset - 1, 0), text.Length - 1);
        for (var index = scanStart; index >= 0; index--)
        {
            if (text[index] == ']')
            {
                depth++;
            }
            else if (text[index] == '[')
            {
                if (depth == 0)
                {
                    openBracket = index;
                    break;
                }

                depth--;
            }
            else if (text[index] is '\n' or ';')
            {
                break;
            }
        }

        if (openBracket <= 0)
        {
            return false;
        }

        var closeBracket = FindClosingBracket(text, openBracket);
        if (closeBracket >= 0 && offset > closeBracket)
        {
            return false;
        }

        var targetStart = FindExpressionStartBeforePostfix(text, openBracket);
        targetExpression = text[targetStart..openBracket].Trim();
        return targetExpression.Length > 0;
    }

    private static int FindClosingBracket(string text, int openBracket)
    {
        var depth = 0;
        for (var index = openBracket; index < text.Length; index++)
        {
            if (text[index] == '[')
            {
                depth++;
            }
            else if (text[index] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string ExtractReceiverBeforeDot(string text, int dotOffset)
    {
        var receiverEnd = dotOffset;
        while (receiverEnd > 0 && char.IsWhiteSpace(text[receiverEnd - 1]))
        {
            receiverEnd--;
        }

        if (receiverEnd <= 0)
        {
            return string.Empty;
        }

        if (text[receiverEnd - 1] == ']')
        {
            var bracketStart = FindMatchingBracketStart(text, receiverEnd - 1);
            if (bracketStart >= 0)
            {
                var targetStart = FindExpressionStartBeforePostfix(text, bracketStart);
                return text[targetStart..receiverEnd].Trim();
            }
        }

        if (text[receiverEnd - 1] == ')')
        {
            var depth = 0;
            for (var index = receiverEnd - 1; index >= 0; index--)
            {
                if (text[index] == ')')
                {
                    depth++;
                }
                else if (text[index] == '(')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text[index..receiverEnd].Trim();
                    }
                }
            }
        }

        var receiverStart = receiverEnd;
        while (receiverStart > 0 && (IsIdentifierChar(text[receiverStart - 1]) || text[receiverStart - 1] == '.'))
        {
            receiverStart--;
        }

        while (receiverStart < receiverEnd && text[receiverStart] == '.')
        {
            receiverStart++;
        }

        return receiverEnd > receiverStart ? text[receiverStart..receiverEnd] : string.Empty;
    }

    private static int FindExpressionStartBeforePostfix(string text, int postfixStart)
    {
        var index = postfixStart;
        while (index > 0 && char.IsWhiteSpace(text[index - 1]))
        {
            index--;
        }

        if (index > 0 && text[index - 1] == ')')
        {
            var depth = 0;
            for (var scan = index - 1; scan >= 0; scan--)
            {
                if (text[scan] == ')')
                {
                    depth++;
                }
                else if (text[scan] == '(')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return scan;
                    }
                }
            }
        }

        while (index > 0 && (IsIdentifierChar(text[index - 1]) || text[index - 1] == '.'))
        {
            index--;
        }

        return index;
    }

    public static InvocationInfo? FindInvocation(string text, int line, int character)
    {
        var offset = OffsetAt(text, line, character);
        if (offset <= 0)
        {
            return null;
        }

        var before = text[..offset];
        var depth = 0;
        var openParen = -1;
        for (var index = before.Length - 1; index >= 0; index--)
        {
            if (before[index] == ')')
            {
                depth++;
            }
            else if (before[index] == '(')
            {
                if (depth == 0)
                {
                    openParen = index;
                    break;
                }

                depth--;
            }
        }

        if (openParen < 0)
        {
            return null;
        }

        var nameEnd = openParen;
        while (nameEnd > 0 && char.IsWhiteSpace(before[nameEnd - 1]))
        {
            nameEnd--;
        }

        var genericName = TryExtractGenericInvocationName(before, nameEnd, out var genericNameStart, out var genericNameText);
        var nameStart = nameEnd;
        if (genericName)
        {
            nameStart = genericNameStart;
        }
        else
        {
            while (nameStart > 0 && IsIdentifierChar(before[nameStart - 1]))
            {
                nameStart--;
            }
        }

        if (nameStart == nameEnd)
        {
            return null;
        }

        var parameterIndex = 0;
        var argumentDepth = 0;
        foreach (var ch in before[(openParen + 1)..])
        {
            if (ch is '(' or '[')
            {
                argumentDepth++;
            }
            else if (ch is ')' or ']')
            {
                argumentDepth = Math.Max(0, argumentDepth - 1);
            }
            else if ((ch is ',' or ';') && argumentDepth == 0)
            {
                parameterIndex++;
            }
        }

        var receiver = string.Empty;
        if (nameStart > 1 && before[nameStart - 1] == '.')
        {
            receiver = ExtractReceiverBeforeDot(before, nameStart - 1);
        }

        return new InvocationInfo(receiver, genericName ? genericNameText : before[nameStart..nameEnd], parameterIndex);
    }

    private static bool TryExtractGenericInvocationName(string text, int nameEnd, out int nameStart, out string name)
    {
        nameStart = nameEnd;
        name = string.Empty;
        if (nameEnd <= 0 || text[nameEnd - 1] != '>')
        {
            return false;
        }

        var depth = 0;
        var genericStart = -1;
        for (var index = nameEnd - 1; index >= 0; index--)
        {
            if (text[index] == '>')
            {
                depth++;
            }
            else if (text[index] == '<')
            {
                depth--;
                if (depth == 0)
                {
                    genericStart = index;
                    break;
                }
            }
        }

        if (genericStart <= 0)
        {
            return false;
        }

        var identifierStart = genericStart;
        while (identifierStart > 0 && IsIdentifierChar(text[identifierStart - 1]))
        {
            identifierStart--;
        }

        if (identifierStart == genericStart)
        {
            return false;
        }

        nameStart = identifierStart;
        name = text[identifierStart..genericStart];
        return true;
    }

    private static int OffsetAt(string text, int line, int character)
    {
        var currentLine = 0;
        var currentCharacter = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (currentLine == line && currentCharacter == character)
            {
                return index;
            }

            if (text[index] == '\n')
            {
                currentLine++;
                currentCharacter = 0;
            }
            else
            {
                currentCharacter++;
            }
        }

        return text.Length;
    }

    private static bool IsIdentifierChar(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static string FormatMethod(MethodDeclarationSyntax declaration)
    {
        var parameters = string.Join("; ", declaration.Parameters.Select(parameter => $"{ParameterModifier(parameter)}{parameter.Identifier.Text}: {parameter.TypeName.ToDisplayString()}"));
        var returnText = declaration.ReturnType is null ? string.Empty : $": {declaration.ReturnType.ToDisplayString()}";
        return $"{declaration.Keyword.Text} {declaration.Identifier.Text}({parameters}){returnText}";
    }

    private static string FormatDelegate(DelegateDeclarationSyntax declaration)
    {
        var parameters = string.Join("; ", declaration.Parameters.Select(parameter => $"{ParameterModifier(parameter)}{parameter.Identifier.Text}: {parameter.TypeName.ToDisplayString()}"));
        var returnText = declaration.ReturnType is null ? string.Empty : $": {declaration.ReturnType.ToDisplayString()}";
        return $"delegate {declaration.SignatureKeyword.Text} {declaration.Identifier.Text}({parameters}){returnText}";
    }

    private static string FormatProperty(PropertyDeclarationSyntax declaration)
    {
        var indexer = declaration.IndexParameter is null
            ? string.Empty
            : $"[{ParameterModifier(declaration.IndexParameter)}{declaration.IndexParameter.Identifier.Text}: {declaration.IndexParameter.TypeName.ToDisplayString()}]";
        return $"{declaration.Identifier.Text}{indexer}: {declaration.TypeName.ToDisplayString()}";
    }

    private static LanguageParameter FormatParameter(ParameterSyntax parameter) =>
        new(parameter.Identifier.Text, parameter.TypeName.ToDisplayString(), ParameterModifier(parameter).Trim(), string.Empty);

    private static string ParameterModifier(ParameterSyntax parameter) =>
        parameter.ModifierKeyword is null ? string.Empty : parameter.ModifierKeyword.Text + " ";

    private static TextSpan SpanFrom(TextSpan start, TextSpan end) =>
        new(start.Start, Math.Max(1, end.End - start.Start));

    private static string PathToUri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static string? UriToPath(string uri)
    {
        try
        {
            return new Uri(uri).LocalPath;
        }
        catch
        {
            return null;
        }
    }
}

internal static class DocumentationReader
{
    public static Documentation FindDocumentationBefore(string text, int offset)
    {
        var searchStart = Math.Clamp(offset - 1, 0, Math.Max(text.Length - 1, 0));
        var declarationLineStart = text.Length == 0 ? -1 : text.LastIndexOf('\n', searchStart);
        var documentationEnd = declarationLineStart < 0 ? 0 : declarationLineStart;
        var prefix = text[..documentationEnd];
        var lines = prefix.Split('\n');
        var docLines = new List<string>();
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                docLines.Insert(0, trimmed[3..].Trim());
                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/—", StringComparison.Ordinal))
            {
                continue;
            }

            break;
        }

        var raw = string.Join('\n', docLines);
        var parameters = ExtractParameters(raw);
        return new Documentation(
            Clean(Extract(raw, "summary") ?? raw),
            Clean(Extract(raw, "returns") ?? string.Empty),
            parameters);
    }

    private static string? Extract(string raw, string tag)
    {
        var start = raw.IndexOf($"<{tag}>", StringComparison.OrdinalIgnoreCase);
        var end = raw.IndexOf($"</{tag}>", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || end < 0 || end <= start)
        {
            return null;
        }

        return raw[(start + tag.Length + 2)..end];
    }

    private static IReadOnlyDictionary<string, string> ExtractParameters(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var searchIndex = 0;
        while (searchIndex < raw.Length)
        {
            var paramStart = raw.IndexOf("<param", searchIndex, StringComparison.OrdinalIgnoreCase);
            if (paramStart < 0)
            {
                break;
            }

            var tagEnd = raw.IndexOf('>', paramStart);
            var paramEnd = raw.IndexOf("</param>", paramStart, StringComparison.OrdinalIgnoreCase);
            if (tagEnd < 0 || paramEnd < 0 || paramEnd <= tagEnd)
            {
                break;
            }

            var tag = raw[paramStart..tagEnd];
            var name = ExtractAttribute(tag, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                result[name] = Clean(raw[(tagEnd + 1)..paramEnd]);
            }

            searchIndex = paramEnd + "</param>".Length;
        }

        return result;
    }

    private static string ExtractAttribute(string tag, string name)
    {
        var marker = name + "=\"";
        var start = tag.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return string.Empty;
        }

        start += marker.Length;
        var end = tag.IndexOf('"', start);
        return end < 0 ? string.Empty : tag[start..end];
    }

    private static string Clean(string text) =>
        text
            .Replace("<c>", "`", StringComparison.OrdinalIgnoreCase)
            .Replace("</c>", "`", StringComparison.OrdinalIgnoreCase)
            .Replace("<see cref=\"", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("\"/>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Aggregate(string.Empty, (current, line) => current.Length == 0 ? line : current + " " + line);
}

internal sealed record Documentation(string Summary, string Returns, IReadOnlyDictionary<string, string> Params);
internal sealed record LanguageParameter(string Name, string Type, string Modifier, string Documentation);
internal sealed record LanguageCompletionItem(string Label, int Kind, string Detail, object? Documentation, string SortText);
internal sealed record LanguageCompletionContext(IReadOnlyList<LanguageSymbol> Symbols, bool IncludeKeywords, bool IncludeBuiltInTypes, string DebugContext = "");
internal sealed record InvocationInfo(string Receiver, string Name, int ParameterIndex);
internal sealed record MissingUsesDiagnostic(LspRange Range, string TypeName, string NamespaceName);
internal sealed record TextEditInfo(LspRange Range, string NewText);
internal sealed record LanguageDocumentSymbol(string Uri, string Name, string Kind, string Signature, LspRange Range, LspRange SelectionRange, IReadOnlyList<LanguageDocumentSymbol> Children);
internal sealed record LocalDeclarationInfo(SyntaxToken Identifier, string ExplicitType, ExpressionSyntax? Initializer, bool IsForeachElement);
internal sealed record SemanticSymbolSet(
    IReadOnlyList<TypeSymbol> Types,
    IReadOnlyList<MethodSymbol> Methods,
    IReadOnlyList<FieldSymbol> Fields,
    IReadOnlyList<ConstantSymbol> Constants,
    IReadOnlyList<PropertySymbol> Properties);
internal sealed record LanguageSymbol(
    string Uri,
    string Name,
    string Kind,
    string Signature,
    string Documentation,
    string ReturnsDocumentation,
    IReadOnlyList<LanguageParameter> Parameters,
    LspRange Range,
    string OwnerType,
    string TypeName)
{
    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.Append("```ilc\n").Append(Signature).Append("\n```\n");
        builder.Append(ToDocumentationMarkdown());
        return builder.ToString();
    }

    public string ToDocumentationMarkdown()
    {
        var builder = new StringBuilder();
        if (Documentation.Length > 0)
        {
            builder.Append(Documentation).Append("\n\n");
        }

        foreach (var parameter in Parameters)
        {
            if (parameter.Documentation.Length > 0)
            {
                builder.Append("*@param* `").Append(parameter.Name).Append("` ").Append(parameter.Documentation).Append("\n\n");
            }
        }

        if (ReturnsDocumentation.Length > 0)
        {
            builder.Append("*@returns* ").Append(ReturnsDocumentation).Append("\n\n");
        }

        return builder.ToString();
    }
}

internal sealed record ModelSnapshot(
    string Uri,
    string Text,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<LanguageSymbol> Symbols,
    IReadOnlyDictionary<string, string> TypeBases,
    IReadOnlyDictionary<string, IReadOnlyList<string>> TypeParameters)
{
    public LspRange ToRange(TextSpan span) => LanguageModel.ToRange(Text, span);
}

internal sealed record CachedModelSnapshot(
    long Revision,
    string Text,
    ModelSnapshot Snapshot);

internal readonly record struct LspPosition(int Line, int Character);
internal readonly record struct LspRange(LspPosition Start, LspPosition End);
