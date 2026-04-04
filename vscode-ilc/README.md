# ILC

Full language support for ILC (Integrated Language Compiler) including syntax highlighting, IntelliSense, debugging, and more.

## Features

- **Syntax Highlighting** for all ILC language constructs
- **IntelliSense** support (coming soon)
- **Debugging** capabilities (coming soon)
- **Code Navigation** and Outline view (coming soon)
- **Full Language Server** integration (planned)

## Installation

1. Open VS Code
2. Go to Extensions (Ctrl+Shift+X)
3. Search for "ILC Syntax Highlighter"
4. Click Install

## Usage

The highlighter automatically activates for files with `.ilc` extension.

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

## License

Apache-2.0 

## Contributing

Issues and pull requests are welcome on the GitHub repository.
