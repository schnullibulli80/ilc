**Managed Threading**
This document defines the next honest implementation step for managed thread execution in ILC.

**Status**
Today `System.Threading` already provides:

- `Thread.CurrentManagedId`
- `Thread.Sleep(milliseconds)`
- `Mutex`

What is still missing is real managed execution on a second host thread:

- `Thread.Start(...)`
- `Thread.Join()`
- `Thread.IsAlive`

Those members are intentionally not shipped yet because the current VM execution model cannot support them correctly without additional runtime work.

**Current Blocker**
`VirtualMachine::execute(...)` currently owns these runtime stores locally inside a single execution:

- managed objects
- arrays
- strings
- static fields
- native handles

That is sufficient for a single-threaded run, but not for managed thread start:

- a worker thread must see the same object graph as its parent
- static fields must be shared across threads of the same module execution
- arrays and strings must stay valid across thread boundaries
- exceptions and stack traces must be attributable to the worker thread that produced them

Without a shared runtime state, a host-backed `Thread.Start(...)` would either:

- run in an isolated VM world, which would be semantically wrong
- or act as a stub, which would be misleading

**First Language Surface**
The first honest language-level preparation is the shipped interface:

```ilc
public interface IRunnable
begin
  public method Run;
end;
```

This gives the runtime a stable managed entry contract without introducing delegates or closures first.

**Recommended Implementation Order**
1. Introduce a shared per-execution runtime state in the VM.
2. Move the currently local stores into that shared state:
   - objects
   - arrays
   - strings
   - static fields
   - native handles
3. Make access to that shared state thread-safe.
4. Add managed thread bookkeeping:
   - host thread handle
   - completion state
   - captured failure information
5. Resolve `IRunnable.Run()` through the existing interface dispatch table.
6. Add `Thread.Start(target: IRunnable): Thread`.
7. Add `Thread.Join()`.
8. Add `Thread.IsAlive`.

**Runtime Shape**
The shared runtime state should belong to a single top-level module execution.

Suggested responsibilities:

- allocation and storage of managed values
- shared static field table
- native handle registry
- managed worker-thread registry
- synchronization primitives for the structures above

This state should be reused by:

- the top-level `execute(...)` call
- every managed thread started from that execution

It should not be reused across unrelated top-level program launches.

**Thread Start Model**
The first supported managed start model should be:

```ilc
var worker := Thread.Start(runnable);
worker.Join();
```

Where `runnable.Run()`:

- is instance-based
- takes no explicit user parameters
- is resolved through normal class/interface dispatch

This keeps the first version small and avoids introducing delegates at the same time.

**Failure Semantics**
If `Run()` terminates with an unhandled exception or runtime error:

- the worker thread should capture the failure
- `Join()` should rethrow a managed/runtime-visible error in the joining thread
- the original worker stack trace should be preserved

This keeps the behavior diagnosable and consistent with the existing debugger and stack trace infrastructure.

**What Is Explicitly Deferred**
The following should not be mixed into the first managed thread cut:

- delegates or anonymous functions
- thread pools
- `Task`
- `async` / `await`
- cancellation tokens
- apartment or scheduler models

Those all depend on a correct managed thread substrate first.

**Next Concrete Work Item**
The next real code step after this document is the VM refactor:

- extract a shared execution state from `VirtualMachine::execute(...)`
- keep the public `execute(...)` surface stable
- make the existing single-threaded behavior continue to work unchanged
- then layer managed `Thread.Start/Join` on top
