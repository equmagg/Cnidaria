"""CPython operator module."""


def add(a, b):
    return a + b


def sub(a, b):
    return a - b


def mul(a, b):
    return a * b


def matmul(a, b):
    return a @ b


def truediv(a, b):
    return a / b


def floordiv(a, b):
    return a // b


def mod(a, b):
    return a % b


def pow(a, b):
    return a ** b


def lshift(a, b):
    return a << b


def rshift(a, b):
    return a >> b


def and_(a, b):
    return a & b


def xor(a, b):
    return a ^ b


def or_(a, b):
    return a | b


def neg(a):
    return -a


def pos(a):
    return +a


def invert(a):
    return ~a


def not_(a):
    return not a


def truth(a):
    return bool(a)


def is_(a, b):
    return a is b


def is_not(a, b):
    return a is not b


def lt(a, b):
    return a < b


def le(a, b):
    return a <= b


def eq(a, b):
    return a == b


def ne(a, b):
    return a != b


def ge(a, b):
    return a >= b


def gt(a, b):
    return a > b


def contains(a, b):
    return b in a


def getitem(a, b):
    return a[b]


def setitem(a, b, c):
    a[b] = c


def delitem(a, b):
    del a[b]


def countOf(a, b):
    count = 0
    for item in a:
        if item == b:
            count += 1
    return count


def indexOf(a, b):
    index = 0
    for item in a:
        if item == b:
            return index
        index += 1
    raise ValueError("sequence.index(x): x not in sequence")


__add__ = add
__sub__ = sub
__mul__ = mul
__matmul__ = matmul
__truediv__ = truediv
__floordiv__ = floordiv
__mod__ = mod
__pow__ = pow
__lshift__ = lshift
__rshift__ = rshift
__and__ = and_
__xor__ = xor
__or__ = or_
__neg__ = neg
__pos__ = pos
__invert__ = invert
__not__ = not_
__lt__ = lt
__le__ = le
__eq__ = eq
__ne__ = ne
__ge__ = ge
__gt__ = gt
__contains__ = contains
__getitem__ = getitem
__setitem__ = setitem
__delitem__ = delitem
