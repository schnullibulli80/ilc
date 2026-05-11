# ILC

Full language support for ILC (Integrated Language Compiler) including syntax highlighting, IntelliSense, debugging, and more.

## Features

- **Syntax Highlighting** for all ILC language constructs
- **Compiler Diagnostics** in the VS Code Problems panel, backed by the real ILC compiler
- **Completions** for keywords, built-in types, document/workspace symbols, and local variables
- **Snippets** for common ILC declarations and XML documentation comments
- **Hover Help** for symbols with `/// <summary>`, `/// <param>`, and `/// <returns>` documentation
- **Signature Help** for method/function calls and indexers through `Ctrl+Shift+Space`
- **Outline / Breadcrumb Symbols** for classes, interfaces, enums, methods, functions, properties, fields, and constants
- **Go to Definition** for symbols found by the lightweight workspace index
- **Workspace Symbol Search** through VS Code's symbol search
- **Build / Run Commands** for the active `.ilc` file
- **Workspace Commands** for local verification and the QtQuick UI smoke
- **Experimental Language Server** backed by the ILC compiler libraries
- **Debugging** capabilities (planned through a VS Code debug adapter)
- **Full semantic language server expansion** (ongoing)

## Installation

From the repository root:

```bash
dotnet build src/ILC.LanguageServer/ILC.LanguageServer.csproj --no-restore
cd vscode-ilc
npm install
npm run vscode:prepublish
npx vsce package
code --install-extension ilc-0.1.0.vsix --force
```

Reload VS Code after installing:

```text
Developer: Reload Window
```

When only the C# language server changes, rebuilding the language server and
reloading VS Code is usually enough. When `vscode-ilc/src/extension.ts`,
`package.json`, snippets, or grammar files change, rebuild and reinstall the
VSIX.

## Usage

The extension automatically activates for files with `.ilc` extension.

Open the Command Palette and use:

- `ILC: Compile Current File`
- `ILC: Run Current File`
- `ILC: Refresh Diagnostics`
- `ILC: Run Local Verification`
- `ILC: Run QtQuick Smoke`
- `ILC: Show Output`

Diagnostics are refreshed on open and save by default. The extension invokes the
repository compiler CLI and parses its diagnostics into the Problems panel.

Completion, hover, and signature help are provided by the experimental language
server when it is enabled. Public XML-style ILC documentation comments such as
`/// <summary>...</summary>` are surfaced in hover and signature help.

The extension now starts the experimental compiler-backed language server by
default. If it cannot start, the lightweight TypeScript symbol index remains as
a fallback for completion, hover, signature help, Outline, Go to Definition, and
Workspace Symbol Search.

Current language-server lookup covers:

- transitive `uses` imports for diagnostics and symbol visibility;
- nested Outline symbols for types and members;
- hover and go-to-definition for globals, members, locals, parameters, `self`,
  namespace prefixes, and simple receiver chains;
- member completion for locals, fields, static types, cast receivers, `self`,
  generic receiver types, arrays, and indexer results;
- signature help for methods, functions, constructors, and default indexers;
- generic member substitution for common cases such as
  `Dictionary<String, Integer>.Item[key: String]: Integer`;
- local `var` type inference for explicit types, `new`, casts, string/integer/
  boolean literals, boolean operators, `is`, `??`, member access, simple calls,
  arrays, and indexers;
- compiler-backed diagnostics, missing-uses diagnostics, and quick fixes.

Known limitations:

- expression type resolution is still heuristic, not a full Binder-backed
  semantic model;
- complex expressions, overload selection, nested generics, lambdas, query
  expressions, and flow-sensitive types can still fall back to `inferred`;
- local scope handling is line-based and not yet a full block-scope model;
- some compiler diagnostics are filtered in the language server when the editor
  resolver intentionally supports a construct that the current Binder diagnostic
  path reports too broadly.

To check that the language server is running:

1. set `ilc.debugOutput` to `true`;
2. open `View` -> `Output`;
3. select `ILC` in the output dropdown;
4. look for `language server initialized` and diagnostics messages.

### Example

```ilc
namespace MyApp;

public class Calculator
    method Add(a: Int32, b: Int32) -> Int32
    begin
        return a + b;
    end;

    method Main()
    begin
        var result := Add(5, 3);
        // result is now 8
    end;
end;
```

## Features Supported

- **Namespaces & Imports**: `namespace`, `uses`
- **Type Declarations**: `class`, `struct`, `record`, `interface`, `enum`, `delegate`
- **Modifiers**: `public`, `private`, `protected`, `internal`, `static`, `async`, `override`, etc.
- **Member Declarations**: `method`, `function`, `procedure`, `property`, `event`, `operator`
- **Control Flow**: `if/then/else`, `case/of`, `match/with`, `while`, `for/foreach`, `repeat/until`
- **Exception Handling**: `try/except`, `finally`, `raise/throw`
- **Keywords**: `new`, `typeof`, `nameof`, `await`, `yield`, `var`, `const`, `ref`, `out`, `params`
- **Operators**: All arithmetic, logical, comparison, and special operators
- **Literals**: Integer (decimal, hex, binary, octal), float, boolean, nil, strings
- **Comments**: Line comments (`//`) and block comments (`(* *)`)
- **XML Documentation**: `/// <summary>`, `/// <param>`, and `/// <returns>` are used for hover and signature help

## Settings

- `ilc.compiler.project`: compiler CLI project path relative to the workspace root
- `ilc.runtime.executable`: VM CLI executable path relative to the workspace root
- `ilc.includeShippedLibraries`: include `libs/shipped/*.ilc` for active-file compiles
- `ilc.diagnostics.onSave`: refresh compiler diagnostics when an `.ilc` file is saved
- `ilc.diagnostics.onOpen`: refresh compiler diagnostics when an `.ilc` file is opened
- `ilc.debugOutput`: write detailed extension and language-server diagnostics to the ILC output channel
- `ilc.languageServer.enabled`: start the experimental compiler-backed language server
- `ilc.languageServer.project`: language server project path relative to the workspace root

The extension detects the ILC repository root by looking for
`scripts/run-local-verification.sh` and `src/ILC.Compiler.Cli/ILC.Compiler.Cli.csproj`.

## Troubleshooting

If IntelliSense, hover, Outline, or Go to Definition behave like the older
fallback implementation:

1. Make sure `ilc.languageServer.enabled` is `true`.
2. Make sure `dotnet build src/ILC.LanguageServer/ILC.LanguageServer.csproj --no-restore` succeeds.
3. Make sure VS Code quick suggestions are enabled for code:
   `"editor.quickSuggestions": { "other": true, "comments": false, "strings": false }`.
4. Reinstall the VSIX if the TypeScript client changed.
5. Run `Developer: Reload Window`.
6. Check the `ILC` output channel with `ilc.debugOutput = true`.

When only `src/ILC.LanguageServer` changed, a VSIX rebuild is not required:

```bash
dotnet build src/ILC.LanguageServer/ILC.LanguageServer.csproj --no-restore
```

Then run `Developer: Reload Window`. Rebuild and reinstall the VSIX only when
the TypeScript client, grammar, snippets, or extension metadata changed.

If the Outline is flat after a client-side change, rebuild and reinstall the
VSIX. VS Code does not pick up changes in `src/extension.ts` from the repository
unless the extension is running in an Extension Development Host.

## License

Apache-2.0 

## Contributing

Issues and pull requests are welcome on the GitHub repository.
