# ILC Architecture

This document describes the current architecture of the ILC project as it exists in the repository, together with the intended architectural direction where the implementation is already clearly defined.

It is written for contributors who want to understand how the system is split, how source code moves through the compiler and runtime, and where new language or runtime features should be implemented.

## 1. Architectural Overview

ILC is structured as a language toolchain and execution stack with a deliberate split:

- the compiler and tooling are implemented in C#
- the runtime virtual machine is implemented in C++
- execution targets a custom platform-independent bytecode format
- shipped library surface is expressed in ILC wherever practical

At a high level, the project currently follows this pipeline:

```text
ILC source
  -> lexer and parser
  -> syntax tree
  -> binding and semantic model
  -> lowered IR
  -> bytecode module emission
  -> ILB module
  -> native ILC VM loader
  -> execution
```

This is not a transpiler architecture. ILC has its own syntax model, semantic model, intermediate representation, bytecode layer, module format, and runtime execution engine.

## 2. Repository Layout

The repository is organized around the execution pipeline.

- `src/`
  - compiler projects in C#
- `runtime/`
  - virtual machine, host services, loader, CLI, and runtime tests in C++
- `libs/shipped/`
  - ILC standard library surface shipped with the toolchain
- `tests/`
  - compiler-focused bootstrap tests
- `benchmarks/`
  - ILC versus Java performance benchmarks and runner scripts
- `scripts/`
  - local verification and developer workflow scripts
- `tmp/`
  - generated local fixtures and logs used during development
- `docs/`
  - long-form project documentation

## 3. Compiler Architecture

The compiler is split into several projects with explicit responsibilities.

### 3.1 `ILC.Compiler.Core`

This project contains shared compiler primitives and common infrastructure used by the higher compiler layers.

Typical responsibilities:

- text spans and source positions
- diagnostics and error reporting primitives
- shared utility abstractions

This project should remain small and dependency-light because every higher compiler layer builds on it.

### 3.2 `ILC.Compiler.Syntax`

This project contains lexing, parsing, tokenization, and syntax tree construction.

Current responsibilities include:

- tokenizing source text into syntax tokens
- building typed syntax nodes
- parsing declarations, statements, expressions, patterns, and type references
- preserving source structure for diagnostics and later semantic work

The syntax layer is intentionally rich. It models language structure rather than collapsing too early into semantic concepts. That is important for:

- precise diagnostics
- later source-aware tooling
- incremental feature work
- lowering decisions that depend on original syntax shape

Examples of implemented syntax concerns already present in the codebase:

- `namespace` and `uses`
- classes, records, enums, properties, constructors
- array syntax and slicing
- `match` and `case`
- `foreach`
- `params`
- `not in`
- unary expressions and parenthesized expressions

### 3.3 `ILC.Compiler.Binding`

This project turns syntax into semantic meaning.

Current responsibilities include:

- symbol creation for types, methods, fields, properties, parameters, locals, and constants
- built-in type recognition
- type checking and type inference
- method and member resolution
- operator validation
- property/indexer validation
- constant value analysis where supported
- control-flow and feature-specific semantic checks

This layer is where the language becomes a real typed system rather than just structured syntax.

Architecturally, this is the most important correctness layer in the compiler. New language features should generally become valid here before they are lowered.

The binder already carries some forward-looking preparation for future numeric expansion, including centralized built-in type resolution and a built-in integer-family model that can later host wider integer types.

### 3.4 `ILC.Compiler.Lowering`

This project translates bound language constructs into a simpler intermediate representation.

The lowering layer exists to remove high-level surface complexity before bytecode emission. It is the main desugaring stage of the compiler.

Current responsibilities include:

- normalizing complex expressions into explicit temporaries
- lowering high-level control flow into explicit branch structure
- rewriting properties and indexers into lower-level access patterns
- turning `foreach`, `match`, `case`, and similar features into simpler decision and loop forms
- materializing array accesses and object interactions explicitly
- preparing explicit call frames and argument packing

ILC’s lowering stage is intentionally substantial. This keeps the bytecode emitter simpler and makes runtime semantics easier to reason about.

### 3.5 `ILC.Compiler.Bytecode`

This project translates lowered IR into the executable bytecode model and serializes ILB modules.

Current responsibilities include:

- defining the VM opcode model
- mapping lowered IR instructions to bytecode instructions
- assigning function, type, field, and string identifiers
- serializing bytecode and metadata into the ILB format
- emitting module-level metadata such as array shape and exception handler information

The bytecode layer is where compiler output stops being compiler-internal and becomes a stable execution artifact.

Recent architectural groundwork in this area includes:

- explicit `InstructionImmediateKind`
- separation between inline immediates and table-indexed immediates
- preparation for a future constant table

That matters because later numeric work, especially wider integers, should not be forced through today’s `int`-centric immediate assumptions.

### 3.6 `ILC.Compiler.Cli`

This project is the command-line compiler entry point.

Current responsibilities include:

- reading source files
- merging imported sources based on `uses`
- compiling source into bytecode modules
- acting as the main local developer entry point for compilation

The CLI is intentionally thin. Most logic belongs in the lower compiler layers so it remains reusable and testable.

### 3.7 `ILC.Compiler.Tests`

The compiler tests are currently a strong bootstrap-style verification layer. They do not just unit test isolated methods; they validate end-to-end compiler behavior over representative source fixtures.

Current test responsibilities include:

- parser and binder sanity
- lowering and bytecode expectations
- feature-specific regression checks
- semantic metadata preservation
- emitted opcode and module shape verification

This test project is effectively the main specification harness for implemented language features.

## 4. Runtime Architecture

The runtime is implemented in C++ and is structured around a compact interpreter-based virtual machine.

### 4.1 `ilcvm_core`

This is the heart of execution.

Current responsibilities include:

- module decoding and metadata access
- VM execution loop
- register-based instruction dispatch
- object and array operations
- field and static access
- method and virtual call handling
- exception propagation
- execution profiling and performance instrumentation

The runtime is currently interpreter-based. That is an explicit architectural choice for the current phase. There is no JIT in the current design baseline.

### 4.2 `ilcvm_host`

This layer provides host services behind a clean abstraction.

Current responsibilities include:

- console I/O
- clock/time access
- environment variable access
- filesystem access
- path utilities
- process-facing host behavior used by shipped library imports

The core VM should not directly depend on platform-specific APIs. Host interaction goes through a host services abstraction so the execution engine remains portable and testable.

### 4.3 `ilcvm_loader`

This layer is responsible for loading ILB modules and presenting decoded runtime structures to the VM.

It forms the bridge between serialized module representation and executable runtime data.

### 4.4 `ilcvm_cli`

This is the runtime command-line executable.

Current responsibilities include:

- loading a compiled ILB file
- running the entry point
- handling `--run`
- optionally exposing performance instrumentation via a dedicated command-line switch

The CLI is deliberately a thin wrapper around the VM and loader rather than the place where execution behavior is implemented.

### 4.5 `ilcvm_tests`

The runtime tests validate VM semantics independently of the compiler tests.

Current responsibilities include:

- opcode and runtime behavior validation
- host import behavior checks
- object, array, field, and exception behavior tests
- performance-related VM guardrails where appropriate

This is important because some bugs only appear in execution, even if the compiler output looks structurally correct.

## 5. Execution Model

ILC currently uses a register-based virtual machine.

At a conceptual level:

- each function owns a register space
- bytecode instructions read from and write to registers
- calls stage arguments into contiguous call frames
- arrays, objects, strings, and statics are managed by the runtime

This design was chosen because it keeps the IR and bytecode relatively direct and avoids some of the overhead and opacity of a deeply stack-oriented execution model.

### 5.1 Bytecode Style

The current bytecode instruction set includes groups such as:

- integer arithmetic
- string operations
- reference and value comparison
- field access
- static field access
- array allocation and element operations
- calls and virtual calls
- branches and returns
- exception-related operations

The current VM is optimized as an interpreter, and the project already includes targeted benchmarking and profiling support to guide performance work.

### 5.2 Module Format

Compiled output is emitted as an ILB module.

The current serialized model contains sections for concepts such as:

- string table
- blob/signature table
- type table
- field table
- method table
- code section
- exception table
- entry point

Compiler-side support is also prepared for a constant table section, even though it is not yet used as a first-class path for wide numeric constants.

## 6. Standard Library Architecture

ILC deliberately keeps a distinction between:

- runtime primitives and host services
- shipped library surface exposed to user programs

The shipped library surface currently lives in ILC source under `libs/shipped/`.

The current `System` shipped library exposes user-facing types such as:

- `System.Console`
- `System.Environment`
- `System.Clock`
- `System.File`
- `System.Path`

Architecturally, these are intentionally expressed in ILC wherever possible, while the actual host-facing implementation remains underneath in runtime host services.

This gives the project a cleaner layering model:

- runtime core provides capabilities
- shipped libraries provide language-facing APIs
- user code consumes shipped libraries through normal ILC syntax

The project already follows a useful pattern here:

- public user-facing members are often clean properties or methods
- low-level host bindings stay hidden behind internal `...Core` members

That separation should remain a design rule.

## 7. Language Feature Placement

A useful contributor rule is:

- syntax concerns belong in `ILC.Compiler.Syntax`
- semantic validity belongs in `ILC.Compiler.Binding`
- desugaring belongs in `ILC.Compiler.Lowering`
- executable representation belongs in `ILC.Compiler.Bytecode`
- runtime semantics belong in `runtime/`

In practice, most language features touch at least three compiler layers:

1. syntax recognition
2. semantic validation and symbol binding
3. lowering into simpler executable form

Only some features also require runtime changes. Many language features can and should be implemented purely through syntax, binding, and lowering on top of existing runtime semantics.

Examples already present in the codebase:

- `not in`
- `include` and `exclude` for sets
- `foreach` over sets
- `case ... when ...`
- `params` argument packing

These are mostly compiler features built on top of existing lower-level runtime behavior.

## 8. Imports, Libraries, and Source Composition

ILC currently supports namespace imports through `uses`.

The current model already includes:

- normal imports such as `uses System;`
- alias imports such as `uses Sys = System;`
- merge logic in the compiler CLI for imported source units
- ambiguity diagnostics for conflicting unaliased imports

The current system is still relatively lightweight compared to a full assembly/package ecosystem, but it is already sufficient for shipped-library composition and multi-file source assembly.

This is a good architectural base for future packaging and library distribution work.

## 9. Performance Architecture

The repository already includes a meaningful performance workflow, which is unusual for an early language project and should be preserved.

### 9.1 Benchmarks

The `benchmarks/` directory currently contains:

- ILC benchmark programs
- Java comparison programs
- a runner script for `ilcvm` versus Java

The existing benchmark suite focuses on categories such as:

- startup
- loops
- arrays
- method dispatch

This is valuable because it keeps runtime work data-driven rather than speculative.

### 9.2 VM Profiling

The runtime CLI can expose profiling counters via a performance switch.

This has already been used to:

- measure instruction counts
- isolate call and virtual call overhead
- analyze array access costs
- analyze move, compare, and branch behavior
- justify targeted interpreter optimizations

This is an important architectural strength of the project. Performance work is not happening blind.

### 9.3 Current Performance Direction

The runtime has already received targeted optimizations such as:

- faster lookup paths
- register access improvements
- call argument packing improvements
- specialized fast paths for hot interpreter patterns

The current architecture remains interpreter-first, but it is already structured in a way that allows further incremental optimization before any future JIT or AOT discussion.

## 10. Security and Packaging Direction

The repository also contains security-focused architectural work that should be understood as part of the broader ILC direction.

The secure packaging and hardened execution work is intentionally treated as a first-class architectural concern rather than an afterthought.

Relevant design material currently lives at the repository root, including:

- secure packaging and execution design
- numeric and cryptographic integer design
- `UInt128` implementation planning

These documents are complementary to the compiler and runtime architecture described here:

- secure packaging defines protected delivery and hardened execution
- numeric design defines future cryptographic integer foundations
- the current compiler/runtime architecture provides the substrate those plans will eventually rely on

## 11. Numeric and Cryptographic Preparation

The current implementation does not yet expose wide integers such as `UInt128` as user-ready language features.

However, the architecture has already begun to prepare for them in meaningful ways:

- centralized built-in type resolution
- built-in integer-family modeling beyond plain `Integer`
- literal metadata that is no longer hard-wired to a raw `int`
- bytecode immediate categorization that prepares for table-backed constants

This matters because cryptographic numerics are not just a library concern. They affect:

- syntax and literal handling
- type binding
- IR design
- bytecode representation
- runtime value representation
- standard library APIs

The current architectural direction is to prepare carefully before implementing `UInt128`, then scale from there toward larger fixed-width integer types.

## 12. Current Strengths of the Architecture

At this stage, the architecture already has several strong qualities:

- clear compiler/runtime split
- explicit lowering stage
- explicit bytecode model
- dedicated shipped library layer
- separate runtime and compiler verification
- reproducible local verification flow
- built-in benchmarking and profiling discipline
- growing architectural preparation for security and cryptography

These are strong foundations for an open source language/runtime project.

## 13. Current Limitations

Contributors should also understand the present limits of the architecture.

Notable current realities include:

- the runtime is interpreter-based, not JIT-based
- the module and library model is still evolving
- wide integer support is prepared but not implemented
- some future-facing claims in root-level project messaging are broader than what is fully implemented today
- several larger language blocks, such as full generics and interfaces, remain future work

That is normal for the project stage. The important point is that the current structure is coherent enough to support incremental growth.

## 14. Guidance for Contributors

When adding new functionality, prefer the following discipline:

- keep syntax-only concerns in the syntax project
- do not sneak semantic decisions into the parser
- centralize type rules in the binder
- lower sugar before bytecode emission
- avoid pushing language complexity into the VM unless it truly belongs there
- keep shipped library APIs clean even when runtime hooks underneath are low-level
- add direct compiler and runtime verification for new behavior
- if performance is touched, use the benchmark and profiling path rather than intuition alone

This project benefits from explicit layers. Contributors should preserve that separation rather than short-circuiting it for convenience.

## 15. Near-Term Architectural Priorities

The most sensible next architectural steps are:

- continue improving the documentation set under `docs/`
- keep the shipped `System` library as the canonical surface above host services
- continue strengthening feature verification with explicit compiler and runtime assertions
- preserve the preparatory path toward `UInt128` and later cryptographic numerics
- expand the language only where the compiler and runtime layering remains clean

## 16. Summary

ILC currently has a real, layered architecture:

- a structured compiler in C#
- a custom bytecode and module format
- a native C++ register-based VM
- a shipped library surface written in ILC
- explicit testing, verification, benchmarking, and profiling workflows

The project is still evolving, but the architecture is already strong enough to support serious language, runtime, and security work without collapsing into an ad hoc toolchain.

That is the most important architectural fact about the repository today.
