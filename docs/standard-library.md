# Standard Library

This document describes the currently shipped standard library implementation for ILC and how it is composed.

## 1) Design

The standard library is intentionally minimal and implementation-first:

- it is written in ILC source (`libs/shipped/system.ilc`);
- it exposes APIs through regular language syntax (`uses System;`);
- platform behavior is implemented through `extern` host bridges;
- the compiler recognizes supported `extern` signatures and maps them to VM host imports;
- runtime behavior is executed through the runtime host service layer.

The result is a clear separation:

- `System.*` is the user API surface;
- runtime host functions stay in C++ runtime;
- compiler and tests verify the binding and call mapping.

## 2) `libs/shipped/system.ilc` (current)

Current shipped types:

- `System.Console`
- `System.Environment`
- `System.Clock`
- `System.File`
- `System.Path`

### 2.1 `System.Console`

- `public static extern method WriteLine(text: String): void`

Only `WriteLine` is currently defined in the shipped surface and mapped to
`HostImportKind.ConsoleWriteLine`.

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

Backed by private `extern` methods returning `String`.

### 2.4 `System.File`

- `Exists(path: String): Boolean`
- `ReadAllText(path: String): String`
- `WriteAllText(path: String; text: String): void`
- `AppendAllText(path: String; text: String): void`

All operations are synchronous and string-based in the current bootstrap.

### 2.5 `System.Path`

- `Combine(left: String; right: String): String`
- `GetFileName(path: String): String`
- `GetDirectoryName(path: String): String`
- `GetExtension(path: String): String`

## 3) Binding and Host Mapping

`extern` methods are resolved in the binder based on exact signature patterns.

Only methods with matching:

- declaring type name (`Console`, `Environment`, `Clock`, `File`, `Path`)
- `static` + `extern`
- exact name and parameter/return types

are converted into a known `HostImportKind` value.

Current mapping is explicit and validated by compiler tests.

Notable mapping examples:

- `Console.WriteLine` → `HostImportKind.ConsoleWriteLine`
- `Environment.GetCommandLineArgsCore` → `HostImportKind.EnvironmentGetCommandLineArgs`
- `Clock.GetMonotonicMillisecondsTextCore` → `HostImportKind.ClockGetMonotonicMillisecondsText`
- `File.ReadAllTextCore` → `HostImportKind.FileReadAllText`
- `Path.CombineCore` → `HostImportKind.PathCombine`

`extern` methods that do not match expected signatures are treated as regular methods
unless the source marks known host import kinds.

## 4) Runtime Semantics and Limits

Current runtime behavior is constrained to bootstrap correctness:

- no async I/O
- no encoding or charset options for file APIs
- text operations are plain UTF-8 string operations at runtime layer boundaries
- errors from host calls propagate as runtime errors through normal execution path

`Environment.Variables[name] := value` does not currently support deletion by assigning `nil`;
it calls host `set_environment_variable` and the runtime can implement this by unsetting with an empty value in the standard host implementation.

`Console.ReadLine` is not currently exposed through `System.Console`; only `WriteLine` is available.

## 5) Where It Is Used

Typical usage:

```ilc
uses System;

public class Program
begin
  public static method Main(): Integer;
  begin
    Console.WriteLine('Hello');
    return Environment.CommandLineArgs.Length;
  end;
end;
```

Examples and integration checks live in compiler tests and runtime tests.

## 6) Extending the Standard Library

Recommended process for adding APIs:

1. define the API surface in `libs/shipped/<namespace>.ilc` or extend `system.ilc`;
2. keep host entry points as private `extern` `_Core` methods with minimal contracts;
3. expose stable public wrappers that can evolve independently of host import details;
4. add binder validation by extending `ResolveHostImportKind`;
5. add VM opcode/host behavior support if new runtime services are required;
6. add compiler and runtime tests for both binding and execution paths.

Suggested package growth order:

- I/O (`System.IO` style modules) after `File` baseline stabilization
- text/date/time modules based on existing `Clock` style
- collections/cryptographic helpers once type support and object model are ready

## 7) Documentation Status

Current status in this repository:

- shipped API is documented here (authoritative for current bootstrap);
- tests assert host import mapping and runtime integration;
- runtime behavior can differ from platform idioms depending on host implementation
- further API additions should be done explicitly and documented before code changes.
