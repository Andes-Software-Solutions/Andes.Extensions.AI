# Store Composition

Shaping a SignalStore: picking features, wiring state / computed / methods / props / hooks, deciding what stays private, observing state changes.
All APIs here come from `@ngrx/signals` core. Entities, RxJS/async, testing, custom reusable features and the events plugin have their own reference files.

- [Store shape & providing](#store-shape--providing) · [withState](#withstate) · [Reading state: DeepSignal](#reading-state-deepsignal) · [patchState & updaters](#patchstate--updaters)
- [withComputed](#withcomputed) · [withMethods](#withmethods) · [withProps](#withprops) · [withLinkedState](#withlinkedstate) · [withHooks](#withhooks)
- [Private members](#private-members) · [getState vs watchState](#getstate-vs-watchstate) · [signalState & deepComputed](#signalstate--deepcomputed) · [Checklist](#checklist)

## Store shape & providing

`signalStore(...features)` composes an ordered list of features into an injectable service. Order matters: each feature sees only what was declared before it. The returned service is **not registered with any injector** — pick a lifetime:

```ts
// Local: instance tied to the component/route lifetime — the right default for component state.
@Component({ providers: [BookSearchStore] })
export class BookSearch {
  readonly store = inject(BookSearchStore);
}

// Global: a single shared instance for the whole app.
export const BookSearchStore = signalStore({ providedIn: 'root' }, withState(initialState));
```

State is **protected by default** (`protectedState: true`): only the store's own methods may call `patchState`, so mutations cannot leak in from components and the data flow stays predictable. This is the recommended posture. `signalStore({ protectedState: false }, ...)` is a deliberate escape hatch letting outside code patch the store — the docs mark such a store as unprotected. Prefer adding an intent-named method over opening the store.

## withState

Adds state slices. The type must be a record/object literal; an array at the root is not allowed (NgRx ships the `with-state-no-arrays-at-root-level` / `signal-state-no-arrays-at-root-level` lint rules for it) — nest arrays under a property.

```ts
import { signalStore, withState } from '@ngrx/signals';
import { Book } from './book';

type BookSearchState = {
  books: Book[];
  isLoading: boolean;
  filter: { query: string; order: 'asc' | 'desc' };
};
const initialState: BookSearchState = {
  books: [],
  isLoading: false,
  filter: { query: '', order: 'asc' },
};

export const BookSearchStore = signalStore(withState(initialState));
```

A second signature takes a **factory**, executed in an injection context, so initial state can come from a service or token:

```ts
const BOOK_SEARCH_STATE = new InjectionToken<BookSearchState>('BookSearchState', {
  factory: () => initialState,
});
export const BookSearchStore = signalStore(withState(() => inject(BOOK_SEARCH_STATE)));
```

## Reading state: DeepSignal

Each slice becomes a signal; object-valued slices become a `DeepSignal`, which reads like a normal signal *and* exposes a signal per nested property. Nested signals are generated lazily on first access.

```ts
store.books;        // Signal<Book[]>
store.filter;       // DeepSignal<{ query: string; order: 'asc' | 'desc' }>
store.filter.query; // Signal<string>

// Union slice: a DeepSignal per object-literal member; primitives / null / dynamic
// records stay a plain Signal. Narrow with `in` before reaching in.
type Status = { type: 'success'; data: string } | { type: 'error'; message: string };
if ('message' in store.status) {
  console.log(store.status.message()); // Signal<string>
}
```

## patchState & updaters

`patchState(storeOrState, ...partialsOrUpdaters)` is the only way to write. It takes partial state objects, updater functions, or a mix.

```ts
import { patchState } from '@ngrx/signals';

patchState(store, { isLoading: true });
patchState(store, (state) => ({ filter: { ...state.filter, query } }));
patchState(store, { isLoading: false }, (state) => ({ books: [...state.books, book] }));
```

Updaters must be **immutable** — return new objects rather than mutating `state`. Mutating in place defeats signal equality checks, so dependents silently fail to update.

Recurring update logic is worth extracting into a standalone **custom updater** returning `PartialStateUpdater<T>`. These are trivially unit-testable, reusable, and compose in one `patchState` call:

```ts
import { PartialStateUpdater } from '@ngrx/signals';

function setFirstName(firstName: string): PartialStateUpdater<{ user: User }> {
  return (state) => ({ user: { ...state.user, firstName } });
}
const setAdmin = () => ({ isAdmin: true });

patchState(store, setFirstName('Stevie'), setAdmin());
```

## withComputed

A factory (run in an injection context) returning derived signals; it receives everything declared so far. A returned plain function is **wrapped in `computed()` automatically** — writing `computed(...)` yourself is optional.

```ts
import { computed } from '@angular/core';
import { signalStore, withComputed, withState } from '@ngrx/signals';

export const BookSearchStore = signalStore(
  withState(initialState),
  withComputed(({ books, filter }) => ({
    booksCount: computed(() => books().length),
    // 👇 same thing — a plain function is wrapped in computed() for you
    sortedBooks: () => {
      const direction = filter.order() === 'asc' ? 1 : -1;
      return books().toSorted((a, b) => direction * a.title.localeCompare(b.title));
    },
  }))
);
```

## withMethods

A factory (injection context) returning the store's methods — state updates and side effects. Dependencies are injected via **default parameters**, keeping the store instance as the first argument:

```ts
import { inject } from '@angular/core';
import { patchState, signalStore, withMethods, withState } from '@ngrx/signals';
import { BooksService } from './books-service';

export const BookSearchStore = signalStore(
  withState(initialState),
  withMethods((store, booksService = inject(BooksService)) => ({
    updateQuery: (query: string) =>
      patchState(store, (state) => ({ filter: { ...state.filter, query } })),
    async loadAll(): Promise<void> {
      patchState(store, { isLoading: true });
      const books = await booksService.getAll();
      patchState(store, { books, isLoading: false });
    },
  }))
);
```

Promise-based effects are fine inline. For debouncing, cancellation, or anything stream-shaped, use `rxMethod` — see `references/async-and-rxjs.md`.

## withProps

For members that are neither state signals nor methods: observables, injected services, constants. Two idioms cover most uses — exposing an observable as an RxJS integration point, and grouping dependencies that *several* later features need. In the second case, destructure the deps off the argument and spread the rest so `patchState` still receives the store itself.

```ts
import { toObservable } from '@angular/core/rxjs-interop';
import { signalStore, withHooks, withMethods, withProps, withState } from '@ngrx/signals';

export const BooksStore = signalStore(
  withState<BooksState>({ books: [], isLoading: false }),
  withProps(({ isLoading }) => ({
    isLoading$: toObservable(isLoading),
    booksService: inject(BooksService),
    logger: inject(Logger),
  })),
  // 👇 pull deps out; `...store` stays a valid patchState target
  withMethods(({ booksService, logger, ...store }) => ({
    async loadBooks(): Promise<void> {
      logger.debug('Loading books...');
      patchState(store, { isLoading: true });
      const books = await booksService.getAll();
      patchState(store, { books, isLoading: false });
    },
  })),
  withHooks({ onInit: ({ logger }) => logger.debug('BooksStore initialized') })
);
```

## withLinkedState

Declares state slices derived from other signals. These are **real state**: they get `DeepSignal`s and can be `patchState`-ed — unlike `withComputed` results, which are read-only.

Implicit form — return a computation function and the store wraps it in `linkedSignal()`. The slice recomputes when its sources change and stays writable in between:

```ts
import { patchState, signalStore, withLinkedState, withMethods, withState } from '@ngrx/signals';

export const OptionsStore = signalStore(
  withState({ options: [1, 2, 3] }),
  withLinkedState(({ options }) => ({
    selectedOption: () => options()[0] ?? undefined,
  })),
  withMethods((store) => ({
    setOptions: (options: number[]) => patchState(store, { options }),
    // 👇 linked slices are writable state
    setSelectedOption: (selectedOption: number) => patchState(store, { selectedOption }),
  }))
);
// selectedOption starts at 1; setSelectedOption(2) → 2; setOptions([4, 5, 6]) → 4
```

Explicit form — return a `WritableSignal`. Use `linkedSignal({ source, computation })` when reconciliation needs the previous value (e.g. keeping a selection alive across a list refresh). Store and original signal stay synchronized both ways.

```ts
import { linkedSignal } from '@angular/core';

withLinkedState(({ options }) => ({
  selectedOption: linkedSignal<Option[], Option>({
    source: options,
    computation: (newOptions, previous) =>
      newOptions.find((o) => o.id === previous?.value.id) ?? newOptions[0],
  }),
}));
```

## withHooks

Object form — `onInit` and/or `onDestroy`, each receiving the store. `onInit` runs in an injection context, so `inject()` and `takeUntilDestroyed()` work there.

```ts
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { interval } from 'rxjs';
import { signalStore, withHooks, withMethods, withState } from '@ngrx/signals';

export const CounterStore = signalStore(
  withState({ count: 0 }),
  withMethods(/* ... increment() ... */),
  withHooks({
    onInit(store) {
      // takeUntilDestroyed() works here: onInit runs in an injection context
      interval(2_000).pipe(takeUntilDestroyed()).subscribe(() => store.increment());
    },
    onDestroy: (store) => console.log('count on destroy', store.count()),
  })
);
```

Factory form — reach for it when `onDestroy` needs an injected dependency or when the two hooks must share closure state; the factory runs in the injection context and the returned hooks close over what it set up.

```ts
withHooks((store) => {
  const logger = inject(Logger);
  let interval = 0;

  return {
    onInit() {
      interval = setInterval(() => store.increment(), 2_000);
    },
    onDestroy() {
      logger.info('count on destroy', store.count());
      clearInterval(interval);
    },
  };
});
```

## Private members

An `_` prefix makes a member inaccessible from outside the store. It works for root-level state slices, computed signals, props and methods — use it to keep caches, raw inputs and internal setters off the public surface while composing freely inside the store.

```ts
export const CounterStore = signalStore(
  withState({ count1: 0, _count2: 0 }), // 👈 _count2: private state slice
  withComputed(({ count1, _count2 }) => ({
    _doubleCount1: computed(() => count1() * 2), // 👈 private computed
    doubleCount2: computed(() => _count2() * 2),
  })),
  withProps(({ _count2, _doubleCount1 }) => ({
    _count2$: toObservable(_count2), // 👈 private prop
    doubleCount1$: toObservable(_doubleCount1),
  })),
  withMethods((store) => ({
    increment1: () => patchState(store, { count1: store.count1() + 1 }),
    // 👇 private method
    _increment2: () => patchState(store, { _count2: store._count2() + 1 }),
  }))
);
```

From a component `store.count1()`, `store.doubleCount2()`, `store.doubleCount1$` and `store.increment1()` resolve; `store._count2()`, `store._doubleCount1()`, `store._count2$` and `store._increment2()` do not.

## getState vs watchState

`getState(store)` returns the whole state object and is tracked when read in a reactive context. Inside an `effect()` it inherits effect semantics: asynchronous, glitch-free, and **coalesced** — several updates in one tick produce a single run with the final value. Good for a settled snapshot, unusable for anything needing every transition. `watchState(store, fn)` is **synchronous** and fires on every change *including the initial state*, which is why undo/redo, storage sync and change-by-change logging need it.

```ts
import { effect } from '@angular/core';
import { getState, patchState, signalStore, watchState, withHooks, withMethods, withState } from '@ngrx/signals';

export const CounterStore = signalStore(
  withState({ count: 0 }),
  withMethods((store) => ({
    increment: () => patchState(store, { count: store.count() + 1 }),
  })),
  withHooks({
    onInit(store) {
      watchState(store, (s) => console.log('[watchState]', s));
      // logs: { count: 0 }, { count: 1 }, { count: 2 } — initial state + every change
      effect(() => console.log('[effect]', getState(store)));
      // logs: { count: 2 } — only the final value for the tick
      store.increment();
      store.increment();
    },
  })
);
```

`watchState` runs in an injection context by default and is cleaned up with its injector. It returns `{ destroy }` for earlier teardown and accepts an `{ injector }` option for use outside an injection context:

```ts
const { destroy } = watchState(store, console.log);
setTimeout(() => destroy(), 5_000);

// outside an injection context (e.g. in ngOnInit):
watchState(this.store, console.log, { injector: this.#injector });
```

## signalState & deepComputed

`signalState(initialState)` is the store-less counterpart — same `DeepSignal` generation, same `patchState` contract — usable directly in a component or service when a full store is overkill. The root type must be a record/object literal here too. `deepComputed(fn)` produces a `DeepSignal` when the computation returns an object literal, giving a computed signal per nested property (lazily, on first access).

```ts
import { deepComputed, patchState, signalState } from '@ngrx/signals';

@Component({ /* ... */ })
export class Counter {
  readonly state = signalState({ count: 0 }); // state.count: Signal<number>
  increment = () => patchState(this.state, ({ count }) => ({ count: count + 1 }));
}

const pagination = deepComputed(() => ({
  currentPage: Math.floor(offset() / limit()) + 1,
  pageSize: limit(),
  totalPages: Math.ceil(totalItems() / limit()),
}));
pagination();             // { currentPage: 1, pageSize: 25, totalPages: 4 }
pagination.currentPage(); // 1
```

## Checklist

- Feature order: `withState` → `withLinkedState` → `withProps` → `withComputed` → `withMethods` → `withHooks`. Each sees only what precedes it.
- Root state is an object literal, never an array.
- Leave `protectedState` on; expose intent-named methods instead of letting components patch the store.
- `providers: [Store]` for component-scoped state; `{ providedIn: 'root' }` for one app-wide instance.
- Derived read-only value → `withComputed`. Derived value that must also be writable → `withLinkedState`.
- Inject deps as default params in `withMethods`; hoist them into `withProps` once more than one feature needs them.
- `_`-prefix anything that is an implementation detail.
- Need every state transition (undo/redo, persistence)? `watchState`. Need a settled snapshot? `getState` inside an `effect`.
