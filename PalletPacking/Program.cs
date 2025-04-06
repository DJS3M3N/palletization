using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

class Box
{
    public string SKU;
    public int Quantity;
    public int Length, Width, Height, Weight;
    public int X, Y, Z;
    public bool Rotated;

    public Box CloneWithRotation()
    {
        return new Box
        {
            SKU = this.SKU,
            Quantity = 1,
            Length = this.Width,
            Width = this.Length,
            Height = this.Height,
            Weight = this.Weight,
            Rotated = !this.Rotated
        };
    }

    public int Volume => Length * Width * Height;

    public Box CloneSingle()
    {
        return new Box
        {
            SKU = this.SKU,
            Quantity = 1,
            Length = this.Length,
            Width = this.Width,
            Height = this.Height,
            Weight = this.Weight,
            Rotated = this.Rotated
        };
    }
}

class Program
{
    const int PalletLength = 1200;
    const int PalletWidth = 1000;

    static void Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: dotnet run -- <input.csv> <output.csv> <clarityFactor>");
            return;
        }

        string inputCsv = args[0];
        string outputCsv = args[1];
        if (!int.TryParse(args[2], out int clarityFactor) || clarityFactor < 1)
        {
            Console.WriteLine("clarityFactor must be a positive integer.");
            return;
        }

        Pack(inputCsv, outputCsv, clarityFactor);
    }

    static void Pack(string inputCsv, string outputCsv, int clarityFactor)
    {
        var stopwatch = Stopwatch.StartNew();
        var boxes = ReadBoxes(inputCsv, clarityFactor);
        var placedBoxes = new List<Box>();

        var boxesToPlace = new List<Box>();
        foreach (var box in boxes)
        {
            for (int i = 0; i < box.Quantity; i++)
            {
                boxesToPlace.Add(box.CloneSingle());
            }
        }

        boxesToPlace = boxesToPlace
            .OrderByDescending(b => b.Height)
            .ThenByDescending(b => b.Volume)
            .ToList();

        var availablePlatforms = new List<(int x, int y, int z, int availLength, int availWidth)>
        {
            (0, 0, 0, PalletLength, PalletWidth)
        };

        for (int j = 0; j < boxesToPlace.Count; j++)
        {
            var box = boxesToPlace[j];
            bool placed = false;

            var variants = new[] { box, box.CloneWithRotation() };

            for (int i = 0; i < availablePlatforms.Count && !placed; i++)
            {
                var platform = availablePlatforms[i];

                foreach (var variant in variants)
                {
                    if (variant.Length <= platform.availLength &&
                        variant.Width <= platform.availWidth)
                    {
                        variant.X = platform.x;
                        variant.Y = platform.y;
                        variant.Z = platform.z;
                        placedBoxes.Add(variant);
                        placed = true;

                        availablePlatforms.RemoveAt(i);

                        if (platform.availLength - variant.Length > 0)
                        {
                            availablePlatforms.Add((
                                platform.x + variant.Length,
                                platform.y,
                                platform.z,
                                platform.availLength - variant.Length,
                                variant.Width));
                        }

                        if (platform.availWidth - variant.Width > 0)
                        {
                            availablePlatforms.Add((
                                platform.x,
                                platform.y + variant.Width,
                                platform.z,
                                platform.availLength,
                                platform.availWidth - variant.Width));
                        }

                        availablePlatforms.Add((
                            platform.x,
                            platform.y,
                            platform.z + variant.Height,
                            variant.Length,
                            variant.Width));

                        availablePlatforms = availablePlatforms
                            .OrderBy(p => p.z)
                            .ThenBy(p => p.y)
                            .ThenBy(p => p.x)
                            .ToList();

                        break;
                    }
                }
            }

            if (!placed)
            {
                // Если не удалось разместить коробку, пробуем создать новую платформу сверху
                int z = placedBoxes.Max(b => b.Z + b.Height);
                availablePlatforms.Add((0, 0, z, PalletLength, PalletWidth));

                // Повторяем попытку размещения
                j--;
                continue;
            }
        }

        stopwatch.Stop();
        double usedVolume = placedBoxes.Sum(b => b.Volume);
        int maxZ = placedBoxes.Max(b => b.Z + b.Height);
        double palletVolume = PalletLength * PalletWidth * maxZ;
        double fillRate = usedVolume / palletVolume * 100;
        Console.WriteLine($"UsedVolume: {usedVolume} mm³");
        Console.WriteLine($"PalletVolume: {palletVolume} mm³");
        Console.WriteLine($"FillRate: {fillRate:F2}%");
        Console.WriteLine($"Total boxes placed: {placedBoxes.Count}");
        WriteOutput(outputCsv, placedBoxes, stopwatch.Elapsed.TotalSeconds, fillRate);
    }

    static List<Box> ReadBoxes(string path, int clarity)
    {
        var boxes = new List<Box>();
        var lines = File.ReadAllLines(path);

        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');

            if (parts[0] == "SKU" || parts.Length < 6) continue;

            var box = new Box
            {
                SKU = parts[0],
                Quantity = int.Parse(parts[1]),
                Length = RoundToFactor(double.Parse(parts[2], CultureInfo.InvariantCulture), clarity),
                Width = RoundToFactor(double.Parse(parts[3], CultureInfo.InvariantCulture), clarity),
                Height = RoundToFactor(double.Parse(parts[4], CultureInfo.InvariantCulture), clarity),
                Weight = int.Parse(parts[5])
            };

            boxes.Add(box);
        }

        return boxes;
    }

    static int RoundToFactor(double value, int factor)
    {
        return (int)(Math.Ceiling(value / factor) * factor);
    }

    static void WriteOutput(string path, List<Box> boxes, double time, double fillRate)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("SKU,X,Y,Z,Length,Width,Height,Rotated");

        foreach (var box in boxes)
        {
            writer.WriteLine($"{box.SKU},{box.X},{box.Y},{box.Z},{box.Length},{box.Width},{box.Height},{box.Rotated}");
        }

        writer.WriteLine();
        writer.WriteLine($"ExecutionTime,{time:F3} seconds");
        writer.WriteLine($"FillRate,{fillRate:F2}%");
    }
}