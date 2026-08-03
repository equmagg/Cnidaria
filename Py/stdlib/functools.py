"""Function tools."""

__all__ = [
    "reduce", "partial", "update_wrapper", "wraps", "total_ordering",
]


_missing = []


def reduce(function, iterable, initial=_missing):
    iterator = iter(iterable)
    if initial is _missing:
        try:
            result = next(iterator)
        except StopIteration:
            raise TypeError("reduce() of empty iterable with no initial value")
    else:
        result = initial
    for item in iterator:
        result = function(result, item)
    return result


class partial:
    def __init__(self, function, *args, **keywords):
        if not callable(function):
            raise TypeError("the first argument must be callable")
        self.func = function
        self.args = args
        self.keywords = dict(keywords)

    def __call__(self, *args, **keywords):
        merged = dict(self.keywords)
        for name in keywords:
            merged[name] = keywords[name]
        return self.func(*self.args, *args, **merged)

    def __repr__(self):
        return "functools.partial(" + repr(self.func) + ")"


def update_wrapper(wrapper, wrapped, assigned=("__name__", "__doc__"), updated=()):
    for attribute in assigned:
        try:
            value = getattr(wrapped, attribute)
        except AttributeError:
            continue
        setattr(wrapper, attribute, value)
    for attribute in updated:
        try:
            destination = getattr(wrapper, attribute)
            source = getattr(wrapped, attribute)
        except AttributeError:
            continue
        for name in source:
            destination[name] = source[name]
    wrapper.__wrapped__ = wrapped
    return wrapper


def wraps(wrapped, assigned=("__name__", "__doc__"), updated=()):
    def decorator(wrapper):
        return update_wrapper(wrapper, wrapped, assigned, updated)
    return decorator


def total_ordering(cls):
    roots = []
    for name in ("__lt__", "__le__", "__gt__", "__ge__"):
        if name in cls.__dict__:
            roots.append(name)
    if not roots:
        raise ValueError("must define at least one ordering operation")

    root = roots[0]
    if root == "__lt__":
        if "__le__" not in cls.__dict__:
            def __le__(self, other):
                return self < other or self == other
            cls.__le__ = __le__
        if "__gt__" not in cls.__dict__:
            def __gt__(self, other):
                return not (self < other or self == other)
            cls.__gt__ = __gt__
        if "__ge__" not in cls.__dict__:
            def __ge__(self, other):
                return not self < other
            cls.__ge__ = __ge__
    elif root == "__le__":
        if "__lt__" not in cls.__dict__:
            def __lt__(self, other):
                return self <= other and self != other
            cls.__lt__ = __lt__
        if "__gt__" not in cls.__dict__:
            def __gt__(self, other):
                return not self <= other
            cls.__gt__ = __gt__
        if "__ge__" not in cls.__dict__:
            def __ge__(self, other):
                return self == other or not self <= other
            cls.__ge__ = __ge__
    elif root == "__gt__":
        if "__lt__" not in cls.__dict__:
            def __lt__(self, other):
                return not (self > other or self == other)
            cls.__lt__ = __lt__
        if "__le__" not in cls.__dict__:
            def __le__(self, other):
                return not self > other
            cls.__le__ = __le__
        if "__ge__" not in cls.__dict__:
            def __ge__(self, other):
                return self > other or self == other
            cls.__ge__ = __ge__
    else:
        if "__lt__" not in cls.__dict__:
            def __lt__(self, other):
                return not self >= other
            cls.__lt__ = __lt__
        if "__le__" not in cls.__dict__:
            def __le__(self, other):
                return self == other or not self >= other
            cls.__le__ = __le__
        if "__gt__" not in cls.__dict__:
            def __gt__(self, other):
                return self >= other and self != other
            cls.__gt__ = __gt__
    return cls
