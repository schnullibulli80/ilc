**Query Expressions**
This document defines the recommended next design step for language-integrated,
set-oriented data processing in ILC.

The immediate goal is not “SQL in the language”.
The immediate goal is to establish a syntax and compiler model that can:

- feel native in ILC source code;
- target the existing `Enumerable<T>` pipeline first;
- remain extensible toward provider-backed translation later;
- avoid locking the language into raw string-SQL as the primary model.

**Why This Matters Now**
ILC now has the key prerequisites that were missing earlier:

- delegates;
- method-group conversion;
- non-capturing lambdas;
- capturing lambdas / closures;
- a meaningful `Enumerable<T>` helper surface.

That means the language finally has enough substrate to move from
“collection helper calls” toward a more integrated query notation.

The next honest step is therefore not a thin SQLite string API, but a design for:

- query-shaped syntax;
- lowering to existing enumerable helpers;
- and a later provider hook for translating the same query shape elsewhere.

**Current Status**
Today ILC already supports query-like programming in library form:

- `Where`
- `Select`
- `Take`
- `Skip`
- `Concat`
- `Distinct`
- `Append`
- `Prepend`
- `Reverse`
- `Contains`
- `Any`
- `Count`
- `First` / `FirstOrDefault`
- `Single` / `SingleOrDefault`
- `Last` / `LastOrDefault`
- `ToList`
- `ToArray`

This is enough to validate query semantics at the library/runtime level.
What is still missing is a language surface that makes such code feel more
native and more obviously data-oriented.

**Design Goals**
The first query-expression cut should be:

- purely additive;
- translatable to existing enumerable helpers;
- conservative in syntax complexity;
- explicit enough that binder/lowerer behavior stays understandable;
- future-compatible with provider-backed translation.

It should not require:

- new runtime opcodes;
- new object model concepts;
- a SQL engine in the compiler;
- or early commitment to database-specific semantics.

**Recommended Implementation Order**
1. Design a query-expression source syntax.
2. Bind query expressions into an explicit query AST.
3. Lower that AST to existing `Enumerable<T>` helpers in v1.
4. Preserve enough semantic structure that a later provider path can branch
   before enumerable lowering.
5. Only after that consider SQLite/provider translation experiments.

This keeps the first cut practical and visible without overcommitting the compiler.

## 1. Recommended Source Shape

The recommended direction is a syntax that feels closer to Pascal/Delphi than to
verbatim C# LINQ query syntax, while still remaining clearly set-oriented.

Proposed v1 shape:

```ilc
var names :=
  from value in people
  where value.IsActive
  select value.Name;
```

With multiple clauses:

```ilc
var topNames :=
  from value in people
  where value.IsActive
  orderby value.Name
  take 10
  select value.Name;
```

This shape is intentionally declarative:

- `from`
- `where`
- `select`
- `take`
- `skip`
- later `orderby`

This is easier to explain than deeply nested helper calls and matches the user
goal of language-native data processing.

**Why Not SQL-like Text Blocks**
A syntax such as:

```ilc
query
begin
  select ...
end
```

would be possible, but it creates two problems too early:

- it feels database-specific instead of collection-/set-oriented;
- it risks coupling the language syntax to SQL shape before provider semantics are ready.

The `from` / `where` / `select` family keeps the first step general.

## 2. V1 Scope

The first query-expression milestone should support:

- `from <name> in <source>`
- `where <predicate>`
- `select <projection>`

Optional but still reasonable in the same overall direction:

- `take <count>`
- `skip <count>`

Deferred:

- joins;
- grouping;
- aggregate clauses;
- ordering;
- multiple `from` clauses;
- query comprehensions over non-enumerable providers.

The v1 target should be “single-source query comprehensions over `IEnumerable<T>`”.

## 3. Semantic Model

The compiler should not bind query syntax directly into nested call expressions.
Instead it should first construct a dedicated bound query model.

Suggested bound nodes:

- `BoundQueryExpression`
- `BoundQueryFromClause`
- `BoundQueryWhereClause`
- `BoundQuerySelectClause`
- later:
  - `BoundQueryTakeClause`
  - `BoundQuerySkipClause`
  - `BoundQueryOrderByClause`
  - `BoundQueryJoinClause`

This matters because a dedicated query AST gives two future paths:

1. enumerable lowering
2. provider translation

If the compiler skips straight to helper-call trees, the later provider path will
need to recover meaning from already-desugared code.

That is unnecessarily fragile.

## 4. Binding Rules

### 4.1 Source binding

For v1, the source after `in` should bind to:

- `IEnumerable<T>`
- or a compatible concrete type with `GetEnumerator()`

The inferred element type becomes the range variable type.

Example:

```ilc
from value in people
```

If `people` is `IEnumerable<Person>`, then:

- `value` has type `Person`

### 4.2 Clause typing

- `where` requires a `Boolean` expression
- `select` determines the result element type
- `take` / `skip` require `Integer`

### 4.3 Scope

The range variable introduced by `from` is visible in later clauses:

- `where`
- `select`
- later `orderby`

It should behave similarly to a lambda parameter scope.

### 4.4 Target result

For v1, a query expression should itself produce:

- `IEnumerable<TResult>`

That means:

```ilc
var q := from value in people where value.IsActive select value.Name;
```

binds to `IEnumerable<String>`.

Materialization remains explicit:

```ilc
var list := (from value in people select value.Name).ToList();
```

or later:

```ilc
var list := (from value in people select value.Name) |> ToList;
```

if a pipeline operator is ever introduced.

## 5. Lowering Strategy For V1

The first lowering target should be the already shipped helper surface.

Examples:

```ilc
from value in people
where value.IsActive
select value.Name
```

lowers to:

```ilc
Enumerable<Person, String>.Select(
  Enumerable<Person>.Where(people, function(value: Person): Boolean => value.IsActive),
  function(value: Person): String => value.Name);
```

Similarly:

```ilc
from value in people
take 10
select value.Name
```

lowers to:

```ilc
Enumerable<Person, String>.Select(
  Enumerable<Person>.Take(people, 10),
  function(value: Person): String => value.Name);
```

This gives immediate end-to-end value with no new runtime work.

## 6. Provider-Friendly Future Path

The important architectural rule is:

- query expressions should be lowered to enumerable helpers only as the v1 backend;
- the bound query AST should remain explicit long enough that a later alternate
  backend can intercept it.

That future alternate backend might:

- translate to SQL;
- translate to SQLite prepared statements;
- translate to HTTP query parameters;
- or build expression/query objects for deferred execution.

This is why the AST should not be “just syntax sugar” internally.

## 7. Recommended Syntax Details

### 7.1 Keywords

Recommended reserved words for the query feature:

- `from`
- `in`
- `where`
- `select`

Potential future additions:

- `take`
- `skip`
- `orderby`
- `join`
- `group`
- `into`

The first cut should reserve only what is actually implemented, unless the parser
benefits from reserving the family consistently.

### 7.2 Expression boundaries

Query expressions should be expression-valued.
That means they can appear in:

- variable initializers;
- assignments;
- call arguments;
- return statements.

### 7.3 Parentheses

Parentheses should be allowed when a query expression participates in a larger expression:

```ilc
var names := (from value in people select value.Name).ToList();
```

This keeps precedence straightforward.

## 8. What V1 Should Explicitly Not Do

The first query-expression cut should not attempt:

- SQL text generation;
- query optimization;
- join reordering;
- database provider abstractions;
- aggregate syntax such as `sum` / `avg`;
- `group by`;
- anonymous projection records;
- materialization shortcuts that hide whether execution is lazy or eager.

Those are valuable later, but they would overcomplicate the first milestone.

## 9. Compiler Work Breakdown

The practical compiler steps should be:

1. Add query-expression syntax nodes.
2. Parse:
   - `from`
   - `where`
   - `select`
   - later `take` / `skip`
3. Bind query syntax to a dedicated bound AST.
4. Reuse the lambda/delegate machinery for generated predicate/selector delegates.
5. Lower bound query AST to existing enumerable helper calls.
6. Reuse current bytecode/runtime paths unchanged.

This is a good next milestone because most of the hard runtime work is already done.

## 10. Suggested First Demo

A minimal first showcase should be:

```ilc
var names :=
  from value in people
  where value.Contains('a')
  select value.Length;

var first := names.FirstOrDefault();
```

This demonstrates:

- query syntax;
- lambda-equivalent binding;
- lowering to shipped enumerable operators;
- and immediate practical value without any database dependency.

## 11. Recommendation

The recommended next move after the current enumerable work is:

1. implement parser and binder support for a minimal query-expression syntax:
   - `from`
   - `where`
   - `select`
2. lower it to existing enumerable helpers;
3. only after that revisit the persistence/data-provider story.

That gives ILC a visibly more language-native data-processing direction while
keeping the implementation grounded in already working infrastructure.
