# Testing a SignalStore

A SignalStore is an Angular service and is tested like one. The doctrine below drives every concrete
choice on this page, so read it first — most brittle store tests come from violating one of its three
rules rather than from a missing API.

Examples follow upstream in using Vitest browser mode (`vitest/browser`'s `page`, `expect.element`,
`expect.poll`, `vi.fn()`, `TestBed.tick()`). The *patterns* — TestBed, `unprotected`, mock the
service not the store — are runner-agnostic and carry over to Jest or Karma; only the polling/flush
helpers differ (`fakeAsync` + `tick()`, `await fixture.whenStable()`, `jest.fn()`).

## Doctrine

**Public API only.** Assert on what the store exposes: state signals, computed signals, and the
observable effect of calling its methods. Asserting on internal state or reaching for internal
methods ties the test to the implementation and makes it brittle — a refactor that preserves
behaviour should not break the test.

**Use TestBed, not `new Store()`.** TestBed supplies dependency injection and the injection context
that `rxMethod()`, `signalMethod()`, and `inject()` require. Constructing the store with `new`
simply will not work for stores that use any of those.

**Do not spy on the store's own methods.** If a method grows complex enough that you want to spy on
it, that is a signal to extract the logic into a service: the method calls the service, and the test
mocks or fakes the service. Asserting on the resulting *state* is preferred over asserting that a
method was called.

Three shapes of store test follow from this: the store in isolation (mock its dependencies, exercise
its API), the store inside a wider feature test alongside components, and a fake/mock store provided
to a component that consumes it.

## Providing the store

```ts
import { TestBed } from '@angular/core/testing';
import { signalStore, withState } from '@ngrx/signals';

// 👇 globally provided: TestBed.inject is enough
const CounterStore = signalStore({ providedIn: 'root' }, withState({ count: 0 }));

it('is defined with an initial count', () => {
  const store = TestBed.inject(CounterStore);

  expect(store).toBeDefined();
  expect(store.count()).toBe(0);
});
```

```ts
// 👇 locally provided: the testing module provides it
const CounterStore = signalStore(withState({ count: 0 }));

it('is defined with an initial count', () => {
  TestBed.configureTestingModule({ providers: [CounterStore] });
  const store = TestBed.inject(CounterStore);

  expect(store.count()).toBe(0);
});
```

Asserting on members is then unremarkable — call a method, read the signals:

```ts
it('updates doubleCount when count changes on increment', () => {
  const store = TestBed.inject(CounterStore); // withComputed(doubleCount), withMethods(increment)

  store.increment();
  expect(store.count()).toBe(1);
  expect(store.doubleCount()).toBe(2);
});
```

## `unprotected` — forcing state in a test

State is protected by default, so `patchState` cannot write to a store instance from outside. Rather
than shipping `{ protectedState: false }` in production code purely to make tests convenient, wrap
the instance with `unprotected` from `@ngrx/signals/testing` — it returns a writable view. This is
the most useful thing on the page: it lets you set up an arbitrary state precondition without a
public setter existing for it.

```ts
import { patchState, signalStore, withComputed, withState } from '@ngrx/signals';
import { unprotected } from '@ngrx/signals/testing';

it('recomputes doubleCount when count is patched via unprotected', () => {
  const store = TestBed.inject(CounterStore);

  //         👇 makes the store writable
  patchState(unprotected(store), { count: 5 });

  expect(store.count()).toBe(5);
  expect(store.doubleCount()).toBe(10);
});
```

## Mocking the store's dependencies

Register the dependency with `useValue` and supply an object implementing only the methods the store
actually calls.

```ts
// store: withMethods((store, stepService = inject(StepService)) => ({
//   increment() { patchState(store, ({ count }) => ({ count: count + stepService.getStep() })); },
// }))

it('increments by the step returned by the injected service', () => {
  const mockStepService = { getStep: () => 3 };

  TestBed.configureTestingModule({
    providers: [{ provide: StepService, useValue: mockStepService }],
  });
  const store = TestBed.inject(CounterStore);

  store.increment();
  expect(store.count()).toBe(3);
});
```

This is also the escape hatch from the doctrine: complex method logic moves into a service, and the
service gets faked here — instead of spying on the store method.

## Testing a `signalMethod`

A static input is processed synchronously. A **signal** input goes through an internal `effect`, so
the call must happen inside an injection context and the effect must be flushed before asserting.

```ts
// store: increment: signalMethod<number>((step) =>
//   patchState(store, ({ count }) => ({ count: count + step }))),

it('increments by a static step synchronously', () => {
  const store = TestBed.inject(CounterStore);

  store.increment(1); // static value: no flush needed
  expect(store.count()).toBe(1);
});

it('increments by a signal step after tick', async () => {
  const store = TestBed.inject(CounterStore);
  const step = signal(2);

  TestBed.runInInjectionContext(() => store.increment(step)); // 👈 injection context
  expect(store.count()).toBe(0);                              // not flushed yet

  await expect.poll(() => store.count()).toBe(2);

  step.set(1);
  TestBed.tick(); // 👈 alternative: flush the effect synchronously
  expect(store.count()).toBe(3);
});
```

## Testing an `rxMethod`

Same ideas as `signalMethod`, with observables added to the accepted input types. A synchronous
source settles before the next line; an asynchronous one needs polling.

```ts
import { asyncScheduler, of, scheduled, tap } from 'rxjs';
import { rxMethod } from '@ngrx/signals/rxjs-interop';

// store: increment: rxMethod<number>(
//   tap((n) => patchState(store, ({ count }) => ({ count: count + n })))),

it('adds emitted values when called with a synchronous Observable', () => {
  const store = TestBed.inject(CounterStore);

  store.increment(of(1, 2, 3));
  expect(store.count()).toBe(6); // asserts immediately
});

it('adds emitted values when called with an asynchronous Observable', async () => {
  const store = TestBed.inject(CounterStore);

  store.increment(scheduled([1, 2, 3], asyncScheduler));
  expect(store.count()).toBe(0);

  await expect.poll(() => store.count()).toBe(6);
});
```

## Mocking the store for a component test

Replace the store with a plain object exposing the same API: `signal()`s for state and computed
values, functions for methods. Provide it with `useValue`.

```ts
it('updates displayed count when the mock implements increment', async () => {
  const count = signal(0);
  const mockStore = { count, increment: () => count.set(count() + 1) };

  TestBed.configureTestingModule({
    providers: [{ provide: CounterStore, useValue: mockStore }],
  }).createComponent(CounterComponent);

  await expect.element(page.getByLabelText('count')).toHaveTextContent('0');
  await page.getByRole('button', { name: 'Increment' }).click();
  await expect.element(page.getByLabelText('count')).toHaveTextContent('1');
});
```

A method can also be a `vi.fn()` to assert it was called — but prefer the version above: asserting
on the resulting state survives refactors that a `toHaveBeenCalled` assertion does not.

## Testing a custom feature

A `signalStoreFeature` is tested by wrapping it in a minimal throwaway "testing store" and asserting
on that store's public API. See `references/custom-features.md` for authoring features.

```ts
it('has initial count and doubleCount, and increment updates both', () => {
  // 👇 "testing store" wraps the feature under test
  const CounterStore = signalStore({ providedIn: 'root' }, withCounter());
  const store = TestBed.inject(CounterStore);

  expect(store.count()).toBe(0);
  expect(store.doubleCount()).toBe(0);

  store.increment();
  expect(store.count()).toBe(1);
  expect(store.doubleCount()).toBe(2);
});
```
