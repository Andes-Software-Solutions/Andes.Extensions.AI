# Recipes

Known-good, end-to-end store shapes. Start from the closest one rather than composing from scratch — most real stores are a variation on the first recipe.

- [1. The reusable request-status feature](#1-the-reusable-request-status-feature) — build this first; the others depend on it
- [2. CRUD entity store with loading and error state](#2-crud-entity-store-with-loading-and-error-state)
- [3. Consuming a store in a component](#3-consuming-a-store-in-a-component)
- [4. Component-scoped store for local state](#4-component-scoped-store-for-local-state)
- [5. Search-as-you-type without race conditions](#5-search-as-you-type-without-race-conditions)
- [6. Persisting state to localStorage](#6-persisting-state-to-localstorage)
- [7. Undo/redo](#7-undoredo)

## 1. The reusable request-status feature

Nearly every store that talks to a network needs "is it loading, did it fail". Writing `isLoading`/`error` booleans into each store by hand is the most common source of drift, and the two can contradict each other. Model it once as a single status value, and expose the derived booleans.

The updaters are standalone functions rather than feature methods on purpose: they tree-shake, they are trivial to unit test, and they compose with *other* updaters in one `patchState` call.

```ts
// request-status.feature.ts
import { computed } from '@angular/core';
import { signalStoreFeature, withComputed, withState, type PartialStateUpdater } from '@ngrx/signals';

export type RequestStatus = 'idle' | 'pending' | 'fulfilled' | { error: string };
export type RequestStatusState = { requestStatus: RequestStatus };

export function withRequestStatus() {
  return signalStoreFeature(
    withState<RequestStatusState>({ requestStatus: 'idle' }),
    withComputed(({ requestStatus }) => ({
      isPending: computed(() => requestStatus() === 'pending'),
      isFulfilled: computed(() => requestStatus() === 'fulfilled'),
      error: computed(() => {
        const status = requestStatus();
        return typeof status === 'object' ? status.error : null;
      }),
    })),
  );
}

export function setPending(): PartialStateUpdater<RequestStatusState> {
  return () => ({ requestStatus: 'pending' });
}

export function setFulfilled(): PartialStateUpdater<RequestStatusState> {
  return () => ({ requestStatus: 'fulfilled' });
}

export function setError(error: string): PartialStateUpdater<RequestStatusState> {
  return () => ({ requestStatus: { error } });
}
```

Because a status can only be one of these at a time, "loading and errored simultaneously" becomes unrepresentable rather than merely unlikely.

## 2. CRUD entity store with loading and error state

The default shape for a collection backed by an API.

```ts
// books.store.ts
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withHooks, withMethods, withState } from '@ngrx/signals';
import { addEntity, removeEntity, setAllEntities, updateEntity, withEntities } from '@ngrx/signals/entities';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { tapResponse } from '@ngrx/operators';
import { pipe, switchMap, tap } from 'rxjs';
import { Book } from './book';
import { BooksService } from './books.service';
import { setError, setFulfilled, setPending, withRequestStatus } from './request-status.feature';

type BooksState = { order: 'asc' | 'desc' };

export const BooksStore = signalStore(
  { providedIn: 'root' },
  withState<BooksState>({ order: 'asc' }),
  withEntities<Book>(),
  withRequestStatus(),
  withComputed(({ entities, order }) => ({
    // withComputed wraps a plain function in computed() for you.
    sortedBooks: () => {
      const direction = order() === 'asc' ? 1 : -1;
      return entities().toSorted((a, b) => direction * a.title.localeCompare(b.title));
    },
    booksCount: computed(() => entities().length),
  })),
  withMethods((store, booksService = inject(BooksService)) => ({
    load: rxMethod<void>(
      pipe(
        tap(() => patchState(store, setPending())),
        switchMap(() =>
          booksService.getAll().pipe(
            tapResponse({
              next: (books) => patchState(store, setAllEntities(books), setFulfilled()),
              error: (err: Error) => patchState(store, setError(err.message)),
            }),
          ),
        ),
      ),
    ),

    // Optimistic local writes. Persisting them is a separate concern; if the server
    // call can fail, do it in an rxMethod and reconcile in tapResponse's error branch.
    add(book: Book): void {
      patchState(store, addEntity(book));
    },

    rename(id: Book['id'], title: string): void {
      patchState(store, updateEntity({ id, changes: { title } }));
    },

    remove(id: Book['id']): void {
      patchState(store, removeEntity(id));
    },

    setOrder(order: BooksState['order']): void {
      patchState(store, { order });
    },
  })),
  withHooks({
    onInit(store) {
      store.load();
    },
  }),
);
```

`setAllEntities` replaces the collection wholesale, which is what you want for a fresh fetch. Reach for `upsertEntities` when merging a partial payload into what is already there — see `entity-management.md` for the distinction, which is easy to get wrong and fails silently.

## 3. Consuming a store in a component

The store is injected like any service. Its signals are read directly in the template — no `async` pipe, no subscription, no teardown.

```ts
import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { BooksStore } from './books.store';

@Component({
  selector: 'app-books',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (store.isPending()) {
      <app-spinner />
    } @else if (store.error(); as error) {
      <app-error [message]="error" />
    } @else {
      <ul>
        @for (book of store.sortedBooks(); track book.id) {
          <li>{{ book.title }}</li>
        }
      </ul>
    }
  `,
})
export class BooksComponent {
  readonly store = inject(BooksStore);
}
```

Because state is protected, the component cannot call `patchState(store, …)` — it goes through `store.setOrder(…)` and friends. That constraint is the point: every write has a named entry point you can find and test.

## 4. Component-scoped store for local state

Omit `providedIn` and list the store in the component's `providers`. The instance is created with the component and destroyed with it, so two instances of the component never share state.

```ts
export const BookFilterStore = signalStore(
  withState({ query: '', showArchived: false }),
  withMethods((store) => ({
    setQuery(query: string): void {
      patchState(store, { query });
    },
    toggleArchived(): void {
      patchState(store, ({ showArchived }) => ({ showArchived: !showArchived }));
    },
  })),
);

@Component({
  selector: 'app-book-filter',
  providers: [BookFilterStore], // lifetime tied to this component
  // ...
})
export class BookFilterComponent {
  readonly store = inject(BookFilterStore);
}
```

For state this simple with no injected dependencies, `signalState` in the component is also a legitimate choice — reach for `signalStore` once behavior or DI shows up.

## 5. Search-as-you-type without race conditions

The single most valuable thing `rxMethod` buys you. Pass the *signal* into the method and it re-runs whenever that signal changes.

```ts
export const BookSearchStore = signalStore(
  { providedIn: 'root' },
  withState({ query: '' }),
  withEntities<Book>(),
  withRequestStatus(),
  withMethods((store, booksService = inject(BooksService)) => ({
    updateQuery(query: string): void {
      patchState(store, { query });
    },

    searchByQuery: rxMethod<string>(
      pipe(
        debounceTime(300),        // wait for a pause in typing
        distinctUntilChanged(),   // ignore a re-emit of the same query
        tap(() => patchState(store, setPending())),
        switchMap((query) =>      // a newer query cancels the request already in flight
          booksService.search(query).pipe(
            tapResponse({
              next: (books) => patchState(store, setAllEntities(books), setFulfilled()),
              error: (err: Error) => patchState(store, setError(err.message)),
            }),
          ),
        ),
      ),
    ),
  })),
  withHooks({
    onInit(store) {
      store.searchByQuery(store.query); // the signal, not store.query()
    },
  }),
);
```

Without `switchMap`, a slow response to "ang" can land *after* a fast response to "angular" and overwrite it — the classic autocomplete bug. `tapResponse` matters too: an error escaping to the outer observable would tear down the `rxMethod`'s subscription for good, and the search box would silently stop working.

For a submit button, swap `switchMap` for `exhaustMap` so double-clicks are ignored while the first request is in flight.

## 6. Persisting state to localStorage

`watchState` is synchronous and fires on every change, including the initial one — an `effect` reading `getState` is coalesced per tick and would miss intermediate values.

```ts
import { patchState, signalStore, watchState, withHooks, withMethods, withState } from '@ngrx/signals';

const STORAGE_KEY = 'books-preferences';

export const PreferencesStore = signalStore(
  { providedIn: 'root' },
  withState({ theme: 'light', pageSize: 20 }),
  withMethods((store) => ({
    setTheme(theme: string): void {
      patchState(store, { theme });
    },
  })),
  withHooks({
    onInit(store) {
      const saved = localStorage.getItem(STORAGE_KEY);
      if (saved) {
        patchState(store, JSON.parse(saved));
      }

      // Registered in an injection context, so it is torn down with the store.
      watchState(store, (state) => {
        localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
      });
    },
  }),
);
```

## 7. Undo/redo

Also a `watchState` job, for the same reason: skipping intermediate states would corrupt the history stack.

Two things make this correct. First, the watcher and the `undo`/`redo` methods share a `_restoring` flag — a plain mutable object in `withProps`, visible to both — so the writes that undo/redo themselves perform are not recorded as new history. Second, `_history` always holds the **current** state on top: `watchState` fires synchronously with the initial state, seeding the stack, so `undo()` pops the current state onto the redo stack and restores what is underneath.

```ts
// undo-redo.feature.ts
import {
  patchState,
  signalStoreFeature,
  type,
  watchState,
  withHooks,
  withMethods,
  withProps,
} from '@ngrx/signals';

export function withUndoRedo<State extends object>() {
  return signalStoreFeature(
    { state: type<State>() },
    withProps(() => ({
      _history: [] as State[],       // top of the stack is always the current state
      _redoStack: [] as State[],
      _restoring: { value: false },  // shared with the watcher in onInit
    })),
    withMethods((store) => {
      const restore = (state: State) => {
        store._restoring.value = true;
        try {
          patchState(store, state);
        } finally {
          store._restoring.value = false;
        }
      };

      return {
        undo(): void {
          if (store._history.length < 2) return; // only the current state left
          store._redoStack.push(store._history.pop()!);
          restore(store._history[store._history.length - 1]);
        },
        redo(): void {
          const next = store._redoStack.pop();
          if (next === undefined) return;
          store._history.push(next);
          restore(next);
        },
      };
    }),
    withHooks({
      onInit(store) {
        watchState(store, (state) => {
          if (store._restoring.value) return; // an undo/redo write — already in the stacks
          store._history.push(state as State);
          store._redoStack.length = 0;        // a fresh change invalidates redo
        });
      },
    }),
  );
}
```

`watchState` is synchronous, so wrapping `patchState` with the flag in `try`/`finally` reliably covers the watcher call — nothing can interleave between the write and the check.

This is deliberately minimal: no history cap, no per-slice tracking. Remember that `withUndoRedo` is **not** part of `@ngrx/signals` (see `references/api-reference.md`); the third-party `@angular-architects/ngrx-toolkit` ships a maintained implementation with those knobs, and this recipe exists to show the `watchState` mechanics.
