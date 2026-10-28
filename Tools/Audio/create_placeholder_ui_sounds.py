#!/usr/bin/env python3
"""外部素材なしの仮UI音。48kHz/16bit/mono、低ピーク、両端ゼロ。"""
from pathlib import Path
import array
import math
import sys
import wave

ROOT = Path(__file__).resolve().parents[2]
OUTPUT = ROOT / 'Assets/Resources/OlcaUiSounds'


def synthesize(name, seconds, frequencies):
    count = round(48000 * seconds)
    envelope = [math.sin(math.pi * i / (count - 1)) ** 2 for i in range(count)]
    values = [envelope[i] * sum(weight * math.sin(2 * math.pi * frequency * i / 48000)
              for frequency, weight in frequencies) for i in range(count)]
    # DC成分を窓内で除去し、端点のゼロを維持する。
    dc = sum(values) / sum(envelope)
    values = [value - dc * env for value, env in zip(values, envelope)]
    gain = .1 / max(abs(value) for value in values)
    samples = [round(value * gain * 32767) for value in values]
    samples[0] = samples[-1] = 0
    pcm = array.array('h', samples)
    if sys.byteorder != 'little':
        pcm.byteswap()
    with wave.open(str(OUTPUT / (name + '.wav')), 'wb') as target:
        target.setparams((1, 2, 48000, count, 'NONE', 'not compressed'))
        target.writeframes(pcm.tobytes())


if __name__ == '__main__':
    OUTPUT.mkdir(parents=True, exist_ok=True)
    synthesize('press', .080, [(750, .7), (1125, .3)])
    synthesize('cancel', .096, [(625, .65), (416.6666666667, .35)])
