"""Regenerate the 111 shuffle vectors without loading or launching the game.

Independent reference implementation from ILSpy's 0.111.0 Rng.Shuffle,
MegaRandom.Splitmix64, NextULongInner, NextDouble and NextInner(int).
The production RavenShuffleHooks implementation is deliberately not imported.
"""

import json

MASK = (1 << 64) - 1


def rotate_left(value, bits):
    return ((value << bits) | (value >> (64 - bits))) & MASK


class LegacyRandom:
    def __init__(self, seed):
        self.state = []
        for _ in range(4):
            seed = (seed + 11400714819323198485) & MASK
            value = ((seed ^ (seed >> 30)) * 13787848793156543929) & MASK
            value = ((value ^ (value >> 27)) * 10723151780598845931) & MASK
            self.state.append(value ^ (value >> 31))

    def next_ulong(self):
        a, b, c, d = self.state
        result = (rotate_left((b * 5) & MASK, 7) * 9) & MASK
        shifted = (b << 17) & MASK
        c ^= a
        d ^= b
        b ^= c
        a ^= d
        c ^= shifted
        self.state = [a, b, c, rotate_left(d, 45)]
        return result

    def shuffle(self, items):
        for index in range(len(items) - 1, 0, -1):
            fraction = (self.next_ulong() >> 11) * 1.1102230246251565e-16
            other = int(fraction * (index + 1))
            items[index], items[other] = items[other], items[index]


def vectors():
    for seed, count in [(0, 0), (0, 1), (918273, 8), (MASK, 16)]:
        random = LegacyRandom(seed)
        items = list(range(count))
        random.shuffle(items)
        next_value = random.next_ulong()
        continuation = list(range(5))
        random.shuffle(continuation)
        yield dict(seed=seed, count=count, items=items, next=next_value,
                   continuation=continuation, after_continuation=random.next_ulong())


if __name__ == "__main__":
    print(json.dumps(list(vectors()), indent=2))
