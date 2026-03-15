# ILC Language Overview

This document describes the language surface that is currently implemented by the
`master`-stage compiler pipeline in this repository (syntax + binding + lowering +
bytecode emission). It is intentionally practical: it focuses on what is
compilable today and highlights where the implementation is still a bootstrap.

## 1) Source model and entry point

An ILC source file is parsed as one **compilation unit**:

- optional `namespace`
- optional `uses`
- zero or more top-level members

The compiler supports merging imported units:

- `uses` declares namespaces to import
- referenced files are merged in CLI mode when their namespace is explicitly listed

Entry point selection in the bootstrap compiler:

- `public static Main`-named function/method is used when present
- if no explicit `Main` exists and there are top-level statements or declarations,
  the compiler synthesizes `__TopLevelMain`
- there must be at most one explicit `Main`

## 2) Declarations

### Types

Supported top-level type declarations:

- `public class`
- `public record`
- `public enum`

`record` is currently parsed through the same declaration path as `class` and does
not yet introduce separate record-only semantics.

### Members

Inside a class/record, the following members are supported:

- `var` fields (static and instance)
- `const` constants (static, optionally untyped/inferred)
- methods and constructors:
  - `method`
  - `function`
  - `procedure`
  - `constructor`
- `property`

Top-level members also support:

- `var` declarations
- `const` declarations
- expression statements

Properties are implemented in two forms:

- short form: `read` / `write`
- full block form with accessor blocks (`get`, `set`, `init`)

Default properties are supported as indexers via
`public default property Item[index: Integer]: T read ... write ...`.

### Modifiers

The parser accepts and validates the following modifiers today:

- `public`
- `private`
- `protected`
- `internal`
- `static`
- `default`
- `readonly`
- `extern`

Current member-level access and storage checks are enforced by binding.

### Parameters

Parameters support:

- typed parameter lists
- separators using commas or semicolons
- passing modes: `out`, `ref`, `in`, `params`
- procedure/function/constructor return type optionality follows `method`/`function`/`procedure`

## 3) Type system (bootstrap)

Built-in types available in symbol resolution today:

- `Object`, `Void`, `Boolean`, `Char`, `Integer`, `UInt128`, `UInt256`, `UInt512`, `UInt1024`, `UInt2048`, `String`, `Nil`

`UInt*` types are present in the symbol model and currently intended as future
numeric direction. The current execution path is still centered on the `Integer`-
centric bytecode shape.

Compound forms:

- `array of T`
- fixed-rank arrays, e.g. `array[3, 3] of T`
- `set of Enum`

`Nil` is used with reference-typed values and for `null`-style checks.

### Runtime-shape note

The current VM and binding design supports:

- integer arithmetic,
- string operations,
- arrays and set members,
- object/record/class references.

The wider integer family is currently a structural/type-system preparation and is
not yet a fully optimized runtime feature set.

## 4) Expressions

### Literals and basic forms

- number literals with `_` grouping (e.g. `1_000`)
- single/double quoted string literals
- `nil`, `true`, `false`
- parenthesized expressions

### Unary

- `not` (Boolean/Integer)
- `+expr` (Integer)
- `-expr` (Integer)

### Assignment

Supported assignment operators:

- `:=`
- `+=`, `-=`, `*=`, `/=`, `div=`, `mod=`, `and=`, `or=`, `xor=`, `shl=`, `shr=`, `??=`

`??=` and `??` are available for reference-like values (including `Nil`).

### Binary arithmetic / comparison

- `+`, `-`, `*`, `/`, `div`, `mod`
- shifts: `shl`, `shr`
- comparisons: `=`, `<>`, `<`, `<=`, `>`, `>=`
- membership/operator forms used in patterns and filters: `in`, `not in`
- type/shape checks: `is`, `as`

### Compound forms

- element access: `expr[... ]`
- call expressions: `f(...)`
- member access: `obj.Member`
- slice expressions: `start..end`
- constructor/object creation: `new Type(...)` and `new Type[...]`
- `match` expressions

## 5) Statements and control flow

Supported statement forms in the bootstrap compiler:

- `begin ... end`
- `if ... then ... else`
- `while ... do`
- `repeat ... until`
- `for` with `to` / `downto` and optional `step`
- `foreach` and `for each`
- `with`
- `case ... of ... else ... end`
- `match ... with ... else ... end`
- `return` / `exit`
- `break`
- `continue`
- `raise` / `throw`
- `try` with `except` and/or `finally`, including `on`
- `include(...)` / `exclude(...)`

`with` is implemented as a compile-time rewrite that scopes member access to the
active receiver.

## 6) Pattern matching and branching

Pattern support in this stage includes:

- wildcard `_`
- typed pattern (`SomeType name` in match arms)
- label unions and guards (`or`, `when`)
- relational patterns using `< <= > >=` on integers
- range patterns `start..end`
- `case` supports label lists and optional `when` guards

`match` is available both as a statement and as an expression.

## 7) Object model and call semantics

Classes and record instances support:

- constructors
- fields and properties
- default and index properties as property-like accessors
- instance and static methods

Call resolution includes:

- normal local and qualified calls
- instance and static dispatch semantics
- a synthetic fast path for host/intrinsic members where available

## 8) String API surface (compiler-backed)

The compiler exposes compiler-intrinsic string operations:

- `StartsWith`, `EndsWith`, `Contains`
- `IndexOf`, `LastIndexOf`
- `Replace`, `Insert`, `Remove`, `Substring`
- `ToUpper`, `ToLower`, `Trim`, `TrimStart`, `TrimEnd`

and `Integer.Parse` / `Integer.TryParse` as type intrinsics.

## 9) Interop and shipped library model

`extern` methods are bound through the host import layer. Unknown host mapping
names are rejected by the binder.

The shipped library lives under `libs/shipped` and currently includes:

- `System.Console` (`WriteLine`)
- `System.Environment` (`CommandLineArgs`, `CurrentDirectory`, `Variables[...]`, etc.)
- `System.Clock`
- `System.File`
- `System.Path`

All of these are reachable via normal `uses` imports and normal ILC syntax.

## 10) Current limitations in this bootstrap

Keep these in mind when evaluating language scope:

- No generic declarations are currently accepted by the parser.
- No inheritance/implements clauses are currently part of declarations.
- No interface member system is implemented.
- `and`/`or` are not full logical infix operators yet; only their compound
  assignment forms are part of assignment lowering.
- The full operator/feature catalog in the product vision is broader than the
  currently compile-able bootstrap subset.
- Wider integer types (`UInt128`+) are represented in symboling, but end-to-end
  runtime behavior is not the primary optimization target yet.

## 11) Roadmap direction

Given the current architecture, the most direct next steps are usually:

1. finish language-level constraints in `ILC.Compiler.Binding` and
   `ILC.Compiler.Lowering` before touching VM dispatch internals
2. preserve the current stable bytecode shape and only extend opcodes when
   required by new semantics
3. add language-complete tests alongside the existing compiler tests for any new
   construct

This keeps the language evolution in the compiler where it is cheapest to reason
about and safest to validate.
