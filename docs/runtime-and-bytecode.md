# Runtime and Bytecode

This document covers the runtime and executable representation implemented in this
repository at the current stage. It focuses on how compiled programs flow into the
native runtime and how the VM executes them.

## 1) Pipeline and Responsibility Split

```text
ILC source
  -> compiler frontend (syntax/binding/lowering)
  -> bytecode emission (.ilb)
  -> ilcvm_core loader
  -> virtual machine execution
```

The compiler pipeline lives in C# projects under `src/`.
The runtime and VM runtime services live in C++ under `runtime/`.

The key responsibility split is:

- compile to a stable module format (`.ilb`) that is platform-independent;
- execute that module in a native runtime with a register machine;
- keep host platform integration in a dedicated host abstraction.

## 2) ILB Module Format (`.ilb`)

The emitted image has:

- a fixed header (`"ILB1"`, version `1.0`);
- a section directory;
- section payloads;
- a small checksum in header positions.

Current section kinds in the emitter:

- string table
- blob table
- type table
- field table
- method table
- code section
- optional exception table
- optional interface dispatch table
- optional entry point

Important implementation facts:

- string and blob tables are serialized as length-prefixed tables with counts;
- method table rows carry owner type, method flags, register/argument layout,
  return type, code offsets and host-import mapping;
- type table rows retain type kind/flags, base type id, and declared field/method
  ranges for runtime metadata consumers;
- interface dispatch rows retain `concrete type -> interface type -> interface method -> implementation method`
  mappings for later interface-call support;
- code section stores fixed-size instructions;
- the method section is referenced by `entry_point` if present;
- loader validates sizes, alignment, and section bounds before execution;
- missing required sections throw at load time.

Runtime does not currently trust external metadata: invalid section sizes, unsupported
versions, bad offsets, malformed references and bad section kinds result in exceptions
before any user code starts.

## 3) Register Machine and Value Model

The VM is register-based. Each function has:

- register count
- argument count
- return presence
- instruction list
- exception handlers

### 3.1 Value Representation

Runtime values use 32-bit VM registers for integers and encoded references:

- positive or zero integers
- encoded references as negative integers using low-bit tagging:
  - arrays: `-(id * 4)`
  - strings: `-(id * 4 + 1)`
  - objects: `-(id * 4 + 2)`
- `0` is treated as null for reference checks.

This is intentionally compact and makes checks cheap in the interpreter.  
Runtime type checks are performed on use-sites (e.g. string op, array op, field op).

### 3.2 Function Framing

- static vs instance is part of compiler metadata;
- the VM receives argument values in contiguous registers;
- non-void methods keep the return value in a known register position.

Current runtime check behavior:

- mismatch in argument count throws at call time,
- receiver null checks for instance/virtual calls throw before dispatch,
- wrong receiver type for fields or methods throws.

## 4) Instruction Model

Current opcode groups:

- integer ALU (`ld_i32`, `mov`, `add/sub/mul/div/mod`, shifts, bit ops)
- integer and reference comparisons
- string operations (`concat`, `starts_with`, `ends_with`, `contains`, index methods,
  replace/insert/remove/substr, case conversion, trim helpers, conversion to/from int)
- object/array/field ops (`new_obj`, `new_arr`, `ld_*`/`st_*`)
- control flow (`br`, `br_false`, `ret`)
- method calls (`call`, `call_virt`)
- exception ops (`throw`, `rethrow`)

The bytecode instruction width in memory is fixed-width in `.ilb` (destination/left/right/immediate + opcode),
which is why the loader can decode deterministically in fixed steps.

## 5) Host Import and Shipped Library Boundary

The VM exposes host functionality through `HostImportKind` and maps these to runtime
services in `ilcvm_host`.

Supported host mappings include:

- console write/read
- process command line args
- environment access and mutation
- filesystem read/write/append/query
- time retrieval
- path helpers

Shipped ILC libraries use these imports for `System.*` APIs instead of embedding
platform code in compiler output. This keeps language surface, runtime implementation
and host integration separate.

## 6) Execution Core

The VM execution loop is interpreter-based:

- decode/dispatch one instruction
- mutate register state
- handle branches and returns
- catch managed exceptions (internal exception type) and route them through
  function-level handler tables

### 6.1 Calls

Call behavior:

- `call` executes a known callee directly
- `call_virt` goes through virtual dispatch machinery
- both support return suppression for `void` methods
- host imports are executed in-process via `StandardHostServices` in CLI mode

Interface dispatch note:

- the ILB/runtime metadata path preserves interface dispatch mappings
- the VM now uses that table for interface method calls and getter-/setter-backed
  interface property access through interface-typed receivers
- inherited interfaces are resolved through the same mapping path, so sub-interface
  receivers can dispatch base-interface members as well
- reference `is` / `as` checks now execute in the VM against runtime type metadata
  instead of degrading to a pure non-`nil` check

### 6.2 Exceptions

Exception metadata is represented by try/catch handler ranges and one handler kind.
When an exception is thrown:

- VM searches matching handler ranges by instruction pointer range;
- supports `catch (Type)` and catch-all behavior by type filter;
- stores exception value in optional target register when present;
- rethrows unhandled exceptions as runtime errors.

## 7) Runtime Object Model

Runtime object model is intentionally minimal and currently non-generalized:

- static fields are stored in a flat vector for direct index access
- object instances are vectors of integer fields plus a type id
- array values are contiguous `int32` vectors
- strings are host C++ `std::string` handles in the module string table side storage

Allocation behavior:

- object allocation records requested field count based on type metadata
- array allocation validates non-negative length
- allocation counters are tracked in profiling

There is no moving GC in the current runtime implementation; allocation is append-only in
runtime-owned containers.

## 8) Benchmarking and Profiling

The runtime CLI (`runtime/ilcvm_cli`) supports a dedicated `--performance` mode.
It reports counters in `performance.*` form, including:

- total ns
- host import ns
- call and virtual-call timing
- array read/write/len timing
- instruction/branch/compare/move counts and ns
- allocation counters (`new_arr`, `new_obj`, strings created, arrays created, objects created)
- fast-path counters and timing

This is currently used by the dedicated benchmark suite to compare against JVM and
identify optimization candidates.

## 9) Current Optimizations and Hot Paths

The VM includes focused interpreter fast-paths for certain small call shapes:

- specialized leaf calls
- known arithmetic loops in benchmark-style leaf functions
- fast path accounting that separates specialized behavior from interpreter path

These optimizations are currently correctness-safe and intentionally narrow; they are
used to remove dispatch overhead where behavior is static and verified by benchmarks.

## 10) Current Limitations

- runtime does not yet implement:
  - JIT or dynamic code generation
  - advanced GC strategies beyond simple allocation accounting
  - wide integer arithmetic/representation in VM value model
- host mapping is intentionally constrained to currently implemented APIs
- exception system is functional but still minimal compared to larger managed runtimes

## 11) Native FFI Runtime Path

`DllImport` calls are a separate runtime path from built-in host imports.

Current behavior:

- native libraries are loaded through the platform dynamic loader on Linux;
- symbols are resolved by the imported entry point;
- calls are invoked through a generic `libffi` call frame;
- arguments are marshalled by individual FFI value kind rather than by complete
  method-signature special cases;
- `NativeHandle` values represent opaque native pointers;
- owned UTF-8 string returns require explicit metadata and a native free entry point;
- callbacks are exposed to native code through VM-created trampolines for the
  currently supported callback shapes.

The VM must not grow UI-specific host imports for graphical backends. The current
Qt Quick backend uses this FFI path through a small C ABI shim instead.

## 12) Practical Consequences for Future Work

This runtime shape is stable enough for:

- adding more opcodes in `BytecodeModel`/interpreter together;
- extending exception and metadata tables;
- preparing constant/metadata-heavy integer paths before runtime widening;
- later introducing a compiler/runtime verifier split if execution throughput must
  move beyond interpreter-only.

The current architecture intentionally keeps the boundary between language features
and runtime capability explicit. That boundary is the main lever for safe evolution.
