# NgRx Signals API Reference

Lookup table for `@ngrx/signals` and its entry points. Use it to confirm a signature, an import path, or which entry point exports a symbol — not as a narrative read. Concepts live in the other reference files (linked at the bottom).

## Contents

- [Install and versions](#install-and-versions)
- [Store config options](#store-config-options)
- [`@ngrx/signals`](#ngrxsignals)
- [`@ngrx/signals/entities`](#ngrxsignalsentities)
- [`@ngrx/signals/rxjs-interop`](#ngrxsignalsrxjs-interop)
- [`@ngrx/signals/events`](#ngrxsignalsevents)
- [`@ngrx/signals/testing`](#ngrxsignalstesting)
- [Things that do not exist in NgRx](#things-that-do-not-exist-in-ngrx)
- [ESLint rules for signals](#eslint-rules-for-signals)
- [See also](#see-also)

## Install and versions

```sh
ng add @ngrx/signals@latest
```

`ng add` updates `package.json` and installs the dependency. Manual install (`npm i @ngrx/signals`) works too.

- Current version: **21.1.1**
- Peer dependency: `@angular/core` **^21**
- `rxjs` is an **optional** peer dependency — needed only if you import from `@ngrx/signals/rxjs-interop`. A store built entirely on `withMethods` + promises + `signalMethod` pulls in no RxJS.
- `@ngrx/operators` (`tapResponse`, `mapResponse`) is a separate package, not part of `@ngrx/signals`.

## Store config options

`signalStore` accepts an optional config object as its first argument, before the features.

```ts
export const BooksStore = signalStore(
  { providedIn: 'root', protectedState: false },
  withState(initialState),
);
```

| Option | Default | Meaning |
| --- | --- | --- |
| `providedIn` | *(none)* | Set to `'root'` to register the store with the root injector, giving a single shared instance app-wide. Omitted, the store must appear in a `providers` array (component, route, or root) before it can be injected — which is what ties it to a component lifecycle for local state. |
| `protectedState` | `true` | State is protected from external `patchState` calls. This is the recommended setting: updates go through store methods, keeping the data flow predictable. Setting it to `false` lets any consumer call `patchState(store, ...)` from outside. |

Tests can bypass protection without disabling it — see `unprotected` below.

## `@ngrx/signals`

**Store creation and features**

| Symbol | One-liner |
| --- | --- |
| `signalStore(config?, ...features)` | Creates an injectable store service from a sequence of features. |
| `signalStoreFeature(input?, ...features)` | Merges features into one reusable custom feature; the optional first argument declares required `state` / `props` / `methods` via `type<T>()`. |
| `type<T>()` | Compile-time-only type carrier. Used for payload types, entity types, and feature input types. It has no runtime value. |
| `withState(initialState \| factory)` | Adds state slices; the state type must be a record/object literal. The factory overload runs in the injection context, so initial state can come from a service or `InjectionToken`. |
| `withComputed(factory)` | Adds computed signals. The factory runs in the injection context and returns a dictionary of `computed` signals or plain functions (auto-wrapped in `computed`). |
| `withMethods(factory)` | Adds methods. The factory runs in the injection context; the store instance (state signals, props, previously defined methods) is the first argument, so dependencies can be default parameters: `(store, svc = inject(Svc)) => ({ ... })`. |
| `withHooks(hooks \| factory)` | Adds `onInit` / `onDestroy`. Object signature receives the store; factory signature runs in the injection context and returns the hooks, which lets `onDestroy` use injected dependencies. |
| `withProps(factory)` | Adds non-signal properties: observables (`toObservable(...)`), grouped injected dependencies, constants. |
| `withLinkedState(factory)` | Adds state slices derived from other signals. A computation function is wrapped in `linkedSignal()`; a `WritableSignal` can be supplied directly and stays two-way synchronized with the store. |
| `withFeature(callback)` | Takes a callback that receives the store instance and returns a feature — the way to pass store members (e.g. `entities`) as *input* to a feature without coupling that feature to the store's shape. |

**Standalone signal utilities**

| Symbol | One-liner |
| --- | --- |
| `signalState(initialState)` | Lightweight signal-based state for a component/service; returns a read-only signal that also exposes a signal per property. State type must be a record. |
| `SignalState<State>` | Type of a `signalState` instance. |
| `deepComputed(computation)` | `computed` that returns a `DeepSignal` when the result is an object literal, giving nested computed signals per property. |
| `DeepSignal<T>` | A read-only signal that also carries a signal for each property of `T`; nested signals are created lazily on first access. |
| `DeepSignalOf<T>` | Type helper resolving the deep-signal shape for `T`. |
| `signalMethod<Input>(processor, config?)` | Creates a processor function accepting a static value, a `Signal`, or a computation function; tracks signal inputs via an internal `effect`. RxJS-free counterpart to `rxMethod`. `config` accepts an `injector`. |
| `SignalMethod<Input>` | Type of a `signalMethod` instance; exposes `destroy()`. |

**State access and updates**

| Symbol | One-liner |
| --- | --- |
| `patchState(stateSource, ...updates)` | Type-safe update; each update is a partial state object or a `PartialStateUpdater`. Updates must be immutable. |
| `getState(stateSource)` | Reads the current state value; tracked when called inside a reactive context. |
| `watchState(stateSource, watcher, config?)` | Synchronously observes every state change (no glitch-free coalescing, unlike `effect`). Runs in an injection context by default; pass `{ injector }` otherwise. Returns an object with `destroy()`. |
| `StateWatcher<State>` | Type of the watcher callback passed to `watchState`. |
| `PartialStateUpdater<State>` | `(state: State) => Partial<State>` — the shape of a custom updater. |
| `StateSource<State>` | The state-carrying interface implemented by both `signalState` and `signalStore` instances. |
| `WritableStateSource<State>` | A `StateSource` that `patchState` is allowed to write to. |
| `isWritableStateSource(source)` | Runtime guard narrowing a `StateSource` to a `WritableStateSource`. |

**Types for feature authors**

| Symbol | One-liner |
| --- | --- |
| `SignalStoreFeature<Input, Output>` | Type of a store feature — what `signalStoreFeature` returns. |
| `SignalStoreFeatureResult` | The `{ state, props, methods }` shape a feature contributes. |
| `EmptyFeatureResult` | A `SignalStoreFeatureResult` contributing nothing; the base for features that add no members. |
| `StateSignals<State>` | The signal dictionary generated from a state type (`DeepSignal` for object slices, `Signal` otherwise). |
| `Prettify<T>` | Type helper that flattens intersections into a single readable object type in tooltips and errors. |

Private members: prefix a root-level state slice, prop, or method with `_` and it is not accessible from outside the store.

## `@ngrx/signals/entities`

`withEntities` adds `ids: Signal<EntityId[]>` and `entityMap: Signal<EntityMap<T>>` as state, plus `entities: Signal<T[]>` as a computed signal. Entities need an `id` of type `EntityId` (`string | number`) unless a `selectId` is supplied.

| Symbol | One-liner |
| --- | --- |
| `withEntities<Entity>()` / `withEntities(config)` | Adds an entity collection. The config form takes `{ entity: type<T>(), collection?, selectId? }`; a `collection` name prefixes the properties (`todoIds`, `todoEntityMap`, `todoEntities`). |
| `entityConfig(config)` | Builds a strongly-typed, reusable config object (`entity` required; `collection` and `selectId` optional) to pass to both `withEntities` and the updaters. |
| `addEntity(entity, config?)` | Adds one entity; existing ID is not overwritten, no error thrown. |
| `addEntities(entities, config?)` | Adds many; existing IDs are not overwritten. |
| `prependEntity(entity, config?)` | Adds one entity at the start of the collection. |
| `prependEntities(entities, config?)` | Adds many at the start, preserving their relative order. |
| `setEntity(entity, config?)` | Adds or **replaces** one entity. |
| `setEntities(entities, config?)` | Adds or replaces many. |
| `setAllEntities(entities, config?)` | Replaces the whole collection. |
| `updateEntity({ id, changes }, config?)` | Partial update by ID; `changes` is a partial object or `(entity) => Partial<Entity>`. No error if missing. |
| `updateEntities({ ids \| predicate, changes }, config?)` | Partial update of many, by IDs or by predicate. |
| `updateAllEntities(changes, config?)` | Partial update of every entity. |
| `upsertEntity(entity, config?)` | Adds, or **merges** into an existing entity (properties not supplied stay unchanged). |
| `upsertEntities(entities, config?)` | Adds or merges many. |
| `removeEntity(id, config?)` | Removes by ID. No error if missing. |
| `removeEntities(ids \| predicate, config?)` | Removes many by IDs or predicate. |
| `removeAllEntities(config?)` | Empties the collection. |

The `config?` argument carries `{ collection }` and/or `{ selectId }` — required for named collections, and required on `add*` / `set*` / `update*` when the entity has no `id` property. The `remove*` updaters resolve the identifier themselves and need no `selectId`.

| Type | One-liner |
| --- | --- |
| `EntityId` | `string \| number`. |
| `EntityMap<Entity>` | Record keyed by `EntityId`. |
| `EntityState<Entity>` | The `{ entityMap, ids }` state shape; use it as `signalStoreFeature({ state: type<EntityState<E>>() })` input. |
| `EntityProps<Entity>` | The computed props contributed by `withEntities` (`entities`). |
| `EntityChanges<Entity>` | The `changes` argument of the `update*` updaters. |
| `SelectEntityId<Entity>` | `(entity: Entity) => EntityId` — a custom ID selector. |
| `NamedEntityState<Entity, Collection>` | `EntityState` for a named collection. |
| `NamedEntityProps<Entity, Collection>` | `EntityProps` for a named collection. |

## `@ngrx/signals/rxjs-interop`

| Symbol | One-liner |
| --- | --- |
| `rxMethod<Input>(operatorChain, config?)` | Creates a reactive method from a chain of RxJS operators. Callable with a static value, a `Signal`, a computation function, or an `Observable`. Must be created in an injection context unless `{ injector }` is passed. |
| `RxMethod<Input>` | Type of an `rxMethod` instance; exposes `destroy()`, and each call returns a ref with its own `destroy()`. |

Calling `rxMethod` (or `signalMethod`) with a signal, computation function, or observable **outside** an injection context without an explicit `injector` is deprecated and will throw in a future version — call it in a constructor/field initializer, or pass `{ injector }`.

## `@ngrx/signals/events`

Opt-in Flux-style layer. See `references/events-plugin.md` for when it is warranted.

| Symbol | One-liner |
| --- | --- |
| `event(type, payload?)` | Declares an event creator; payload type via `type<T>()`. Convention for the type string: `"[Source] EventName"`. |
| `eventGroup({ source, events })` | Declares many creators sharing a source; types become `"[Source] eventName"`. |
| `EventCreator<Type, Payload>` | Type of an event creator function. |
| `EventInstance<Type, Payload>` | Type of a dispatched event object (`{ type }`, plus `payload` when defined). |
| `on(...eventCreators, handler)` | Case reducer: `(event, state) => Partial<State> \| PartialStateUpdater \| Array<either>`. |
| `withReducer(...caseReducers)` | Store feature applying `on(...)` case reducers to the store's state. |
| `withEventHandlers(factory)` | Store feature for side effects; the factory receives the store and returns a dictionary or array of observables. Any event a handler emits is dispatched automatically. |
| `Dispatcher` | Injectable event bus; `dispatch(event, config?)`, where `config` may carry `{ scope }`. |
| `provideDispatcher()` | Providers creating a **local** `Dispatcher` + `Events` scope at a component/route injector. |
| `injectDispatch(eventCreators)` | Returns an object mirroring the creators, each member creating and dispatching its event. Call it as `dispatch({ scope }).someEvent(...)` to target another scope. |
| `Events` | Injectable service; `on(...creators)` returns an observable of dispatched events filtered to those types. |
| `ReducerEvents` | Like `Events`, but receives events **before** it — use it for state transitions that `withReducer` cannot express, so `Events`-based side effects still see the updated state. |
| `EventScope` | The scope union: `'self'` (default), `'parent'`, `'global'`. |
| `toScope(scope)` | Forwards a single returned event to the given scope. |
| `mapToScope(scope)` | RxJS operator forwarding every event returned by a handler to the given scope. |

## `@ngrx/signals/testing`

| Symbol | One-liner |
| --- | --- |
| `unprotected(store)` | Returns a writable view of a protected store so a test can call `patchState(unprotected(store), { ... })` directly. |

That is the entire entry point — `unprotected` is the only export.

## Things that do not exist in NgRx

- **`withDevtools` is not part of NgRx.** It comes from the third-party `@angular-architects/ngrx-toolkit` package. The NgRx FAQ states plainly that there is no official connection between `@ngrx/signals` and the Redux DevTools. Do not import it from `@ngrx/signals` or any of its entry points.
- There is no `withDevTools`, `withStorageSync`, `withUndoRedo`, or `withCallState` in `@ngrx/signals` either — those are also community features (mostly `ngrx-toolkit`). The official surface is exactly the tables above.

## ESLint rules for signals

NgRx ships an ESLint plugin whose signals rules are the machine-checkable form of the idioms above:

| Rule | Enforces |
| --- | --- |
| `prefer-protected-state` | Do not set `protectedState: false`; keep state updates inside the store. |
| `enforce-type-call` | `type<T>` must be **called** (`type<T>()`), since it is a runtime function, not a bare type reference. |
| `signal-state-no-arrays-at-root-level` | The `signalState` root must be a record — put arrays on a property. |
| `with-state-no-arrays-at-root-level` | Same constraint for `withState`. |
| `signal-store-feature-should-use-generic-type` | Custom features that take input but declare no generic should add an unused generic (`function withX<_>()`), working around a TypeScript inference issue that otherwise breaks stores combining several such features. |

## See also

- Store structure and composition — `references/store-composition.md`
- Entity collections — `references/entity-management.md`
- Reusable features — `references/custom-features.md`
- Async, `rxMethod` vs `signalMethod` — `references/async-and-rxjs.md`
- Testing — `references/testing.md`
- Events plugin in depth — `references/events-plugin.md`
