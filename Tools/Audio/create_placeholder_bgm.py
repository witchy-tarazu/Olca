#!/usr/bin/env python3
"""外部音源や既存曲を使わない8秒の自作仮ループ2曲。"""
from pathlib import Path
import array, math, sys, wave
ROOT = Path(__file__).resolve().parents[2]
RATE = 48000
COUNT = RATE * 8

def render(name, notes, battle):
    values = [0.0] * COUNT
    def note(midi, start, duration, gain):
        frequency = 440 * 2 ** ((midi - 69) / 12)
        size = round(duration * RATE)
        for j in range(size):
            index = round(start * RATE) + j
            if index >= COUNT: break
            phase = 2 * math.pi * frequency * j / RATE
            envelope = math.sin(math.pi * j / (size - 1)) ** 2
            values[index] += gain * envelope * (math.sin(phase) + .12 * math.sin(2 * phase))
    for step, midi in enumerate(notes):
        note(midi, step * .5, .46 if battle else .9, .35)
    for beat in range(8):
        note(([45, 45, 48, 43] if battle else [45, 48, 41, 43])[beat // 2], beat, .95, .5)
    # 最後に余韻を閉じ、先頭へ波形を不連続に飛ばさない。
    edge = 480
    for j in range(edge):
        gain = math.sin(math.pi * j / (2 * (edge - 1))) ** 2
        values[j] *= gain
        values[-1-j] *= gain
    dc = sum(values) / sum(math.sin(math.pi*i/(COUNT-1))**2 for i in range(COUNT))
    values = [v - dc * math.sin(math.pi*i/(COUNT-1))**2 for i,v in enumerate(values)]
    scale = .035 / max(map(abs, values))
    samples = [round(v * scale * 32767) for v in values]
    samples[0] = samples[-1] = 0
    pcm = array.array('h', samples)
    if sys.byteorder != 'little': pcm.byteswap()
    output = ROOT / 'Assets/Audio/Bgm'
    output.mkdir(parents=True, exist_ok=True)
    target = output / (name + '.wav')
    with wave.open(str(target), 'wb') as w:
        w.setparams((1, 2, RATE, COUNT, 'NONE', 'not compressed'))
        w.writeframes(pcm.tobytes())
    resource = ROOT / 'Assets/Resources/OlcaBgm'
    resource.mkdir(parents=True, exist_ok=True)
    (resource / (name + '.bytes')).write_bytes(target.read_bytes())

if __name__ == '__main__':
    render('exploration', [69,72,76,72,67,72,74,72,65,69,72,69,67,71,74,71], False)
    render('battle', [57,64,69,64,60,67,72,67,55,62,67,62,59,64,71,64], True)
