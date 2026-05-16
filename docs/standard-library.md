# Standard Library

This document describes the currently shipped standard library implementation for ILC and how it is composed.

## 1) Design

The standard library is intentionally minimal and implementation-first:

- it is written in ILC source (`libs/shipped/system.ilc`, `libs/shipped/diagnostics.ilc`);
- it exposes APIs through regular language syntax (`uses System;`);
- built-in platform behavior is implemented through explicit `extern` host bridges;
- the compiler recognizes supported built-in `extern` signatures and maps them to VM host imports;
- native ABI behavior is implemented separately through `DllImport` and the FFI runtime path;
- runtime behavior is executed either through the runtime host service layer or the native FFI layer, depending on the declaration.

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
- `libs/shipped/json.ilc`
- `libs/shipped/net.ilc`
- `libs/shipped/threading.ilc`
- `libs/shipped/collections.ilc`
- `libs/shipped/ui.ilc`
- `libs/shipped/ui-hosting.ilc`
- `libs/shipped/ui-backends-qtquick.ilc`

Current shipped types:

- `System.Console`
- `System.Environment`
- `System.Clock`
- `System.IException`
- `System.Exception`
- `System.NotSupportedException`
- `System.NativeHandle`
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
- `System.Text.Json.JsonKind`
- `System.Text.Json.JsonValue`
- `System.Text.Json.JsonNull`
- `System.Text.Json.JsonBoolean`
- `System.Text.Json.JsonNumber`
- `System.Text.Json.JsonString`
- `System.Text.Json.JsonArray`
- `System.Text.Json.JsonObject`
- `System.Text.Json.Json`
- `System.Net.Uri`
- `System.Net.TcpClient`
- `System.Net.HttpClient`
- `System.Net.WebSocketClient`
- `System.Threading.Thread`
- `System.Threading.Task`
- `System.Threading.Mutex`
- `System.Collections.IEnumerator<T>`
- `System.Collections.IEnumerable<T>`
- `System.Collections.IReadOnlyList<T>`
- `System.Collections.ICollection<T>`
- `System.Collections.IList<T>`
- `System.Collections.Predicate<T>`
- `System.Collections.Selector<TSource, TResult>`
- `System.Collections.Enumerable<T>`
- `System.Collections.Enumerable<TSource, TResult>`
- `System.Collections.ListEnumerator<T>`
- `System.Collections.List<T>`
- `System.Collections.StringList`
- `System.Ui.Application`
- `System.Ui.Window`
- `System.Ui.View`
- `System.Ui.StackPanel`
- `System.Ui.TextBlock`
- `System.Ui.Button`
- `System.Ui.TextBox`
- `System.Ui.CheckBox`
- `System.Ui.Slider`
- `System.Ui.Command`
- `System.Ui.Hosting.IUiBackend`
- `System.Ui.Hosting.IWindowHost`
- `System.Ui.Hosting.IViewHost`
- `System.Ui.Backends.QtQuick.QtQuickBackend`
- `System.Ui.Backends.QtQuick.QtQuickNative`

The current shipped file does **not** define a `System.String` class. String
helpers such as `StartsWith(...)` or `Trim()` are currently provided as
compiler-recognized intrinsics on the built-in `String` type.

### 2.1 Query projection note

Anonymous query projectors such as:

```ilc
select new { PrimaryLength := value.Length, SecondaryLength := value.Length + 1 }
```

are not shipped as source-level library types. They are compiler-synthesized
internal shapes used by the query/binding/lowering pipeline.

Current practical model:

- projectors behave like small reference objects with readable members;
- the compiler emits synthetic internal types and constructors for them;
- enumerable pipelines can return and iterate these shapes normally;
- this path is covered by bootstrap compiler tests and runtime smoke.

Current limitation:

- the implementation is still bootstrap-oriented and internally uses synthetic
  projector symbols rather than a polished public structural type feature.

### 2.2 `System.Console`

- `public static extern method Write(text: String): void`
- `public static extern method WriteLine(text: String): void`

These are mapped to:

- `HostImportKind.ConsoleWrite`
- `HostImportKind.ConsoleWriteLine`

### 2.3 `System.Environment`

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

### 2.4 `System.Clock`

- `MonotonicMillisecondsText: String` (get)
- `WallMillisecondsText: String` (get)
- `WallDateTimeText: String` (get)

Backed by private `extern` methods returning `String`.

### 2.5 `System` exceptions

Current shipped exception surface:

- `IException`
  - `Message: String`
- `Exception : IException`
- `NotSupportedException : Exception`
- `NativeHandle`
  - `Null = 0`

This is currently a small bootstrap exception model intended to support
library-raised failures such as unsupported diagnostics stack traces.

### 2.6 `System.Math`

Current shipped numeric helpers:

- `Min(left: Integer; right: Integer): Integer`
- `Max(left: Integer; right: Integer): Integer`
- `Abs(value: Integer): Integer`
- `Clamp(value: Integer; minimum: Integer; maximum: Integer): Integer`

Unlike `Console`, `Environment`, `Clock`, `File`, or `Path`, `System.Math` is
currently implemented entirely in ILC source and does not depend on host imports.

### 2.7 `System.Convert`

Current shipped conversion helpers:

- `ToInteger(value: String): Integer`
- `TryToInteger(value: String; out parsedValue: Integer): Boolean`
- `ToString(value: Integer): String`
- `ToBoolean(value: Integer): Boolean`

`System.Convert` is currently implemented entirely in ILC source and layers over
existing language/compiler intrinsics such as `Integer.Parse`, `Integer.TryParse`,
`Integer.ToString()`, and Integer comparison.

More generally, `out` / `ref` calls now work for normal declared methods through
the current VM call frame. The runtime uses copy-in / copy-out behavior for
these arguments rather than a separate address/reference object model.

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
- `WriteErrorWithStackTrace(...)` writes the error plus the current stack trace
- when debug symbols are present, stack trace lines are source-enriched

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

## 5) `libs/shipped/json.ilc`

### 5.1 `System.Text.Json`

Current shipped JSON surface:

- `JsonKind`
  - `Null`, `Boolean`, `Number`, `String`, `Array`, `Object`
- `JsonValue`
  - `Kind: JsonKind`
  - `IsNull: Boolean`
- `JsonNull`
- `JsonBoolean`
  - `Value: Boolean`
- `JsonNumber`
  - `Value: Integer`
- `JsonString`
  - `Value: String`
- `JsonArray`
  - `Count: Integer`
  - `Item[index: Integer]: JsonValue`
  - `Add(value: JsonValue)`
- `JsonObject`
  - `Count: Integer`
  - `Item[name: String]: JsonValue`
  - `Add(name: String; value: JsonValue)`
  - `Contains(name: String): Boolean`
  - `NameAt(index: Integer): String`
  - `ValueAt(index: Integer): JsonValue`
- `Json`
  - `Parse(text: String): JsonValue`
  - `Stringify(value: JsonValue): String`

The current JSON implementation is source-level and intentionally small. It supports:

- objects
- arrays
- strings
- integer numbers
- booleans
- `null`
- compact serialization back into JSON text

Richer number support and formatting options are future work.

## 6) `libs/shipped/net.ilc`

### 6.1 `System.Net`

Current shipped networking surface:

- `Uri`
  - `OriginalString: String`
  - `Scheme: String`
  - `Host: String`
  - `Path: String`
  - `Query: String`
  - `HasQuery: Boolean`
  - `Fragment: String`
  - `Port: Integer`
  - `IsAbsoluteUri: Boolean`
  - `Authority: String`
  - `PathAndQuery: String`
  - `Parse(text: String): Uri`
  - `TryParse(text: String; out value: Uri): Boolean`
  - `ContainsQueryParameter(name: String): Boolean`
  - `GetQueryParameter(name: String): String`
- `TcpClient`
  - `IsConnected: Boolean`
  - `Connect(host: String; port: Integer): Boolean`
  - `ReadLine(): String`
  - `WriteLine(text: String): void`
  - `Close(): void`
- `HttpClient`
  - `GetString(url: String): String`
- `WebSocketClient`
  - `IsConnected: Boolean`
  - `Connect(url: String): Boolean`
  - `ReceiveText(): String`
  - `SendText(text: String): void`
  - `Close(): void`

The current `System.Net` slice now combines:

- URI parsing and normalization
- a minimal line-oriented TCP client backed by host imports
- a minimal synchronous HTTP text client for deterministic GET showcases
- a minimal synchronous `ws://` WebSocket client for local echo demos

`TcpClient` is intentionally small and synchronous. It exists as a pragmatic
transport showcase and bootstrap building block for later socket and HTTP work.

`HttpClient` is intentionally narrow in v1. It currently focuses on
`GetString(...)` for local demos and bootstrap scenarios.

`WebSocketClient` is intentionally synchronous and text-only in v1. It exists
as a pragmatic showcase step between plain transport primitives and later
higher-level streaming/async APIs.

## 7) `libs/shipped/threading.ilc`

### 7.1 `System.Threading`

Current shipped threading surface:

- `IRunnable`
  - `Run(): void`
- `Thread`
  - `CurrentManagedId: Integer`
  - `Start(target: IRunnable): Thread`
  - `Join(): void`
  - `IsAlive: Boolean`
  - `Sleep(milliseconds: Integer): void`
- `Task`
  - `Run(target: IRunnable): Task`
  - `Wait(): void`
  - `IsCompleted: Boolean`
  - `IsFaulted: Boolean`
  - `ErrorMessage: String`
- `ITaskRunnable<T>`
  - `Run(): T`
- `ITaskResultSink<T>`
  - internal completion sink used by the shipped `Task<T>` worker path
- `Task<T>`
  - `Task(target: ITaskRunnable<T>)`
  - `Wait(): void`
  - `IsCompleted: Boolean`
  - `IsFaulted: Boolean`
  - `ErrorMessage: String`
  - `Result: T`
- `Mutex`
  - `IsValid: Boolean`
  - `WaitOne(): Boolean`
  - `Release(): void`
  - `Close(): void`

The current `System.Threading` slice now covers:

- current-thread identification
- sleeping
- host-backed mutex synchronization
- managed `IRunnable`-based thread start/join
- a first `Task` / `Task<T>` abstraction on top of managed threads

`Task` and `Task<T>` are currently intentionally small and pragmatic. They wrap
managed thread execution and expose completion/fault state, but higher-level
shapes such as `async` / `await` are still future work.

## 8) `libs/shipped/collections.ilc`

### 8.1 `System.Collections.IEnumerator<T>`

Current shipped generic enumerator view:

- `Current: T`
- `MoveNext(): Boolean`
- `Reset()`

### 8.2 `System.Collections.IEnumerable<T>`

Current shipped generic enumerable view:

- `GetEnumerator(): IEnumerator<T>`

In the current bootstrap this is available as a collection API surface and for
manual enumerator-driven iteration. It is now also wired into generic `foreach`
lowering.

### 8.3 `System.Collections.IReadOnlyList<T>`

Current shipped read-only generic list view:

- `Count: Integer`
- default indexer `Item[index: Integer]: T` (get)

This is the smallest generic collection abstraction currently shipped and is
intended for APIs that want indexed read access without mutation.

### 8.4 `System.Collections.ICollection<T>`

Current shipped mutable generic collection view:

- `Count: Integer`
- `Add(value: T)`
- `Clear()`
- `Contains(value: T): Boolean`

This is the current minimal mutable collection abstraction beneath list-shaped
APIs.

### 8.5 `System.Collections.IList<T>`

Current shipped mutable generic list view:

- inherits `IReadOnlyList<T>`
- inherits `ICollection<T>`
- `AddRange(values: array of T)`
- `IndexOf(value: T): Integer`
- `ToArray(): array of T`

### 8.6 `System.Collections.StringList`

`System.Collections.StringList` is now a thin compatibility subclass of:

- `List<String>`

It remains available for bootstrap code, but the real long-term collection
surface now starts at the generic `IEnumerator<T>` / `IEnumerable<T>` / `IReadOnlyList<T>` / `ICollection<T>` / `IList<T>` / `List<T>` set.

### 8.7 `System.Collections.ListEnumerator<T>`

Current shipped generic list enumerator:

- `ListEnumerator<T>(items: array of T; count: Integer)`
- `Current: T`
- `MoveNext(): Boolean`
- `Reset()`

### 8.8 `System.Collections.List<T>`

Current shipped first generic collection type:

- `List<T>()`
- `IEnumerable<T>`
- `IReadOnlyList<T>`
- `ICollection<T>`
- `IList<T>`
- `GetEnumerator(): IEnumerator<T>`
- `Count: Integer`
- default indexer `Item[index: Integer]: T`
- `Add(value: T)`
- `AddRange(values: array of T)`
- `Clear()`
- `Contains(value: T): Boolean`
- `IndexOf(value: T): Integer`
- `ToArray(): array of T`

### 8.9 `System.Collections.Dictionary<TKey, TValue>`

Current shipped generic dictionary surface:

- `Dictionary<TKey, TValue>()`
- `Count: Integer`
- default indexer `Item[key: TKey]: TValue`
- `Add(key: TKey; value: TValue)`
- `ContainsKey(key: TKey): Boolean`
- `TryGetValue(key: TKey; out value: TValue): Boolean`
- `Clear()`

The current implementation is intentionally simple and bootstrap-oriented:

- keys and values are stored in parallel arrays
- lookup is linear
- the API surface is ahead of any hash-based optimization work

### 8.10 `System.Collections.Predicate<T>` and `Selector<TSource, TResult>`

Current shipped enumerable pipeline delegate surface:

- `Predicate<T>(value: T): Boolean`
- `Selector<TSource, TResult>(value: TSource): TResult`

These delegates are intended as the first reusable callback substrate for
query-style enumerable helpers.

### 8.11 `System.Collections.Enumerable<T>` and `Enumerable<TSource, TResult>`

Current shipped enumerable pipeline helper surface:

- `Enumerable<T>.Where(source: IEnumerable<T>; predicate: Predicate<T>): IEnumerable<T>`
- `Enumerable<T>.Take(source: IEnumerable<T>; count: Integer): IEnumerable<T>`
- `Enumerable<T>.Skip(source: IEnumerable<T>; count: Integer): IEnumerable<T>`
- `Enumerable<T>.Concat(first: IEnumerable<T>; second: IEnumerable<T>): IEnumerable<T>`
- `Enumerable<T>.Distinct(source: IEnumerable<T>): IEnumerable<T>`
- `Enumerable<T>.Append(source: IEnumerable<T>; value: T): IEnumerable<T>`
- `Enumerable<T>.Prepend(source: IEnumerable<T>; value: T): IEnumerable<T>`
- `Enumerable<T>.Reverse(source: IEnumerable<T>): IEnumerable<T>`
- `Enumerable<T>.Contains(source: IEnumerable<T>; value: T): Boolean`
- `Enumerable<T>.Any(source: IEnumerable<T>): Boolean`
- `Enumerable<T>.Any(source: IEnumerable<T>; predicate: Predicate<T>): Boolean`
- `Enumerable<T>.Count(source: IEnumerable<T>): Integer`
- `Enumerable<T>.Count(source: IEnumerable<T>; predicate: Predicate<T>): Integer`
- `Enumerable<T>.First(source: IEnumerable<T>): T`
- `Enumerable<T>.First(source: IEnumerable<T>; predicate: Predicate<T>): T`
- `Enumerable<T>.FirstOrDefault(source: IEnumerable<T>): T`
- `Enumerable<T>.FirstOrDefault(source: IEnumerable<T>; predicate: Predicate<T>): T`
- `Enumerable<T>.Single(source: IEnumerable<T>): T`
- `Enumerable<T>.Single(source: IEnumerable<T>; predicate: Predicate<T>): T`
- `Enumerable<T>.SingleOrDefault(source: IEnumerable<T>): T`
- `Enumerable<T>.SingleOrDefault(source: IEnumerable<T>; predicate: Predicate<T>): T`
- `Enumerable<T>.Last(source: IEnumerable<T>): T`
- `Enumerable<T>.Last(source: IEnumerable<T>; predicate: Predicate<T>): T`
- `Enumerable<T>.LastOrDefault(source: IEnumerable<T>): T`
- `Enumerable<T>.LastOrDefault(source: IEnumerable<T>; predicate: Predicate<T>): T`
- `Enumerable<T>.ToList(source: IEnumerable<T>): List<T>`
- `Enumerable<T>.ToArray(source: IEnumerable<T>): array of T`
- `Enumerable<TSource, TResult>.Select(source: IEnumerable<TSource>; selector: Selector<TSource, TResult>): IEnumerable<TResult>`

The current implementation is intentionally wrapper-based:

- `Where`, `Take`, `Skip`, `Concat`, `Distinct`, `Append`, `Prepend`, `Reverse`, and `Select` return lazy enumerable wrappers
- wrappers allocate enumerators on demand
- filtering and projection are applied during enumeration, not eagerly
- `Any`, `Count`, `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `ToList`, and `ToArray` enumerate eagerly over the current source

## 9) Binding and Host Mapping

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

`DllImport` is now a separate extern path from built-in host services.

Current `DllImport` status:

- syntax:
  - `[DllImport('libname', EntryPoint := 'symbol', CallingConvention := CallingConvention.Cdecl)]`
- binder:
  - accepted only on `static extern` methods
- bytecode / ILB:
  - library name, entry point, and calling convention are serialized into method metadata
- runtime:
  - Linux shared-library loading via `dlopen` / `dlsym`
  - free functions only
  - generic runtime dispatch through `libffi`
  - currently supported FFI value types:
    - parameters: `Integer`, `Boolean`, `String`, `NativeHandle`, supported callback delegates
    - return: `Integer`, `Boolean`, `Void`, `NativeHandle`, owned UTF-8 `String`
  - callbacks:
    - VM trampolines for the current integer callback shapes
  - strings:
    - ILC-to-native strings are UTF-8 call arguments
    - native-to-ILC strings require explicit owned UTF-8 metadata and a free entry point

This is intentionally separate from the private shipped `_Core` host bridge methods.

## 10) UI shipped surface

The shipped UI layer is intentionally backend-neutral at the public model level.
The Qt Quick backend is the first concrete backend and is implemented as a
`DllImport` bridge over a native C ABI shim.

Current source units:

- `libs/shipped/ui.ilc`
- `libs/shipped/ui-hosting.ilc`
- `libs/shipped/ui-backends-qtquick.ilc`

Current neutral UI surface:

- `Application`
- `Window`
  - `Title`
  - `Content`
  - `Background`
  - `Resize(width; height)`
  - `SetMinimumSize(width; height)`
- `View`
  - `Name`
- `StackPanel`
  - `Orientation`
  - child view collection
- `TextBlock`
  - `Text`
  - `Foreground`
- `Button`
  - `Text`
  - `Background`
  - `Command`
- `TextBox`
  - `Text`
  - `PlaceholderText`
  - `Padding`
  - `TextChangedCommand`
- `CheckBox`
  - `Text`
  - `IsChecked`
  - `ToggledCommand`
- `Slider`
  - `Minimum`
  - `Maximum`
  - `Value`
  - `ValueChangedCommand`
- `Command`

Current Qt Quick backend behavior:

- `QtQuickBackend` implements the hosting interfaces;
- native calls are declared in `QtQuickNative` with `DllImport`;
- the native bridge library is `libilc_qtbridge.so`;
- callbacks from QML are routed through VM callback trampolines;
- interaction dispatch currently uses full declarative refresh from ILC state;
- `ILC_QTBRIDGE_DEBUG=1` logs the active refresh mode.

The incremental native patch helpers in `ui-backends-qtquick.ilc` are
experimental and not active in the interaction dispatch path.

## 11) Built-in Intrinsics Outside the shipped source library

The following APIs are currently supported even though they are not declared in
the shipped source file:

### 11.1 `String` intrinsic instance methods

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

### 11.2 `Integer` intrinsic methods

- `Integer.Parse(value: String): Integer`
- `Integer.TryParse(value: String; out parsedValue: Integer): Boolean`
- `value.ToString(): String` for `Integer`

`Integer.TryParse(...)`, `System.Convert.TryToInteger(...)`, and
`Dictionary<TKey, TValue>.TryGetValue(...)` all now exercise the same general
`out` argument call path rather than isolated ad-hoc lowering behavior.

## 12) Runtime Semantics and Limits

Current runtime behavior is constrained to bootstrap correctness:

- no async I/O
- no encoding or charset options for file APIs
- text operations are plain UTF-8 string operations at runtime layer boundaries
- errors from host calls propagate as runtime errors through normal execution path

`Environment.Variables[name] := value` does not currently support deletion by assigning `nil`;
it calls host `set_environment_variable` and the runtime can implement this by unsetting with an empty value in the standard host implementation.

`Console.ReadLine` is not currently exposed through `System.Console`.

## 13) Where It Is Used

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

## 14) Extending the Standard Library

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

## 15) Documentation Status

Current status in this repository:

- shipped API is documented here (authoritative for current bootstrap);
- tests assert host import mapping and runtime integration;
- runtime behavior can differ from platform idioms depending on host implementation
- further API additions should be done explicitly and documented before code changes.
