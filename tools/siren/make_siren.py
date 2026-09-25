"""
Makes the police siren the race mode plays (apps/new-modes/sr_race/siren.wav): a 1970s wail, synthesized here so
the game ships no recording it has no licence for.

A wail is a tone swept up and down between two pitches, a few seconds a cycle. The electronic sirens of the time
drove a horn speaker with a square wave, so the tone is odd harmonics rolling off, softly clipped as a horn driver
clips. The file loops: it holds whole wail cycles, and the pitch is nudged so the tone ends on a whole number of
waves, which leaves no click where the loop comes round.

    python tools/siren/make_siren.py

Standard library only.
"""

import math
import os
import struct
import wave

RATE = 22050
LOW_HZ = 620.0
HIGH_HZ = 1480.0
RISE_S = 1.6
FALL_S = 2.4
CYCLES = 2
HARMONICS = (1, 3, 5, 7, 9)
AMPLITUDE = 0.8

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'apps', 'new-modes', 'sr_race', 'siren.wav')


def pitch(t):
    """The wail's pitch at t seconds into a cycle: a motor spinning up quickly and coasting down"""
    t %= RISE_S + FALL_S
    if t < RISE_S:
        x = t / RISE_S
        shape = 1 - (1 - x) ** 2
    else:
        x = (t - RISE_S) / FALL_S
        shape = (1 - x) ** 1.5
    return LOW_HZ + (HIGH_HZ - LOW_HZ) * shape


def main():
    count = int(round((RISE_S + FALL_S) * CYCLES * RATE))
    freqs = [pitch(i / RATE) for i in range(count)]

    # Whole waves over the file, so the loop is seamless for every harmonic
    waves = sum(freqs) / RATE
    scale = round(waves) / waves

    norm = sum(1.0 / n for n in HARMONICS)
    frames = bytearray()
    phase = 0.0
    for f in freqs:
        sample = sum(math.sin(n * phase) / n for n in HARMONICS) / norm
        sample = math.tanh(2.2 * sample) / math.tanh(2.2)
        frames += struct.pack('<h', int(max(-1.0, min(1.0, sample * AMPLITUDE)) * 32767))
        phase = (phase + 2 * math.pi * f * scale / RATE) % (2 * math.pi)

    with wave.open(OUT, 'wb') as out:
        out.setnchannels(1)
        out.setsampwidth(2)
        out.setframerate(RATE)
        out.writeframes(bytes(frames))
    print(f'{os.path.normpath(OUT)}: {count / RATE:.1f} s, pitch scaled by {scale:.5f}')


if __name__ == '__main__':
    main()
