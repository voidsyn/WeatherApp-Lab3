using Core;
using DataAccess;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        using var context = new WeatherContext();

        context.Database.EnsureCreated();

        // Laddar in data från CSV-fil om databasen är tom
        if (!context.Readings.Any())
        {
            Console.WriteLine("Läser in data från CSV-fil...");

            // leta efter filen
            string path = "../../../TempFuktData.csv";
            if (!File.Exists(path))
            {
                path = "TempFuktData.csv";
            }
                
            if (!File.Exists(path))

            {
                Console.WriteLine($"CSV-fil saknas: {path}");
                return;
            }

            else
            {
                var allText = File.ReadAllText(path, Encoding.UTF8);
                // fixar felaktiga tecken  
                allText = allText.Replace("âˆ’", "-");

                // läser in data med regex
                var pattern = @"^(\d{4}-\d{2}-\d{2}\s+\d{1,2}:\d{2}),\s*([^,]+),\s*([-+]?\d+(?:[.,]\d+)?),\s*(\d+)\s*$";
                var rx = new Regex(pattern, RegexOptions.Multiline | RegexOptions.Compiled);

                var records = new List<WeatherReading>();

                foreach (Match m in rx.Matches(allText))
                {
                    // normalisera och parsa värden
                    var dateStr = m.Groups[1].Value.Trim();
                    var location = m.Groups[2].Value.Trim();
                    var tempStr = m.Groups[3].Value.Trim().Replace(',', '.');
                    var humidityStr = m.Groups[4].Value.Trim();

                    // Acceptera både H och HH i timmar
                    var formats = new[] { "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm" };
                    if (!DateTime.TryParseExact(dateStr, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
                        continue;

                    if (!double.TryParse(tempStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp))
                        continue;

                    if (!int.TryParse(humidityStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var humidity))
                        continue;

                    records.Add(new WeatherReading
                    {
                        Timestamp = timestamp,
                        Location = location,
                        Temperature = temp,
                        Humidity = humidity
                    });
                }

                if (records.Count > 0)
                {
                    context.Readings.AddRange(records);
                    context.SaveChanges();
                    Console.WriteLine($"Data inläst och sparad ({records.Count} rader).");
                }
                else
                {
                    Console.WriteLine("Ingen giltig data hittades i CSV-filen.");
                }
            }
        }
        else
        {
            Console.WriteLine("Data finns redan i databasen, hoppar över inläsning.");
        }

        Console.WriteLine("\nBeräknar statistik...");

        // Ladda alla läsningar
        var allReadings = context.Readings.ToList();

        static List<DailyStats> BuildDailyStats(IEnumerable<WeatherReading> readings)
        {
            return readings
                .GroupBy(r => r.Timestamp.Date)
                .Select(g =>
                {
                    var avgTemp = g.Average(x => x.Temperature);
                    var avgHum = g.Average(x => x.Humidity);
                    return new DailyStats
                    {
                        Date = g.Key,
                        AvgTemp = avgTemp,
                        AvgHumidity = avgHum,
                        MoldRisk = CalculateMoldRisk(avgTemp, avgHum)
                    };
                })
                .OrderBy(x => x.Date)
                .ToList();
        }

        var outdoorDaily = BuildDailyStats(allReadings.Where(r => string.Equals(r.Location, "Ute", StringComparison.OrdinalIgnoreCase)));
        var indoorDaily = BuildDailyStats(allReadings.Where(r => string.Equals(r.Location, "Inne", StringComparison.OrdinalIgnoreCase)));

        void PrintTop(string title, IEnumerable<string> lines)
        {
            Console.WriteLine($"\n{title}");
            foreach (var l in lines)
                Console.WriteLine(l);
        }

        // Utomhus
        PrintTop("=== UTOMHUS ===", Enumerable.Empty<string>());

        PrintTop(" Utomhus - medeltemperatur per dag",
            outdoorDaily.Take(10).Select(d => $"{d.Date:yyyy-MM-dd}: {d.AvgTemp:F1} °C"));

        PrintTop(" Varmaste dagar utomhus:",
            outdoorDaily.OrderByDescending(d => d.AvgTemp).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: {d.AvgTemp:F1} °C"));

        PrintTop(" Torraste dagar utomhus:",
            outdoorDaily.OrderBy(d => d.AvgHumidity).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: {d.AvgHumidity:F0}% RH"));

        PrintTop(" Fuktigaste dagar utomhus:",
            outdoorDaily.OrderByDescending(d => d.AvgHumidity).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: {d.AvgHumidity:F0}% RH"));

        PrintTop(" Minst risk för mögel (utomhus):",
            outdoorDaily.OrderBy(x => x.MoldRisk).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: Index {d.MoldRisk:F2}"));

        Console.WriteLine("\n=== ÅRSTIDER ===");
        // Meteorologisk höst/vinter
        var autumnDate = FindSeasonStart(outdoorDaily, 10.0);
        Console.WriteLine("\nMeteorologisk höst: " + (autumnDate.HasValue ? autumnDate.Value.ToString("yyyy-MM-dd") : "Ej inträffat"));

        var winterDate = FindSeasonStart(outdoorDaily, 0.0);
        Console.WriteLine("Meteorologisk vinter: " + (winterDate.HasValue ? winterDate.Value.ToString("yyyy-MM-dd") : "Ej inträffat"));

        // Inomhus
        PrintTop("=== INOMHUS ===", Enumerable.Empty<string>());

        PrintTop(" Varmaste dagar (inomhus):",
            indoorDaily.OrderByDescending(x => x.AvgTemp).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: {d.AvgTemp:F1} °C"));

        PrintTop(" Torraste dagar (inomhus):",
            indoorDaily.OrderBy(x => x.AvgHumidity).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: {d.AvgHumidity:F0}% RH"));

        PrintTop(" Störst risk för mögel (inomhus):",
            indoorDaily.OrderByDescending(x => x.MoldRisk).Take(5).Select(d => $"{d.Date:yyyy-MM-dd}: Index {d.MoldRisk:F2}"));

        // Inne/ute temperatur skillnad
        var diffList = outdoorDaily.Join(indoorDaily,
                o => o.Date,
                i => i.Date,
                (o, i) => new { Date = o.Date, Diff = Math.Abs(o.AvgTemp - i.AvgTemp) })
            .OrderByDescending(x => x.Diff)
            .Take(5)
            .ToList();

        PrintTop(" Störst temperatur skillnad inne/ute:",
            diffList.Select(d => $"{d.Date:yyyy-MM-dd}: {d.Diff:F1} °C skillnad"));

        Console.WriteLine("\n SÖK DATUM");
        while (true)
        {
            Console.Write("\nAnge datum (ÅÅÅÅ-MM-DD) eller 'q' för att avsluta: ");
            String? input = Console.ReadLine();
            if (input?.Trim().ToLower() == "q") break;

            if (DateTime.TryParse(input, out DateTime date))
            {

                var outData = outdoorDaily.FirstOrDefault(d => d.Date == date);
                var inData = indoorDaily.FirstOrDefault(d => d.Date == date);
                if (outData != null) Console.WriteLine($"\nUtomhus {outData.Date:yyyy-MM-dd} - Medeltemp: {outData.AvgTemp:F1} °C, Medelfukt: {outData.AvgHumidity:F0}% RH, Mögelrisk index: {outData.MoldRisk:F2}");
                else Console.WriteLine("[Ute] ingen data.");
                if (inData != null) Console.WriteLine($"Inomhus {inData.Date:yyyy-MM-dd} - Medeltemp: {inData.AvgTemp:F1} °C, Medelfukt: {inData.AvgHumidity:F0}% RH, Mögelrisk index: {inData.MoldRisk:F2}");
                else Console.WriteLine("[Inne] ingen data.");
            }
            else
            {
                Console.WriteLine("Ogiltigt datumformat. Försök igen.");
            }
        }
    
    }
    // Simpel mögelrisk kalkyl baserad på temperatur och fuktighet
    static double CalculateMoldRisk(double temp, double humidity)
    {
        if (humidity < 70 || temp < 0)
            return 0.0;

        // Normalisera värden till [0,1]
        var humNorm = Math.Clamp((humidity - 70) / 30.0, 0.0, 1.0);
        var tempNorm = Math.Clamp((temp - 5) / 20.0, 0.0, 1.0);

        return humNorm * tempNorm;
    }

    // Daglig statistik record
    record DailyStats
    {
        public DateTime Date { get; init; }
        public double AvgTemp { get; init; }
        public double AvgHumidity { get; init; }
        public double MoldRisk { get; init; }
    }

    // Hitta startdatum för meteorologisk säsong baserat på temperaturgränser
    static DateTime? FindSeasonStart(IEnumerable<DailyStats> daily, double maxTemp)
    {
        var sorted = daily.OrderBy(d => d.Date).ToList();
        int consecutive = 0;

        for (int i =0; i < sorted.Count; i++)
        {
            if (sorted[i].AvgTemp <= maxTemp)
            {
                consecutive++;
                if (consecutive >= 5)
                {
                    return sorted[i - 4].Date; // första dagen i serien
                }
            }
            else
            {
                consecutive = 0;
            }
        }
        return null;
    }
}
// Record för daglig statistik§
record DailyStats
{
    public DateTime Date { get; init; }
    public double AvgTemp { get; init; }
    public double AvgHumidity { get; init; }
    public double MoldRisk { get; init; }
}