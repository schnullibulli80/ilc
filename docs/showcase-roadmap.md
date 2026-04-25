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
- native interop through `DllImport`;
- JSON parsing/building;
- TCP, HTTP, and WebSocket client smokes;
- threading and first task primitives;
- a Qt Quick UI smoke with callbacks and state-driven refresh.

This is already enough for a credible “language core + standard library + FFI +
native UI bridge” presentation.

## 3) Recommended showcase order

### Stage 1. Native interop hardening

Status: largely implemented for the current Linux/C-ABI slice

Why:

- native interop is the bridge to many other showcase areas;
- it makes the language feel connected to the host platform;
- it reduces pressure to hard-code every integration into the runtime first.

Implemented baseline:

- `NativeHandle` in `System`
- primitive, string, handle, and owned UTF-8 string return marshalling
- generic `libffi` runtime dispatch instead of per-signature VM dispatch
- VM callback trampolines for the current integer callback shapes
- native C ABI shim usage through the Qt Quick bridge

Good demo outcomes:

- call operating-system or libc functions;
- hold a native handle;
- open/close an external resource through FFI.

Remaining hardening:

- Windows shared-library loading path
- records/POD marshalling
- richer callback lifetime policy
- broader native test matrix

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

Status: first Qt Quick showcase slice implemented

Why:

- UI is one of the most visible showcase targets;
- but it depends on stable interop, handles, lifetime rules, and probably callbacks/events.

Implemented scope:

- FFI primitives needed by the Qt bridge
- minimal `System.Ui` object model
- `System.Ui.Hosting`
- `System.Ui.Backends.QtQuick`
- Qt Quick native bridge in `libilc_qtbridge.so`
- UI backend outside the VM, bridged through `DllImport`
- C ABI shim in front of Qt / C++
- dedicated UI smoke script

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
- checkbox, textbox, slider callbacks;
- full declarative refresh from ILC state;
- small form-style Qt Quick demo.

Current UI smoke:

```bash
ILC_QTBRIDGE_DEBUG=1 scripts/run-ui-qtquick-smoke.sh
```

Current active UI refresh behavior:

- interactions dispatch from QML through the native bridge into an ILC callback;
- ILC state and commands are updated;
- the Qt Quick backend regenerates QML from the neutral UI model;
- logs report `refresh mode=full-refresh`;
- the experimental incremental patch path is intentionally inactive.

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

1. Harden the current FFI/Qt bridge diagnostics and docs.
2. Add a small list/data UI demo once `ListView` is ready.
3. Start SQLite/local persistence over `DllImport`.
4. Add a connected UI demo that combines HTTP/WebSocket + UI.
5. Continue async/task work on top of the existing threading substrate.

Reason:

- the basic systems/application/integration story already exists;
- the next showcase value comes from composition;
- UI should remain backend-neutral and driven by the generic FFI bridge.

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
