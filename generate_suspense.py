import math
import os
import struct
import wave

rate = 22050
seconds = 8
output = "dist/PennywiseJumpscare/suspense.wav"
os.makedirs(os.path.dirname(output), exist_ok=True)

with wave.open(output, "wb") as out:
    out.setnchannels(1)
    out.setsampwidth(2)
    out.setframerate(rate)
    frames = bytearray()

    for n in range(rate * seconds):
        t = n / rate

        # Original ominous layered drone. All oscillators complete whole cycles
        # in eight seconds so the loop does not click at its boundary.
        slow_pulse = 0.88 + 0.12 * math.sin(2 * math.pi * 0.25 * t)
        low = (
            0.24 * math.sin(2 * math.pi * 55 * t)
            + 0.17 * math.sin(2 * math.pi * 73 * t)
            + 0.12 * math.sin(2 * math.pi * 110 * t)
            + 0.08 * math.sin(2 * math.pi * 146 * t)
        )
        eerie = (
            0.055 * math.sin(2 * math.pi * 220 * t)
            + 0.035 * math.sin(2 * math.pi * 330 * t)
            + 0.022 * math.sin(2 * math.pi * 440 * t)
        )
        rumble = 0.045 * math.sin(2 * math.pi * 27.5 * t + 0.3 * math.sin(2 * math.pi * 0.125 * t))

        sample = (low + eerie + rumble) * slow_pulse
        sample = max(-0.85, min(0.85, sample))
        frames += struct.pack("<h", int(sample * 32767))

    out.writeframes(frames)
