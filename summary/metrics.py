import os
import numpy as np
import matplotlib.pyplot as plt
from matplotlib.backends.backend_pdf import PdfPages

ALGORITHMS = ['algo1', 'algo2', 'algo3', 'algo4']
ROUND_MM = [0, 2, 5, 10, 20, 50]
ROOT = 'results'

METRICS = ('Avg Fill', 'Min Fill', 'Max Fill', 'Avg Time')
COL = {'Avg Fill': 4, 'Min Fill': 2, 'Max Fill': 3, 'Avg Time': 1}

R = np.full((len(ALGORITHMS), len(ROUND_MM), len(METRICS)), np.nan)

for ai, alg in enumerate(ALGORITHMS):
    for ri, mm in enumerate(ROUND_MM):
        path = os.path.join(ROOT, alg, f'{mm}mm', 'summary.csv')
        if not os.path.isfile(path):
            continue
        data = np.genfromtxt(path, delimiter=',', skip_header=1)
        for mi, m in enumerate(METRICS):
            R[ai, ri, mi] = data[COL[m]]

with PdfPages('metrics_report.pdf') as pdf:
    x = np.arange(len(ROUND_MM))
    for mi, metric in enumerate(METRICS):
        plt.figure()
        for ai, alg in enumerate(ALGORITHMS):
            y = R[ai, :, mi]
            if np.all(np.isnan(y)):
                continue
            plt.plot(x, y, marker='o', label=alg)
        plt.xticks(x, [f'{mm} mm' if mm else '0 mm' for mm in ROUND_MM])
        plt.ylabel(metric + (' (%)' if 'Fill' in metric else ' (s)'))
        plt.xlabel('Шаг округления')
        plt.title(metric)
        plt.grid(True, linestyle=':')
        plt.legend()
        pdf.savefig()
        plt.close()

print('PDF-отчёт готов: metrics_report.pdf')
