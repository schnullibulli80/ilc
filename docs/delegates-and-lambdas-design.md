**Delegates And Lambdas**
This document defines the recommended design direction for first-class callable values in ILC.

The goal is not only to add a convenience feature.
The goal is to unlock the next class of language-native APIs:

- query/data pipelines;
- callback-based networking and UI APIs;
- richer `Task` composition;
- later `async` / `await`;
- eventually language-integrated data processing beyond raw SQL strings.

**Why This Matters Now**
The current showcase roadmap still lists SQLite / local persistence as the next obvious data-app step.
That would be easy to implement with raw SQL strings, but it would not match the longer-term direction of ILC as a language with a more integrated, set-oriented programming model.

If ILC is meant to grow toward language-native data processing, then the next honest prerequisite is:

- first-class callable values;
- closures over surrounding state;
- a delegate model that APIs can target consistently.

Without that, the language remains unnecessarily constrained in:

- filtering and projection APIs;
- event and callback surfaces;
- continuation-style async composition;
- future provider-backed query translation.

**Current Status**
Today ILC supports:

- methods, instance methods, constructors, properties;
- interfaces such as `IRunnable` / `ITaskRunnable<T>`;
- interface dispatch in the runtime;
- managed thread and `Task` substrates;
- generic collections and generic `foreach`.

What it does **not** support yet:

- first-class function values;
- anonymous functions;
- method group conversions;
- closures over local variables;
- generic APIs that accept predicates/selectors as language-native callables.

That means the language can model “callable behavior” today only through explicit interface objects such as `IRunnable`.

**Design Goals**
The first delegate/lambda cut should be:

- small enough to implement incrementally;
- strong enough to support real library APIs;
- compatible with the current object/interface-based runtime model;
- expressive enough to serve as the basis for later query and async work.

The design should avoid introducing a second unrelated callable mechanism later.

**Recommended Implementation Order**
1. Add nominal delegate types to the language.
2. Add method-group-to-delegate conversion.
3. Add anonymous functions / lambdas without capture.
4. Add captured-variable closures.
5. Add shipped library surfaces that consume delegates.
6. Only then design higher-level query operators and data pipelines.

This order gives the compiler and runtime a stable callable model before layering more syntax and library complexity on top.

**1. Delegate Type Model**
The recommended first-class callable abstraction is a nominal delegate type.

Proposed syntax:

```ilc
public delegate function Comparison(left: Integer; right: Integer): Integer;
public delegate method Action;
public delegate function Predicate<T>(value: T): Boolean;
public delegate function Selector<TSource, TResult>(value: TSource): TResult;
```

Recommended semantics:

- a delegate declares exactly one callable signature;
- delegate types are reference types;
- delegate types may be generic;
- delegates can be stored in fields, locals, arrays, properties, and parameters;
- equality semantics can be reference identity in v1.

This should be treated as a language-level type declaration, not just a library convention.

**Why Nominal Delegates First**
A nominal delegate type gives several benefits over trying to start with structural function types:

- easier binding and overload resolution;
- simpler runtime representation;
- clearer library surface in shipped modules;
- easier interop with future events and UI backends;
- a stable target for method groups and lambdas.

Structural function types can be reconsidered much later if they are still desirable.

**2. Invocation Syntax**
Delegates should use normal call syntax:

```ilc
var keep := predicate(item);
var mapped := selector(value);
```

No separate `Invoke(...)` syntax should be required for normal use.

Internally, v1 can still lower to a synthetic `Invoke` method.

Recommended member shape:

- every delegate has a callable signature exposed by the language;
- the compiler may synthesize an `Invoke(...)` method in metadata/bytecode;
- source code keeps normal function-call syntax.

**3. Method Group Conversion**
The first practical conversion should be a method-group conversion:

```ilc
function IsPositive(value: Integer): Boolean;
begin
  return value > 0;
end;

var predicate: Predicate<Integer> := IsPositive;
```

Supported in the first cut:

- static methods;
- instance methods on an explicit receiver, e.g. `obj.Transform`;
- exact signature match only.

Deferred:

- open instance method groups;
- variance;
- overload groups requiring complex inference.

This keeps the binder tractable in v1.

**4. Lambda Syntax**
Recommended source syntax:

```ilc
var predicate := (value) => value > 0;
var sum := (left; right) => left + right;
var action := () => Trace.WriteLine('tick');
```

For multi-statement bodies:

```ilc
var work := (value) =>
begin
  var next := value + 1;
  return next * 2;
end;
```

Recommended rules:

- parameter types may be inferred from the target delegate type;
- explicit parameter types may be allowed later, but are not required in v1;
- expression-bodied and block-bodied lambdas should share one semantic model;
- lambda expressions require a target delegate type for binding in the first cut.

This “target-typed first” rule is a good bootstrap compromise and avoids complex independent type inference.

**5. Capture Semantics**
There are two stages here.

**Stage A: Non-capturing lambdas**

These can be lowered to:

- synthetic static methods;
- plus delegate instances pointing at those methods.

Example:

```ilc
var predicate: Predicate<Integer> := (value) => value > 0;
```

This is the cheapest useful first implementation.

**Stage B: Capturing lambdas**

Example:

```ilc
var threshold := 10;
var predicate: Predicate<Integer> := (value) => value > threshold;
```

Recommended lowering model:

- synthesize a closure class;
- captured locals become fields;
- the lambda body becomes an instance method on that closure class;
- create a closure object at runtime;
- create a delegate bound to that closure instance.

This is the most compatible design with the current ILC object/runtime model.

**Capture Lifetime**
Captured locals should be treated as shared closure state, not copied snapshots.

That means:

```ilc
var count := 0;
var next := () =>
begin
  count := count + 1;
  return count;
end;
```

should observe `count` as closure state across calls.

This is important for:

- query builder patterns;
- event handlers;
- UI callbacks;
- future iterator/async transformations.

**6. Runtime Representation**
The first delegate runtime shape should be explicit and simple.

Recommended stored parts:

- target object handle or `nil` for static target;
- target method id;
- delegate type id.

That is enough for:

- invoking a delegate;
- storing it as an object reference;
- later extending toward combined/multicast delegates if desired.

Suggested runtime strategy:

- represent delegate instances as managed objects;
- reserve a distinct runtime-recognized delegate type flag or nominal base shape;
- invocation bytecode can call a synthetic `Invoke` method or a dedicated delegate-call path.

**7. Lowering Strategy**
Recommended compiler approach:

**Phase 1**

- bind delegate declarations as named types with one callable signature;
- lower method-group conversions to delegate construction;
- lower non-capturing lambdas to synthetic methods + delegate construction.

**Phase 2**

- introduce synthesized closure types;
- rewrite captures to closure fields;
- lower captured lambdas to closure instance + bound delegate.

This keeps the first implementation checkpoint small while preserving the long-term model.

**8. Binder Rules**
Recommended initial binder rules:

- assignment/conversion to delegate requires exact arity;
- parameter passing modes must match exactly;
- return type must match exactly;
- lambda without target delegate type is rejected in v1;
- method group conversions do not try advanced overload resolution in v1.

This should keep diagnostics predictable.

Examples of useful early diagnostics:

- “Lambda expression requires a target delegate type.”
- “Method group does not match delegate signature.”
- “Captured variable cannot be used before closure lowering support is enabled.” (if Stage A lands before Stage B)

**9. Bytecode / VM Direction**
The runtime likely needs one of two paths:

1. delegate objects with normal `CallVirt Invoke`
2. a dedicated delegate-call opcode later

Recommended first choice:

- use normal object/reference semantics;
- synthesize/emit callable metadata in a way the existing dispatch model can consume.

Reason:

- it minimizes VM disruption;
- it keeps delegates close to the existing type/object model;
- it lets the compiler do most of the heavy lifting first.

If performance later requires it, a dedicated opcode can be added without changing source semantics.

**10. Standard Library Impact**
Once delegate types exist, the shipped library can start exposing more language-native APIs, for example:

```ilc
public delegate function Predicate<T>(value: T): Boolean;
public delegate function Selector<TSource, TResult>(value: TSource): TResult;
public delegate function Action;
public delegate function Action<T>(value: T): Void;
```

Then later:

- `List<T>.Find(predicate)`
- `Enumerable.Where(source, predicate)`
- `Enumerable.Select(source, selector)`
- event/callback APIs in UI and networking
- continuation APIs on `Task` / `Task<T>`

This is why delegates should be treated as a platform feature, not just syntax sugar.

**11. Query / Data Direction**
The reason this matters for data work is strategic.

If ILC eventually wants a language-native, set-oriented data model rather than raw SQL strings everywhere, then it will need:

- predicates;
- selectors;
- projections;
- ordering/grouping expressions;
- later perhaps an analyzable query AST.

Delegates/lambdas are the first honest prerequisite.

They do **not** solve query translation alone, but they create the language surface on which a later query system can be designed.

Without them, any “LINQ-like” direction is mostly blocked.

**12. What Is Explicitly Deferred**
These should not be mixed into the first delegate/lambda cut:

- expression trees;
- provider-backed query translation;
- multicast delegates/events;
- async lambdas;
- closure serialization or distributed execution;
- variance and complex overload selection;
- full functional-programming features.

Those are real future directions, but they should not dilute the first implementation slice.

**13. Recommended First Milestone**
The first concrete milestone after this document should be:

1. syntax + symbols for delegate declarations;
2. runtime representation for delegate instances;
3. method-group conversion for exact matches;
4. non-capturing lambdas only;
5. one shipped demo API that consumes a delegate.

A good first demo API would be something like:

- `List<T>.Find(predicate)`
- or `Task.ContinueWith(action)`

That would give an immediately visible payoff without requiring the full future query model yet.

**14. Recommended Second Milestone**
After that:

1. captured lambdas;
2. closure classes;
3. delegate parameters in more library APIs;
4. early pipeline helpers (`Where`, `Select`, `Any`, `FirstOrDefault`)

Only after that should the project revisit:

- integrated query syntax;
- data-provider translation;
- or a more language-native equivalent to LINQ.

**Next Concrete Work Item**
The next real code step after this document should be:

- add `delegate` declarations to syntax and symbol binding;
- define the delegate runtime object shape;
- then implement exact-match method-group conversion before touching full lambda capture support.
