"""Python 3.14 keyword tables."""

__all__ = ["iskeyword", "issoftkeyword", "kwlist", "softkwlist"]

kwlist = [
    "False", "None", "True", "and", "as", "assert", "async", "await",
    "break", "class", "continue", "def", "del", "elif", "else", "except",
    "finally", "for", "from", "global", "if", "import", "in", "is",
    "lambda", "nonlocal", "not", "or", "pass", "raise", "return", "try",
    "while", "with", "yield",
]

softkwlist = ["_", "case", "match", "type"]


def iskeyword(value):
    return value in kwlist


def issoftkeyword(value):
    return value in softkwlist
