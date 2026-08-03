"""The statistics module."""

from math import sqrt

__all__ = [
    "mean", "fmean", "geometric_mean", "harmonic_mean", "median",
    "median_low", "median_high", "pvariance", "variance", "pstdev", "stdev",
]


def _values(data):
    values = list(data)
    if not values:
        raise ValueError("data requires at least one data point")
    return values


def mean(data):
    values = _values(data)
    return sum(values) / len(values)


def fmean(data, weights=None):
    values = _values(data)
    if weights is None:
        return float(sum(values)) / len(values)
    weight_values = list(weights)
    if len(values) != len(weight_values):
        raise ValueError("data and weights must be the same length")
    total_weight = sum(weight_values)
    if total_weight == 0:
        raise ZeroDivisionError("sum of weights must be non-zero")
    total = 0.0
    index = 0
    while index < len(values):
        total += values[index] * weight_values[index]
        index += 1
    return total / total_weight


def geometric_mean(data):
    values = _values(data)
    result = 1.0
    for value in values:
        if value < 0:
            raise ValueError("geometric mean requires non-negative data")
        result *= value
    return result ** (1.0 / len(values))


def harmonic_mean(data):
    values = _values(data)
    reciprocal_sum = 0.0
    for value in values:
        if value < 0:
            raise ValueError("harmonic mean does not support negative values")
        if value == 0:
            return 0
        reciprocal_sum += 1.0 / value
    return len(values) / reciprocal_sum


def median(data):
    values = sorted(_values(data))
    middle = len(values) // 2
    if len(values) % 2:
        return values[middle]
    return (values[middle - 1] + values[middle]) / 2


def median_low(data):
    values = sorted(_values(data))
    return values[(len(values) - 1) // 2]


def median_high(data):
    values = sorted(_values(data))
    return values[len(values) // 2]


def pvariance(data, mu=None):
    values = _values(data)
    if mu is None:
        mu = mean(values)
    total = 0
    for value in values:
        difference = value - mu
        total += difference * difference
    return total / len(values)


def variance(data, xbar=None):
    values = _values(data)
    if len(values) < 2:
        raise ValueError("variance requires at least two data points")
    if xbar is None:
        xbar = mean(values)
    total = 0
    for value in values:
        difference = value - xbar
        total += difference * difference
    return total / (len(values) - 1)


def pstdev(data, mu=None):
    return sqrt(pvariance(data, mu))


def stdev(data, xbar=None):
    return sqrt(variance(data, xbar))
