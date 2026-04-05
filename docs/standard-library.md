# Standard Library

This document describes the currently shipped standard library implementation for ILC and how it is composed.

## 1) Design

The standard library is intentionally minimal and implementation-first:

- it is written in ILC source (`libs/shipped/system.ilc`, `libs/shipped/diagnostics.ilc`);
- it exposes APIs through regular language syntax (`uses System;`);
- platform behavior is implemented through `extern` host bridges;
- the compiler recognizes supported `extern` signatures and maps them to VM host imports;
- runtime behavior is executed through the runtime host service layer.

In addition to the shipped source library, the bootstrap currently exposes a
small set of compiler/runtime-backed built-ins for `String` and `Integer`. Those
APIs are part of the practical language surface, but they are not declared in
`libs/shipped/system.ilc`.

The result is a clear separation:

- `System.*` is the user API surface;
- built-in scalar/reference intrinsics (for example `String` methods) are owned by
  the compiler + VM pipeline;
- runtime host functions stay in C++ runtime;
- compiler and tests verify the binding and call mapping.

## 2) Shipped Library Units (current)

The current shipped library is split across:

- `libs/shipped/system.ilc`
- `libs/shipped/diagnostics.ilc`
- `libs/shipped/text.ilc`
- `libs/shipped/collections.ilc`

Current shipped types:

- `System.Console`
- `System.Environment`
- `System.Clock`
- `System.IException`
- `System.Exception`
- `System.NotSupportedException`
- `System.Math`
- `System.Convert`
- `System.File`
- `System.Path`
- `System.Diagnostics.Stopwatch`
- `System.Diagnostics.TraceLevel`
- `System.Diagnostics.TraceTarget`
- `System.Diagnostics.ITrace`
- `System.Diagnostics.ConsoleTrace`
- `System.Diagnostics.FileTrace`
- `System.Diagnostics.NullTrace`
- `System.Diagnostics.CompositeTrace`
- `System.Diagnostics.Trace`
- `System.Text.Text`
- `System.Collections.StringList`

The current shipped file does **not** define a `System.String` class. String
helpers such as `StartsWith(...)` or `Trim()` are currently provided as
compiler-recognized intrinsics on the built-in `String` type.

### 2.1 `System.Console`

- `public static extern method Write(text: String): void`
- `public static extern method WriteLine(text: String): void`

These are mapped to:

- `HostImportKind.ConsoleWrite`
- `HostImportKind.ConsoleWriteLine`

### 2.2 `System.Environment`

Public static properties:

- `CurrentDirectory: String` (get)
- `CommandLineArgs: array of String` (get)
- `UserName: String` (get)
- `MachineName: String` (get)
- `HomeDirectory: String` (get)
- `TempDirectory: String` (get)
- `Variables[name: String]: String` (get/set indexer)

Private `extern` cores:

- `GetCommandLineArgsCore`
- `GetCurrentDirectoryCore`
- `GetUserNameCore`
- `GetMachineNameCore`
- `GetHomeDirectoryCore`
- `GetTempDirectoryCore`
- `GetEnvironmentVariableCore`
- `SetEnvironmentVariableCore`

### 2.3 `System.Clock`

- `MonotonicMillisecondsText: String` (get)
- `WallMillisecondsText: String` (get)
- `WallDateTimeText: String` (get)

Backed by private `extern` methods returning `String`.

### 2.4 `System` exceptions

Current shipped exception surface:

- `IException`
  - `Message: String`
- `Exception : IException`
- `NotSupportedException : Exception`

This is currently a small bootstrap exception model intended to support
library-raised failures such as unsupported diagnostics stack traces.

### 2.5 `System.Math`

Current shipped numeric helpers:

- `Min(left: Integer; right: Integer): Integer`
- `Max(left: Integer; right: Integer): Integer`
- `Abs(value: Integer): Integer`
- `Clamp(value: Integer; minimum: Integer; maximum: Integer): Integer`

Unlike `Console`, `Environment`, `Clock`, `File`, or `Path`, `System.Math` is
currently implemented entirely in ILC source and does not depend on host imports.

### 2.6 `System.Convert`

Current shipped conversion helpers:

- `ToInteger(value: String): Integer`
- `TryToInteger(value: String; out result: Integer): Boolean`
- `ToString(value: Integer): String`
- `ToBoolean(value: Integer): Boolean`

`System.Convert` is currently implemented entirely in ILC source and layers over
existing language/compiler intrinsics such as `Integer.Parse`, `Integer.TryParse`,
`Integer.ToString()`, and Integer comparison.

## 3) `libs/shipped/diagnostics.ilc`

### 3.1 `System.Diagnostics.Stopwatch`

Current shipped diagnostics/timing helper:

- `Stopwatch.StartNew(): Stopwatch`
- `Start(): void`
- `Stop(): void`
- `Restart(): void`
- `Reset(): void`
- `IsRunning: Boolean` (get)
- `ElapsedMilliseconds: Integer` (get)

`System.Diagnostics.Stopwatch` is currently implemented in ILC source on top of
`Clock.MonotonicMillisecondsText` plus `Integer.Parse(...)`. This keeps the
runtime surface small while still providing a practical benchmark/timing helper.

### 3.2 Tracing

Current shipped tracing surface:

- `TraceLevel` enum
  - `Debug`
  - `Information`
  - `Warning`
  - `Error`
- `TraceTarget` enum
  - `Console`
  - `File`
  - `Null`
- `ITrace`
  - `Targets: array of TraceTarget`
  - `MinimumLevel: TraceLevel`
  - `Write(...)`, `WriteLine(...)`
  - `WriteDebug(...)`, `WriteInformation(...)`, `WriteWarning(...)`, `WriteError(...)`
  - `WriteErrorWithStackTrace(...)`
- `ConsoleTrace`
- `FileTrace`
  - `FilePath: String`
- `NullTrace`
- `CompositeTrace`
  - `Entries: array of ITrace`
- `Trace`
  - static facade with `Current: ITrace`

The current tracing implementation is source-level and intentionally simple:

- messages are timestamped through `Clock.WallDateTimeText`
- console output uses `Console.Write` / `Console.WriteLine`
- file output uses `File.AppendAllText(...)`
- level filtering is handled in ILC code through `MinimumLevel`
- `WriteErrorWithStackTrace(...)` currently raises `System.NotSupportedException`
  because bootstrap runtime stack traces/debug symbols are not yet available

### 3.3 `System.File`

- `Exists(path: String): Boolean`
- `ReadAllText(path: String): String`
- `WriteAllText(path: String; text: String): void`
- `AppendAllText(path: String; text: String): void`

All operations are synchronous and string-based in the current bootstrap.

### 3.4 `System.Path`

- `Combine(left: String; right: String): String`
- `GetFileName(path: String): String`
- `GetDirectoryName(path: String): String`
- `GetExtension(path: String): String`
- `HasExtension(path: String): Boolean`
- `GetFileNameWithoutExtension(path: String): String`
- `ChangeExtension(path: String; extension: String): String`

## 4) `libs/shipped/text.ilc`

### 4.1 `System.Text.Text`

Current shipped text helpers:

- `IsNullOrEmpty(value: String): Boolean`
- `NullIfEmpty(value: String): String`
- `TrimToNull(value: String): String`
- `CollapseWhitespace(value: String): String`
- `Indent(value: String; prefix: String): String`
- `Join(separator: String; values: array of String): String`
- `Repeat(value: String; count: Integer): String`
- `PadLeft(value: String; totalWidth: Integer; padding: String): String`
- `PadRight(value: String; totalWidth: Integer; padding: String): String`
- `Center(value: String; totalWidth: Integer; padding: String): String`
- `StartsWithIgnoreCase(value: String; prefix: String): Boolean`
- `EndsWithIgnoreCase(value: String; suffix: String): Boolean`
- `ContainsIgnoreCase(value: String; part: String): Boolean`
- `Split(value: String; separator: String): array of String`
- `Lines(value: String): array of String`

`System.Text.Text` is currently implemented entirely in ILC source and builds on
the existing `String` intrinsic surface.

## 5) `libs/shipped/collections.ilc`

### 5.1 `System.Collections.StringList`

Current shipped first collection type:

- `StringList()`
- `Count: Integer`
- default indexer `Item[index: Integer]: String`
- `Add(value: String)`
- `AddRange(values: array of String)`
- `Clear()`
- `Contains(value: String): Boolean`
- `IndexOf(value: String): Integer`
- `ToArray(): array of String`

`System.Collections.StringList` is currently implemented entirely in ILC source
as a simple dynamically growing string-backed list. It is a pragmatic bootstrap
step ahead of generic collections.

## 6) Binding and Host Mapping

`extern` methods are resolved in the binder based on exact signature patterns.

Only methods with matching:

- declaring type name (`Console`, `Environment`, `Clock`, `File`, `Path`)
- `static` + `extern`
- exact name and parameter/return types

are converted into a known `HostImportKind` value.

Current mapping is explicit and validated by compiler tests.

Notable mapping examples:

- `Console.Write` → `HostImportKind.ConsoleWrite`
- `Console.WriteLine` → `HostImportKind.ConsoleWriteLine`
- `Environment.GetCommandLineArgsCore` → `HostImportKind.EnvironmentGetCommandLineArgs`
- `Clock.GetMonotonicMillisecondsTextCore` → `HostImportKind.ClockGetMonotonicMillisecondsText`
- `Clock.GetWallDateTimeTextCore` → `HostImportKind.ClockGetWallDateTimeText`
- `File.ReadAllTextCore` → `HostImportKind.FileReadAllText`
- `Path.CombineCore` → `HostImportKind.PathCombine`

`extern` methods that do not match expected signatures are treated as regular methods
unless the source marks known host import kinds.

## 7) Built-in Intrinsics Outside the shipped source library

The following APIs are currently supported even though they are not declared in
the shipped source file:

### 7.1 `String` intrinsic instance methods

- `StartsWith(value: String): Boolean`
- `EndsWith(value: String): Boolean`
- `Contains(value: String): Boolean`
- `IndexOf(value: String): Integer`
- `LastIndexOf(value: String): Integer`
- `Substring(start: Integer; length: Integer): String`
- `Replace(oldValue: String; newValue: String): String`
- `Insert(index: Integer; value: String): String`
- `Remove(index: Integer; length: Integer): String`
- `ToUpper(): String`
- `ToLower(): String`
- `Trim(): String`
- `TrimStart(): String`
- `TrimEnd(): String`

These are resolved by the binder as synthetic methods, lowered through dedicated
IR instructions, emitted as dedicated bytecode opcodes, and executed directly by
the VM.

### 7.2 `Integer` intrinsic methods

- `Integer.Parse(value: String): Integer`
- `Integer.TryParse(value: String; out result: Integer): Boolean`
- `value.ToString(): String` for `Integer`

## 8) Runtime Semantics and Limits

Current runtime behavior is constrained to bootstrap correctness:

- no async I/O
- no encoding or charset options for file APIs
- text operations are plain UTF-8 string operations at runtime layer boundaries
- errors from host calls propagate as runtime errors through normal execution path

`Environment.Variables[name] := value` does not currently support deletion by assigning `nil`;
it calls host `set_environment_variable` and the runtime can implement this by unsetting with an empty value in the standard host implementation.

`Console.ReadLine` is not currently exposed through `System.Console`.

## 9) Where It Is Used

Typical usage:

```ilc
uses System, System.Diagnostics, System.Text, System.Collections;

public class Program
begin
  public static method Main(): Integer;
  begin
    Console.WriteLine('Hello');
    Trace.WriteInformation('boot');
    Console.WriteLine(Text.Join(', ', Environment.CommandLineArgs));
    var words := new StringList();
    words.Add('hello');
    return Environment.CommandLineArgs.Length;
  end;
end;
```

Examples and integration checks live in compiler tests and runtime tests.

## 10) Extending the Standard Library

Recommended process for adding APIs:

1. define the API surface in `libs/shipped/<namespace>.ilc` or extend `system.ilc`;
2. keep host entry points as private `extern` `_Core` methods with minimal contracts;
3. expose stable public wrappers that can evolve independently of host import details;
4. add binder validation by extending `ResolveHostImportKind`;
5. add VM opcode/host behavior support if new runtime services are required;
6. add compiler and runtime tests for both binding and execution paths.

Suggested package growth order:

- `System.Text` as explicit higher-level APIs after the current intrinsic string
  surface is documented and stabilized
- I/O (`System.IO` style modules) after `File` baseline stabilization
- text/date/time modules based on existing `Clock` style
- collections/cryptographic helpers once type support and object model are ready

## 8) Documentation Status

Current status in this repository:

- shipped API is documented here (authoritative for current bootstrap);
- tests assert host import mapping and runtime integration;
- runtime behavior can differ from platform idioms depending on host implementation
- further API additions should be done explicitly and documented before code changes.
