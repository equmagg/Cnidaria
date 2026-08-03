"""Heap queue algorithms."""

__all__ = [
    "heappush", "heappop", "heapify", "heapreplace", "heappushpop",
    "merge", "nlargest", "nsmallest",
]


def _siftdown(heap, start, position):
    item = heap[position]
    while position > start:
        parent = (position - 1) // 2
        parent_item = heap[parent]
        if item < parent_item:
            heap[position] = parent_item
            position = parent
            continue
        break
    heap[position] = item


def _siftup(heap, position):
    end = len(heap)
    start = position
    item = heap[position]
    child = 2 * position + 1
    while child < end:
        right = child + 1
        if right < end and not heap[child] < heap[right]:
            child = right
        heap[position] = heap[child]
        position = child
        child = 2 * position + 1
    heap[position] = item
    _siftdown(heap, start, position)


def heappush(heap, item):
    heap.append(item)
    _siftdown(heap, 0, len(heap) - 1)


def heappop(heap):
    last = heap.pop()
    if not heap:
        return last
    result = heap[0]
    heap[0] = last
    _siftup(heap, 0)
    return result


def heapreplace(heap, item):
    result = heap[0]
    heap[0] = item
    _siftup(heap, 0)
    return result


def heappushpop(heap, item):
    if heap and heap[0] < item:
        item, heap[0] = heap[0], item
        _siftup(heap, 0)
    return item


def heapify(heap):
    index = len(heap) // 2 - 1
    while index >= 0:
        _siftup(heap, index)
        index -= 1


def merge(*iterables, key=None, reverse=False):
    active = []
    sequence = 0
    for iterable in iterables:
        iterator = iter(iterable)
        try:
            value = next(iterator)
        except StopIteration:
            continue
        comparison = value if key is None else key(value)
        active.append([comparison, sequence, value, iterator])
        sequence += 1

    while active:
        best = 0
        index = 1
        while index < len(active):
            left = active[index][0]
            right = active[best][0]
            if (right < left) if reverse else (left < right):
                best = index
            index += 1
        entry = active[best]
        yield entry[2]
        try:
            value = next(entry[3])
        except StopIteration:
            active[best] = active[-1]
            active.pop()
            continue
        entry[0] = value if key is None else key(value)
        entry[2] = value


def nsmallest(n, iterable, key=None):
    if n <= 0:
        return []
    return sorted(iterable, key=key)[:n]


def nlargest(n, iterable, key=None):
    if n <= 0:
        return []
    return sorted(iterable, key=key, reverse=True)[:n]
