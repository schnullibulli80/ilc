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

Routine keyword semantics:

- `function` must declare an explicit non-`Void` return type and exposes
  implicit `Result`
- `procedure` must not declare a return type and does not expose `Result`
- `method` is intentionally flexible: with a return type it behaves like a
  `function`, without a return type it behaves like a `procedure`
- `constructor` must not declare a return type
- `Void` is an internal symbol/runtime shape, not a source-level return
  annotation; use `procedure` or `method` without a return type instead

`function` and `procedure` are readonly with respect to the current object.
They may read instance state, but they must not directly assign to instance
fields or properties on implicit `self` or explicit `self`, must not use compound
assignment or `inc`/`dec` on those members, and must not call mutating `method`
members on `self`. Use `method` for stateful operations, including stateful
operations that also return a value. `this` is not a `self` alias.

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

Current call behavior for passing modes:

- `out` and `ref` are supported for normal declared methods
- `out` arguments must be writable targets
- `ref` arguments use the current runtime call frame with copy-in / copy-out behavior
- `in` is accepted by the front-end and currently behaves like a normal value argument at runtime
- `params` is lowered into a synthetic packed array argument

## 3) Type system (bootstrap)

Built-in types available in symbol resolution today:

- `Object`, `Boolean`, `Char`, `Integer`, `UInt128`, `UInt256`, `UInt512`, `UInt1024`, `UInt2048`, `String`, `Nil`

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
- anonymous projectors: `new { Name := expr, Other := expr }`
- `match` expressions

Anonymous projectors are currently available as expression-level projection
shapes, primarily for query pipelines.

Example:

```ilc
var projected :=
  from value in words
  where value.Contains('w')
  select new { PrimaryLength := value.Length, SecondaryLength := value.Length + 1 };
```

Current behavior:

- the compiler synthesizes an internal reference type for the projector shape;
- projector members are readable with normal member syntax;
- enumerable/query pipelines can carry these shapes through `Select(...)` and
  enumeration;
- this is implemented and verified end-to-end, but the internal compiler model
  is still bootstrap-oriented rather than a finalized public structural type
  design.

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

Functions expose an implicit `Result` value with the function return type.
`Result := ...` updates the function result and continues executing. `exit`
is the Delphi-style early routine exit; `exit <expression>` also assigns the
function result before terminating. `return` is currently accepted as a
compatibility alias for `exit` with the same behavior and is not diagnosed as
deprecated today. If a deprecation warning is added later, it should be gated by
a compiler directive or option. Procedures do not expose `Result`. Names are
case-insensitive, so `result`, `RESULT`, and `Result` refer to the same implicit
function result; functions therefore cannot declare parameters or locals that
differ from `Result` only by case.
The compiler reports exact duplicate names as errors in non-overloadable
case-insensitive scopes, and reports case-only name differences as warnings so
ambiguous APIs can be cleaned up before they become user-visible.
Methods and constructors may be overloaded, but duplicate signatures are errors.
Local variables use C#-like active block scopes: a local, loop variable,
typed-match variable, exception-handler variable, or lambda parameter cannot
reuse a name from its active scope chain, but a name can be reused after the
earlier block scope has ended.

Early routine exits from the protected part of a `try` statement that has a
`finally` block are currently rejected until the lowering can preserve the exact
finally-before-exit behavior.

`with` is implemented as a compile-time rewrite that scopes member access to the
active receiver.

`foreach` currently supports:

- `String`
- arrays
- sets
- generic `IEnumerable<T>` through `GetEnumerator()` / `MoveNext()` / `Current`

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

Anonymous projector values participate in normal member access after binding,
but today they are still backed by compiler-synthesized internal projector
symbols rather than a user-visible named type feature.

## 8) String API surface (compiler-backed)

The current C#-like string method surface is implemented as built-in compiler/VM
intrinsics on `String`. These methods are available in normal user code, but
they are not currently declared through a shipped `System.String` source type.

Supported intrinsic string instance methods:

- `StartsWith`, `EndsWith`, `Contains`
- `IndexOf`, `LastIndexOf`
- `Replace`, `Insert`, `Remove`, `Substring`
- `ToUpper`, `ToLower`, `Trim`, `TrimStart`, `TrimEnd`

and `Integer.Parse` / `Integer.TryParse` as type intrinsics.

## 9) Interop and shipped library model

Plain shipped-library `extern` methods are bound through the built-in host
import layer. Unknown built-in host mappings are rejected by the binder.

In addition to built-in host imports, the language now also supports a first
native FFI slice through method attributes:

```ilc
[DllImport('libc.so.6', EntryPoint := 'atoi', CallingConvention := CallingConvention.Cdecl)]
public static extern function Atoi(text: String): Integer;
```

Current `DllImport` scope:

- `static extern` free functions only
- Linux shared libraries through `dlopen` / `dlsym`
- runtime invocation through generic `libffi` dispatch
- current FFI type subset:
  - parameter: `Integer`, `Boolean`, `String`, `NativeHandle`, supported callback delegates
  - return: `Integer`, `Boolean`, no return value, `NativeHandle`, owned UTF-8 `String`
- callbacks are supported through VM-generated trampolines for the current integer callback shapes
- no native structs/records or arbitrary user-visible function pointers yet

The shipped library lives under `libs/shipped` and currently includes:

- `System.Console` (`Write`, `WriteLine`)
- `System.Environment` (`CommandLineArgs`, `CurrentDirectory`, `Variables[...]`, etc.)
- `System.Clock`
- `System.IException`, `System.Exception`, `System.NotSupportedException`
- `System.NativeHandle`
- `System.Math` (`Min`, `Max`, `Abs`, `Clamp`)
- `System.Convert` (`ToInteger`, `TryToInteger`, `ToString`, `ToBoolean`)
- `System.Diagnostics.Stopwatch` (`StartNew`, `Start`, `Stop`, `Restart`, `Reset`, `IsRunning`, `ElapsedMilliseconds`)
- `System.Diagnostics` tracing types
  - `TraceLevel`, `TraceTarget`, `ITrace`
  - `ConsoleTrace`, `FileTrace`, `NullTrace`, `CompositeTrace`
  - `Trace`
- `System.Text.Text` (`IsNullOrEmpty`, `NullIfEmpty`, `TrimToNull`, `CollapseWhitespace`, `Indent`, `Join`, `Repeat`, `PadLeft`, `PadRight`, `Center`, `StartsWithIgnoreCase`, `EndsWithIgnoreCase`, `ContainsIgnoreCase`, `Split`, `Lines`)
- `System.Text.Json`
  - `JsonKind`, `JsonValue`, `JsonNull`, `JsonBoolean`, `JsonNumber`, `JsonString`, `JsonArray`, `JsonObject`, `Json`
  - currently supports parse + compact stringify roundtrips for the shipped JSON value model
- `System.Net`
  - `Uri`
  - `TcpClient`
  - `HttpClient`
  - `WebSocketClient`
- `System.Threading`
  - `Thread`
  - `Task`
  - `Task<T>`
  - `Mutex`
  - currently supports `IRunnable`, `ITaskRunnable<T>`, managed thread start/join, first `Task` / `Task<T>` abstractions, current thread id, sleeping, and host-backed mutex synchronization
- `System.Collections.IEnumerator<T>`, `System.Collections.IEnumerable<T>`, `System.Collections.IReadOnlyList<T>`, `System.Collections.ICollection<T>`, `System.Collections.IList<T>`, `System.Collections.Predicate<T>`, `System.Collections.Selector<TSource, TResult>`, `System.Collections.Enumerable<T>`, `System.Collections.Enumerable<TSource, TResult>`, `System.Collections.ListEnumerator<T>`, `System.Collections.List<T>`, `System.Collections.StringList`, `System.Collections.Dictionary<TKey, TValue>`
- `System.Ui` neutral UI model (`Application`, `Window`, `View`, `StackPanel`, `TextBlock`, `Button`, `TextBox`, `CheckBox`, `Slider`, `Command`)
- `System.Ui.Hosting` backend contracts
- `System.Ui.Backends.QtQuick` first Qt Quick backend over `DllImport`
- `System.File`
- `System.Path`

All of these are reachable via normal `uses` imports and normal ILC syntax.

## 10) Current limitations in this bootstrap

Keep these in mind when evaluating language scope:

- Generic class/interface declarations and closed generic type references are
  accepted in the compiler front-end for the current bootstrap path.
- A first generic runtime/library slice is available through
  `System.Collections.List<T>`, `ListEnumerator<T>`, `IEnumerable<T>`,
  `IList<T>`, `ICollection<T>`, `IReadOnlyList<T>`, `Predicate<T>`,
  `Selector<TSource, TResult>`, `Enumerable<T>`,
  `Enumerable<TSource, TResult>`, and `Dictionary<TKey, TValue>`.
- Generic `foreach` lowering now also supports `IEnumerable<T>`.
- `Enumerable<T>` now also ships lazy `Where`, `Take`, `Skip`, `Concat`, `Distinct`, `Append`, `Prepend`, and `Reverse`, plus eager helpers `Contains`, `Any`, `Count`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `ToList`, and `ToArray`.
- Wider generic coverage beyond the current closed-specialization bootstrap
  slice is still future work.
- Interface declarations and class interface lists are available.
- Interface method dispatch through interface-typed receivers is supported in
  the current bootstrap path.
- Interface property reads through interface-typed receivers are supported in
  the current bootstrap path.
- Interface property writes through interface-typed receivers are supported in
  the current bootstrap path.
- Interface inheritance is supported, including dispatch through sub-interface
  receiver types.
- `is` / `as` for reference types now use runtime type checks, including
  interface targets.
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
