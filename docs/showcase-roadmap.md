# Showcase Roadmap

This document defines a pragmatic showcase-oriented implementation order for ILC.
It complements the broader [roadmap.md](./roadmap.md) by focusing on what should be
demonstrable early when presenting the language to others.

The goal is not just language completeness.
The goal is to show, step by step, that ILC can already build realistic applications
across different domains:

- console applications;
- native interop scenarios;
- networking tools and services;
- data-driven applications;
- graphical applications.

## 1) Showcase principles

When choosing what to implement next, prefer features that are:

- easy to explain in a short demo;
- useful in real applications;
- foundational for later capability growth;
- testable in the existing bootstrap and local verification flow.

A feature is more valuable for showcase purposes when it unlocks multiple later stories.

Example:

- `DllImport` is more valuable than a single UI control,
  because it unlocks native libraries, operating-system integration, database bindings,
  networking shims, and later UI backends.

## 2) Current showcase baseline

ILC can already demonstrate:

- console applications;
- text formatting and processing;
- collections and generics;
- file and path handling;
- diagnostics and tracing;
- generic `foreach`;
- `out` / `ref`;
- native interop v1 through `DllImport`.

This is already enough for a credible “language core + standard library + FFI”
presentation.

## 3) Recommended showcase order

### Stage 1. Native interop hardening

Priority: very high

Why:

- native interop is the bridge to many other showcase areas;
- it makes the language feel connected to the host platform;
- it reduces pressure to hard-code every integration into the runtime first.

Recommended next work:

- `NativeHandle` or `Pointer` in `System`
- pointer/handle parameters and return values in FFI
- first callback design notes
- one small native demo library or C shim for controlled examples

Good demo outcomes:

- call operating-system or libc functions;
- hold a native handle;
- open/close an external resource through FFI.

### Stage 2. JSON and data exchange

Priority: very high

Why:

- almost every modern application exchanges structured text data;
- JSON demos are easy to understand;
- networking and web features become much more useful once JSON exists.

Recommended scope:

- `System.Text.Json` or equivalent shipped module
- parse object/array/string/number/boolean/null
- build JSON values programmatically
- basic serialization of simple structures later

Good demo outcomes:

- read a config file;
- parse an HTTP response body;
- generate JSON for logs or APIs.

### Stage 3. Networking foundation

Priority: high

Why:

- modern languages are expected to talk to other systems;
- networking demos are highly visible;
- HTTP and WebSocket features depend on a lower-level transport story.

Recommended scope:

- TCP client
- TCP listener/server
- hostname/port utilities
- timeouts later if needed

Good demo outcomes:

- echo client/server;
- port probe;
- simple remote command or chat-style example.

### Stage 4. HTTP

Priority: high

Why:

- HTTP is the most recognizable application-layer protocol;
- it immediately enables API and service demos;
- it builds naturally on networking + JSON.

Recommended scope:

- minimal HTTP client first
- later a minimal HTTP server
- headers, status code, body, URL helpers

Good demo outcomes:

- fetch JSON from a remote service;
- serve a small local endpoint;
- build a tiny CLI API client.

### Stage 5. Threading and synchronization

Priority: high

Why:

- a modern language is expected to support concurrency;
- this is needed for serious UI, networking, and background work;
- it is more foundational than jumping directly to higher-level async syntax.

Recommended scope:

- `Thread`
- `Lock` / monitor-style synchronization
- wait/signal primitive
- thread-safe queue later

Good demo outcomes:

- background worker example;
- producer/consumer example;
- parallel processing of a file or collection.

Important note:

- the raw thread substrate and first `Task` / `Task<T>` abstractions are now in place;
- `async` / `await` should still come later on top of the stabilized runtime model.

### Stage 6. SQLite / local persistence

Priority: medium-high

Why:

- local persistence makes demos feel like real applications;
- SQLite is compact, portable, and widely understood;
- this is an excellent FFI-based showcase target.

Recommended scope:

- thin SQLite wrapper over native interop
- open database
- execute statement
- step through rows
- basic typed reads

Good demo outcomes:

- todo app backend;
- settings database;
- query local data from a console app.

### Stage 7. WebSockets

Priority: medium

Why:

- useful and modern;
- good for interactive demos;
- better tackled after TCP/HTTP/JSON are already present.

Recommended scope:

- WebSocket client first
- text frames first
- server support later

Good demo outcomes:

- live status client;
- chat or event stream demo;
- integration with browser tooling.

### Stage 8. UI foundation

Priority: medium

Why:

- UI is one of the most visible showcase targets;
- but it depends on stable interop, handles, lifetime rules, and probably callbacks/events.

Recommended scope:

- finalize FFI primitives needed by a backend bridge
- build a minimal `System.Ui` object model
- target one backend first
- prefer a backend strategy over hard-coded runtime UI hooks
- keep the UI backend outside the VM and bridge it through `DllImport`
- use a small C ABI shim in front of Qt / C++

For this stage, the FFI baseline should be:

- primitive types: transparent
- enums: transparent with fixed `Int32` representation
- strings: transparent with explicit encoding + ownership rules
- records: transparent only when blittable / POD-like
- callbacks: transparent via VM trampolines with explicit registration / lifetime rules
- complex foreign types: opaque handles / pointers

This keeps the UI boundary stable while avoiding UI-specific runtime contracts.

Good demo outcomes:

- hello window;
- button click + state update;
- small form or list application.

### Stage 9. Higher-level async model

Priority: medium

Why:

- modern language users expect async workflows;
- but it should be built on top of proven call, thread, and synchronization behavior.

Recommended scope:

- `Task`
- `Task<T>`
- cancellation model later
- `async` / `await` only after the runtime model is clear

Good demo outcomes:

- concurrent HTTP requests;
- background IO without blocking UI;
- simple pipeline or scheduled jobs.

## 4) Recommended near-term sequence

For the next implementation window, the most sensible sequence is:

1. Extend FFI with `NativeHandle` / pointer-style support.
2. Add JSON.
3. Add TCP networking.
4. Add minimal HTTP client support.
5. Add basic thread + lock primitives.

Reason:

- this gives a strong “systems + application + integration” story quickly;
- every step feeds later UI and service features;
- none of these steps requires prematurely committing to a full UI backend.

## 5) Showcase bundles

To make progress easy to present, group features into small demo bundles.

### Bundle A. Console and tooling

- diagnostics
- text formatting
- collections
- file/path helpers

Suggested demo:

- a small CLI utility that reads files, formats output, and writes logs.

### Bundle B. Native integration

- `DllImport`
- primitive native calls
- handle/pointer support

Suggested demo:

- call a native library, manipulate a native resource, print the result.

### Bundle C. Connected app

- JSON
- TCP
- HTTP

Suggested demo:

- fetch remote JSON, parse it, and display formatted results in the console.

### Bundle D. Data app

- SQLite
- collections
- diagnostics

Suggested demo:

- store and query local application data.

### Bundle E. Interactive app

- threading
- UI
- HTTP or WebSockets

Suggested demo:

- small UI app with background work and live updates.

## 6) What not to prioritize too early

These may be useful later, but they are not the best immediate showcase investments:

- large widget/control families before the backend story is ready
- broad async syntax before the runtime concurrency model is stable
- too many niche collection types before networking/data/UI needs demand them
- platform-specific UI features before a neutral backend boundary exists

## 7) Working definition of showcase readiness

A showcase area is ready when:

- a bootstrap or dedicated smoke demonstrates it end-to-end;
- docs explain the supported surface clearly;
- the demo is short enough to explain live without deep caveats;
- the capability composes naturally with other implemented areas.

That last point matters most.
A feature becomes especially valuable once it helps demonstrate several others.
