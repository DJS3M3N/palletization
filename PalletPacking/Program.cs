using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

class Box
{
    public string SKU { get; set; } = string.Empty;
    public int Quantity;
    public int Length, Width, Height;
    public int Weight;

    public int X, Y, Z;
    public bool Rotated;

    public long Volume => (long)Length * Width * Height;

    public Box CloneWithRotation()
    {
        return new Box
        {
            SKU = SKU,
            Quantity = 1,
            Length = Width,
            Width = Length,
            Height = Height,
            Weight = Weight,
            Rotated = !Rotated
        };
    }

    public Box CloneSingle()
    {
        return new Box
        {
            SKU = SKU,
            Quantity = 1,
            Length = Length,
            Width = Width,
            Height = Height,
            Weight = Weight,
            Rotated = Rotated
        };
    }
}

class Program
{
    const int PalletLength = 1200;
    const int PalletWidth = 1000;

    const string InputFolder = "data";
    const string OutputFolder = "results";

    static void Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: dotnet run <clarityFactor>");
            return;
        }
        if (!int.TryParse(args[0], out int clarityFactor) || clarityFactor < 1)
        {
            Console.WriteLine("clarityFactor must be positive.");
            return;
        }

        Directory.CreateDirectory(InputFolder);
        Directory.CreateDirectory(OutputFolder);

        var csvFiles = Directory.GetFiles(InputFolder, "*.csv");
        if (csvFiles.Length == 0)
        {
            Console.WriteLine("No .csv files found in 'data' folder.");
            return;
        }

        var globalSw = Stopwatch.StartNew();
        double totalTime = 0.0;
        double sumFillRates = 0.0;
        double minFill = double.MaxValue;
        double maxFill = double.MinValue;
        int countPallets = 0;

        foreach (var csvPath in csvFiles)
        {
            var name = Path.GetFileNameWithoutExtension(csvPath);
            var outCsv = Path.Combine(OutputFolder, name + "_result.csv");

            var (fillRate, timeSec) = Pack(csvPath, outCsv, clarityFactor);

            totalTime += timeSec;
            sumFillRates += fillRate;
            if (fillRate < minFill) minFill = fillRate;
            if (fillRate > maxFill) maxFill = fillRate;

            countPallets++;
            Console.WriteLine(
                $"File {name}.csv processed. " +
                $"FillRate={fillRate.ToString("F2", CultureInfo.InvariantCulture)}%, " +
                $"Time={timeSec.ToString("F3", CultureInfo.InvariantCulture)} sec."
            );
        }

        globalSw.Stop();
        double overallTime = globalSw.Elapsed.TotalSeconds;
        double avgTime = totalTime / countPallets;
        double avgFill = sumFillRates / countPallets;

        using (var writer = new StreamWriter("summary.csv"))
        {
            writer.WriteLine("Total processing time (sec),Average time (sec),Min fillRate (%),Max fillRate (%),Average fillRate (%)");
            writer.WriteLine(
                $"{overallTime.ToString("F3", CultureInfo.InvariantCulture)}," +
                $"{avgTime.ToString("F3", CultureInfo.InvariantCulture)}," +
                $"{minFill.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"{maxFill.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"{avgFill.ToString("F2", CultureInfo.InvariantCulture)}"
            );
        }

        Console.WriteLine("----------");
        Console.WriteLine("Summary written to summary.csv");
        Console.WriteLine($"Pallets processed: {countPallets}");
        Console.WriteLine($"Average fill rate: {avgFill.ToString("F2", CultureInfo.InvariantCulture)}%");
    }

    static (double fillRate, double time) Pack(string inputCsv, string outCsv, int clarityFactor)
    {
        var sw = Stopwatch.StartNew();

        var inputBoxes = ReadBoxes(inputCsv, clarityFactor);

        var boxes = inputBoxes
            .Where(b => b.Length <= PalletLength && b.Width <= PalletWidth)
            .ToList();

        var expanded = new List<Box>();
        foreach (var b in boxes)
        {
            for (int i = 0; i < b.Quantity; i++)
                expanded.Add(b.CloneSingle());
        }

        expanded = expanded
            .OrderByDescending(b => b.Volume)
            .ThenByDescending(b => b.Weight)
            .ToList();

        var placedBoxes = new List<Box>();
        int currentZ = 0;
        int layerIndex = 0;

        var unplaced = new List<Box>(expanded);

        while (unplaced.Count > 0)
        {
            layerIndex++;
            var anchor = unplaced[0];
            unplaced.RemoveAt(0);

            int offsetXY = layerIndex * 10;

            anchor.X = offsetXY;
            anchor.Y = offsetXY;
            anchor.Z = currentZ;
            placedBoxes.Add(anchor);

            var freeRects = new List<(int x, int y, int w, int h)>
            {
                (
                    anchor.X + anchor.Length,
                    offsetXY,
                    PalletLength - anchor.Length - offsetXY,
                    anchor.Width
                ),
                (
                    offsetXY,
                    anchor.Y + anchor.Width,
                    PalletLength - offsetXY,
                    PalletWidth - (anchor.Y + anchor.Width)
                )
            };

            PlaceBoxesInLayer(unplaced, placedBoxes, freeRects, currentZ);

            int layerHeight = placedBoxes
                .Where(bx => bx.Z == currentZ)
                .Select(bx => bx.Height)
                .DefaultIfEmpty(0)
                .Max();

            if (layerHeight == 0)
            {
                layerHeight = anchor.Height;
            }

            currentZ += layerHeight;
        }

        sw.Stop();

        long usedVolume = placedBoxes.Sum(b => b.Volume);
        long maxZ = placedBoxes.Count > 0
            ? placedBoxes.Max(b => (long)b.Z + b.Height)
            : 0;

        long palletVolume = PalletLength * (long)PalletWidth * maxZ;
        double fillRate = 0.0;
        if (palletVolume > 0)
        {
            fillRate = usedVolume / (double)palletVolume * 100.0;
        }

        WriteOutput(outCsv, placedBoxes, sw.Elapsed.TotalSeconds, fillRate);

        return (fillRate, sw.Elapsed.TotalSeconds);
    }

    static void PlaceBoxesInLayer(
        List<Box> unplaced,
        List<Box> placedBoxes,
        List<(int x, int y, int w, int h)> freeRects,
        int currentZ)
    {
        bool boxPlacedInThisIteration;
        do
        {
            boxPlacedInThisIteration = false;

            for (int i = 0; i < unplaced.Count; i++)
            {
                var candidate = unplaced[i];

                var variants = new[]
                {
                    (L: candidate.Length, W: candidate.Width, R: candidate.Rotated),
                    (L: candidate.Width,  W: candidate.Length, R: !candidate.Rotated)
                };

                bool placedOk = false;

                foreach (var v in variants)
                {
                    for (int frIdx = 0; frIdx < freeRects.Count; frIdx++)
                    {
                        var fr = freeRects[frIdx];

                        if (v.L <= fr.w && v.W <= fr.h)
                        {
                            candidate.X = fr.x;
                            candidate.Y = fr.y;
                            candidate.Z = currentZ;
                            candidate.Length = v.L;
                            candidate.Width = v.W;
                            candidate.Rotated = v.R;

                            placedBoxes.Add(candidate);
                            unplaced.RemoveAt(i);

                            int leftoverRightW = fr.w - v.L;
                            int leftoverTopH = fr.h - v.W;

                            if (leftoverRightW > 0)
                            {
                                freeRects.Add((
                                    fr.x + v.L,
                                    fr.y,
                                    leftoverRightW,
                                    fr.h
                                ));
                            }

                            if (leftoverTopH > 0)
                            {
                                freeRects.Add((
                                    fr.x,
                                    fr.y + v.W,
                                    v.L,
                                    leftoverTopH
                                ));
                            }

                            freeRects.RemoveAt(frIdx);

                            placedOk = true;
                            boxPlacedInThisIteration = true;
                            break;
                        }
                    }

                    if (placedOk)
                        break;
                }

                if (placedOk)
                {
                    i--;
                }
            }

        } while (boxPlacedInThisIteration);
    }

    static List<Box> ReadBoxes(string path, int clarityFactor)
    {
        var result = new List<Box>();
        if (!File.Exists(path)) return result;

        var lines = File.ReadAllLines(path);
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 6)
                continue;

            var sku = parts[0];
            if (!int.TryParse(parts[1], out int qty)) continue;
            if (!double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double length)) continue;
            if (!double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out double width)) continue;
            if (!double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out double height)) continue;
            if (!int.TryParse(parts[5], out int weight)) continue;

            int L = RoundToFactor(length, clarityFactor);
            int W = RoundToFactor(width, clarityFactor);
            int H = RoundToFactor(height, clarityFactor);

            if (qty <= 0 || L <= 0 || W <= 0 || H <= 0)
                continue;

            result.Add(new Box
            {
                SKU = sku,
                Quantity = qty,
                Length = L,
                Width = W,
                Height = H,
                Weight = weight
            });
        }
        return result;
    }

    static int RoundToFactor(double value, int factor)
    {
        return (int)(Math.Ceiling(value / factor) * factor);
    }

    static void WriteOutput(string path, List<Box> placed, double timeSec, double fillRate)
    {
        using var wr = new StreamWriter(path);
        wr.WriteLine("SKU,X,Y,Z,Length,Width,Height,Rotated");

        foreach (var b in placed)
        {
            wr.WriteLine(
                $"{b.SKU}," +
                $"{b.X}," +
                $"{b.Y}," +
                $"{b.Z}," +
                $"{b.Length}," +
                $"{b.Width}," +
                $"{b.Height}," +
                $"{b.Rotated}"
            );
        }

        wr.WriteLine();
        wr.WriteLine($"ExecutionTime,{timeSec.ToString("F3", CultureInfo.InvariantCulture)} seconds");
        wr.WriteLine($"FillRate,{fillRate.ToString("F2", CultureInfo.InvariantCulture)}%");
    }
}
