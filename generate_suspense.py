import math
import os
import struct
import wave

# Original horror ambience: bowed-string dissonance, a slow low drone,
# and a rising high note. The loop is seamless and contains no samples.
rate = 22050
seconds = 12
output = "dist/PennywiseJumpscare/suspense.wav"
os.makedirs(os.path.dirname(output), exist_ok=True)

with wave.open(output, "wb") as out:
    out.setnchannels(1)
    out.setsampwidth(2)
    out.setframerate(rate)
    frames = bytearray()
    for n in range(rate * seconds):
        t = n / rate
        pulse = 0.82 + 0.18 * math.sin(2 * math.pi * (1 / 6) * t)
        fade = min(1.0, t / 0.35, (seconds - t) / 0.45)
        drone = (0.20 * math.sin(2 * math.pi * 55.0 * t)
                 + 0.13 * math.sin(2 * math.pi * 82.4 * t + 0.3)
                 + 0.08 * math.sin(2 * math.pi * 110.0 * t))
        swell = 0.5 + 0.5 * math.sin(2 * math.pi * (1 / 12) * t - math.pi / 2)
        strings = (0.12 * math.sin(2 * math.pi * 220.0 * t + 0.18 * math.sin(2 * math.pi * 0.17 * t))
                   + 0.085 * math.sin(2 * math.pi * 233.1 * t)
                   + 0.055 * math.sin(2 * math.pi * 311.1 * t + 0.2)
                   + 0.035 * math.sin(2 * math.pi * 466.2 * t)) * (0.25 + 0.75 * swell)
        rise_hz = 660.0 + 90.0 * (0.5 + 0.5 * math.sin(2 * math.pi * t / seconds))
        high = 0.035 * math.sin(2 * math.pi * rise_hz * t)
        tremor = 0.025 * math.sin(2 * math.pi * 27.5 * t + 0.6 * math.sin(2 * math.pi * 0.25 * t))
        sample = (drone + strings + high + tremor) * pulse * max(0.0, fade)
        sample = max(-0.82, min(0.82, sample))
        frames += struct.pack("<h", int(sample * 32767))
    out.writeframes(frames)
