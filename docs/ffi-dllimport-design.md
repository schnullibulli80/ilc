# FFI / `DllImport` Design

This document defines the recommended first foreign-function-interface design for ILC.
It focuses on native shared-library interop and intentionally does not attempt to solve
all external binary integration problems at once.

The immediate goal is:

- let ILC call native shared-library functions;
- keep the surface C#-like rather than Delphi-like;
- support enough ABI detail to unlock real integration work;
- avoid conflating native FFI with future compiled-ILC library import.

## 1) Design Position

The first FFI feature should be:

- `DllImport`

It should **not** start as:

- `LibraryImport`
- a general import mechanism for both native and ILC libraries
- a source-generation system

Reason:

- the first real problem is native interop;
- the first required backend story is likely a native C ABI boundary;
- compiled ILC library import is a different problem and should be designed separately later.

## 2) Why `DllImport` First

`DllImport` is the clearest initial model because it communicates:

- native library boundary
- native symbol lookup
- explicit calling convention

This fits the likely first use cases:

- libc-style experiments
- operating-system APIs
- small C shim libraries
- Qt bridge shims

`LibraryImport` would introduce ambiguity too early:

- does it mean native shared libraries?
- does it mean compiled ILC libraries?
- does it imply source generation?
- does it differ from `DllImport` semantically?

That ambiguity is not useful in the first implementation.

## 3) Proposed Syntax

The recommended first syntax is a C#-style attribute on `extern` methods:

```ilc
[DllImport('libc.so.6', EntryPoint := 'puts', CallingConvention := CallingConvention.Cdecl)]
public static extern function Puts(text: String): Integer;
```

Another example:

```ilc
[DllImport('user32.dll', EntryPoint := 'MessageBoxW', CallingConvention := CallingConvention.StdCall)]
public static extern function MessageBox(
  hWnd: NativeHandle;
  text: String;
  caption: String;
  flags: Integer): Integer;
```

This keeps:

- the declaration itself in normal ILC form
- the import details in attribute metadata

That is much cleaner than trying to re-create Delphi’s:

- `external 'dllname'`
- `name 'export'`
- `index n`

inside the declaration grammar.

## 4) Required First Metadata

The first `DllImport` version should support:

- library name
- entry point name
- calling convention

Recommended named arguments:

- `EntryPoint`
- `CallingConvention`

Minimal shape:

```ilc
[DllImport('libname', EntryPoint := 'symbol', CallingConvention := CallingConvention.Cdecl)]
```

## 5) Calling Convention

The first enum should stay small:

```ilc
public enum CallingConvention
begin
  Cdecl,
  StdCall
end;
```

Why this first set:

- `Cdecl` is the most useful default for Linux/macOS and many C libraries
- `StdCall` covers older Win32-style APIs and aligns with the requested design direction

Do **not** include these in v1 unless required:

- `ThisCall`
- `FastCall`
- `WinApi`
- `SystemV`
- `VectorCall`

They may become relevant later, but they add implementation scope and platform
branching immediately.

## 6) What V1 Should Support

The first version should support only:

- free functions
- static `extern` methods
- simple argument passing
- simple return values
- dynamic library loading by name
- symbol lookup by entry-point name

This is enough to unlock:

- system APIs
- libc-style calls
- narrow C-ABI bridges
- future UI shim libraries

### Current implementation status

The current repository implementation now covers more than the original first
vertical slice:

- `DllImport` method attributes are parsed on methods
- binder metadata is attached to `MethodSymbol`
- metadata is serialized into ILB method rows
- runtime metadata is deserialized again from ILB
- Linux shared-library loading works through `dlopen` / `dlsym`
- native calls are dispatched through a generic `libffi` call frame
- callbacks are supported through VM-created native trampolines for the current callback shapes
- owned UTF-8 string returns are supported when the import declares the ownership/free convention

What is implemented right now in the runtime:

- platform:
  - Linux / POSIX-style shared libraries
- symbol form:
  - free functions only
- calling conventions:
  - `Cdecl`
  - `StdCall` metadata is accepted, but the current runtime path does not differentiate code generation by platform ABI yet
- supported parameter types:
  - `Integer`
  - `Boolean`
  - `String`
  - `NativeHandle`
  - callback delegates currently shaped as `Integer -> Integer` or `Integer -> Void`
- supported return types:
  - `Integer`
  - `Boolean`
  - `Void`
  - `NativeHandle`
  - `String` when `StringReturn := StringReturn.Utf8Owned` and `StringFreeEntryPoint` is provided

The runtime no longer contains per-signature function-pointer dispatch paths.
It marshals each argument by FFI value kind, prepares an `ffi_cif`, and invokes
the native symbol through `ffi_call`. This keeps adding new supported shapes from
turning into a cascade of complete-signature special cases.

This is intentionally still an early interop system, not a complete ABI model.

## 7) What V1 Should Explicitly Exclude

The first version should **not** attempt to support:

- function pointers in user code
- C++ member functions
- C++ name-mangled direct imports
- struct/record by-value marshalling
- variadic native functions
- exception propagation across ABI boundary
- ordinal export lookup

Callback support exists for the current VM trampoline shapes, but user-facing
function-pointer values and arbitrary native callback signatures are still out
of scope.

These are all real features, but they should be layered on later.

## 8) Type Mapping Strategy

The first type mapping must be narrow and explicit.

### 8.1 Recommended V1 types

- `Integer`
- `Boolean`
- `String`
- `Void`
- `NativeHandle`

Optional but useful:

- `Pointer`

### 8.1.1 Actually implemented today

The runtime implementation currently supports:

- `Integer`
- `Boolean`
- `String` as inbound UTF-8 argument
- owned UTF-8 `String` returns when an explicit free entry point is declared
- `Void` return
- `NativeHandle` as an opaque runtime-managed native pointer handle
- callback delegates for the currently implemented VM trampoline shapes

Not implemented yet in the runtime path:

- `Pointer`
- byref native marshalling
- records/structs
- arbitrary native callback signatures

### 8.2 Suggested semantics

- `Integer`
  - maps to 32-bit signed integer
- `Boolean`
  - maps to 32-bit integer boolean for ABI simplicity in v1
- `Void`
  - no return value
- `NativeHandle`
  - opaque machine pointer represented by a VM reference-sized native value in the FFI layer
- `String`
  - should use one clearly documented string convention in v1

### 8.3 String policy

This is one of the most important design choices.

Recommended v1 policy:

- define `String` FFI marshaling as UTF-8, null-terminated, temporary call buffer
- inbound `String` arguments:
  - runtime allocates temporary UTF-8 memory for the call
- returned string pointers:
  - supported only when the declaration explicitly states that the native value
    is owned UTF-8 and provides a matching native free function

This avoids implicit ownership guessing.

If native code must return text in v1, prefer:

- explicit owned-string declarations with `StringFreeEntryPoint`
- caller-provided buffer patterns later
- dedicated shim functions
- explicit `NativeHandle`/`Pointer` handling where ownership is not string-like

## 9) Why Qt Still Needs a Shim

Even after `DllImport` exists, Qt is still not a direct v1 target.

Reason:

- Qt is a C++ framework
- uses classes, virtual methods, signals/slots, and metaobject behavior
- does not present itself as a clean C ABI for direct v1 FFI usage

So the likely Qt path remains:

- ILC `DllImport`
- C-compatible shim library
- shim calls Qt/C++

That is still a win:

- the ILC/runtime side stays generic
- the Qt-specific part is isolated in a backend shim

## 10) Binder Responsibilities

The binder should validate that `DllImport` is only used on declarations that fit v1 scope.

Recommended binder rules:

- method must be `extern`
- method must be `static`
- method must not have a body
- attribute target must be a method/function/procedure/constructor declaration
- constructors should be rejected in v1
- argument and return types must be within the supported FFI type set
- `params` should be rejected
- generic methods should be rejected
- instance methods should be rejected

The binder should then attach a structured FFI metadata object to the method symbol.

Suggested metadata shape:

- library name
- entry point
- calling convention

## 11) Lowering Responsibilities

The lowerer should not special-case specific imported functions by name.

Instead, it should produce a generic FFI call shape for methods marked with FFI metadata.

That implies:

- either a new IR opcode family for native calls
- or an extension of the current call target metadata

Recommended direction:

- add a distinct native-call IR concept

Reason:

- avoids pretending that native functions are normal VM functions
- keeps the bytecode/runtime boundary explicit

Current implementation note:

- the runtime dispatch is already generic through `libffi`;
- argument and return handling branches only by individual FFI value kind;
- complete method-signature cascades are intentionally avoided.

## 12) Bytecode Responsibilities

The bytecode layer should preserve enough data for runtime resolution.

That likely means storing, per imported method:

- library name
- entry point
- calling convention
- marshaling-relevant parameter metadata

This can be represented:

- in the method table
- or in a dedicated interop/import table

A dedicated table is likely cleaner long-term, but method-table extension may be enough for v1.

## 13) Runtime Responsibilities

The runtime must implement:

- shared-library loading
- symbol lookup
- native function invocation
- marshaling for supported v1 types
- callback trampoline registration for supported callback delegate shapes

Platform-specific examples:

- Linux/macOS:
  - `dlopen`
  - `dlsym`
- Windows:
  - `LoadLibrary`
  - `GetProcAddress`

The runtime should also define:

- failure behavior when a library cannot be loaded
- failure behavior when a symbol cannot be found
- failure behavior when a signature is unsupported

Recommended v1 behavior:

- fail fast with explicit runtime errors
- emit `ILC_VM_FFI_DEBUG=1` / callback debug diagnostics around normalized
  signatures, marshalled arguments, return values, and trampoline failures

## 14) Error Model

Recommended binder errors:

- `DllImport` used on non-`extern` method
- unsupported parameter type
- unsupported return type
- unsupported use on generic/instance/constructor member
- missing required metadata

Recommended runtime errors:

- library load failure
- symbol resolution failure
- unsupported calling convention on the active platform
- unsupported marshaling case

## 15) Relationship to Future ILC Library Import

Compiled ILC library import should **not** be designed as part of this first FFI step.

That should later become one of:

- normal `uses` plus linker/module resolution
- a dedicated `IlcImport`
- another purpose-built module import mechanism

This distinction matters:

- `DllImport` is for native ABI interop
- compiled ILC library loading is a language/runtime module-system problem

They should not share a name just because both involve “external code”.

## 16) Example First-Scope Use Cases

Good v1 FFI targets:

- libc helper calls
- OS process/file helpers exposed through C ABI
- tiny native shim libraries
- Qt bridge shim

Bad v1 FFI targets:

- direct Qt class interop
- arbitrary callback-heavy APIs
- complex struct-heavy native SDKs
- graphics APIs with large pointer/ownership surfaces

## 17) Recommended Implementation Order

1. define `DllImport` syntax and attribute binding
2. add `CallingConvention` enum
3. validate legal `extern` declarations in binder
4. add FFI metadata to method symbols
5. extend IR for native-call representation
6. extend bytecode/module format for FFI imports
7. implement runtime dynamic loading + invocation for Linux first
8. add compiler/runtime smoke tests using a tiny native test library
9. build higher-level native integrations such as UI backends over a C ABI shim
10. add Windows support

## 18) Strategic Summary

The correct first interop move for ILC is:

- `DllImport`
- C#-style attribute syntax
- C-ABI scope
- small supported type set
- explicit calling conventions
- generic `libffi` runtime dispatch rather than signature-specific VM code
- no attempt to solve compiled ILC library import yet

This gives ILC a real native interop foundation without overloading the design
with future concerns too early.
