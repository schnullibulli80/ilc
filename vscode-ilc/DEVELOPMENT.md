# ILC - Installation & Development Guide

## Project Structure

```
vscode-ilc-highlighter/
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
cd vscode-ilc-highlighter
npm install
```

### 2. Compile TypeScript
```bash
npm run compile
```

### 3. Test the Extension

In VS Code:
1. Open this folder in VS Code: `code vscode-ilc-highlighter`
2. Press `F5` to launch the Extension Development Host
3. A new VS Code window will open with the extension loaded
4. Open `test-files/comprehensive-example.ilc` to see syntax highlighting in action

### 4. Build for Release
```bash
npm run vscode:prepublish
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
