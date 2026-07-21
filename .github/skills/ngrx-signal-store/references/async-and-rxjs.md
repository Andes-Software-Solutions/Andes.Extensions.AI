# Async & RxJS in a SignalStore

How a SignalStore talks to HTTP and other async sources: `rxMethod` (the RxJS-powered reactive
method), `signalMethod` (the RxJS-free reactive method), and the cleanup rules that decide whether
your subscriptions leak. RxJS is an *optional* peer dependency of `@ngrx/signals` — the RxJS APIs
live behind the `@ngrx/signals/rxjs-interop` entry point.

Store shape (`withState` / `withComputed` / `withMethods`) is covered in
`references/store-composition.md`; `withEntities` in `references/entity-management.md`; reusable
features such as `withRequestStatus` in `references/custom-features.md`.

**Contents:** [rxMethod](#rxmethod) · [the signal-in idiom](#the-signal-in-idiom-declarative-auto-refetch) ·
[the canonical load pipeline](#the-canonical-load-pipeline) · [choosing the flattening operator](#choosing-the-flattening-operator) ·
[tapResponse](#tapresponse-keeps-the-stream-alive) · [signalMethod](#signalmethod) ·
[rxMethod vs signalMethod](#rxmethod-vs-signalmethod) · [cleanup, injectors, and the deprecation](#cleanup-injectors-and-the-deprecation)

## rxMethod

`rxMethod` is a standalone factory that takes a chain of RxJS operators and returns a *reactive
method*. The generic argument types its input.

```ts
import { map, pipe, tap } from 'rxjs';
import { rxMethod } from '@ngrx/signals/rxjs-interop';

// input type: number | (() => number) | Signal<number> | Observable<number>
readonly logDoubledNumber = rxMethod<number>(
  pipe(
    map((num) => num * 2),
    tap(console.log)
  )
);
```

Every invocation pushes the input through the chain. What "an invocation" means depends on the
argument: a static `T` runs the chain once; a `Signal<T>` runs it on every signal change; a
computation `() => T` runs it whenever any signal read inside it changes; an `Observable<T>` runs it
on every emission.

```ts
this.logDoubledNumber(1);                // 2
this.logDoubledNumber(num);              // re-runs whenever `num` changes
this.logSum(() => ({ a: a(), b: b() })); // re-runs when `a` or `b` changes
this.logDoubledNumber(of(100, 200));     // 200, 400
```

For a method that takes no argument, use `void` as the generic:

```ts
loadAllBooks: rxMethod<void>(
  exhaustMap(() =>
    booksService.getAll().pipe(
      tapResponse({
        next: (books) => patchState(store, { books }),
        error: console.error,
      })
    )
  )
), // call site: store.loadAllBooks()
```

## The signal-in idiom (declarative auto-refetch)

This is the reason `rxMethod` exists. Hand it a signal and the method re-runs itself whenever that
signal changes — no `effect`, no manual subscribe, no re-invocation from the component.

```ts
export const BookSearchStore = signalStore(
  withState(initialState),
  withMethods((store, booksService = inject(BooksService)) => ({
    updateQuery(query: string): void {
      patchState(store, (state) => ({ filter: { ...state.filter, query } }));
    },
    loadByQuery: rxMethod<string>(/* pipeline, see below */),
  })),
  withHooks({
    onInit(store) {
      // 👇 one wiring call: every `filter.query` change refetches.
      store.loadByQuery(store.filter.query);
    },
  })
);
```

The component now only calls `store.updateQuery(...)`. Fetching is a *consequence* of state,
not something the component orchestrates. Pass a computation function when the trigger depends on
several signals: `store.loadByQuery(() => ({ query: store.query(), page: store.page() }))`.

## The canonical load pipeline

```ts
import { debounceTime, distinctUntilChanged, pipe, switchMap, tap } from 'rxjs';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { setFulfilled, setPending, setError } from './with-request-status';

loadByQuery: rxMethod<string>(
  pipe(
    debounceTime(300),                                  // 1
    distinctUntilChanged(),                             // 2
    tap(() => patchState(store, setPending())),         // 3
    switchMap((query) =>                                // 4
      booksService.getByQuery(query).pipe(
        tapResponse({                                   // 5
          next: (books) => patchState(store, { books }, setFulfilled()),
          error: (err: Error) => patchState(store, setError(err.message)),
        })
      )
    )
  )
),
```

1. **`debounceTime(300)`** — a keystroke is not a query. Waits for the user to pause before a
   request is worth making.
2. **`distinctUntilChanged()`** — drops re-emissions of an unchanged value (typing then deleting a
   character lands back on the same query), so no redundant request goes out.
3. **`tap(() => patchState(store, setPending()))`** — flip the loading flag *before* the request,
   inside the pipeline, so the state transition can't be forgotten at a call site.
4. **`switchMap`** — cancels the in-flight request when a new query arrives. Without it, a slow
   response to "ang" can land *after* the fast response to "angular" and paint stale results over
   fresh ones. This race-condition safety is the main reason to reach for `rxMethod` at all.
5. **`tapResponse`** — see below.

`setPending()` / `setFulfilled()` / `setError()` are updaters from a `withRequestStatus` custom
feature (`references/custom-features.md`); a plain `patchState(store, { books, isLoading: false })`
works just as well when the store tracks loading with a boolean.

## Choosing the flattening operator

- **`switchMap`** — search / filter / anything driven by rapidly-changing input. Latest request
  wins; earlier ones are cancelled.
- **`exhaustMap`** — submit-style actions (save, login, "load all" on a button). Ignores new calls
  while one is in flight, so a double-click sends one request.
- **`concatMap`** — when every input must be processed and order matters (e.g. loading each selected
  id in turn, queueing writes). Nothing is dropped or cancelled.
- **`mergeMap`** — fully concurrent; no ordering or cancellation guarantees. Use only when responses
  are genuinely independent.

## tapResponse keeps the stream alive

`tapResponse` from `@ngrx/operators` is the recommended way to handle API responses inside a
reactive method. It is not stylistic: an error that escapes the inner observable reaches the *outer*
observable and terminates it, which **permanently kills the rxMethod's subscription** — the method
silently stops working for the rest of the injector's life. `tapResponse` traps `next` and `error`
inside the inner stream so the outer chain survives a failed request. Keep it on the *inner*
observable (inside `switchMap`), never appended after it. It also takes an optional `finalize`
callback that runs on both the success and error paths — a tidy place to clear a loading flag:

```ts
tapResponse({
  next: (books) => patchState(store, { books }),
  error: console.error,
  finalize: () => patchState(store, { isLoading: false }),
});
```

## signalMethod

`signalMethod` (from `@ngrx/signals`, no RxJS) takes a processor callback and returns a method
accepting `T | (() => T) | Signal<T>` — the same reactive-input idea as `rxMethod`, minus
observables and minus operators.

```ts
import { signalMethod } from '@ngrx/signals';

readonly logDoubledNumber = signalMethod<number>((num) => console.log(num * 2));

this.logDoubledNumber(1);     // 2
this.logDoubledNumber(count); // re-runs on every `count` change
```

Internally it uses an `effect` to track signal changes, but the docs call out three advantages over
writing that `effect` yourself:

- **Flexible input** — the argument may be a static value, not just a signal or computation, and the
  processor can be called many times with different inputs.
- **No injection context required to call it** — an `effect` needs an injection context or an
  `Injector`; the processor function does not.
- **Explicit tracking** — only the signal you pass (or the signals read inside the computation
  function) are tracked. Signals read *inside* the processor body stay untracked, so the method
  won't re-fire on state it merely reads.

## rxMethod vs signalMethod

`signalMethod` is "`rxMethod` without RxJS" and is therefore much smaller in bundle terms — relevant
because RxJS is only an optional peer dependency of `@ngrx/signals`, so a store that never imports
`rxjs-interop` need not ship it.

The trade-off is real: signals are glitch-free, meaning several synchronous changes propagate only
the last value, and there are no operators — no `switchMap`, no `concatMap`, no `debounceTime`. The
docs state plainly that RxJS is superior for managing race conditions. `signalMethod` cannot cancel
an in-flight request or sequence overlapping ones. Decision rule:

- Need cancellation, sequencing, debouncing, retry, or any other operator → **`rxMethod`**.
- Simple reaction to a signal, and you want RxJS out of the bundle → **`signalMethod`**.
- One-shot imperative call with no reactive input (e.g. an `async` method awaiting a promise) → just
  a **plain method in `withMethods`**.

## Cleanup, injectors, and the deprecation

Both `rxMethod` and `signalMethod` must be *created* in an injection context, and both bind their
subscription/effect to an injector's lifetime. When the method is created in an **ancestor** injector
(a `providedIn: 'root'` store or service) and *called* from a component **outside** an injection
context, the subscription binds to the root injector, outlives the component, and leaks.

Calling a reactive method with a signal, a computation function, or an observable outside an
injection context without an explicit injector is **deprecated and will throw in a future version**.
Either call it in an injection context (constructor, field initializer, `withHooks` `onInit`) or
pass the injector via the config parameter.

```ts
@Injectable({ providedIn: 'root' })
export class NumbersService {
  readonly log = rxMethod<number>(tap(console.log));
}

@Component({ /* ... */ })
export class Numbers implements OnInit {
  readonly #injector = inject(Injector);
  readonly #numbersService = inject(NumbersService);

  constructor() {
    // ✅ injection context: cleaned up when the component is destroyed.
    this.#numbersService.log(interval(1_000));
  }

  ngOnInit(): void {
    // ⚠️ not an injection context — pass the component injector explicitly,
    // otherwise cleanup waits for the root injector (i.e. never).
    this.#numbersService.log(interval(2_000), { injector: this.#injector });
  }
}
```

Static values need no injector: `this.#numbersService.log(2)` in `ngOnInit` is fine.

### Manual teardown and creation outside an injection context

`rxMethod` returns an object with a `destroy` method, and *each call* returns a ref with its own
`destroy` — so a single subscription can be torn down while the method's other calls keep running.
To create a reactive method outside an injection context, pass an injector as the factory's second
argument. `signalMethod` takes the same `{ injector }` config, at creation and at call time.

```ts
ngOnInit(): void {
  const logNumber = rxMethod<number>(tap(console.log), { injector: this.#injector });

  const num1Ref = logNumber(interval(500));
  logNumber(interval(1_000));

  num1Ref.destroy();  // kill just this call
  logNumber.destroy(); // kill the reactive method and all of its calls
}
```
