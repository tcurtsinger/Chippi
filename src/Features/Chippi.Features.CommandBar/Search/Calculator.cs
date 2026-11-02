// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) Chippi contributors

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Chippi.Features.CommandBar.Search;

/// <summary>A sum or a conversion: the answer as the row's name, what was asked as its detail, and what Copy copies.</summary>
public sealed record Answer(string Name, string Detail, string Copy);

/// <summary>
/// Math and conversions (docs/phase-5.5/command-bar.md, What it finds): "12*7" → 84, "5 km in mi" → 3.107 mi,
/// "72 f in c" → 22.222 °C. Sums take + − × ÷ (and * / x), ^, a trailing %, and brackets; a lone number isn't a sum.
/// </summary>
public static partial class Calculator
{
    private enum Measure { Length, Mass, Volume, Temperature, Time, Speed, Data }

    private sealed record Unit(string Symbol, Measure Measure, double Factor);

    // Each unit's factor to its measure's base (metres, grams, litres, seconds, metres a second, bytes); temperature apart.
    private static readonly Dictionary<string, Unit> Units = Build(
        (new("mm", Measure.Length, 0.001), "mm millimeter millimeters millimetre millimetres"),
        (new("cm", Measure.Length, 0.01), "cm centimeter centimeters centimetre centimetres"),
        (new("m", Measure.Length, 1), "m meter meters metre metres"),
        (new("km", Measure.Length, 1000), "km kilometer kilometers kilometre kilometres"),
        (new("in", Measure.Length, 0.0254), "in inch inches \""),
        (new("ft", Measure.Length, 0.3048), "ft foot feet '"),
        (new("yd", Measure.Length, 0.9144), "yd yard yards"),
        (new("mi", Measure.Length, 1609.344), "mi mile miles"),
        (new("mg", Measure.Mass, 0.001), "mg milligram milligrams"),
        (new("g", Measure.Mass, 1), "g gram grams"),
        (new("kg", Measure.Mass, 1000), "kg kilo kilos kilogram kilograms"),
        (new("oz", Measure.Mass, 28.349523125), "oz ounce ounces"),
        (new("lb", Measure.Mass, 453.59237), "lb lbs pound pounds"),
        (new("st", Measure.Mass, 6350.29318), "st stone stones"),
        (new("ml", Measure.Volume, 0.001), "ml milliliter milliliters millilitre millilitres"),
        (new("l", Measure.Volume, 1), "l liter liters litre litres"),
        (new("tsp", Measure.Volume, 0.00492892159375), "tsp teaspoon teaspoons"),
        (new("tbsp", Measure.Volume, 0.01478676478125), "tbsp tablespoon tablespoons"),
        (new("fl oz", Measure.Volume, 0.0295735295625), "floz fl oz"),
        (new("cup", Measure.Volume, 0.2365882365), "cup cups"),
        (new("pt", Measure.Volume, 0.473176473), "pt pint pints"),
        (new("qt", Measure.Volume, 0.946352946), "qt quart quarts"),
        (new("gal", Measure.Volume, 3.785411784), "gal gallon gallons"),
        (new("°C", Measure.Temperature, 0), "c °c celsius centigrade"),
        (new("°F", Measure.Temperature, 0), "f °f fahrenheit"),
        (new("K", Measure.Temperature, 0), "k kelvin"),
        (new("ms", Measure.Time, 0.001), "ms millisecond milliseconds"),
        (new("s", Measure.Time, 1), "s sec secs second seconds"),
        (new("min", Measure.Time, 60), "min mins minute minutes"),
        (new("hr", Measure.Time, 3600), "h hr hrs hour hours"),
        (new("days", Measure.Time, 86400), "d day days"),
        (new("weeks", Measure.Time, 604800), "wk wks week weeks"),
        (new("m/s", Measure.Speed, 1), "m/s mps"),
        (new("km/h", Measure.Speed, 1000 / 3600.0), "km/h kmh kph"),
        (new("mph", Measure.Speed, 0.44704), "mph"),
        (new("kn", Measure.Speed, 0.514444), "kn knot knots"),
        (new("B", Measure.Data, 1), "b byte bytes"),
        (new("KB", Measure.Data, 1e3), "kb kilobyte kilobytes"),
        (new("MB", Measure.Data, 1e6), "mb megabyte megabytes"),
        (new("GB", Measure.Data, 1e9), "gb gigabyte gigabytes"),
        (new("TB", Measure.Data, 1e12), "tb terabyte terabytes"),
        (new("KiB", Measure.Data, 1024), "kib kibibyte kibibytes"),
        (new("MiB", Measure.Data, 1 << 20), "mib mebibyte mebibytes"),
        (new("GiB", Measure.Data, 1 << 30), "gib gibibyte gibibytes"),
        (new("TiB", Measure.Data, 1L << 40), "tib tebibyte tebibytes"));

    private static Dictionary<string, Unit> Build(params (Unit Unit, string Names)[] units)
    {
        var all = new Dictionary<string, Unit>(StringComparer.OrdinalIgnoreCase);
        foreach (var (unit, names) in units)
        {
            // "fl oz" is two words; every other name is one.
            foreach (var name in unit.Symbol == "fl oz" ? ["fl oz", "floz", "fluid ounce", "fluid ounces"] : names.Split(' '))
                all[name] = unit;
        }
        return all;
    }

    [GeneratedRegex(@"^\s*(-?\d+(?:\.\d+)?)\s*(.+?)\s+(?:in|to|as|into|=)\s+(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Conversion();

    // A sum has an operator between two numbers ("12*7", "(3+4)/2"); "12" or "-5" alone isn't one.
    [GeneratedRegex(@"\d\s*[%)]?\s*[-+*/x×÷^]\s*[-(]*\s*\.?\d", RegexOptions.CultureInvariant)]
    private static partial Regex Operator();

    [GeneratedRegex(@"^[\d\s.,+\-*/x×÷^()%]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SumOnly();

    /// <summary>The answer to <paramref name="text"/>, or null when it's neither a sum nor a conversion.</summary>
    public static Answer? Solve(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (Conversion().Match(text) is { Success: true } m && Convert(m) is { } converted)
            return converted;
        if (!SumOnly().IsMatch(text) || !Operator().IsMatch(text))
            return null;
        try
        {
            var sum = new Sum(text.Replace(",", ""));
            double value = sum.Parse();
            return double.IsFinite(value) ? new Answer(Format(value), sum.Shown, Plain(value)) : null;
        }
        catch (FormatException) { return null; }
    }

    private static Answer? Convert(Match m)
    {
        if (!double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !Units.TryGetValue(m.Groups[2].Value.Trim(), out var from) || !Units.TryGetValue(m.Groups[3].Value.Trim(), out var to)
            || from.Measure != to.Measure || from == to)
            return null;
        double result = from.Measure == Measure.Temperature ? Temperature(value, from.Symbol, to.Symbol) : value * from.Factor / to.Factor;
        return new Answer($"{Format(result)} {to.Symbol}", $"{Format(value)} {from.Symbol}", Plain(result));
    }

    private static double Temperature(double value, string from, string to)
    {
        double celsius = from switch { "°F" => (value - 32) * 5 / 9, "K" => value - 273.15, _ => value };
        return to switch { "°F" => celsius * 9 / 5 + 32, "K" => celsius + 273.15, _ => celsius };
    }

    /// <summary>"84", "3.107", "1,234.57": whole numbers and big ones grouped with two decimals at most, small ones three.</summary>
    internal static string Format(double value)
    {
        if (value == 0)
            return "0"; // never "-0"
        return Math.Abs(value) >= 1000 || value == Math.Round(value)
            ? value.ToString("#,##0.##", CultureInfo.CurrentCulture)
            : value.ToString("0.###", CultureInfo.CurrentCulture);
    }

    /// <summary>What Copy copies: the number as shown, without the grouping.</summary>
    private static string Plain(double value) => Format(value).Replace(CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator, "");

    /// <summary>A recursive-descent sum: brackets, ^ (right to left), a unary minus, × and ÷, then + and −.</summary>
    private sealed class Sum(string text)
    {
        private int _at;
        private readonly StringBuilder _shown = new();

        /// <summary>The sum as the detail shows it: "12 × 7".</summary>
        public string Shown => _shown.ToString().Trim();

        public double Parse()
        {
            double value = Add();
            Skip();
            if (_at != text.Length)
                throw new FormatException();
            return value;
        }

        private double Add()
        {
            double value = Multiply();
            while (Next() is '+' or '-')
            {
                char op = text[_at++];
                _shown.Append(op == '+' ? " + " : " − ");
                double right = Multiply();
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }

        private double Multiply()
        {
            double value = Power();
            while (Next() is '*' or '/' or 'x' or 'X' or '×' or '÷')
            {
                char op = text[_at++];
                bool divide = op is '/' or '÷';
                _shown.Append(divide ? " ÷ " : " × ");
                double right = Power();
                value = divide ? value / right : value * right;
            }
            return value;
        }

        private double Power()
        {
            double value = Unary();
            if (Next() == '^')
            {
                _at++;
                _shown.Append('^');
                return Math.Pow(value, Power());
            }
            return value;
        }

        private double Unary()
        {
            if (Next() == '-')
            {
                _at++;
                _shown.Append('−');
                return -Unary();
            }
            double value = Atom();
            if (Next() == '%')
            {
                _at++;
                _shown.Append('%');
                value /= 100;
            }
            return value;
        }

        private double Atom()
        {
            if (Next() == '(')
            {
                _at++;
                _shown.Append('(');
                double inner = Add();
                if (Next() != ')')
                    throw new FormatException();
                _at++;
                _shown.Append(')');
                return inner;
            }
            Skip();
            int start = _at;
            while (_at < text.Length && (char.IsAsciiDigit(text[_at]) || text[_at] == '.'))
                _at++;
            string number = text[start.._at];
            if (!double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double value))
                throw new FormatException();
            _shown.Append(Format(value));
            return value;
        }

        private char? Next()
        {
            Skip();
            return _at < text.Length ? text[_at] : null;
        }

        private void Skip()
        {
            while (_at < text.Length && text[_at] == ' ')
                _at++;
        }
    }
}
