"""Bisection algorithms."""

__all__ = ["bisect", "bisect_left", "bisect_right", "insort", "insort_left", "insort_right"]


def bisect_right(a, x, lo=0, hi=None, *, key=None):
    if lo < 0:
        raise ValueError("lo must be non-negative")
    if hi is None:
        hi = len(a)
    while lo < hi:
        mid = (lo + hi) // 2
        value = a[mid]
        if key is not None:
            value = key(value)
        if x < value:
            hi = mid
        else:
            lo = mid + 1
    return lo


def bisect_left(a, x, lo=0, hi=None, *, key=None):
    if lo < 0:
        raise ValueError("lo must be non-negative")
    if hi is None:
        hi = len(a)
    while lo < hi:
        mid = (lo + hi) // 2
        value = a[mid]
        if key is not None:
            value = key(value)
        if value < x:
            lo = mid + 1
        else:
            hi = mid
    return lo


def _insert(a, index, value):
    a.append(value)
    position = len(a) - 1
    while position > index:
        a[position] = a[position - 1]
        position -= 1
    a[index] = value


def insort_right(a, x, lo=0, hi=None, *, key=None):
    comparison = x if key is None else key(x)
    _insert(a, bisect_right(a, comparison, lo, hi, key=key), x)


def insort_left(a, x, lo=0, hi=None, *, key=None):
    comparison = x if key is None else key(x)
    _insert(a, bisect_left(a, comparison, lo, hi, key=key), x)


bisect = bisect_right
insort = insort_right
