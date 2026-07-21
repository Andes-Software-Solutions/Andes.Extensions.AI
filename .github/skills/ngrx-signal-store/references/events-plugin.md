# Events Plugin (`@ngrx/signals/events`)

An opt-in Flux/redux-style layer on top of SignalStore: events are dispatched, reducers turn them into state, and event handlers run side effects. It is a separate entry point, and most stores never import it.

## Decide first: do you actually need this?

The NgRx docs are explicit about positioning:

> While the default SignalStore approach is sufficient for most use cases, the Events plugin excels in more advanced scenarios that involve inter-store coordination or benefit from a decoupled architecture.

Read that as a decision rule:

| Situation | Use |
| --- | --- |
| One store owns the state; a component calls it directly | `withMethods` — a plain method is the right answer |
| One thing that happens must be observed by **several** stores | Events plugin |
| You want dispatch/reducer separation (the _what_ decoupled from the _how_) | Events plugin |
| Micro-frontends / feature modules that need an isolated event bus | Events plugin + scoped events |

If a single store owns the state and a method would do, write the method. Reaching for events to model a button click that updates one store adds an event creator, a reducer entry, and a dispatch call in place of one method — cost without the coupling benefit it is meant to buy.

The architecture, once you opt in, is four pieces: **Event** (something happened) → **Dispatcher** (event bus) → **Store** (reducers for state transitions, event handlers for side effects) → **View** (renders state, dispatches new events).

## Defining events

`event(type, payloadSchema?)` declares a single event creator. The payload type is declared with `type<T>()` from `@ngrx/signals`.

```ts
import { type } from '@ngrx/signals';
import { event } from '@ngrx/signals/events';
import { Book } from './book';

export const opened = event('[Book Search Page] Opened');
export const queryChanged = event('[Book Search Page] Query Changed', type<string>());
export const loadedSuccess = event('[Books API] Loaded Success', type<Book[]>());
```

Calling a creator returns a plain object: `opened()` → `{ type: '[Book Search Page] Opened' }`, `loadedSuccess(books)` → `{ type: '[Books API] Loaded Success', payload: books }`. `type` is the unique identifier; `payload` is optional.

The docs recommend the `"[Source] EventName"` pattern for event types — it keeps the origin of an event visible at every dispatch site.

`eventGroup({ source, events })` removes the repetition when many events share a source. Each key is an event name, each value is its payload type; the resulting types are prefixed with the source automatically.

```ts
import { type } from '@ngrx/signals';
import { eventGroup } from '@ngrx/signals/events';
import { Book } from './book';

export const bookSearchEvents = eventGroup({
  source: 'Book Search Page',
  events: {
    opened: type<void>(), // no payload
    queryChanged: type<string>(),
  },
});

export const booksApiEvents = eventGroup({
  source: 'Books API',
  events: {
    loadedSuccess: type<Book[]>(),
    loadedFailure: type<string>(),
  },
});
```

`bookSearchEvents.opened()` yields `{ type: '[Book Search Page] opened' }`; `booksApiEvents.loadedSuccess(books)` yields `{ type: '[Books API] loadedSuccess', payload: books }`.

## State transitions: `withReducer` + `on`

`withReducer(...caseReducers)` is the store feature; `on(...events, handler)` maps one or more events to a handler. The handler receives the dispatched event first and the current state second, and returns a partial state object, a `PartialStateUpdater`, or an array of either.

```ts
import { signalStore, withState } from '@ngrx/signals';
import { on, withReducer } from '@ngrx/signals/events';
import { bookSearchEvents } from './book-search-events';
import { booksApiEvents } from './books-api-events';
import { Book } from './book';

type State = { query: string; books: Book[]; isLoading: boolean };

export const BookSearchStore = signalStore(
  withState<State>({ query: '', books: [], isLoading: false }),
  withReducer(
    on(bookSearchEvents.opened, () => ({ isLoading: true })),
    on(bookSearchEvents.queryChanged, ({ payload: query }) => ({ query, isLoading: true })),
    on(booksApiEvents.loadedSuccess, ({ payload: books }) => ({ books, isLoading: false })),
    on(booksApiEvents.loadedFailure, () => ({ isLoading: false })),
  ),
);
```

Returning `PartialStateUpdater`s (from `@ngrx/signals`) instead of literals keeps reusable logic out of the reducer body — `on(increment, () => incrementFirst())`, or `on(incrementBoth, () => [incrementFirst(), incrementSecond()])` for several at once.

## Side effects: `withEventHandlers`

`withEventHandlers` takes a factory receiving the store instance and returning either a dictionary or an array of observables. Each handler listens via the `Events` service, whose `on(...creators)` method returns an observable of dispatched events filtered to those types. **If a handler emits an event, that event is dispatched automatically** — that is how an API result feeds back into the reducers.

```ts
import { inject } from '@angular/core';
import { switchMap, tap } from 'rxjs';
import { Events, withEventHandlers } from '@ngrx/signals/events';
import { mapResponse } from '@ngrx/operators';
import { BooksService } from './books-service';

export const BookSearchStore = signalStore(
  // ...withState / withReducer
  withEventHandlers((store, events = inject(Events), booksService = inject(BooksService)) => ({
    loadBooksByQuery$: events
      .on(bookSearchEvents.opened, bookSearchEvents.queryChanged)
      .pipe(
        switchMap(() =>
          booksService.getByQuery(store.query()).pipe(
            mapResponse({
              next: (books) => booksApiEvents.loadedSuccess(books),
              error: (error: { message: string }) => booksApiEvents.loadedFailure(error.message),
            }),
          ),
        ),
      ),
    logError$: events
      .on(booksApiEvents.loadedFailure)
      .pipe(tap(({ payload }) => console.error(payload))),
  })),
);
```

Handlers are not restricted to the `Events` service — any observable works (e.g. `timer(0, 30_000).pipe(exhaustMap(...))` for polling), and the factory may return an array instead of a dictionary.

`ReducerEvents` is the sibling service to reach for when `withReducer` cannot express a transition. It receives dispatched events *before* `Events` does, so state is already updated by the time other handlers react:

```ts
import { patchState } from '@ngrx/signals';
import { ReducerEvents, withEventHandlers } from '@ngrx/signals/events';

export const CounterStore = signalStore(
  withState({ count: 0 }),
  withEventHandlers((store, events = inject(ReducerEvents)) => [
    events.on(counterPageEvents.set).pipe(tap(({ payload }) => patchState(store, { count: payload }))),
  ]),
);
```

## Dispatching

The plugin changes only how state is *updated*, not how it is read — components still read `store.query()`, `store.books()` and so on.

`Dispatcher` is the service (`dispatch(event, config?)`). `injectDispatch(eventCreators)` is the ergonomic wrapper: pass an event group (or any dictionary of creators) and get back an object of methods that create and dispatch in one call.

```ts
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { injectDispatch } from '@ngrx/signals/events';
import { BookSearchStore } from './book-search-store';
import { bookSearchEvents } from './book-search-events';

@Component({
  selector: 'ngrx-book-search',
  imports: [FormsModule],
  template: `
    <input type="text" [ngModel]="store.query()" (ngModelChange)="dispatch.queryChanged($event)" />
    @if (store.isLoading()) { <p>Loading...</p> }
    <ul>
      @for (book of store.books(); track book.id) { <li>{{ book.title }}</li> }
    </ul>
  `,
  providers: [BookSearchStore],
})
export class BookSearch {
  readonly store = inject(BookSearchStore);
  readonly dispatch = injectDispatch(bookSearchEvents);

  constructor() {
    this.dispatch.opened();
  }
}
```

## Scoped events

By default `Dispatcher` and `Events` are global: every dispatched event is visible application-wide. `provideDispatcher()` in a component's or route's `providers` creates a local scope, which is what you want for component-local state or for micro-frontends that must not leak events into the host.

Each dispatch picks a scope:

- `self` (default) — dispatched and handled only within the local scope.
- `parent` — forwarded to the parent dispatcher.
- `global` — forwarded to the global dispatcher.

```ts
@Component({
  providers: [provideDispatcher(), BookSearchStore], // local Dispatcher + Events
})
export class BookSearch {
  readonly dispatch = injectDispatch(bookSearchEvents);

  constructor() {
    this.dispatch.opened(); // local scope
  }

  changeQuery(query: string): void {
    this.dispatch({ scope: 'parent' }).queryChanged(query);
  }
}
```

With `Dispatcher` directly, the scope is the second argument: `dispatcher.dispatch(counterPageEvents.increment(), { scope: 'parent' })`.

Visibility is hierarchical: an `Events` service receives events from its own scope and from any ancestor scope including global; events dispatched locally are **not** visible to ancestors.

Inside event handlers, `EventScope` values are applied with two helpers:

- `toScope(scope)` — forwards a single returned event to that scope. Return it alongside the event: `error: (e) => [booksApiEvents.loadedFailure(e.message), toScope('global')]`.
- `mapToScope(scope)` — an RxJS operator that forwards every event returned by that handler: `.pipe(mapResponse({ ... }), mapToScope('parent'))`.

## See also

- Store structure, `withState` / `withComputed` / `withMethods` — `references/store-composition.md`
- Async without events (`rxMethod`, promises) — `references/async-and-rxjs.md`
- Full symbol list and import paths — `references/api-reference.md`
