# UI Architecture

This document defines a pragmatic first architecture for graphical applications in ILC.
It is intentionally platform-neutral at the API surface, while still assuming that
the first realistic backend should be an existing mature UI toolkit rather than a
fully custom renderer.

The main goal is:

- keep ILC application code platform-independent;
- avoid hard-coding desktop GUI behavior directly into the language runtime;
- enable a first real backend quickly;
- leave room for multiple backends later.

## 1) Design Direction

The recommended direction is:

- define a small, stable, platform-neutral ILC UI API;
- implement that API over one real backend first;
- treat rendering/input/windowing as backend responsibilities;
- keep ILC focused on application structure, state, commands, and binding.

This means ILC should not try to implement a full native control toolkit per OS
as the first step.

Instead:

- `System.Ui` defines the user-facing API;
- backend adapters map `System.Ui` objects to a real UI toolkit;
- the first backend should target Qt Quick / QML.

## 2) Why Not a Direct Native-OS Mapping First

A direct mapping from ILC controls to each operating system's native widget APIs
looks attractive, but scales poorly very quickly.

A true native-per-OS stack would require:

- Windows backend
- Linux backend
- macOS backend
- Android backend
- layout engine integration
- focus/input semantics
- font/text handling
- styling/theming
- accessibility
- DPI/scaling
- image/asset loading
- lifecycle and threading rules per platform

That is far too much for a first graphical stack.

For ILC, the better tradeoff is:

- one neutral UI model;
- one strong first backend;
- additional backends only after the API proves itself.

## 3) Recommended First Backend

The recommended first backend is:

- Qt 6
- Qt Quick / QML

Why:

- cross-platform on Windows, Linux, macOS, and Android;
- mature UI/runtime/tooling ecosystem;
- good fit for declarative and adaptive UIs;
- strong separation between application logic and presentation;
- much cheaper than building a renderer/toolkit from scratch.

Qt Widgets is still a valid secondary backend for more traditional desktop apps,
but Qt Quick / QML is the better first backend for a modern, backend-driven ILC UI model.

## 4) Layering

The proposed stack is:

1. `System.Ui`
2. `System.Ui.Hosting`
3. `System.Ui.Binding`
4. `System.Ui.Controls`
5. `System.Ui.Layout`
6. `System.Ui.Backends.QtQuick`

Responsibilities:

- `System.Ui`
  - core UI object model
- `System.Ui.Hosting`
  - backend abstractions and application bootstrap
- `System.Ui.Binding`
  - state propagation, change notification, commands
- `System.Ui.Controls`
  - platform-neutral control classes
- `System.Ui.Layout`
  - platform-neutral layout containers
- `System.Ui.Backends.QtQuick`
  - actual mapping to Qt/QML objects

Optional future layers:

- `System.Ui.Markup`
- `System.Ui.Media`
- `System.Ui.Navigation`

These should not be part of the first MVP.

## 5) Core API Model

The first API should stay small.

### 5.1 Base types

Recommended first core types:

- `Application`
- `Window`
- `View`
- `Container`
- `Page`

`View` should be the base class for visible UI elements.

`Container` should be a `View` with children.

`Window` should own one root view, typically via:

- `Content: View`

### 5.2 Controls

Recommended first control set:

- `TextBlock`
- `Button`
- `TextBox`
- `ListView`

Recommended first container/layout set:

- `StackPanel`

This is enough for:

- a simple hello-world app
- forms
- command-driven interaction
- simple lists

Avoid adding large control families too early.

### 5.3 Layout primitives

Recommended first layout types:

- `Orientation`
- `HorizontalAlignment`
- `VerticalAlignment`
- `Thickness`
- `Size`
- `Visibility`

These should stay toolkit-neutral.

## 6) State and Binding

Without a minimal state model, the UI layer will become awkward immediately.

Recommended first state/binding types:

- `ObservableObject`
- `ICommand`
- `Command`

Optional later:

- `ObservableList<T>`
- `Binding`
- `DataContext`

### 6.1 `ObservableObject`

`ObservableObject` should be the base class for view-model-like objects.

It should provide:

- property change notification

The exact mechanism can stay simple at first. The important part is that the UI backend
has a single consistent hook for state changes.

### 6.2 `Command`

Commands should be the first-class way to connect UI actions to application logic.

Recommended minimal shape:

- `Execute()`
- optional `CanExecute(): Boolean`

This is a better first abstraction than exposing backend-specific click handlers directly.

## 7) Backend Boundary

The neutral UI layer should not know about QML, QWidget, DOM, or native handles.

Instead, define a backend boundary such as:

- `IUiBackend`
- `IWindowHost`
- `IViewHost`

Possible first responsibilities:

- create application
- create window
- attach root view
- start event loop
- reflect property changes into backend objects

The backend is responsible for:

- rendering
- event delivery
- native window lifecycle
- platform integration

The neutral UI model is responsible for:

- object structure
- state
- commands
- binding metadata

## 8) Qt Quick / QML Mapping

The first backend should map the neutral controls onto Qt Quick primitives.

Recommended mapping:

- `Window` -> `ApplicationWindow`
- `StackPanel` -> `ColumnLayout` / `RowLayout`
- `TextBlock` -> `Label` or `Text`
- `Button` -> `Button`
- `TextBox` -> `TextField`
- `ListView` -> `ListView`

The ILC side should provide:

- window metadata
- view hierarchy
- control properties
- list/model data
- commands

Qt/QML should provide:

- rendering
- input/focus
- styling
- animation
- platform integration

## 9) Imperative First, Declarative Later

The first usable ILC UI layer should be imperative/object-based, not markup-first.

That means code like:

```ilc
var window := new Window();
var layout := new StackPanel();
var title := new TextBlock();
var button := new Button();

title.Text := 'Hello ILC';
button.Text := 'Click';

layout.Children.Add(title);
layout.Children.Add(button);
window.Content := layout;
```

This avoids blocking the UI effort on:

- a markup parser
- a loader
- declarative binding syntax
- template infrastructure

Declarative markup can be added later once the runtime object model is stable.

## 10) MVP Scope

The first milestone should be intentionally small.

### 10.1 MVP goals

- open a window
- render text
- render a button
- handle a button command
- support one vertical layout container
- support one textbox
- support one list view

### 10.2 MVP type set

- `Application`
- `Window`
- `View`
- `Container`
- `StackPanel`
- `TextBlock`
- `Button`
- `TextBox`
- `ListView`
- `ObservableObject`
- `Command`

### 10.3 MVP backend

- Qt Quick / QML only

No second backend should exist before the first one is stable and useful.

## 11) What To Defer

Do not include these in the first UI milestone:

- custom rendering primitives
- canvas
- menus/toolbars
- dialogs
- drag and drop
- full styling/theme system
- accessibility
- rich text
- advanced layout families
- animations in the neutral API
- desktop notifications
- platform-specific shell integration
- mobile-specific APIs

These can all come later, but they should not define the first architecture.

## 12) Suggested Namespace Layout

Recommended initial namespace layout:

- `System.Ui`
- `System.Ui.Controls`
- `System.Ui.Layout`
- `System.Ui.Binding`
- `System.Ui.Hosting`
- `System.Ui.Backends.QtQuick`

This keeps the core API understandable and gives the backend a clear home without
polluting the neutral surface.

## 13) First Implementation Plan

Recommended implementation sequence:

1. define `System.Ui` base classes and enums
2. add `ObservableObject` and `Command`
3. add the MVP controls and `StackPanel`
4. define backend hosting interfaces
5. implement a minimal Qt Quick backend
6. build a `Hello UI` sample
7. add one interaction sample with state + command
8. add one list-binding sample

Only after that should the project consider:

- markup
- advanced binding
- more controls
- additional backends

## 14) Strategic Summary

The right first move for graphical ILC applications is not:

- a fully custom renderer
- or a separate native widget stack per operating system

The right first move is:

- a small, platform-neutral ILC UI model
- mapped onto one strong, cross-platform backend
- with Qt Quick / QML as the first implementation target

That gives ILC:

- a credible cross-platform GUI story
- a manageable implementation scope
- and a path to future backend plurality without committing too early to an OS-specific UI stack
