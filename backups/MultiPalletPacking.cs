using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

class Rect { public int X, Y, W, H; public Rect(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; } }

class Box
{
    public string SKU = "";
    public int Quantity, Length, Width, Height, Weight;
    public int X, Y, Z; public bool Rotated; public int PalletId;
    public long Volume => (long)Length * Width * Height;
    public Box CloneSingle() => (Box)MemberwiseClone();
}

class Pallet
{
    public int Id; public int CurrentZ; public List<Box> Placed = new();
    public Pallet(int id) => Id = id;
    public string Address =>
        (Id % 3 == 0 && Id % 2 != 0) ? "A" : (Id % 2 == 0 && Id % 3 != 0) ? "B" : "C";
    public double FillRate => CurrentZ == 0 ? 0 :
        Placed.Sum(b => (double)b.Volume) / ((double)Program.PL * Program.PW * CurrentZ) * 100.0;
}

class Program
{
    public const int PL = 1200, PW = 1000;
    const string IN = "data", OUT = "results";
    static void Main(string[] a)
    {
        if (a.Length == 0 || !int.TryParse(a[0], out int cf) || cf <= 0)
        {
            Console.WriteLine("Usage: dotnet run <clarityFactor>"); return;
        }
        cf = Math.Min(cf, Math.Min(PL, PW));
        Directory.CreateDirectory(IN); Directory.CreateDirectory(OUT);

        var files = Directory.GetFiles(IN, "*.csv");
        if (files.Length == 0) { Console.WriteLine("Нет CSV-файлов в папке data"); return; }

        var groups = files.Select(f => new
        {
            Id = int.Parse(Path.GetFileNameWithoutExtension(f)),
            File = f
        })
                        .GroupBy(x => AddrOf(x.Id))
                        .ToDictionary(g => g.Key, g => g.ToList());

        double sFill = 0, min = double.MaxValue, max = 0, sTime = 0;
        var gSw = Stopwatch.StartNew();

        foreach (var (addr, fileList) in groups)
        {
            var sw = Stopwatch.StartNew();
            var boxes = fileList.SelectMany(f => Read(f.File, cf)
                                    .SelectMany(b => Enumerable.Range(0, b.Quantity)
                                                             .Select(_ => b.CloneSingle())))
                              .OrderByDescending(b => b.Volume)
                              .ToList();
            var pallets = fileList.Select(f => new Pallet(f.Id)).ToList();
            PackAcrossPallets(boxes, pallets);

            foreach (var p in pallets)
                WritePallet(Path.Combine(OUT, $"pallet{p.Id:D3}_{p.Address}.csv"),
                            p, sw.Elapsed.TotalSeconds);

            double avg = pallets.Average(p => p.FillRate);
            Console.WriteLine($"Address {addr}: {pallets.Count} pallets, avg fill {avg:F2}%");
            sFill += avg; min = Math.Min(min, avg); max = Math.Max(max, avg); sTime += sw.Elapsed.TotalSeconds;
        }

        gSw.Stop();
        File.WriteAllText("summary.csv",
            string.Format(CultureInfo.InvariantCulture,
                "TotalTime,AvgTime,MinFill,MaxFill,AvgFill\n{0:F3},{1:F3},{2:F2},{3:F2},{4:F2}",
                gSw.Elapsed.TotalSeconds, sTime / groups.Count, min, max, sFill / groups.Count));
        Console.WriteLine($"Summary AvgFill={sFill / groups.Count:F2}% (min {min:F2}, max {max:F2})");
    }

    record Layer(List<Box> Boxes, int H);
    static void PackAcrossPallets(List<Box> boxes, List<Pallet> pallets)
    {
        var layers = BuildLayers(boxes);
        foreach (var layer in layers)
        {
            var tgt = pallets.OrderBy(p => p.CurrentZ).First();
            foreach (var b in layer.Boxes) { b.Z = tgt.CurrentZ; b.PalletId = tgt.Id; tgt.Placed.Add(b); }
            tgt.CurrentZ += layer.H;
        }
    }

    static List<Layer> BuildLayers(List<Box> boxes)
    {
        var layers = new List<Layer>(); var rest = boxes.ToList();
        while (rest.Count > 0)
        {
            var first = rest[0]; int h = first.Height;
            var free = new List<Rect> { new Rect(0, 0, PL, PW) }; var cur = new List<Box>();
            Place(first, free, cur, 0, 0);
            rest.RemoveAt(0);

            for (int i = 0; i < rest.Count; i++)
            {
                var b = rest[i]; if (b.Height > h) continue;
                if (TryFit(b, free, out int x, out int y, out bool rot))
                {
                    Place(b, free, cur, x, y, rot); rest.RemoveAt(i--);
                }
            }
            layers.Add(new Layer(cur, h));
        }
        return layers;
    }

    static bool TryFit(Box b, List<Rect> free, out int x, out int y, out bool rot)
    {
        rot = false; x = y = 0;
        foreach (var v in new[] { (b.Length, b.Width, false), (b.Width, b.Length, true) })
        {
            int w = v.Item1, h = v.Item2;
            foreach (var r in free) if (w <= r.W && h <= r.H) { x = r.X; y = r.Y; rot = v.Item3; return true; }
        }
        return false;
    }

    static void Place(Box b, List<Rect> free, List<Box> cur, int x, int y, bool rot = false)
    {
        if (rot) { (b.Length, b.Width) = (b.Width, b.Length); b.Rotated = true; }
        b.X = x; b.Y = y; cur.Add(b);

        var newFree = new List<Rect>();
        foreach (var r in free)
        {
            if (!Intersects(b, r)) { newFree.Add(r); continue; }
            if (r.X < b.X) newFree.Add(new Rect(r.X, r.Y, b.X - r.X, r.H));
            int bx2 = b.X + b.Length;
            if (r.X + r.W > bx2) newFree.Add(new Rect(bx2, r.Y, r.X + r.W - bx2, r.H));
            if (r.Y < b.Y) newFree.Add(new Rect(Math.Max(r.X, b.X), r.Y,
                                             Math.Min(bx2, r.X + r.W) - Math.Max(r.X, b.X),
                                             b.Y - r.Y));
            int by2 = b.Y + b.Width;
            if (r.Y + r.H > by2) newFree.Add(new Rect(Math.Max(r.X, b.X), by2,
                                                 Math.Min(bx2, r.X + r.W) - Math.Max(r.X, b.X),
                                                 r.Y + r.H - by2));
        }
        newFree.RemoveAll(r => r.W == 0 || r.H == 0);
        for (int i = 0; i < newFree.Count; i++)
            for (int j = i + 1; j < newFree.Count; j++)
                if (Contains(newFree[i], newFree[j])) newFree.RemoveAt(j--);
                else if (Contains(newFree[j], newFree[i])) { newFree.RemoveAt(i--); break; }

        free.Clear(); free.AddRange(newFree);
    }

    static bool Intersects(Box b, Rect r) =>
        b.X < b.X + b.Length && r.X < r.X + r.W &&
        b.X + b.Length > r.X && r.X + r.W > b.X &&
        b.Y < b.Y + b.Width && r.Y < r.Y + r.H &&
        b.Y + b.Width > r.Y && r.Y + r.H > b.Y;

    static bool Contains(Rect a, Rect b) =>
        b.X >= a.X && b.Y >= a.Y && b.X + b.W <= a.X + a.W && b.Y + b.H <= a.Y + a.H;

    static string AddrOf(int id) => (id % 3 == 0 && id % 2 != 0) ? "A" : (id % 2 == 0 && id % 3 != 0) ? "B" : "C";

    static IEnumerable<Box> Read(string path, int cf)
    {
        foreach (var (line, i) in File.ReadLines(path).Select((l, idx) => (l, idx)))
        {
            if (i == 0 || string.IsNullOrWhiteSpace(line)) continue;
            var s = line.Split(','); if (s.Length < 6) continue;
            if (!int.TryParse(s[1], out int q)) continue;
            if (!double.TryParse(s[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var Ld)) continue;
            if (!double.TryParse(s[3], NumberStyles.Any, CultureInfo.InvariantCulture, out var Wd)) continue;
            if (!double.TryParse(s[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var Hd)) continue;
            if (!int.TryParse(s[5], out int w)) continue;

            int L = (int)Math.Ceiling(Ld / cf) * cf,
                W = (int)Math.Ceiling(Wd / cf) * cf,
                H = (int)Math.Ceiling(Hd / cf) * cf;
            if (L <= 0 || W <= 0 || H <= 0 || q <= 0) continue;

            yield return new Box { SKU = s[0], Quantity = q, Length = L, Width = W, Height = H, Weight = w };
        }
    }

    static void WritePallet(string path, Pallet p, double t)
    {
        using var w = new StreamWriter(path);
        var iv = CultureInfo.InvariantCulture;
        w.WriteLine($"PalletId,{p.Id}"); w.WriteLine($"Address,{p.Address}");
        w.WriteLine($"ExecutionTime,{t.ToString("F3", iv)} seconds\n");
        w.WriteLine("SKU,X,Y,Z,Length,Width,Height,Rotated");
        foreach (var b in p.Placed)
            w.WriteLine($"{b.SKU},{b.X},{b.Y},{b.Z},{b.Length},{b.Width},{b.Height},{b.Rotated}");
        w.WriteLine($"\nFillRate,{p.FillRate.ToString("F2", iv)}%");
    }
}

/* маленький helper для Add+return */
static class LExt { public static T AddRet<T>(this List<T> l, T x) { l.Add(x); return x; } }
