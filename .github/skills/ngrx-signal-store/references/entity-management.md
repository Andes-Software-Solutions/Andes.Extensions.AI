# Entity Management

The `@ngrx/signals/entities` plugin manages keyed collections inside a SignalStore: the `withEntities`
feature plus a catalogue of standalone updaters that pair with `patchState`. Reach for it when a store
holds a collection of records addressed by id.

## `withEntities`

By default an entity must have an `id` property of type `EntityId` (`string | number`).

```ts
import { signalStore } from '@ngrx/signals';
import { withEntities } from '@ngrx/signals/entities';

type Todo = { id: number; text: string; completed: boolean };

export const TodosStore = signalStore(withEntities<Todo>());
```

This adds three members:

| Member | Type | Kind |
| --- | --- | --- |
| `ids` | `Signal<EntityId[]>` | state slice |
| `entityMap` | `Signal<EntityMap<Todo>>` | state slice |
| `entities` | `Signal<Todo[]>` | **computed** |

`entities` is derived from `ids` + `entityMap`, so it is never patched directly — change the
collection through the updaters below and `entities` recomputes.

## Updaters

Updaters are standalone functions imported from `@ngrx/signals/entities` and passed to `patchState`,
typically from inside `withMethods`, so several compose in a single call.

```ts
import { patchState, signalStore, withMethods } from '@ngrx/signals';
import { addEntity, updateAllEntities, withEntities } from '@ngrx/signals/entities';

export const TodosStore = signalStore(
  withEntities<Todo>(),
  withMethods((store) => ({
    addTodo(todo: Todo): void {
      patchState(store, addEntity(todo));
    },
    completeAllTodos(): void {
      patchState(store, updateAllEntities({ completed: true }));
    },
  }))
);
```

The collision semantics are the point of the catalogue — none of these updaters throw, so picking the
wrong family fails silently.

### Add — no-op on id collision

`addEntity` / `addEntities` append; `prependEntity` / `prependEntities` insert at the front
(`prependEntities` preserves the input's relative order). If an entity with the same id already
exists, it is not overridden and no error is thrown — the call is simply a no-op for that entity.

```ts
patchState(store, addEntity(todo));
patchState(store, addEntities([todo1, todo2]));
patchState(store, prependEntity(todo));
patchState(store, prependEntities([todo1, todo2]));
```

Fits when a duplicate id means "already have it, leave it alone".

### Set — add or replace

`setEntity` / `setEntities` add, or fully replace an existing entity with the same id.
`setAllEntities` replaces the entire collection — the natural fit for a fresh server response.

```ts
patchState(store, setEntity(todo));
patchState(store, setEntities([todo1, todo2]));
patchState(store, setAllEntities([todo1, todo2, todo3]));
```

### Upsert — add or merge

`upsertEntity` / `upsertEntities` add, or **merge** into the existing entity. Only the properties
present on the incoming object are written; properties not specified remain unchanged. That is the
whole difference from `set*`: set discards the old entity, upsert keeps the untouched fields.

```ts
patchState(store, upsertEntity(todo));
patchState(store, upsertEntities([todo1, todo2]));
```

### Update — partial change by id, ids, or predicate

`changes` is either a partial object or a `(entity) => partial` function. Nothing is thrown if the
target doesn't exist.

```ts
// by id — partial object, or a function of the current entity
patchState(store, updateEntity({ id: 1, changes: { completed: true } }));
patchState(store, updateEntity({ id: 1, changes: (t) => ({ completed: !t.completed }) }));

// by ids
patchState(store, updateEntities({ ids: [1, 2], changes: { completed: true } }));

// by predicate
patchState(
  store,
  updateEntities({
    predicate: ({ text }) => text.endsWith('✅'),
    changes: (todo) => ({ text: todo.text.slice(0, -1) }),
  })
);

// every entity
patchState(store, updateAllEntities({ text: '' }));
patchState(store, updateAllEntities((todo) => ({ text: `${todo.text} ${todo.id}` })));
```

### Remove — by id, ids, or predicate

```ts
patchState(store, removeEntity(1));
patchState(store, removeEntities([1, 2]));
patchState(store, removeEntities((todo) => todo.completed));
patchState(store, removeAllEntities());
```

Removing a non-existent entity is a no-op, not an error.

## Custom entity identifier

When the identifier is not named `id`, supply a `selectId` returning a `string` or `number`. Every
`add*`, `set*`, and `update*` updater takes an optional config object as its second argument.

```ts
import { patchState, signalStore, withMethods } from '@ngrx/signals';
import {
  addEntities,
  removeEntity,
  SelectEntityId,
  updateAllEntities,
  withEntities,
} from '@ngrx/signals/entities';

type Todo = { key: number; text: string; completed: boolean };

const selectId: SelectEntityId<Todo> = (todo) => todo.key;

export const TodosStore = signalStore(
  withEntities<Todo>(),
  withMethods((store) => ({
    addTodos(todos: Todo[]): void {
      patchState(store, addEntities(todos, { selectId }));
    },
    completeAllTodos(): void {
      patchState(store, updateAllEntities({ completed: true }, { selectId }));
    },
    removeTodo(key: number): void {
      patchState(store, removeEntity(key)); // 👈 no selectId
    },
  }))
);
```

The `remove*` updaters select the correct identifier automatically, so they never need `selectId`.

## Named entity collections

A `collection` name prefixes the generated members.

```ts
import { signalStore, type } from '@ngrx/signals';
import { withEntities } from '@ngrx/signals/entities';

export const TodosStore = signalStore(
  // entity type declared with the `type` helper
  withEntities({ entity: type<Todo>(), collection: 'todo' })
);
```

`ids` / `entityMap` / `entities` become `todoIds` / `todoEntityMap` / `todoEntities`, and every
updater then has to name the collection:

```ts
patchState(store, addEntity(todo, { collection: 'todo' }));
patchState(store, removeEntity(id, { collection: 'todo' }));
```

Because collections are named, `withEntities` can appear several times in one store
(`collection: 'book'`, `collection: 'author'`, …). The docs note this is possible but recommend
dedicated stores per entity type in most cases — the multi-collection store is the exception.

## `entityConfig` — the DRY answer

Repeating `{ collection, selectId }` at every call site is where mistakes creep in. `entityConfig`
builds the config once, strongly typed (entity required; `collection` and `selectId` optional), and
the same object goes to `withEntities` *and* to every updater. Prefer this whenever a collection is
named or has a custom id.

<!-- Deliberate divergence from the upstream entity-management.md page, whose entityConfig example
     passes the whole `todo` to removeEntity. That does not compile: removeEntity's overloads take
     an EntityId (modules/signals/entities/src/updaters/remove-entity.ts), never an entity object.
     /ngrx-signals-sync: do not "fix" removeTodo back to the upstream form. -->

```ts
import { patchState, signalStore, type, withMethods } from '@ngrx/signals';
import {
  addEntity,
  entityConfig,
  removeEntity,
  withEntities,
} from '@ngrx/signals/entities';

type Todo = { key: number; text: string; completed: boolean };

const todoConfig = entityConfig({
  entity: type<Todo>(),
  collection: 'todo',
  selectId: (todo) => todo.key,
});

export const TodosStore = signalStore(
  withEntities(todoConfig),
  withMethods((store) => ({
    addTodo(todo: Todo): void {
      patchState(store, addEntity(todo, todoConfig));
    },
    removeTodo(key: number): void {
      patchState(store, removeEntity(key, todoConfig)); // removeEntity takes the id, not the entity
    },
  }))
);
```

## Private entity collections

A collection name prefixed with `_` makes the generated members private to the store. Re-expose a
curated view through `withComputed` so consumers see only what you intend.

```ts
const todoConfig = entityConfig({
  entity: type<Todo>(),
  collection: '_todo', // 👈 private
});

const TodosStore = signalStore(
  withEntities(todoConfig),
  withComputed(({ _todoEntities }) => ({
    todos: _todoEntities, // 👈 the public surface
  }))
);
```

The component binds to `store.todos()`; `_todoEntityMap` and `_todoIds` stay internal. See
`references/store-composition.md` for private store members in general, and
`references/custom-features.md` for packaging entity logic into a reusable feature.
