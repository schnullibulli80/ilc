# ILC — I Love Coding

**ILC** is a modern, fully object-oriented programming language with **Pascal-oriented syntax** and **C#-class language capabilities**.

It is built for developers who still value readable code and explicit structure, but who do not want to give up modern language features, platform-independent execution, or security-aware packaging.

In one sentence:

> **ILC aims to bring Delphi-style readability back — with the power, consistency, and ambition of a modern language/runtime stack.**

---

## Why this project exists

ILC started from a very personal idea.

I learned programming with **Delphi in the 1990s**. I always preferred Delphi over C-oriented languages because the code felt more readable, more structured, and easier to reason about.

At some point, I had to move to **C#**, because Delphi no longer evolved in the direction I needed. And over time, I genuinely learned to love C#. Its type system, feature set, and overall language design are outstanding.

But one thing never really went away:
I still missed the readability and style of classic Delphi.

The problem was simple:
there was no Pascal-like language that offered the same modern strength, consistency, and expressiveness that C# had already made feel normal.

So instead of waiting for that language to appear, I started building it.

ILC is, in a way, an attempt to **revive the spirit of Delphi** — but this time properly modernized, fully object-oriented, platform-aware, and unapologetically ambitious.

With today's AI-assisted workflows, this is also no longer an impossible solo project. It is something that can be specified, built, tested, and iterated on in a realistic way.

And because this project comes from a genuine love of programming — and from a long-standing affection for Delphi — the name became obvious:

**I Love Coding**

**ILC**

---

## Project goals

ILC is designed around a clear set of goals:

- **Readable Pascal-style syntax**
- **Modern language power comparable to C#**
- **A clean, explicit, parser-friendly language design**
- **A custom platform-independent bytecode model**
- **A compact native runtime / vCPU**
- **Security-aware packaging and hardened execution options**
- **A serious foundation for cryptography-oriented numeric types**

This is not meant to be nostalgia as a product strategy.
It is meant to be a coherent language and runtime design.

---

## Design principles

ILC follows a few strict principles:

- **Readability over brevity**
- **Explicit structure over punctuation-heavy syntax**
- **Strong object orientation**
- **Modern features without sacrificing clarity**
- **Portable execution through stable bytecode and module formats**
- **Security features designed in early, not bolted on later**
- **Specification-first engineering**

---

## Language highlights

ILC deliberately adopts a number of proven ideas from C# while keeping a Pascal-oriented surface.

### Pascal-oriented syntax

ILC keeps a syntax style that favors clarity and structure, including:

- `begin ... end` blocks
- `:=` assignment
- `namespace` and `uses`
- `function`, `procedure`, and `method`
- readable declarations for types and members

### Modern features inspired by C#

ILC includes or specifies support for features such as:

- **Generics**
- **Operator overloading**
- **Properties**
- **Events**
- **Nullable types / nullability analysis**
- **Pattern matching**
- **Lambdas**
- **Extension methods**
- **Records**
- **Tuples**
- **Async / await**
- **Iterators and `yield`**
- **Interfaces, inheritance, overload resolution, and virtual dispatch**
- **Strong typing with practical type inference**

### Example

```ilc
namespace Demo.App;

uses System;

public class Program
begin
  public static method Main(args: array of String): Integer;
  begin
    var name := 'World';
    Console.WriteLine($'Hello, {name}!');
    return 0;
  end;
end;
```

---

## Architecture at a glance

ILC is not intended to be “just a transpiler”.
It has its own execution model and its own runtime architecture.

### Planned implementation split

- **Compiler frontend and toolchain in C#**
- **Runtime / vCPU in C++**

This split is intentional:

- C# is a strong fit for compiler tooling, analysis, and iteration speed
- C++ is a strong fit for a compact, high-performance native runtime

### Execution pipeline

```text
ILC Source
  -> Lexer / Parser
  -> AST
  -> Semantic Model
  -> Lowered IR
  -> ILC Bytecode
  -> .ilb Module
  -> vCPU Loader
  -> Execution
```

### Core execution model

ILC is currently specified around:

- a **custom platform-independent bytecode**
- a **register-based virtual machine**
- a binary module format called **`.ilb`**
- an optional secure package format called **`.ilcp`**
- a compact native runtime with type materialization, allocation, dispatch, exceptions, and GC

### Runtime baseline for 1.0

The initial runtime direction is intentionally focused:

- **64-bit only**
- **stable runtime type descriptors**
- **managed objects, arrays, strings, and statics**
- **boxed and unboxed value semantics**
- **a simple non-moving mark-and-sweep GC as the recommended first implementation**

---

## Platform independence

ILC targets platform independence at the **bytecode level**.

That means:

- the **same `.ilb` module** should be runnable across supported targets
- the **runtime itself is built natively per platform**
- runtime semantics must stay consistent across hosts

Initial target platforms are:

- **Windows x64**
- **Linux x64**
- **Linux ARM64**
- **macOS ARM64**

The goal is portability without dragging in a heavyweight execution model.

---

## Secure packaging and cryptography

Cryptography is not an afterthought in ILC.
It is a first-class architectural concern.

### Secure packaging profiles

ILC currently defines three protection profiles:

- **S** — signed bytecode
- **SE** — signed + encrypted bytecode
- **SEH** — signed + encrypted + encoded execution hardening

### Security direction

The secure packaging design includes support for:

- signed packages
- AEAD-encrypted code segments
- separated key roles for signing, encryption, and execution encoding
- optional hardened execution with encoded internal execution domains

Recommended algorithm families currently include:

- **AES-256-GCM**
- **ChaCha20-Poly1305**
- **Ed25519**
- **ECDSA P-256**
- **RSA-PSS 3072+**

### Important reality check

ILC does **not** pretend that software protection can make code magically impossible to analyze.
The goal is to **raise the cost of analysis**, strengthen distribution scenarios, and provide a serious hardening model for shipped applications and modules.

That distinction matters.
This project is interested in engineering, not fairy tales.

---

## Cryptographic integer roadmap

ILC also aims to provide a stronger built-in foundation for crypto-related numerics.

Instead of starting with a huge, all-purpose arbitrary-precision surface, the current direction focuses on **fixed-width wide integers**, including:

- `UInt128`
- `UInt256`
- `UInt512`
- `UInt1024`
- `UInt2048`

The first implementation slice is **`UInt128`**, with proper support across:

- parser and binder
- IR
- bytecode
- runtime primitives
- standard library surface

Longer term, this opens the door for:

- modular arithmetic helpers
- cryptographic utility types
- security-aware numeric routines
- constant-time variants where practical

---

## Why open source still makes sense

Yes, ILC includes secure packaging and hardened execution concepts.
And yes, that raises an obvious question:

**Does open sourcing such a project still make sense?**

I believe the answer is **yes**.

Open source and secure packaging are not contradictions.
They solve different problems.

- **Open source** helps with trust, reviewability, collaboration, auditability, and long-term ecosystem growth.
- **Secure packaging** helps protect shipped applications, signed modules, distribution models, and hardened runtime scenarios.

In other words:

- the compiler and runtime can be open
- the specs can be open
- the implementation can be auditable
- and shipped applications can still opt into signing, encryption, and hardened execution profiles

If anything, openness may actually strengthen this project:
security-sensitive designs benefit from scrutiny much more than from wishful secrecy.

---

## Current status

ILC is currently in a **specification-first phase**.

The project already has substantial design material, including:

- a language reference
- a formal grammar (EBNF / ANTLR)
- separate lexer and parser grammars
- a bytecode specification
- a `.ilb` container specification
- a runtime object model specification
- a secure packaging and hardened execution specification
- a numeric and cryptographic integer specification

So while ILC is still early as an implementation project, it is already much more than “just an idea for a language”.

---

## Roadmap

A realistic implementation path looks like this:

1. **Compiler skeleton + runtime skeleton**
2. **Scalar execution**
3. **Objects and type system basics**
4. **Arrays and collections**
5. **Exceptions + debug information**
6. **Generics + broader language surface**
7. **Packaging, hardening, and runtime maturation**

The idea is simple:
make it real first, then make it broad, then make it tougher.

---

## Documentation

- [Architecture](docs/architecture.md)
- [Language overview](docs/language-overview.md)
- [Runtime and bytecode](docs/runtime-and-bytecode.md)
- [Standard library](docs/standard-library.md)
- [Building and testing](docs/building-and-testing.md)
- [Benchmarks](docs/benchmarks.md)
- [Roadmap](docs/roadmap.md)
- [Contributing](docs/contributing.md)
- [FAQ](docs/faq.md)

---

## Repository structure

A repository layout in this direction is recommended:

```text
/ilc
  /benchmarks
  /docs
  /libs
  /runtime
      /ilcvm_cli
      /ilcvm_core
      /ilcvm_host
      /ilcvm_tests
  /scripts
  /src
    /ILC.Compiler.Binding
    /ILC.Compiler.Bytecode
    /ILC.Compiler.Cli
    /ILC.Compiler.Core
    /ILC.Compiler.Lowering
    /ILC.Compiler.Syntax
  /tests
    /ILC.Compiler.Tests
```

This keeps the split between language tooling, runtime execution, specifications, tests, and examples easy to understand.

---

## What makes ILC different

ILC is trying to combine a set of qualities that are rarely pursued together:

- **Pascal-style readability**
- **C#-class expressive power**
- **a custom platform-neutral bytecode**
- **a compact native vCPU runtime**
- **security-aware packaging and hardened execution**
- **crypto-oriented numeric evolution**

ILC is not trying to be trendy.
It is trying to be coherent.

---

## Who this project is for

ILC will likely be interesting to developers who:

- grew up with Delphi or Object Pascal
- appreciate C# and modern language design
- miss readable syntax and structured code blocks
- care about portable execution formats
- are interested in secure software delivery models
- enjoy language/runtime projects with ambitious architecture

If you have ever thought:

> “I wish Delphi had kept evolving and learned everything modern C# knows.”

then you already understand the basic idea behind ILC.

---

## Contributing

Contributions follow [docs/contributing.md](docs/contributing.md).

---

## License

**License: Apache 2.0**

This project is licensed under the Apache License, Version 2.0. See [LICENSE.txt](LICENSE.txt).

---

## Long-term vision

The long-term goal is not just to recreate old syntax.

The goal is to build a language and runtime that are:

- pleasant to read
- serious to implement
- practical to run
- portable by design
- security-aware where it matters
- open to future JIT and/or AOT strategies without requiring them for 1.0

ILC is a love letter to readable programming.
But it is also a real engineering project.

---

## Final note

Readable code still matters.
Modern language design still matters.
And powerful tooling does not need to look like punctuation soup.

ILC exists because there is still room for a language that takes those ideas seriously.

And because sometimes the right response to a dead ecosystem is not nostalgia.
Sometimes the right response is to build the thing you wish still existed.
