"""Iterator building blocks implemented as guest generators."""

__all__ = [
    "accumulate", "batched", "chain", "chain_from_iterable", "combinations",
    "combinations_with_replacement", "compress", "count", "cycle",
    "dropwhile", "filterfalse", "islice", "pairwise", "permutations",
    "product", "repeat", "takewhile", "zip_longest",
]

_missing = []


def count(start=0, step=1):
    value = start
    while True:
        yield value
        value += step


def repeat(object, times=None):
    if times is None:
        while True:
            yield object
    else:
        while times > 0:
            yield object
            times -= 1


def cycle(iterable):
    saved = []
    for element in iterable:
        yield element
        saved.append(element)
    while saved:
        for element in saved:
            yield element


def chain(*iterables):
    for iterable in iterables:
        for element in iterable:
            yield element


def chain_from_iterable(iterable):
    for inner in iterable:
        for element in inner:
            yield element


def compress(data, selectors):
    data_iterator = iter(data)
    selector_iterator = iter(selectors)
    while True:
        try:
            datum = next(data_iterator)
            selected = next(selector_iterator)
        except StopIteration:
            return
        if selected:
            yield datum


def dropwhile(predicate, iterable):
    iterator = iter(iterable)
    for element in iterator:
        if not predicate(element):
            yield element
            break
    for element in iterator:
        yield element


def takewhile(predicate, iterable):
    for element in iterable:
        if not predicate(element):
            return
        yield element


def filterfalse(predicate, iterable):
    if predicate is None:
        for element in iterable:
            if not element:
                yield element
    else:
        for element in iterable:
            if not predicate(element):
                yield element


def islice(iterable, start, stop=_missing, step=1):
    if stop is _missing:
        stop = start
        start = 0
    if start is None:
        start = 0
    if step is None:
        step = 1
    if start < 0:
        raise ValueError("Indices for islice() must be None or an integer: 0 <= x")
    if stop is not None and stop < 0:
        raise ValueError("Stop argument for islice() must be None or an integer: 0 <= x")
    if step <= 0:
        raise ValueError("Step for islice() must be a positive integer or None")

    index = 0
    next_index = start
    for element in iterable:
        if stop is not None and index >= stop:
            return
        if index == next_index:
            yield element
            next_index += step
        index += 1


def accumulate(iterable, func=None, *, initial=None):
    iterator = iter(iterable)
    if initial is None:
        try:
            total = next(iterator)
        except StopIteration:
            return
    else:
        total = initial
    yield total

    if func is None:
        for element in iterator:
            total += element
            yield total
    else:
        for element in iterator:
            total = func(total, element)
            yield total


def pairwise(iterable):
    iterator = iter(iterable)
    try:
        previous = next(iterator)
    except StopIteration:
        return
    for current in iterator:
        yield (previous, current)
        previous = current


def batched(iterable, n, *, strict=False):
    if n < 1:
        raise ValueError("n must be at least one")
    iterator = iter(iterable)
    while True:
        batch = []
        index = 0
        while index < n:
            try:
                batch.append(next(iterator))
            except StopIteration:
                if not batch:
                    return
                if strict:
                    raise ValueError("batched(): incomplete batch")
                yield tuple(batch)
                return
            index += 1
        yield tuple(batch)


def _tuple_from_indices(pool, indices, count_value):
    result = []
    index = 0
    while index < count_value:
        result.append(pool[indices[index]])
        index += 1
    return tuple(result)


def product(*iterables, repeat=1):
    if repeat < 0:
        raise ValueError("repeat argument cannot be negative")

    base_pools = []
    for iterable in iterables:
        base_pools.append(tuple(iterable))

    pools = []
    repetition = 0
    while repetition < repeat:
        index = 0
        while index < len(base_pools):
            pools.append(base_pools[index])
            index += 1
        repetition += 1

    pool_count = len(pools)
    if pool_count == 0:
        yield ()
        return

    for pool in pools:
        if len(pool) == 0:
            return

    indices = [0] * pool_count
    while True:
        result = []
        index = 0
        while index < pool_count:
            result.append(pools[index][indices[index]])
            index += 1
        yield tuple(result)

        position = pool_count - 1
        while position >= 0:
            indices[position] += 1
            if indices[position] < len(pools[position]):
                break
            indices[position] = 0
            position -= 1
        if position < 0:
            return


def permutations(iterable, r=None):
    pool = tuple(iterable)
    size = len(pool)
    if r is None:
        r = size
    if r < 0:
        raise ValueError("r must be non-negative")
    if r > size:
        return

    indices = list(range(size))
    cycles = list(range(size, size - r, -1))
    yield _tuple_from_indices(pool, indices, r)

    while size:
        position = r - 1
        while position >= 0:
            cycles[position] -= 1
            if cycles[position] == 0:
                removed = indices[position]
                shift = position
                while shift < size - 1:
                    indices[shift] = indices[shift + 1]
                    shift += 1
                indices[size - 1] = removed
                cycles[position] = size - position
                position -= 1
            else:
                swap_index = size - cycles[position]
                indices[position], indices[swap_index] = indices[swap_index], indices[position]
                yield _tuple_from_indices(pool, indices, r)
                break
        if position < 0:
            return


def combinations(iterable, r):
    pool = tuple(iterable)
    size = len(pool)
    if r < 0:
        raise ValueError("r must be non-negative")
    if r > size:
        return

    indices = list(range(r))
    yield _tuple_from_indices(pool, indices, r)

    while True:
        position = r - 1
        while position >= 0 and indices[position] == position + size - r:
            position -= 1
        if position < 0:
            return
        indices[position] += 1
        next_position = position + 1
        while next_position < r:
            indices[next_position] = indices[next_position - 1] + 1
            next_position += 1
        yield _tuple_from_indices(pool, indices, r)


def combinations_with_replacement(iterable, r):
    pool = tuple(iterable)
    size = len(pool)
    if r < 0:
        raise ValueError("r must be non-negative")
    if size == 0 and r > 0:
        return

    indices = [0] * r
    yield _tuple_from_indices(pool, indices, r)

    while True:
        position = r - 1
        while position >= 0 and indices[position] == size - 1:
            position -= 1
        if position < 0:
            return
        value = indices[position] + 1
        while position < r:
            indices[position] = value
            position += 1
        yield _tuple_from_indices(pool, indices, r)


def zip_longest(*iterables, fillvalue=None):
    iterators = []
    active = []
    for iterable in iterables:
        iterators.append(iter(iterable))
        active.append(True)

    remaining = len(iterators)
    if remaining == 0:
        return

    while True:
        values = []
        index = 0
        while index < len(iterators):
            if active[index]:
                try:
                    values.append(next(iterators[index]))
                except StopIteration:
                    active[index] = False
                    remaining -= 1
                    values.append(fillvalue)
            else:
                values.append(fillvalue)
            index += 1
        if remaining == 0:
            return
        yield tuple(values)
