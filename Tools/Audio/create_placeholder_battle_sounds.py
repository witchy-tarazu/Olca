#!/usr/bin/env python3
"""外部素材なしの戦闘仮SE。合成ノイズも固定seedで再現する。"""
from pathlib import Path
import array
import math
import random
import sys
import wave

OUTPUT = Path(__file__).resolve().parents[2] / 'Assets/Resources/OlcaBattleSounds'


def create(name, seconds, tone, noise):
    count = round(seconds * 48000)
    rng = random.Random(271026)
    envelope = [math.sin(math.pi * i / (count - 1)) ** 2 for i in range(count)]
    # 高域のノイズを軽く抑え、UIの純音とは異なる短い質感にする。
    filtered = 0.0
    values = []
    for i, env in enumerate(envelope):
        filtered = .65 * filtered + .35 * rng.uniform(-1, 1)
        values.append(env * (tone(i / 48000, i / (count - 1)) + noise * filtered))
    dc = sum(values) / sum(envelope)
    values = [v - dc * e for v, e in zip(values, envelope)]
    gain = .08 / max(map(abs, values))
    samples = [round(v * gain * 32767) for v in values]
    samples[0] = samples[-1] = 0
    pcm = array.array('h', samples)
    if sys.byteorder != 'little':
        pcm.byteswap()
    with wave.open(str(OUTPUT / (name + '.wav')), 'wb') as target:
        target.setparams((1, 2, 48000, count, 'NONE', 'not compressed'))
        target.writeframes(pcm.tobytes())


if __name__ == '__main__':
    OUTPUT.mkdir(parents=True, exist_ok=True)
    create('damage', .096, lambda t, u: .7 * math.sin(2 * math.pi * (210 * t - 420 * t * t)), .5)
    create('defense', .072, lambda t, u: .6 * math.sin(2 * math.pi * 1375 * t) + .3 * math.sin(2 * math.pi * 1925 * t), .08)
    create('healing', .128, lambda t, u: .65 * math.sin(2 * math.pi * 500 * t) + .35 * math.sin(2 * math.pi * 1000 * t), 0)
