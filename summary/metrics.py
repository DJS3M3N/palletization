import os
import pandas as pd
import matplotlib.pyplot as plt
import matplotlib as mpl
import numpy as np

rounding_levels = ["0", "2 мм", "5 мм", "1 см", "2 см", "5 см"]
data = {
    "alg":  ["Greedy"]*6 + ["Platforms"]*6 + ["Prototype"]*6 +
            ["Layer-new"]*6 + ["Multipallet"]*6,
    "round": rounding_levels*5,
    "time":  [0.821, 0.774, 0.764, 0.877, 0.965, 0.827,
              3.451, 0.646, 0.704, 0.693, 0.628, 0.730,
              1.157, 0.913, 0.968, 0.969, 1.181, 1.040,
              3.075, 3.615, 4.532, 6.116, 7.000, 4.031,
              8.840, 8.731, 8.131, 7.583, 8.002, 7.298],
    "fill": [44.40, 44.58, 45.01, 46.05, 48.07, 53.79,
             41.29, 41.44, 41.67, 42.39, 44.08, 46.82,
             53.21, 53.44, 53.81, 54.25, 54.61, 57.32,
             61.51, 61.49, 61.96, 62.34, 64.88, 72.34,
             82.69, 83.20, 84.86, 85.85, 86.84, 88.64]
}
df = pd.DataFrame(data)

mpl.rcParams.update({
    "axes.edgecolor": "black", "axes.linewidth": 2,
    "grid.color": "grey",   "grid.linewidth": 1,
    "xtick.major.width": 2, "ytick.major.width": 2,
})
os.makedirs("pdf", exist_ok=True)

x_mm = np.array([0, 2, 5, 10, 20, 50])

g = df.groupby("round").agg(
    {"fill": "mean", "time": "mean"}).loc[rounding_levels]

fig1, ax1 = plt.subplots(figsize=(8, 3))
ax1.plot(x_mm, g["fill"], marker="o")
ax1.set_xlim(0, x_mm[-1])
ax1.set_xticks(x_mm)
ax1.set_xticklabels(rounding_levels, fontsize=9)
ax1.set_ylim(0)
ax1.grid(True, axis="y")

ax2 = ax1.twinx()
ax2.plot(x_mm, g["time"], marker="s", linestyle="--")
ax2.set_ylim(0)
for s in ax2.spines.values():
    s.set_edgecolor("black")
    s.set_linewidth(2)

fig1.tight_layout(pad=0.3)
fig1.savefig("pdf/round_vs_metrics.pdf")

algs_order = ["Greedy", "Platforms", "Prototype", "Layer-new", "Multipallet"]
d_1 = df[df["round"] == "1 см"].set_index("alg").loc[algs_order]

fig2, ax3 = plt.subplots(figsize=(6, 3))
ax3.bar(d_1.index, d_1["time"])
ax3.set_ylim(0)
ax3.grid(True, axis="y")
fig2.tight_layout(pad=0.2)
fig2.savefig("pdf/time_alg_1cm.pdf")

fig3, ax4 = plt.subplots(figsize=(6, 3))
ax4.bar(d_1.index, d_1["fill"])
ax4.set_ylim(0)
ax4.grid(True, axis="y")
fig3.tight_layout(pad=0.2)
fig3.savefig("pdf/fill_alg_1cm.pdf")

minv = [35.48, 30.83, 24.17, 42.49, 82.69]
maxv = [66.88, 67.19, 67.98, 76.26, 88.64]
avg = d_1["fill"].values

err_low = avg - np.array(minv)
err_high = np.array(maxv) - avg

fig4, ax5 = plt.subplots(figsize=(6, 3))
ax5.bar(algs_order, avg, yerr=[err_low, err_high], capsize=4)
ax5.set_ylim(0)
ax5.grid(True, axis="y")
fig4.tight_layout(pad=0.2)
fig4.savefig("pdf/fill_dispersion_1cm.pdf")
