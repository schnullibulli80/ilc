import * as childProcess from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';

const languageId = 'ilc';
const diagnosticSource = 'ilc';

let output: vscode.OutputChannel;
let diagnostics: vscode.DiagnosticCollection;
let languageServer: IlcLanguageServerClient | undefined;

interface CommandResult {
  stdout: string;
  stderr: string;
}

interface IlcParameter {
  name: string;
  type: string;
  modifier: string;
  documentation: string;
}

interface IlcSymbol {
  name: string;
  kind: vscode.SymbolKind;
  signature: string;
  detail: string;
  documentation: string;
  parameters: IlcParameter[];
  returnsDocumentation: string;
  uri: vscode.Uri;
  range: vscode.Range;
}

interface LspResponse {
  id?: number;
  method?: string;
  params?: unknown;
  result?: unknown;
}

const keywordCompletions = [
  'namespace', 'uses', 'public', 'private', 'protected', 'internal', 'static', 'extern',
  'class', 'record', 'interface', 'enum', 'delegate', 'constructor', 'method', 'function',
  'procedure', 'property', 'var', 'const', 'begin', 'end', 'if', 'then', 'else', 'while',
  'do', 'for', 'foreach', 'in', 'repeat', 'until', 'case', 'of', 'match', 'with', 'try',
  'except', 'finally', 'raise', 'return', 'new', 'nil', 'true', 'false', 'out', 'ref'
];

const typeCompletions = [
  'Integer', 'Boolean', 'String', 'Void', 'NativeHandle', 'array', 'List', 'Dictionary'
];

export function activate(context: vscode.ExtensionContext) {
  output = vscode.window.createOutputChannel('ILC');
  diagnostics = vscode.languages.createDiagnosticCollection('ilc');
  languageServer = new IlcLanguageServerClient(context);

  context.subscriptions.push(output, diagnostics);
  context.subscriptions.push(vscode.commands.registerCommand('ilc.compileCurrentFile', compileCurrentFile));
  context.subscriptions.push(vscode.commands.registerCommand('ilc.runCurrentFile', runCurrentFile));
  context.subscriptions.push(vscode.commands.registerCommand('ilc.refreshDiagnostics', refreshDiagnosticsCommand));
  context.subscriptions.push(vscode.commands.registerCommand('ilc.verifyWorkspace', verifyWorkspace));
  context.subscriptions.push(vscode.commands.registerCommand('ilc.runQtQuickSmoke', runQtQuickSmoke));
  context.subscriptions.push(vscode.commands.registerCommand('ilc.openOutput', () => output.show()));
  context.subscriptions.push(vscode.languages.registerCompletionItemProvider(languageId, new IlcCompletionProvider(), '.', ':'));
  context.subscriptions.push(vscode.languages.registerHoverProvider(languageId, new IlcHoverProvider()));
  context.subscriptions.push(vscode.languages.registerSignatureHelpProvider(languageId, new IlcSignatureHelpProvider(), '(', ',', ';'));
  context.subscriptions.push(vscode.languages.registerDocumentSymbolProvider(languageId, new IlcDocumentSymbolProvider()));
  context.subscriptions.push(vscode.languages.registerDefinitionProvider(languageId, new IlcDefinitionProvider()));
  context.subscriptions.push(vscode.languages.registerCodeActionsProvider(languageId, new IlcCodeActionProvider(), { providedCodeActionKinds: [vscode.CodeActionKind.QuickFix] }));
  context.subscriptions.push(vscode.languages.registerWorkspaceSymbolProvider(new IlcWorkspaceSymbolProvider()));

  context.subscriptions.push(vscode.workspace.onDidOpenTextDocument(document => {
    if (isIlcDocument(document) && languageServer?.isRunning === true) {
      languageServer.didOpen(document);
    } else if (isIlcDocument(document) && getConfig().get<boolean>('diagnostics.onOpen', true)) {
      refreshDiagnostics(document);
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidSaveTextDocument(document => {
    if (isIlcDocument(document) && languageServer?.isRunning === true) {
      languageServer.didSave(document);
    } else if (isIlcDocument(document) && getConfig().get<boolean>('diagnostics.onSave', true)) {
      refreshDiagnostics(document);
    }
  }));

  context.subscriptions.push(vscode.workspace.onDidChangeTextDocument(event => {
    if (isIlcDocument(event.document) && languageServer?.isRunning === true) {
      languageServer.didChange(event.document);
    }
  }));

  languageServer.start();

  if (vscode.window.activeTextEditor && isIlcDocument(vscode.window.activeTextEditor.document)) {
    if (languageServer.isRunning) {
      languageServer.didOpen(vscode.window.activeTextEditor.document);
    } else {
      refreshDiagnostics(vscode.window.activeTextEditor.document);
    }
  }

  logDebug('ILC extension activated.');
}

export function deactivate() {
  diagnostics?.clear();
  languageServer?.dispose();
}

async function compileCurrentFile(): Promise<void> {
  const document = getActiveIlcDocument();
  if (!document) {
    return;
  }

  await document.save();
  const result = await runCompiler(document, true);
  if (result) {
    vscode.window.showInformationMessage(`ILC compile finished: ${path.basename(document.fileName)}`);
  }
}

async function runCurrentFile(): Promise<void> {
  const document = getActiveIlcDocument();
  if (!document) {
    return;
  }

  await document.save();
  const compileResult = await runCompiler(document, true);
  if (!compileResult) {
    return;
  }

  const repoRoot = findRepoRoot(document);
  if (!repoRoot) {
    vscode.window.showErrorMessage('ILC workspace root not found.');
    return;
  }

  const runtimePath = resolveWorkspacePath(repoRoot, getConfig().get<string>('runtime.executable', 'build/runtime/ilcvm_cli/ilcvm_cli'));
  if (!fs.existsSync(runtimePath)) {
    vscode.window.showErrorMessage(`ILC runtime not found: ${runtimePath}`);
    return;
  }

  const ilbPath = replaceExtension(document.fileName, '.ilb');
  if (!fs.existsSync(ilbPath)) {
    vscode.window.showErrorMessage(`ILC bytecode not found after compile: ${ilbPath}`);
    return;
  }

  const terminal = vscode.window.createTerminal('ILC Run');
  terminal.show();
  terminal.sendText(`${quoteShell(runtimePath)} ${quoteShell(ilbPath)} --run`);
}

async function refreshDiagnosticsCommand(): Promise<void> {
  const document = getActiveIlcDocument();
  if (!document) {
    return;
  }

  await refreshDiagnostics(document);
}

async function verifyWorkspace(): Promise<void> {
  const repoRoot = findRepoRoot();
  if (!repoRoot) {
    vscode.window.showErrorMessage('ILC workspace root not found.');
    return;
  }

  runScriptInTerminal(repoRoot, 'scripts/run-local-verification.sh', 'ILC Verification', { ILC_QTBRIDGE_DEBUG: '1' });
}

async function runQtQuickSmoke(): Promise<void> {
  const repoRoot = findRepoRoot();
  if (!repoRoot) {
    vscode.window.showErrorMessage('ILC workspace root not found.');
    return;
  }

  runScriptInTerminal(repoRoot, 'scripts/run-ui-qtquick-smoke.sh', 'ILC QtQuick Smoke', { ILC_QTBRIDGE_DEBUG: '1' });
}

async function refreshDiagnostics(document: vscode.TextDocument): Promise<void> {
  if (!isIlcDocument(document) || document.isUntitled) {
    return;
  }

  const result = await runCompiler(document, false);
  if (!result) {
    return;
  }

  const parsedDiagnostics = parseCompilerDiagnostics(document, `${result.stdout}\n${result.stderr}`);
  diagnostics.set(document.uri, parsedDiagnostics);
}

async function runCompiler(document: vscode.TextDocument, revealOutput: boolean): Promise<CommandResult | undefined> {
  const repoRoot = findRepoRoot(document);
  if (!repoRoot) {
    vscode.window.showErrorMessage('ILC workspace root not found.');
    return undefined;
  }

  const compilerProject = resolveWorkspacePath(repoRoot, getConfig().get<string>('compiler.project', 'src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj'));
  if (!fs.existsSync(compilerProject)) {
    vscode.window.showErrorMessage(`ILC compiler project not found: ${compilerProject}`);
    return undefined;
  }

  const args = [
    'run',
    '--project',
    compilerProject,
    '--',
    '--debug',
    document.fileName,
    ...getShippedLibraryPaths(repoRoot, document.fileName)
  ];

  logDebug(`compiler cwd=${repoRoot}`);
  logDebug(`compiler args=${args.map(quoteForLog).join(' ')}`);

  if (revealOutput) {
    output.show(true);
  }

  output.appendLine(`[ilc] compiling ${document.fileName}`);

  try {
    const result = await execFile('dotnet', args, repoRoot);
    appendProcessOutput(result);
    diagnostics.set(document.uri, parseCompilerDiagnostics(document, `${result.stdout}\n${result.stderr}`));
    return result;
  } catch (error) {
    const result = error as CommandResult & { message?: string };
    appendProcessOutput(result);
    if (result.message) {
      output.appendLine(result.message);
    }

    const parsedDiagnostics = parseCompilerDiagnostics(document, `${result.stdout ?? ''}\n${result.stderr ?? ''}`);
    diagnostics.set(document.uri, parsedDiagnostics);

    if (revealOutput) {
      vscode.window.showErrorMessage(`ILC compile failed: ${path.basename(document.fileName)}`);
    }

    return undefined;
  }
}

function parseCompilerDiagnostics(document: vscode.TextDocument, text: string): vscode.Diagnostic[] {
  const parsed: vscode.Diagnostic[] = [];
  const activeFileName = path.basename(document.fileName);
  const diagnosticPattern = /^(.+?)\((\d+),(\d+)\):\s+(error|warning|info)\s+([^:]+):\s+(.*)$/gm;

  for (const match of text.matchAll(diagnosticPattern)) {
    const fileName = path.basename(match[1]);
    if (fileName !== activeFileName) {
      continue;
    }

    const line = Math.max(Number.parseInt(match[2], 10) - 1, 0);
    const column = Math.max(Number.parseInt(match[3], 10) - 1, 0);
    const severity = toDiagnosticSeverity(match[4]);
    const code = match[5].trim();
    const message = match[6].trim();
    const range = new vscode.Range(line, column, line, Math.max(column + 1, column));
    const diagnostic = new vscode.Diagnostic(range, message, severity);
    diagnostic.code = code;
    diagnostic.source = diagnosticSource;
    parsed.push(diagnostic);
  }

  logDebug(`diagnostics file=${document.fileName} count=${parsed.length}`);
  return parsed;
}

function toDiagnosticSeverity(value: string): vscode.DiagnosticSeverity {
  switch (value.toLowerCase()) {
    case 'error':
      return vscode.DiagnosticSeverity.Error;
    case 'warning':
      return vscode.DiagnosticSeverity.Warning;
    case 'info':
      return vscode.DiagnosticSeverity.Information;
    default:
      return vscode.DiagnosticSeverity.Hint;
  }
}

class IlcLanguageServerClient implements vscode.Disposable {
  private process: childProcess.ChildProcessWithoutNullStreams | undefined;
  private nextId = 1;
  private buffer = Buffer.alloc(0);
  private pending = new Map<number, (value: unknown) => void>();

  public isRunning = false;

  public constructor(private readonly context: vscode.ExtensionContext) {
  }

  public start(): void {
    if (!getConfig().get<boolean>('languageServer.enabled', true)) {
      logDebug('language server disabled by configuration');
      return;
    }

    const repoRoot = findRepoRoot();
    if (!repoRoot) {
      logDebug('language server not started: repo root not found');
      return;
    }

    const projectPath = resolveWorkspacePath(repoRoot, getConfig().get<string>('languageServer.project', 'src/ILC.LanguageServer/ILC.LanguageServer.csproj'));
    if (!fs.existsSync(projectPath)) {
      logDebug(`language server not started: project not found ${projectPath}`);
      return;
    }

    const args = ['run', '--project', projectPath, '--', '--workspace', repoRoot];
    if (getConfig().get<boolean>('debugOutput', false)) {
      args.push('--debug');
    }

    this.process = childProcess.spawn('dotnet', args, {
      cwd: repoRoot,
      stdio: ['pipe', 'pipe', 'pipe']
    });

    this.context.subscriptions.push(this);
    this.isRunning = true;
    this.process.stdout.on('data', chunk => this.handleData(chunk));
    this.process.stderr.on('data', chunk => output.append(chunk.toString()));
    this.process.on('exit', code => {
      this.isRunning = false;
      logDebug(`language server exited code=${code ?? '<signal>'}`);
    });

    void this.initialize(repoRoot);
  }

  public dispose(): void {
    if (this.process && !this.process.killed) {
      this.process.kill();
    }
  }

  public didOpen(document: vscode.TextDocument): void {
    this.notify('textDocument/didOpen', {
      textDocument: {
        uri: document.uri.toString(),
        languageId,
        version: document.version,
        text: document.getText()
      }
    });
  }

  public didChange(document: vscode.TextDocument): void {
    this.notify('textDocument/didChange', {
      textDocument: {
        uri: document.uri.toString(),
        version: document.version
      },
      contentChanges: [{ text: document.getText() }]
    });
  }

  public didSave(document: vscode.TextDocument): void {
    this.notify('textDocument/didSave', {
      textDocument: { uri: document.uri.toString() }
    });
  }

  public async completion(document: vscode.TextDocument, position: vscode.Position): Promise<vscode.CompletionItem[] | undefined> {
    const result = await this.request('textDocument/completion', this.textDocumentPosition(document, position));
    if (!isObject(result) || !Array.isArray(result.items)) {
      return undefined;
    }

    return result.items.map(item => {
      const label = asString(item.label);
      const completion = new vscode.CompletionItem(label, toVsCodeCompletionItemKind(item.kind));
      completion.detail = asString(item.detail);
      completion.documentation = extractDocumentation(item.documentation);
      completion.sortText = asString(item.sortText);
      return completion;
    });
  }

  public async hover(document: vscode.TextDocument, position: vscode.Position): Promise<vscode.Hover | undefined> {
    const result = await this.request('textDocument/hover', this.textDocumentPosition(document, position));
    if (!isObject(result) || !isObject(result.contents)) {
      return undefined;
    }

    const value = asString(result.contents.value);
    if (value.length === 0) {
      return undefined;
    }

    return new vscode.Hover(new vscode.MarkdownString(value));
  }

  public async signatureHelp(document: vscode.TextDocument, position: vscode.Position): Promise<vscode.SignatureHelp | undefined> {
    const result = await this.request('textDocument/signatureHelp', this.textDocumentPosition(document, position));
    if (!isObject(result) || !Array.isArray(result.signatures)) {
      return undefined;
    }

    const help = new vscode.SignatureHelp();
    help.activeSignature = asNumber(result.activeSignature, 0);
    help.activeParameter = asNumber(result.activeParameter, 0);
    help.signatures = result.signatures.map(signature => {
      const info = new vscode.SignatureInformation(asString(signature.label), extractDocumentation(signature.documentation));
      if (Array.isArray(signature.parameters)) {
        info.parameters = signature.parameters.map((parameter: Record<string, unknown>) =>
          new vscode.ParameterInformation(asString(parameter.label), extractDocumentation(parameter.documentation)));
      }
      return info;
    });
    return help;
  }

  public async documentSymbols(document: vscode.TextDocument): Promise<vscode.DocumentSymbol[] | undefined> {
    const result = await this.request('textDocument/documentSymbol', {
      textDocument: { uri: document.uri.toString() }
    });
    if (!Array.isArray(result)) {
      return undefined;
    }

    return result.map(toDocumentSymbol);
  }

  public async definition(document: vscode.TextDocument, position: vscode.Position): Promise<vscode.Definition | undefined> {
    const result = await this.request('textDocument/definition', this.textDocumentPosition(document, position));
    if (!Array.isArray(result)) {
      return undefined;
    }

    return result.map(location => new vscode.Location(
      vscode.Uri.parse(asString(location.uri)),
      toVsCodeRange(location.range)));
  }

  public async workspaceSymbols(query: string): Promise<vscode.SymbolInformation[] | undefined> {
    const result = await this.request('workspace/symbol', { query });
    if (!Array.isArray(result)) {
      return undefined;
    }

    return result.map(symbol => new vscode.SymbolInformation(
      asString(symbol.name),
      toVsCodeSymbolKind(symbol.kind),
      '',
      new vscode.Location(vscode.Uri.parse(asString(symbol.location?.uri)), toVsCodeRange(symbol.location?.range))));
  }

  public async codeActions(document: vscode.TextDocument, range: vscode.Range): Promise<vscode.CodeAction[] | undefined> {
    const result = await this.request('textDocument/codeAction', {
      textDocument: { uri: document.uri.toString() },
      range: toLspRange(range),
      context: { diagnostics: [] }
    });
    if (!Array.isArray(result)) {
      return undefined;
    }

    return result.map(toCodeAction);
  }

  private async initialize(repoRoot: string): Promise<void> {
    await this.request('initialize', {
      processId: process.pid,
      rootUri: vscode.Uri.file(repoRoot).toString(),
      capabilities: {}
    });
    this.notify('initialized', {});
    for (const document of vscode.workspace.textDocuments.filter(isIlcDocument)) {
      this.didOpen(document);
    }
    logDebug('language server initialized');
  }

  private textDocumentPosition(document: vscode.TextDocument, position: vscode.Position): object {
    return {
      textDocument: { uri: document.uri.toString() },
      position: { line: position.line, character: position.character }
    };
  }

  private request(method: string, params: object): Promise<unknown> {
    if (!this.isRunning) {
      return Promise.resolve(undefined);
    }

    const id = this.nextId++;
    const message = { jsonrpc: '2.0', id, method, params };
    return new Promise(resolve => {
      this.pending.set(id, resolve);
      this.writeMessage(message);
    });
  }

  private notify(method: string, params: object): void {
    if (!this.isRunning) {
      return;
    }

    this.writeMessage({ jsonrpc: '2.0', method, params });
  }

  private writeMessage(message: object): void {
    if (!this.process) {
      return;
    }

    const json = JSON.stringify(message);
    const content = Buffer.from(json, 'utf8');
    const header = Buffer.from(`Content-Length: ${content.length}\r\n\r\n`, 'ascii');
    this.process.stdin.write(Buffer.concat([header, content]));
  }

  private handleData(chunk: Buffer): void {
    this.buffer = Buffer.concat([this.buffer, chunk]);
    while (true) {
      const headerEnd = this.buffer.indexOf('\r\n\r\n');
      if (headerEnd < 0) {
        return;
      }

      const header = this.buffer.slice(0, headerEnd).toString('ascii');
      const lengthMatch = header.match(/Content-Length:\s*(\d+)/i);
      if (!lengthMatch) {
        this.buffer = this.buffer.slice(headerEnd + 4);
        continue;
      }

      const length = Number.parseInt(lengthMatch[1], 10);
      const messageStart = headerEnd + 4;
      const messageEnd = messageStart + length;
      if (this.buffer.length < messageEnd) {
        return;
      }

      const payload = this.buffer.slice(messageStart, messageEnd).toString('utf8');
      this.buffer = this.buffer.slice(messageEnd);
      this.handleMessage(payload);
    }
  }

  private handleMessage(payload: string): void {
    let message: LspResponse;
    try {
      message = JSON.parse(payload) as LspResponse;
    } catch (error) {
      logDebug(`language server invalid json: ${String(error)}`);
      return;
    }

    if (typeof message.id === 'number') {
      const pending = this.pending.get(message.id);
      if (pending) {
        this.pending.delete(message.id);
        pending(message.result);
      }
      return;
    }

    if (message.method === 'textDocument/publishDiagnostics' && isObject(message.params)) {
      this.applyDiagnostics(message.params);
    } else if (message.method === 'window/logMessage' && isObject(message.params)) {
      logDebug(asString(message.params.message));
    }
  }

  private applyDiagnostics(params: Record<string, unknown>): void {
    const uriText = asString(params.uri);
    if (uriText.length === 0 || !Array.isArray(params.diagnostics)) {
      return;
    }

    const uri = vscode.Uri.parse(uriText);
    const parsedDiagnostics = params.diagnostics.map(item => {
      const diagnostic = new vscode.Diagnostic(
        toVsCodeRange(item.range),
        asString(item.message),
        toVsCodeDiagnosticSeverity(asNumber(item.severity, 3)));
      diagnostic.source = asString(item.source) || diagnosticSource;
      diagnostic.code = asString(item.code);
      return diagnostic;
    });
    diagnostics.set(uri, parsedDiagnostics);
    logDebug(`language server diagnostics uri=${uriText} count=${parsedDiagnostics.length}`);
  }
}

class IlcCompletionProvider implements vscode.CompletionItemProvider {
  async provideCompletionItems(
    document: vscode.TextDocument,
    position: vscode.Position,
    token: vscode.CancellationToken,
    context: vscode.CompletionContext
  ): Promise<vscode.CompletionItem[] | undefined> {
    const requestedVersion = document.version;
    if (context.triggerKind !== vscode.CompletionTriggerKind.Invoke) {
      const shouldContinue = await delayUnlessCanceled(160, token);
      if (!shouldContinue) {
        logDebug(`completion canceled before request file=${document.fileName}`);
        return undefined;
      }

      if (document.version !== requestedVersion) {
        logDebug(`completion skipped stale request file=${document.fileName} requestedVersion=${requestedVersion} currentVersion=${document.version}`);
        return undefined;
      }
    }

    const serverItems = await languageServer?.completion(document, position);
    if (serverItems) {
      return serverItems;
    }

    if (token.isCancellationRequested) {
      logDebug(`completion canceled before fallback file=${document.fileName}`);
      return undefined;
    }

    const items: vscode.CompletionItem[] = [];

    for (const keyword of keywordCompletions) {
      const item = new vscode.CompletionItem(keyword, vscode.CompletionItemKind.Keyword);
      item.detail = 'ILC keyword';
      items.push(item);
    }

    for (const typeName of typeCompletions) {
      const item = new vscode.CompletionItem(typeName, vscode.CompletionItemKind.Class);
      item.detail = 'ILC built-in type';
      items.push(item);
    }

    for (const local of collectLocalSymbols(document, position)) {
      const item = new vscode.CompletionItem(local.name, vscode.CompletionItemKind.Variable);
      item.detail = local.detail;
      items.push(item);
    }

    for (const symbol of await collectWorkspaceSymbols(document)) {
      const item = new vscode.CompletionItem(symbol.name, completionKindFromSymbolKind(symbol.kind));
      item.detail = symbol.detail;
      item.documentation = formatSymbolMarkdown(symbol);
      items.push(item);
    }

    logDebug(`completion file=${document.fileName} items=${items.length}`);
    return items;
  }
}

class IlcHoverProvider implements vscode.HoverProvider {
  async provideHover(
    document: vscode.TextDocument,
    position: vscode.Position
  ): Promise<vscode.Hover | undefined> {
    const serverHover = await languageServer?.hover(document, position);
    if (serverHover) {
      return serverHover;
    }

    const range = document.getWordRangeAtPosition(position);
    if (!range) {
      return undefined;
    }

    const word = document.getText(range);
    const local = collectLocalSymbols(document, position).reverse().find(symbol => symbol.name === word);
    if (local) {
      const markdown = new vscode.MarkdownString();
      markdown.appendCodeblock(local.detail, 'ilc');
      return new vscode.Hover(markdown, range);
    }

    const symbols = await collectWorkspaceSymbols(document);
    const symbol = symbols.find(candidate => candidate.name === word);
    if (!symbol) {
      return undefined;
    }

    return new vscode.Hover(formatSymbolMarkdown(symbol), range);
  }
}

class IlcSignatureHelpProvider implements vscode.SignatureHelpProvider {
  async provideSignatureHelp(
    document: vscode.TextDocument,
    position: vscode.Position
  ): Promise<vscode.SignatureHelp | undefined> {
    const serverHelp = await languageServer?.signatureHelp(document, position);
    if (serverHelp) {
      return serverHelp;
    }

    const invocation = findInvocation(document, position);
    if (!invocation) {
      return undefined;
    }

    const candidates = (await collectWorkspaceSymbols(document))
      .filter(symbol =>
        symbol.name === invocation.name &&
        (symbol.kind === vscode.SymbolKind.Method ||
          symbol.kind === vscode.SymbolKind.Function ||
          symbol.kind === vscode.SymbolKind.Constructor));

    if (candidates.length === 0) {
      return undefined;
    }

    const help = new vscode.SignatureHelp();
    help.activeSignature = 0;
    help.activeParameter = Math.max(0, invocation.parameterIndex);
    help.signatures = candidates.map(symbol => {
      const signature = new vscode.SignatureInformation(symbol.signature, formatSymbolDocumentationMarkdown(symbol, true));
      signature.parameters = symbol.parameters.map(parameter => {
        const label = `${parameter.modifier ? `${parameter.modifier} ` : ''}${parameter.name}: ${parameter.type}`;
        return new vscode.ParameterInformation(label, parameter.documentation);
      });
      return signature;
    });

    logDebug(`signature name=${invocation.name} candidates=${help.signatures.length} activeParam=${help.activeParameter}`);
    return help;
  }
}

class IlcDocumentSymbolProvider implements vscode.DocumentSymbolProvider {
  async provideDocumentSymbols(document: vscode.TextDocument): Promise<vscode.DocumentSymbol[]> {
    const serverSymbols = await languageServer?.documentSymbols(document);
    if (serverSymbols) {
      return serverSymbols;
    }

    const symbols = parseIlcSymbols(document.getText(), document.uri);
    const documentSymbols = symbols.map(symbol => new vscode.DocumentSymbol(
      symbol.name,
      symbol.detail,
      symbol.kind,
      symbol.range,
      symbol.range));

    logDebug(`document symbols file=${document.fileName} count=${documentSymbols.length}`);
    return documentSymbols;
  }
}

class IlcDefinitionProvider implements vscode.DefinitionProvider {
  async provideDefinition(
    document: vscode.TextDocument,
    position: vscode.Position
  ): Promise<vscode.Definition | undefined> {
    const serverDefinition = await languageServer?.definition(document, position);
    if (serverDefinition) {
      return serverDefinition;
    }

    const range = document.getWordRangeAtPosition(position);
    if (!range) {
      return undefined;
    }

    const word = document.getText(range);
    const symbols = await collectWorkspaceSymbols(document);
    const matches = symbols
      .filter(symbol => symbol.name === word)
      .sort((left, right) => scoreDefinitionMatch(left, document) - scoreDefinitionMatch(right, document))
      .map(symbol => new vscode.Location(symbol.uri, symbol.range));

    logDebug(`definition word=${word} matches=${matches.length}`);
    return matches.length > 0 ? matches : undefined;
  }
}

class IlcCodeActionProvider implements vscode.CodeActionProvider {
  async provideCodeActions(
    document: vscode.TextDocument,
    range: vscode.Range
  ): Promise<vscode.CodeAction[] | undefined> {
    const serverActions = await languageServer?.codeActions(document, range);
    if (serverActions) {
      return serverActions;
    }

    return undefined;
  }
}

class IlcWorkspaceSymbolProvider implements vscode.WorkspaceSymbolProvider {
  async provideWorkspaceSymbols(query: string): Promise<vscode.SymbolInformation[]> {
    const serverSymbols = await languageServer?.workspaceSymbols(query);
    if (serverSymbols) {
      return serverSymbols;
    }

    const normalizedQuery = query.trim().toLowerCase();
    const symbols = await collectSymbolsFromWorkspace();
    const matches = symbols
      .filter(symbol => normalizedQuery.length === 0 || symbol.name.toLowerCase().includes(normalizedQuery))
      .slice(0, 500)
      .map(symbol => new vscode.SymbolInformation(
        symbol.name,
        symbol.kind,
        '',
        new vscode.Location(symbol.uri, symbol.range)));

    logDebug(`workspace symbol query="${query}" matches=${matches.length}`);
    return matches;
  }
}

function scoreDefinitionMatch(symbol: IlcSymbol, document: vscode.TextDocument): number {
  if (symbol.uri.fsPath === document.uri.fsPath) {
    return 0;
  }

  if (symbol.uri.fsPath.includes(`${path.sep}libs${path.sep}shipped${path.sep}`)) {
    return 2;
  }

  return 1;
}

function completionKindFromSymbolKind(kind: vscode.SymbolKind): vscode.CompletionItemKind {
  switch (kind) {
    case vscode.SymbolKind.Class:
    case vscode.SymbolKind.Interface:
    case vscode.SymbolKind.Enum:
      return vscode.CompletionItemKind.Class;
    case vscode.SymbolKind.Method:
      return vscode.CompletionItemKind.Method;
    case vscode.SymbolKind.Function:
    case vscode.SymbolKind.Constructor:
      return vscode.CompletionItemKind.Function;
    case vscode.SymbolKind.Property:
      return vscode.CompletionItemKind.Property;
    case vscode.SymbolKind.Field:
      return vscode.CompletionItemKind.Field;
    case vscode.SymbolKind.Constant:
      return vscode.CompletionItemKind.Constant;
    default:
      return vscode.CompletionItemKind.Text;
  }
}

async function collectWorkspaceSymbols(document: vscode.TextDocument): Promise<IlcSymbol[]> {
  return collectSymbolsFromWorkspace(document);
}

async function collectSymbolsFromWorkspace(document?: vscode.TextDocument): Promise<IlcSymbol[]> {
  const documents = new Map<string, { uri: vscode.Uri; text: string }>();
  if (document) {
    documents.set(document.uri.fsPath, { uri: document.uri, text: document.getText() });
  }

  const repoRoot = findRepoRoot(document);
  if (repoRoot) {
    const shippedDir = path.join(repoRoot, 'libs', 'shipped');
    if (fs.existsSync(shippedDir)) {
      for (const fileName of getShippedLibraryPaths(repoRoot, document?.fileName ?? '')) {
        documents.set(fileName, { uri: vscode.Uri.file(fileName), text: fs.readFileSync(fileName, 'utf8') });
      }
    }
  }

  const workspaceFiles = await vscode.workspace.findFiles('**/*.ilc', '**/{node_modules,build,tmp,out}/**', 200);
  for (const uri of workspaceFiles) {
    if (!documents.has(uri.fsPath)) {
      try {
        documents.set(uri.fsPath, { uri, text: fs.readFileSync(uri.fsPath, 'utf8') });
      } catch (error) {
        logDebug(`symbol read failed file=${uri.fsPath} error=${String(error)}`);
      }
    }
  }

  const symbols: IlcSymbol[] = [];
  for (const entry of documents.values()) {
    symbols.push(...parseIlcSymbols(entry.text, entry.uri));
  }

  logDebug(`workspace symbols=${symbols.length}`);
  return symbols;
}

function collectLocalSymbols(document: vscode.TextDocument, position: vscode.Position): IlcSymbol[] {
  const text = document.getText(new vscode.Range(new vscode.Position(0, 0), position));
  const symbols: IlcSymbol[] = [];
  const callablePattern = /\b(?:method|function|procedure)\s+[A-Za-z_][A-Za-z0-9_]*\s*\(([^)]*)\)|\bconstructor\s*\(([^)]*)\)/g;
  const parameterPattern = /\b(?:(?:out|ref)\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)/g;
  const variablePattern = /\bvar\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*([^:=;\n]+))?/g;

  for (const callable of text.matchAll(callablePattern)) {
    const parameterText = callable[1] ?? callable[2] ?? '';
    for (const parameter of parameterText.matchAll(parameterPattern)) {
      const name = parameter[1];
      const typeName = parameter[2];
      symbols.push({
        name,
        kind: vscode.SymbolKind.Variable,
        signature: `${name}: ${typeName}`,
        detail: `${name}: ${typeName}`,
        documentation: '',
        parameters: [],
        returnsDocumentation: '',
        uri: document.uri,
        range: new vscode.Range(position, position)
      });
    }
  }

  for (const match of text.matchAll(variablePattern)) {
    const name = match[1];
    const typeName = (match[2] ?? 'inferred').trim();
    symbols.push({
      name,
      kind: vscode.SymbolKind.Variable,
      signature: `${name}: ${typeName}`,
      detail: `${name}: ${typeName}`,
      documentation: '',
      parameters: [],
      returnsDocumentation: '',
      uri: document.uri,
      range: new vscode.Range(position, position)
    });
  }

  return symbols;
}

function parseIlcSymbols(text: string, uri: vscode.Uri): IlcSymbol[] {
  const symbols: IlcSymbol[] = [];
  const lines = text.split(/\r?\n/);
  let pendingDoc: string[] = [];

  for (let index = 0; index < lines.length; ++index) {
    const line = lines[index];
    const trimmed = line.trim();
    if (trimmed.startsWith('///')) {
      pendingDoc.push(trimmed.slice(3).trim());
      continue;
    }

    if (isIgnorableDocumentationSeparator(trimmed)) {
      continue;
    }

    if (trimmed.length === 0) {
      continue;
    }

    const symbol = parseDeclarationLine(line, uri, index, pendingDoc);
    if (symbol) {
      symbols.push(symbol);
    }

    pendingDoc = [];
  }

  return symbols;
}

function isIgnorableDocumentationSeparator(trimmedLine: string): boolean {
  if (trimmedLine.length === 0) {
    return true;
  }

  if (trimmedLine.startsWith('//')) {
    return true;
  }

  return /^\/[—\-]+$/.test(trimmedLine);
}

function parseDeclarationLine(line: string, uri: vscode.Uri, lineIndex: number, docLines: string[]): IlcSymbol | undefined {
  const normalized = line.trim();
  const typeMatch = normalized.match(/^(?:(?:public|private|protected|internal|static|abstract|sealed|partial)\s+)*(class|record|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)/);
  if (typeMatch) {
    const kind = typeMatch[1] === 'interface'
      ? vscode.SymbolKind.Interface
      : typeMatch[1] === 'enum'
        ? vscode.SymbolKind.Enum
        : vscode.SymbolKind.Class;
    return createSymbol(typeMatch[2], kind, normalized, normalized, uri, lineIndex, docLines, []);
  }

  const delegateMatch = normalized.match(/^(?:(?:public|private|protected|internal|static)\s+)*delegate\s+(?:function|procedure|method)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*\(([^)]*)\))?(?:\s*:\s*([^;]+))?/);
  if (delegateMatch) {
    return createSymbol(delegateMatch[1], vscode.SymbolKind.Function, normalized, normalized, uri, lineIndex, docLines, parseParameters(delegateMatch[2] ?? '', docLines));
  }

  const constructorMatch = normalized.match(/^(?:(?:public|private|protected|internal|static|extern)\s+)*constructor(?:\s*\(([^)]*)\))?/);
  if (constructorMatch) {
    return createSymbol('constructor', vscode.SymbolKind.Constructor, normalized, normalized, uri, lineIndex, docLines, parseParameters(constructorMatch[1] ?? '', docLines));
  }

  const methodMatch = normalized.match(/^(?:(?:public|private|protected|internal|static|extern|virtual|override|abstract|sealed|async)\s+)*(method|function|procedure)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*\(([^)]*)\))?(?:\s*:\s*([^;]+))?/);
  if (methodMatch) {
    const kind = methodMatch[1] === 'function' ? vscode.SymbolKind.Function : vscode.SymbolKind.Method;
    return createSymbol(methodMatch[2], kind, normalized, normalized, uri, lineIndex, docLines, parseParameters(methodMatch[3] ?? '', docLines));
  }

  const propertyMatch = normalized.match(/^(?:(?:public|private|protected|internal|static|virtual|override|abstract)\s+)*(?:default\s+)?property\s+([A-Za-z_][A-Za-z0-9_]*)(?:\[[^\]]*\])?\s*:\s*([^;{]+)?/);
  if (propertyMatch) {
    const typeName = (propertyMatch[2] ?? '').trim();
    return createSymbol(propertyMatch[1], vscode.SymbolKind.Property, normalized, `${propertyMatch[1]}: ${typeName}`, uri, lineIndex, docLines, []);
  }

  const fieldMatch = normalized.match(/^(?:(?:public|private|protected|internal|static|readonly)\s+)+(var|const)\s+([A-Za-z_][A-Za-z0-9_]*)(?:\s*:\s*([^:=;]+))?/);
  if (fieldMatch) {
    const kind = fieldMatch[1] === 'const' ? vscode.SymbolKind.Constant : vscode.SymbolKind.Field;
    const typeName = (fieldMatch[3] ?? 'inferred').trim();
    return createSymbol(fieldMatch[2], kind, normalized, `${fieldMatch[2]}: ${typeName}`, uri, lineIndex, docLines, []);
  }

  return undefined;
}

function createSymbol(
  name: string,
  kind: vscode.SymbolKind,
  signature: string,
  detail: string,
  uri: vscode.Uri,
  lineIndex: number,
  docLines: string[],
  parameters: IlcParameter[]
): IlcSymbol {
  const docs = parseDocumentation(docLines);
  return {
    name,
    kind,
    signature,
    detail,
    documentation: docs.summary,
    parameters: parameters.map(parameter => ({
      ...parameter,
      documentation: docs.params.get(parameter.name) ?? parameter.documentation
    })),
    returnsDocumentation: docs.returns,
    uri,
    range: new vscode.Range(new vscode.Position(lineIndex, 0), new vscode.Position(lineIndex, signature.length))
  };
}

function parseParameters(parameterText: string, docLines: string[]): IlcParameter[] {
  const docs = parseDocumentation(docLines);
  return parameterText
    .split(';')
    .map(value => value.trim())
    .filter(value => value.length > 0)
    .map(value => {
      const match = value.match(/^(?:(ref|out)\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.+)$/);
      if (!match) {
        return { name: value, type: '', modifier: '', documentation: docs.params.get(value) ?? '' };
      }

      return {
        modifier: match[1] ?? '',
        name: match[2],
        type: match[3].trim(),
        documentation: docs.params.get(match[2]) ?? ''
      };
    });
}

function parseDocumentation(docLines: string[]): { summary: string; params: Map<string, string>; returns: string } {
  const raw = docLines.join('\n');
  const params = new Map<string, string>();
  let summary = '';
  let returns = '';

  const summaryMatch = raw.match(/<summary>([\s\S]*?)<\/summary>/i);
  if (summaryMatch) {
    summary = cleanDocumentation(summaryMatch[1]);
  } else if (raw.length > 0) {
    summary = cleanDocumentation(raw);
  }

  const paramPattern = /<param\s+name=["']([^"']+)["']>([\s\S]*?)<\/param>/gi;
  for (const match of raw.matchAll(paramPattern)) {
    params.set(match[1], cleanDocumentation(match[2]));
  }

  const returnsMatch = raw.match(/<returns>([\s\S]*?)<\/returns>/i);
  if (returnsMatch) {
    returns = cleanDocumentation(returnsMatch[1]);
  }

  return { summary, params, returns };
}

function cleanDocumentation(value: string): string {
  return value
    .replace(/<see\s+cref=["']([^"']+)["']\s*\/>/gi, '$1')
    .replace(/<c>([\s\S]*?)<\/c>/gi, '`$1`')
    .replace(/<[^>]+>/g, '')
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(line => line.length > 0)
    .join(' ');
}

function formatSymbolMarkdown(symbol: IlcSymbol): vscode.MarkdownString {
  const markdown = new vscode.MarkdownString(undefined, true);
  markdown.appendCodeblock(symbol.signature, 'ilc');
  appendSymbolDocumentation(markdown, symbol, true);
  return markdown;
}

function formatSymbolDocumentationMarkdown(symbol: IlcSymbol, includeLocation: boolean): vscode.MarkdownString {
  const markdown = new vscode.MarkdownString(undefined, true);
  appendSymbolDocumentation(markdown, symbol, includeLocation);
  return markdown;
}

function appendSymbolDocumentation(markdown: vscode.MarkdownString, symbol: IlcSymbol, includeLocation: boolean): void {
  if (symbol.documentation.length > 0) {
    markdown.appendMarkdown(`${symbol.documentation}\n\n`);
  }

  for (const parameter of symbol.parameters) {
    if (parameter.documentation.length > 0) {
      markdown.appendMarkdown(`*@param* \`${parameter.name}\` ${parameter.documentation}\n\n`);
    }
  }

  if (symbol.returnsDocumentation.length > 0) {
    markdown.appendMarkdown(`*@returns* ${symbol.returnsDocumentation}\n\n`);
  }

  if (includeLocation) {
    markdown.appendMarkdown(`_${path.basename(symbol.uri.fsPath)}:${symbol.range.start.line + 1}_`);
  }
}

function isObject(value: unknown): value is Record<string, any> {
  return typeof value === 'object' && value !== null;
}

function asString(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

function asNumber(value: unknown, fallback: number): number {
  return typeof value === 'number' ? value : fallback;
}

function toVsCodeSymbolKind(value: unknown): vscode.SymbolKind {
  const lspKind = asNumber(value, 13);
  return Math.max(0, lspKind - 1) as vscode.SymbolKind;
}

function toVsCodeCompletionItemKind(value: unknown): vscode.CompletionItemKind {
  const lspKind = asNumber(value, 1);
  return Math.max(0, lspKind - 1) as vscode.CompletionItemKind;
}

function extractDocumentation(value: unknown): string | vscode.MarkdownString {
  if (isObject(value) && typeof value.value === 'string') {
    return new vscode.MarkdownString(value.value);
  }

  return asString(value);
}

function toVsCodeRange(value: unknown): vscode.Range {
  if (!isObject(value) || !isObject(value.start) || !isObject(value.end)) {
    return new vscode.Range(0, 0, 0, 0);
  }

  return new vscode.Range(
    asNumber(value.start.line, 0),
    asNumber(value.start.character, 0),
    asNumber(value.end.line, 0),
    asNumber(value.end.character, 0));
}

function toLspRange(range: vscode.Range): object {
  return {
    start: { line: range.start.line, character: range.start.character },
    end: { line: range.end.line, character: range.end.character }
  };
}

function toCodeAction(value: unknown): vscode.CodeAction {
  const action = new vscode.CodeAction(asString(isObject(value) ? value.title : ''), vscode.CodeActionKind.QuickFix);
  if (isObject(value) && isObject(value.edit)) {
    action.edit = toWorkspaceEdit(value.edit);
  }

  return action;
}

function toWorkspaceEdit(value: unknown): vscode.WorkspaceEdit | undefined {
  if (!isObject(value) || !isObject(value.changes)) {
    return undefined;
  }

  const edit = new vscode.WorkspaceEdit();
  for (const [uriText, changes] of Object.entries(value.changes)) {
    if (!Array.isArray(changes)) {
      continue;
    }

    const uri = vscode.Uri.parse(uriText);
    for (const change of changes) {
      if (!isObject(change)) {
        continue;
      }

      edit.replace(uri, toVsCodeRange(change.range), asString(change.newText));
    }
  }

  return edit;
}

function toVsCodeDiagnosticSeverity(value: number): vscode.DiagnosticSeverity {
  switch (value) {
    case 1:
      return vscode.DiagnosticSeverity.Error;
    case 2:
      return vscode.DiagnosticSeverity.Warning;
    case 3:
      return vscode.DiagnosticSeverity.Information;
    default:
      return vscode.DiagnosticSeverity.Hint;
  }
}

function toDocumentSymbol(value: Record<string, unknown>): vscode.DocumentSymbol {
  const symbol = new vscode.DocumentSymbol(
    asString(value.name),
    asString(value.detail),
    toVsCodeSymbolKind(value.kind),
    toVsCodeRange(value.range),
    toVsCodeRange(value.selectionRange ?? value.range));

  if (Array.isArray(value.children)) {
    symbol.children.push(...value.children.map(child => toDocumentSymbol(child)));
  }

  return symbol;
}

function findInvocation(document: vscode.TextDocument, position: vscode.Position): { name: string; parameterIndex: number } | undefined {
  const offset = document.offsetAt(position);
  const text = document.getText().slice(0, offset);
  let depth = 0;
  let openParen = -1;

  for (let index = text.length - 1; index >= 0; --index) {
    const char = text[index];
    if (char === ')') {
      ++depth;
    } else if (char === '(') {
      if (depth === 0) {
        openParen = index;
        break;
      }
      --depth;
    }
  }

  if (openParen < 0) {
    return undefined;
  }

  const beforeParen = text.slice(0, openParen).trimEnd();
  const nameMatch = beforeParen.match(/([A-Za-z_][A-Za-z0-9_]*)$/);
  if (!nameMatch) {
    return undefined;
  }

  const argumentsText = text.slice(openParen + 1);
  let parameterIndex = 0;
  let argumentDepth = 0;
  for (const char of argumentsText) {
    if (char === '(' || char === '[') {
      ++argumentDepth;
    } else if (char === ')' || char === ']') {
      argumentDepth = Math.max(0, argumentDepth - 1);
    } else if ((char === ',' || char === ';') && argumentDepth === 0) {
      ++parameterIndex;
    }
  }

  return { name: nameMatch[1], parameterIndex };
}

function getActiveIlcDocument(): vscode.TextDocument | undefined {
  const editor = vscode.window.activeTextEditor;
  if (!editor || !isIlcDocument(editor.document)) {
    vscode.window.showWarningMessage('Open an .ilc file first.');
    return undefined;
  }

  if (editor.document.isUntitled) {
    vscode.window.showWarningMessage('Save the .ilc file before using ILC commands.');
    return undefined;
  }

  return editor.document;
}

function isIlcDocument(document: vscode.TextDocument): boolean {
  return document.languageId === languageId || document.fileName.endsWith('.ilc');
}

function findRepoRoot(document?: vscode.TextDocument): string | undefined {
  const candidates: string[] = [];
  if (document && !document.isUntitled) {
    candidates.push(path.dirname(document.fileName));
  }

  for (const folder of vscode.workspace.workspaceFolders ?? []) {
    candidates.push(folder.uri.fsPath);
  }

  for (const candidate of candidates) {
    const root = findUp(candidate, current =>
      fs.existsSync(path.join(current, 'scripts', 'run-local-verification.sh')) &&
      fs.existsSync(path.join(current, 'src', 'ILC.Compiler.Cli', 'ILC.Compiler.Cli.csproj')));
    if (root) {
      return root;
    }
  }

  return undefined;
}

function findUp(start: string, predicate: (candidate: string) => boolean): string | undefined {
  let current = path.resolve(start);
  while (true) {
    if (predicate(current)) {
      return current;
    }

    const parent = path.dirname(current);
    if (parent === current) {
      return undefined;
    }

    current = parent;
  }
}

function getShippedLibraryPaths(repoRoot: string, activeFile: string): string[] {
  if (!getConfig().get<boolean>('includeShippedLibraries', true)) {
    return [];
  }

  const shippedDir = path.join(repoRoot, 'libs', 'shipped');
  if (!fs.existsSync(shippedDir)) {
    return [];
  }

  const preferredOrder = [
    'system.ilc',
    'diagnostics.ilc',
    'text.ilc',
    'json.ilc',
    'net.ilc',
    'threading.ilc',
    'collections.ilc',
    'ui.ilc',
    'ui-hosting.ilc',
    'ui-backends-qtquick.ilc'
  ];
  const activeResolved = path.resolve(activeFile);
  const shippedFiles = new Set(fs.readdirSync(shippedDir).filter(fileName => fileName.endsWith('.ilc')));
  const orderedFiles = [
    ...preferredOrder.filter(fileName => shippedFiles.has(fileName)),
    ...Array.from(shippedFiles).filter(fileName => !preferredOrder.includes(fileName)).sort()
  ];

  return orderedFiles
    .map(fileName => path.join(shippedDir, fileName))
    .filter(fileName => path.resolve(fileName) !== activeResolved);
}

function runScriptInTerminal(repoRoot: string, relativeScriptPath: string, terminalName: string, env: Record<string, string>): void {
  const scriptPath = path.join(repoRoot, relativeScriptPath);
  if (!fs.existsSync(scriptPath)) {
    vscode.window.showErrorMessage(`ILC script not found: ${scriptPath}`);
    return;
  }

  const envPrefix = Object.entries(env)
    .map(([key, value]) => `${key}=${quoteShell(value)}`)
    .join(' ');
  const terminal = vscode.window.createTerminal({ name: terminalName, cwd: repoRoot });
  terminal.show();
  terminal.sendText(`${envPrefix} ${quoteShell(scriptPath)}`);
}

function execFile(command: string, args: string[], cwd: string): Promise<CommandResult> {
  return new Promise((resolve, reject) => {
    childProcess.execFile(command, args, { cwd, maxBuffer: 1024 * 1024 * 16 }, (error, stdout, stderr) => {
      const result = { stdout, stderr };
      if (error) {
        reject(Object.assign(error, result));
        return;
      }

      resolve(result);
    });
  });
}

function appendProcessOutput(result: Partial<CommandResult>): void {
  if (result.stdout) {
    output.append(result.stdout);
  }

  if (result.stderr) {
    output.append(result.stderr);
  }
}

function delayUnlessCanceled(milliseconds: number, token: vscode.CancellationToken): Promise<boolean> {
  if (token.isCancellationRequested) {
    return Promise.resolve(false);
  }

  return new Promise(resolve => {
    const timeout = setTimeout(() => {
      subscription.dispose();
      resolve(!token.isCancellationRequested);
    }, milliseconds);
    const subscription = token.onCancellationRequested(() => {
      clearTimeout(timeout);
      subscription.dispose();
      resolve(false);
    });
  });
}

function getConfig(): vscode.WorkspaceConfiguration {
  return vscode.workspace.getConfiguration('ilc');
}

function resolveWorkspacePath(repoRoot: string, value: string | undefined): string {
  if (!value) {
    return repoRoot;
  }

  return path.isAbsolute(value) ? value : path.join(repoRoot, value);
}

function replaceExtension(fileName: string, extension: string): string {
  return path.join(path.dirname(fileName), `${path.basename(fileName, path.extname(fileName))}${extension}`);
}

function quoteShell(value: string): string {
  return `'${value.replace(/'/g, `'\\''`)}'`;
}

function quoteForLog(value: string): string {
  return value.includes(' ') ? `"${value}"` : value;
}

function logDebug(message: string): void {
  if (getConfig().get<boolean>('debugOutput', false)) {
    output.appendLine(`[ilc-ext] ${message}`);
  }
}
