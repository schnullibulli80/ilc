# ILC - Installation & Development Guide

## Project Structure

```
vscode-ilc/
├── syntaxes/
│   └── ilc.tmLanguage.json      # TextMate grammar definition
├── src/
│   └── extension.ts              # Main extension code
├── test-files/
│   └── comprehensive-example.ilc # Test file with ILC syntax examples
├── .vscode/
│   ├── launch.json              # VS Code debug configuration
│   ├── tasks.json               # Build tasks
│   └── extensions.json          # Recommended extensions
├── package.json                 # Extension metadata
├── language-configuration.json  # Language settings (brackets, indentation)
├── tsconfig.json                # TypeScript configuration
└── README.md                    # User documentation
```

## Quick Start

### 1. Install Dependencies
```bash
cd vscode-ilc
npm install
```

### 2. Compile TypeScript
```bash
npm run compile
```

### 3. Test the Extension

In VS Code:
1. Open this folder in VS Code: `code vscode-ilc`
2. Press `F5` to launch the Extension Development Host
3. A new VS Code window will open with the extension loaded
4. Open `test-files/comprehensive-example.ilc` to see syntax highlighting in action
5. Open the ILC repository root in the Extension Development Host if you want compiler-backed commands and diagnostics

### 4. Build for Release
```bash
npm run vscode:prepublish
```

### 5. Package and Install Locally

From `vscode-ilc`:

```bash
npx vsce package
code --install-extension ilc-0.1.0.vsix --force
```

Then reload VS Code:

```text
Developer: Reload Window
```

## Features

### Syntax Highlighting
- **Keywords**: namespace, class, method, if, while, for, etc.
- **Types**: User-defined types follow PascalCase naming
- **String Literals**: Single-quoted, double-quoted, raw, and interpolated
- **Numbers**: Decimal, hexadecimal ($), binary (%), octal (&)
- **Comments**: Line (//) and block ((* *)) comments
- **Operators**: All arithmetic, logical, comparison, and special operators
- **Attributes**: [AttributeName] syntax highlighting

### Language Features
- **Auto-closing pairs**: Brackets, quotes, braces
- **Bracket matching**: Proper nesting detection
- **Smart indentation**: Automatic indentation for code blocks
- **Comment support**: Both line and block comments recognized

### Compiler-backed integration

The extension currently provides the first non-LSP integration layer:

- `ILC: Compile Current File`
- `ILC: Run Current File`
- `ILC: Refresh Diagnostics`
- `ILC: Run Local Verification`
- `ILC: Run QtQuick Smoke`
- `ILC: Show Output`
- keyword, built-in type, local variable, and workspace-symbol completions
- snippets from `snippets/ilc.json`
- hover documentation from `/// <summary>`, `/// <param>`, and `/// <returns>`
- signature help for method/function calls
- document symbols for Outline and Breadcrumbs
- workspace symbol search
- go-to-definition by exact symbol name

Diagnostics are produced by invoking:

```bash
dotnet run --project src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj -- --debug <active-file> libs/shipped/*.ilc
```

The extension parses compiler diagnostics of the form:

```text
file.ilc(line,column): error ILC0000: message
```

Set `ilc.debugOutput` to `true` to log command arguments, repository-root
detection, and diagnostic counts to the `ILC` output channel.

### Language server boundary

The experimental language server lives in:

```text
src/ILC.LanguageServer
```

It speaks a minimal stdio-based LSP subset and reuses:

- `ILC.Compiler.Syntax`
- `ILC.Compiler.Binding`
- `ILC.Compiler.Core`

The VS Code extension starts it with:

```bash
dotnet run --project src/ILC.LanguageServer/ILC.LanguageServer.csproj -- --workspace <repo-root>
```

With `ilc.debugOutput = true`, the extension appends `--debug` so the language
server can report extra state through LSP `window/logMessage` notifications
without writing to stdout.

Before testing the extension against a changed language server, build it from
the repository root:

```bash
dotnet build src/ILC.LanguageServer/ILC.LanguageServer.csproj --no-restore
```

Disable it with `ilc.languageServer.enabled := false` to force the TypeScript
fallback providers.

Do not reimplement the ILC parser or binder in TypeScript for advanced editor
features. Completion, hover, go-to-definition, rename, semantic highlighting,
and richer diagnostics should move into this language server.

Current semantic lookup in the language server includes transitive imported
namespace filtering for workspace symbols, nested document symbols for Outline,
local variable declarations with heuristic type inference, member access lookup
through locals/static types/enums, `self`, cast receivers, array/indexer
receivers, namespace prefixes, and inherited members. Completion is
context-aware: member access returns only members for the resolved receiver
type, `new` returns constructible classes, type-name contexts return known types
plus built-ins, and normal word completion combines scoped locals with visible
global symbols.
Signature help resolves member invocations through the receiver type, so
`window.SetMinimumSize(...)` is matched against `Window` methods instead of all
methods with the same name. Constructor calls such as `new Task<Integer>(...)`
and default indexers such as `dictionary[...]` are handled as signature-help
targets too.
Code actions currently provide a first quick fix for missing imports: if the
identifier under the cursor is known in another workspace namespace, the server
offers `Add uses <namespace>`.
The same missing-import scan publishes `ILC1001` diagnostics so the lightbulb is
discoverable without manually invoking Quick Fix on an unmarked identifier.
Editor diagnostics include parser diagnostics plus Binder diagnostics from the
same merge model as the compiler CLI: active document plus imported workspace
namespaces. The server filters the result back to the active document before
publishing diagnostics.

### Resolver Behavior

The language server is intentionally still lighter than the compiler Binder.
It builds a workspace symbol model and resolves common editor scenarios without
running a full semantic analysis for every hover/completion request.

Currently covered:

- local and parameter symbols, including `in`, `out`, `ref`, and `params`
  modifiers in signatures and hover text;
- explicit local types, `new` expressions, casts, string/integer/boolean
  literals, boolean expressions, `is`, `??`, simple member access, simple method
  calls, arrays, and indexers for `var` inference;
- `self` hover and member completion against the enclosing class;
- static type/member access such as `System.Console.WriteLine(...)`;
- generic receiver substitution for common collection patterns;
- array `Length` and array indexer hints, including multi-dimensional arrays;
- default property/indexer hints for types such as `Dictionary<TKey, TValue>`.

Known limits:

- overload resolution is intentionally approximate;
- complex expression trees, lambdas, query expressions, chained arithmetic, and
  flow-sensitive null/type facts can still produce `inferred`;
- local scoping is line-oriented and can over-approximate symbols from nearby
  blocks;
- the resolver should eventually share a Binder-backed expression type model
  instead of accumulating individual heuristics.

With `ilc.debugOutput = true`, the language server logs cache invalidations,
diagnostic scheduling/cancellation, import-cache hits, completion contexts,
signature candidates, and resolver-relevant counts to the `ILC` output channel.
Keep new resolver work similarly diagnosable so regressions can be inspected
from user-provided logs.

The current TypeScript symbol index is intentionally a fallback bridge:

- it scans `.ilc` files and shipped libraries with line-based declaration patterns;
- it is good enough for first completions, hover, signature help, Outline, workspace symbols, and simple go-to-definition;
- it should not become the long-term semantic source of truth.

### Reload rules

- C# language server only changed: rebuild `src/ILC.LanguageServer`, then reload VS Code.
- TypeScript extension client changed: run `npm run vscode:prepublish`, rebuild the VSIX, reinstall it, then reload VS Code.
- Grammar/snippet/package metadata changed: rebuild and reinstall the VSIX.
- If behavior looks stale, first check the `ILC` output channel with `ilc.debugOutput = true`.

## Publishing to VS Code Marketplace

### Prerequisites
- VS Code CLI (`vsce`) installed: `npm install -g @vscode/vsce`
- Personal Access Token from https://dev.azure.com/

### Steps
1. Update version in `package.json`
2. Update `CHANGELOG.md`
3. Create package:
   ```bash
   vsce package
   ```
4. Publish:
   ```bash
   vsce publish
   ```

## Customization

### Adding New Keywords
Edit `syntaxes/ilc.tmLanguage.json` and add to the appropriate pattern in the `keywords` repository.

### Modifying Colors
Colors are determined by your VS Code theme. To customize, add to your `settings.json`:
```json
"editor.tokenColorCustomizations": {
  "textMateRules": [
    {
      "scope": "keyword.control.ilc",
      "settings": {
        "foreground": "#0000FF"
      }
    }
  ]
}
```

### Adding Theme Support
Create a `.json` file in the `themes/` directory with theme definitions.

## Troubleshooting

### Syntax highlighting not working
1. Ensure file extension is `.ilc`
2. Check `language-configuration.json` is valid JSON
3. Verify `package.json` contributes section correctly references the grammar

### Debug the grammar
1. Use TextMate scope inspector (built into VS Code)
2. Press Ctrl+Shift+P and search "Developer: Inspect Editor Tokens and Scopes"
3. Position cursor on problematic code to see applied scopes

## Development Tips

- **Test incrementally**: Edit `syntaxes/ilc.tmLanguage.json`, save, and reload window
- **Use the test file**: `comprehensive-example.ilc` covers all major syntax features
- **Check regex patterns**: VS Code uses ECMA regular expressions in grammars
- **Reference**: [TextMate Grammar Documentation](https://macromates.com/manual/en/language_grammars)

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes and test thoroughly
4. Submit a pull request

## Resources

- [VS Code Extension API](https://code.visualstudio.com/api)
- [TextMate Language Grammars](https://macromates.com/manual/en/language_grammars)
- [ANTLR Grammar](../ILCLexer.g4, ../ILCParser.g4)
- [ILC Language Documentation](../README.md)
