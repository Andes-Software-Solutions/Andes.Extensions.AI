# Custom Store Features

`signalStoreFeature` packages state, computed signals, methods, and hooks into a unit that plugs into
any `signalStore`. Reach for it when the same logic repeats across stores — request status, logging,
selection, filtering.

## Creating a feature

`signalStoreFeature` takes a sequence of base or custom features and merges them into one.

```ts
import { computed } from '@angular/core';
import { signalStoreFeature, withComputed, withState } from '@ngrx/signals';

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
    }))
  );
}
```

### State updaters belong outside the feature

The headline recommendation from the docs: define a feature's state updaters as **standalone
functions**, not as feature methods. Standalone updaters tree-shake when unused, are trivial to unit
test (plain input → state object), and — the practical payoff — compose with other updaters inside a
single `patchState` call.

```ts
export function setPending(): RequestStatusState {
  return { requestStatus: 'pending' };
}

export function setFulfilled(): RequestStatusState {
  return { requestStatus: 'fulfilled' };
}

export function setError(error: string): RequestStatusState {
  return { requestStatus: { error } };
}
```

These return the partial state object directly; returning a `PartialStateUpdater<RequestStatusState>`
instead (as in `references/recipes.md`) is equivalent — `patchState` accepts both forms.

Consuming it, the entity update and the status update land in one atomic patch:

```ts
import { inject } from '@angular/core';
import { patchState, signalStore, withMethods } from '@ngrx/signals';
import { setAllEntities, withEntities } from '@ngrx/signals/entities';
import { setFulfilled, setPending, withRequestStatus } from './with-request-status';
import { BooksService } from './books-service';
import { Book } from './book';

export const BooksStore = signalStore(
  withEntities<Book>(),
  withRequestStatus(),
  withMethods((store, booksService = inject(BooksService)) => ({
    async loadAll() {
      patchState(store, setPending());

      const books = await booksService.getAll();
      // 👇 two updaters, one patch — impossible if `setFulfilled` were a feature method
      patchState(store, setAllEntities(books), setFulfilled());
    },
  }))
);
```

`BooksStore` now exposes `entityMap` / `ids` / `entities` from `withEntities`, `requestStatus` plus
`isPending` / `isFulfilled` / `error` from `withRequestStatus`, and `loadAll(): Promise<void>`.

### A feature can be pure behavior

Features need not contribute state. `withHooks` alone is enough, and the factory can take plain
arguments:

```ts
import { effect } from '@angular/core';
import { getState, signalStoreFeature, withHooks } from '@ngrx/signals';

export function withLogger(name: string) {
  return signalStoreFeature(
    withHooks({
      onInit(store) {
        effect(() => console.log(`${name} state changed`, getState(store)));
      },
    })
  );
}

export const BooksStore = signalStore(withEntities<Book>(), withLogger('books'));
```

## Features with input

A feature can require the host store to already provide certain state, props, or methods. Declare the
expectation as the **first argument** to `signalStoreFeature`, using the `type` helper. The compiler
then rejects any store that doesn't satisfy it.

The docs temper this: prefer loosely-coupled, independent features whenever possible. An input
requirement couples the feature to the host store's internal shape, so use it only when the feature
genuinely cannot work without those members.

### State as input

```ts
import { computed } from '@angular/core';
import { signalStoreFeature, type, withComputed, withState } from '@ngrx/signals';
import { EntityId, EntityState } from '@ngrx/signals/entities';

export type SelectedEntityState = { selectedEntityId: EntityId | null };

export function withSelectedEntity<Entity>() {
  return signalStoreFeature(
    { state: type<EntityState<Entity>>() }, // 👈 requires `entityMap` + `ids`
    withState<SelectedEntityState>({ selectedEntityId: null }),
    withComputed(({ entityMap, selectedEntityId }) => ({
      selectedEntity: computed(() => {
        const selectedId = selectedEntityId();
        return selectedId ? entityMap()[selectedId] : null;
      }),
    }))
  );
}

export const BooksStore = signalStore(
  withEntities<Book>(), // 👈 satisfies the `EntityState` requirement
  withSelectedEntity()
);
```

Without a matching state contribution, compilation fails:

```ts
export const BooksStore = signalStore(
  withState({ books: [] as Book[], isLoading: false }),
  // Error: `EntityState` properties (`entityMap` and `ids`) are missing in the `BooksStore`.
  withSelectedEntity()
);
```

### Props and methods as input

```ts
import { Signal } from '@angular/core';
import { signalStoreFeature, type, withMethods } from '@ngrx/signals';

export function withBaz<Foo extends string | number>() {
  return signalStoreFeature(
    {
      props: type<{ foo: Signal<Foo> }>(),
      methods: type<{ bar(foo: number): void }>(),
    },
    withMethods((store) => ({
      baz(): void {
        const foo = store.foo();
        store.bar(typeof foo === 'number' ? foo : Number(foo));
      },
    }))
  );
}
```

`withBaz` compiles only in a store that already defines the `foo` prop and the `bar` method.

## `withFeature` — reading what came before

Feature inputs demand that the host store expose members with *exactly* the expected names.
`withFeature` is the more flexible alternative: it takes a callback receiving the store built so far
and returns a feature, so the feature itself stays a plain function of its arguments and knows nothing
about the store's structure.

```ts
import { computed, Signal } from '@angular/core';
import {
  patchState,
  signalStore,
  signalStoreFeature,
  withComputed,
  withFeature,
  withMethods,
  withState,
} from '@ngrx/signals';
import { withEntities } from '@ngrx/signals/entities';
import { Book } from './book';

export function withBooksFilter(books: Signal<Book[]>) {
  return signalStoreFeature(
    withState({ query: '' }),
    withComputed(({ query }) => ({
      filteredBooks: computed(() => books().filter((b) => b.name.includes(query()))),
    })),
    withMethods((store) => ({
      setQuery(query: string): void {
        patchState(store, { query });
      },
    }))
  );
}

export const BooksStore = signalStore(
  withEntities<Book>(),
  // 👇 pass the store's `entities` signal into the feature
  withFeature(({ entities }) => withBooksFilter(entities))
);
```

Choose `withFeature` when the feature only needs *a signal of some shape* (here: any `Signal<Book[]>`)
rather than a store that happens to name it `entities`. That keeps it reusable across stores whose
members are named differently, and lets you adapt or rename on the way in.

## TypeScript gotcha: two input-taking features

Combining multiple features that take input but declare **no generic parameters** produces a
compilation error — a known limitation, not a mistake in your store:

```ts
function withZ() {
  return signalStoreFeature({ state: type<{ x: number }>() }, withState({ z: 10 }));
}

function withW() {
  return signalStoreFeature({ state: type<{ y: number }>() }, withState({ w: 100 }));
}

const Store = signalStore(withState({ x: 10, y: 100 }), withZ(), withW()); // ❌ compilation error
```

The fix is an unused ("dummy") generic parameter on each such feature — nothing else changes:

```ts
//            👇 dummy generic
function withZ<_>() {
  return signalStoreFeature({ state: type<{ x: number }>() }, withState({ z: 10 }));
}

function withW<_>() {
  return signalStoreFeature({ state: type<{ y: number }>() }, withState({ w: 100 }));
}

const Store = signalStore(withState({ x: 10, y: 100 }), withZ(), withW()); // ✅ works
```

Features that already have a real generic (like `withSelectedEntity<Entity>`) are unaffected. NgRx
ships an ESLint rule, `signal-store-feature-should-use-generic-type`, that flags input-taking features
missing a generic so this never bites at the call site.

## Related

- Store anatomy — `withState` / `withComputed` / `withMethods` / `withHooks`, private members:
  `references/store-composition.md`
- `rxMethod` and async loading inside features: `references/async-and-rxjs.md`
- Testing standalone updaters and features: `references/testing.md`
- `withEntities` and the entity updaters used above: `references/entity-management.md`
