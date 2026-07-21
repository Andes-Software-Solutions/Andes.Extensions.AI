---
name: ngrx-signal-store
description: "Build, review, and test NgRx SignalStore state management in Angular, using production patterns pinned to the official @ngrx/signals docs. Use this whenever Angular state management comes up: writing or reviewing a store, choosing between signalState and signalStore or between rxMethod and signalMethod, adding an entity collection with withEntities, extracting a reusable signalStoreFeature, wiring an HTTP load with loading and error status, fixing a store that leaks or drops race conditions, or testing one with TestBed. Also use it on any mention of signalStore, signalState, withState, withComputed, withMethods, withHooks, withProps, patchState, protectedState, withEntities, entityConfig, deepComputed, rxMethod, signalMethod, or rxjs-interop. Prefer this over recalling NgRx from memory, which is usually out of date: the Signals API changed substantially and pre-v17 habits produce wrong code."
---

# NgRx Signal Store

Production guidance for `@ngrx/signals`. Pinned to **21.1.1** (`sources.json`); refresh with `/ngrx-signals-sync`.

This is not classic NgRx. There are no actions, reducers, effects, or `dispatch` unless you deliberately opt into the Events plugin. If you catch yourself reaching for `createAction` or `createReducer`, you are in the wrong library.

## Mental model

A store is a **composition of features**, evaluated left to right. Each feature sees everything the features before it added, and contributes state, computed signals, props, or methods to what follows. `signalStore(...)` returns an injectable service.

State is a **deeply-signalized tree**: every object-literal member becomes a `DeepSignal` you can drill into (`store.filter.query()`), while primitives stay plain signals. State is **protected by default** — only the store's own methods may write to it, via `patchState`.

## Production defaults

- **Leave `protectedState` on.** It is the default, and it is what stops components from reaching in and mutating state. `signalStore({ protectedState: false }, …)` exists, but it trades away the one guarantee that keeps data flow one-directional. Tests do not need it — use `unprotected()` from `@ngrx/signals/testing` instead.
- **Choose the lifetime deliberately.** `signalStore({ providedIn: 'root' }, …)` gives one app-wide instance. Listing the store in a component's or route's `providers: []` ties its lifetime to that component, which is what you want for local, disposable state.
- **Inject via default parameters** — `withMethods((store, books = inject(BooksService)) => ({ … }))`. It keeps the feature a plain function and stays testable.
- **Write state updaters as standalone functions** returning `PartialStateUpdater<T>`, not as store methods. They tree-shake, they test in isolation, and they compose in a single call: `patchState(store, setAllEntities(books), setFulfilled())`.
- **Prefix private members with `_`.** `_totals`, `_load()` — inaccessible from outside the store, so the public surface stays intentional.
- **One store per entity type.** Multiple collections in one store are supported, but the docs recommend dedicated stores, and it keeps each store's methods coherent.
- **Never mutate in an updater.** `patchState` updaters must return new objects; mutating in place silently breaks change propagation.

## Decision rules

**`signalState` or `signalStore`?** `signalState` is a signalized state container and nothing more — reach for it for local component state with no behavior attached. The moment you need methods, dependency injection, lifecycle hooks, or a lifetime you control, use `signalStore`.

**`rxMethod`, `signalMethod`, or a plain method?** This is the choice that most often gets made wrong.
- **`rxMethod`** when the work can overlap or arrive out of order. Only RxJS gives you `switchMap` (cancel the in-flight request — the fix for a search-as-you-type race), `exhaustMap` (ignore clicks while a submit is in flight), and `debounceTime`. Any HTTP load driven by user input belongs here.
- **`signalMethod`** when you need to react to a signal but need no operators. It is `rxMethod` without RxJS and is markedly smaller — `rxjs` is only an *optional* peer of `@ngrx/signals`. It cannot cancel or sequence, so it is the wrong tool for racing requests.
- **A plain method** in `withMethods` for a one-shot imperative call. Not everything needs to be reactive.

**Extract a `signalStoreFeature`?** When the third store repeats the same shape. Loading/error status is the canonical case — `withRequestStatus()` with standalone `setPending()` / `setFulfilled()` / `setError()` updaters. Two stores sharing a shape is a coincidence; three is a feature.

**`withEntities` or a plain array?** Any keyed collection you update or remove by id. It gives you `entityMap`, `ids`, and a computed `entities`, and the updaters handle the bookkeeping. A short, read-only list is fine as a plain array in state.

**The Events plugin?** Only when one thing that happens must be observed by *several* stores, or you specifically want dispatch/reducer separation. If a single store owns the state and a method would do, write the method — the docs are explicit that the default approach suffices for most cases.

## Shape of a production store

```ts
export const BooksStore = signalStore(
  { providedIn: 'root' },                       // omit to scope it to a component's providers
  withState<BooksState>({ query: '', order: 'asc' }),
  withEntities<Book>(),                         // ids + entityMap + computed entities
  withRequestStatus(),                          // reusable feature: pending / fulfilled / error
  withComputed(({ entities, order }) => ({
    sortedBooks: () => {                        // plain fn — withComputed wraps it in computed()
      const dir = order() === 'asc' ? 1 : -1;
      return entities().toSorted((a, b) => dir * a.title.localeCompare(b.title));
    },
  })),
  withMethods((store, books = inject(BooksService)) => ({
    loadByQuery: rxMethod<string>(
      pipe(
        debounceTime(300),
        distinctUntilChanged(),
        tap(() => patchState(store, setPending())),
        switchMap((query) =>                    // switchMap: a newer query cancels the older one
          books.search(query).pipe(
            tapResponse({                       // keeps the rxMethod alive if the request errors
              next: (found) => patchState(store, setAllEntities(found), setFulfilled()),
              error: (e: Error) => patchState(store, setError(e.message)),
            }),
          ),
        ),
      ),
    ),
  })),
  withHooks({
    onInit(store) {
      store.loadByQuery(store.query);           // pass the signal itself: reloads whenever it changes
    },
  }),
);
```

Passing `store.query` (the signal, not `store.query()`) into an `rxMethod` is the idiom that makes the load declarative — it re-runs on every change, with no manual subscription.

Full runnable versions of this and other shapes are in `references/recipes.md`.

## Traps

- **`withDevtools` is not part of NgRx.** It comes from the third-party `@angular-architects/ngrx-toolkit`. It is a common hallucination — do not present it as official, and add the dependency if a project actually wants it.
- **Calling `rxMethod` / `signalMethod` with a signal outside an injection context, without an explicit `injector`, is deprecated and will throw in a future version.** It bites when a root-provided store's method is called from a component's `ngOnInit`. Pass `{ injector: this.injector }`.
- **State must be a record, not an array.** `withState([])` and `signalState([])` are invalid; NgRx ships ESLint rules for exactly this.
- **`withComputed` auto-wraps plain functions** in `computed()`. Both `() => …` and `computed(() => …)` work; don't double-wrap.
- **`watchState` is synchronous, `getState` inside an `effect` is coalesced.** Undo/redo and storage sync need to see every intermediate value, so they need `watchState` — an effect would only show the last value of the tick.
- **Combining two input-taking `signalStoreFeature`s that declare no generics is a compile error.** The workaround is a dummy generic: `function withZ<_>() { … }`.
- **Entity updaters are not interchangeable.** `add*` no-ops on an id collision, `set*` replaces, `upsert*` merges. Picking the wrong one produces silent data loss.

## References

Read these on demand — they are not loaded until you need them.

| Read this | When |
| --- | --- |
| `references/store-composition.md` | Authoring or reshaping a store: state, computed, props, methods, hooks, linked state, private members, state tracking |
| `references/async-and-rxjs.md` | The store talks to HTTP or any async source; choosing or debugging `rxMethod` / `signalMethod` |
| `references/entity-management.md` | State holds a keyed collection: `withEntities`, `entityConfig`, the updater catalogue |
| `references/custom-features.md` | Logic repeats across stores; building a reusable `signalStoreFeature` |
| `references/testing.md` | Writing or fixing store tests: TestBed, `unprotected`, mocking |
| `references/events-plugin.md` | Event-based/redux-style state, or several stores reacting to one event |
| `references/recipes.md` | Starting a new store, or wanting a known-good end-to-end shape |
| `references/api-reference.md` | Unsure of a signature, an import path, or which entry point exports a symbol |

## Beyond NgRx

For Angular questions that are not about state — components, zoneless, routing, the CLI — use the `angular-cli` MCP server (`list_projects` → `get_best_practices` → `search_documentation` → `find_examples`) rather than relying on memory.

NgRx's own ESLint rules (`prefer-protected-state`, `enforce-type-call`, `signal-state-no-arrays-at-root-level`, `with-state-no-arrays-at-root-level`, `signal-store-feature-should-use-generic-type`) are the machine-checkable form of the defaults above; recommend them when a project is adopting the library seriously.
