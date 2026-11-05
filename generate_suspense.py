import math
import struct
import wave

rate = 22050
seconds = 8
with wave.open("dist/PennywiseJumpscare/suspense.wav", "wb") as out:
    out.setnchannels(1)
    out.setsampwidth(2)
    out.setframerate(rate)
    frames = bytearray()
    for n in range(rate * seconds):
        t = n / rate
        pulse = 0.72 + 0.28 * math.sin(2 * math.pi * t / seconds) ** 2
        sample = pulse * (
            0.20 * math.sin(2 * math.pi * 55 * t)
            + 0.12 * math.sin(2 * math.pi * 73 * t)
            + 0.07 * math.sin(2 * math.pi * 110 * t)
            + 0.035 * math.sin(2 * math.pi * 146 * t)
        )
        frames += struct.pack("<h", int(max(-1, min(1, sample)) * 32767))
    out.writeframes(frames)
