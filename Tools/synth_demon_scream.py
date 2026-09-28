"""
Synthese du cri du demon (poupee) : Assets/_Game/Audio/Demon/Demon_Scream.wav

Cri aigu d'enfant deforme : trois voix legerement desaccordees en dents de scie
qui montent puis s'effondrent, formants de voix, souffle, sous-harmonique grave
(grognement) et saturation. Relancer ce script pour regenerer le son.
"""
import os
import wave

import numpy as np
from scipy import signal

SR = 44100
DURATION = 2.6
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "_Game", "Audio", "Demon", "Demon_Scream.wav")

rng = np.random.default_rng(666)
t = np.arange(int(SR * DURATION)) / SR


def envelope(times, values):
    return np.interp(t, times, values)


# Hauteur : monte vite, tient en tremblant, puis s'effondre en rale.
f0 = envelope([0.0, 0.12, 0.5, 1.5, 2.0, 2.6], [380.0, 760.0, 820.0, 700.0, 330.0, 180.0])
f0 *= 1.0 + 0.035 * np.sin(2 * np.pi * 7.5 * t) + 0.02 * np.sin(2 * np.pi * 13.0 * t + 1.3)
f0 *= 1.0 + 0.01 * signal.resample(rng.standard_normal(200), len(t))  # instabilite de la voix

voice = np.zeros_like(t)
for detune, gain in ((1.0, 1.0), (1.017, 0.7), (0.986, 0.7)):
    phase = 2 * np.pi * np.cumsum(f0 * detune) / SR
    voice += gain * signal.sawtooth(phase)

# Sous-harmonique : grognement sous le cri.
growl_phase = 2 * np.pi * np.cumsum(f0 * 0.5) / SR
voice += 0.45 * signal.sawtooth(growl_phase, width=0.3) * envelope([0, 0.3, 1.8, 2.6], [0.2, 0.6, 1.0, 1.0])

# Formants de voix (a / e ouvert) : filtres passe-bande en parallele.
def bandpass(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="bandpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)

formants = (bandpass(voice, 700, 1300) * 1.0 + bandpass(voice, 2200, 3200) * 0.7
            + bandpass(voice, 3400, 4600) * 0.35 + bandpass(voice, 120, 500) * 0.5)

# Souffle / dechirement de gorge.
noise = rng.standard_normal(len(t))
breath = bandpass(noise, 1500, 7000) * envelope([0, 0.1, 1.6, 2.6], [0.9, 0.35, 0.45, 0.8])

mix = formants + 0.35 * breath

# Saturation (voix qui se dechire) puis enveloppe globale.
mix = np.tanh(mix * 2.8)
amp = envelope([0.0, 0.06, 0.2, 1.7, 2.3, 2.6], [0.0, 1.0, 0.95, 0.85, 0.3, 0.0])
mix *= amp

# Un peu de reverberation (foret) : echos courts decroissants.
wet = np.zeros_like(mix)
for delay, gain in ((0.043, 0.35), (0.071, 0.28), (0.113, 0.2), (0.167, 0.14), (0.241, 0.09)):
    d = int(delay * SR)
    wet[d:] += mix[:-d] * gain
mix = mix + wet

mix /= np.max(np.abs(mix)) + 1e-9
mix *= 0.95

os.makedirs(os.path.dirname(OUT), exist_ok=True)
with wave.open(OUT, "wb") as w:
    w.setnchannels(1)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes((mix * 32767).astype(np.int16).tobytes())

print(os.path.abspath(OUT))
