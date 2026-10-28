using System.Globalization;
using System.Text.RegularExpressions;

namespace SurfaceNotebook.Services;

// A bounded arithmetic parser. Notebook input is never executed as code.
// Definitions belong to one card; resolving them lazily permits formulas above their inputs.
public static partial class CalculationEngine
{
    public const int MaxSourceLength = 12000;
    public const int MaxLines = 64;
    public sealed record Row(string Source, string? Name, double Value)
    {
        public string FormattedValue => (Value == 0 ? 0 : Value).ToString("G12", CultureInfo.InvariantCulture);
        public string Display => Name is not null && Source[(Source.IndexOf('=') + 1)..].Trim() == FormattedValue
            ? Source : $"{Source.TrimEnd('=', ' ')}  → {FormattedValue}";
    }
    public sealed record Result(IReadOnlyList<Row> Rows)
    {
        public string DisplayText => string.Join(Environment.NewLine, Rows.Select(r => r.Display));
    }
    public sealed class CalculationException(string message) : FormatException(message);
    private sealed record Definition(string Source, string Expression, string? Name, int Line);
    [GeneratedRegex(@"^[\p{L}_][\p{L}\p{Nd}_]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
        { "sqrt", "abs", "sin", "cos", "tan", "asin", "acos", "atan", "ln", "log", "log10", "exp", "floor", "ceil", "round", "min", "max" };

    public static Result Evaluate(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new CalculationException("Write or enter a calculation first.");
        if (source.Length > MaxSourceLength) throw new CalculationException("Use at most 12,000 characters in one calculation.");
        var lines = Normalize(source).Replace("\r", "").Split(['\n', ';']);
        var definitions = new List<Definition>();
        var variables = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].Trim(); if (text.Length == 0) continue;
            if (definitions.Count >= MaxLines) throw new CalculationException("Use at most 64 lines in one calculation.");
            if (text.Length > 512) throw new CalculationException($"Line {i + 1}: split this into shorter expressions (512 characters per line).");
            // A finishing equals sign asks for the value, rather than defining a variable.
            var expression = text.EndsWith('=') && text.Count(c => c == '=') == 1 ? text[..^1].Trim() : text;
            string? name = null;
            var equals = expression.IndexOf('=');
            if (equals >= 0)
            {
                name = expression[..equals].Trim(); expression = expression[(equals + 1)..].Trim();
                if (!Identifier().IsMatch(name) || name is "pi" or "e" || Functions.Contains(name))
                    throw new CalculationException($"Line {i + 1}: use a variable name such as x, mass, or F on the left of =. Constants and function names are reserved.");
                if (!variables.TryAdd(name, definitions.Count)) throw new CalculationException($"Line {i + 1}: '{name}' is defined twice. Edit its original value instead.");
            }
            definitions.Add(new Definition(text, expression, name, i + 1));
        }
        if (definitions.Count == 0) throw new CalculationException("Write or enter a calculation first.");
        var values = new Dictionary<int, double>(); var resolving = new HashSet<int>();
        double Resolve(int index)
        {
            if (values.TryGetValue(index, out var cached)) return cached;
            var definition = definitions[index];
            if (!resolving.Add(index)) throw new CalculationException($"Line {definition.Line}: circular definition involving '{definition.Name}'.");
            try
            {
                double Variable(string name) => name switch
                {
                    "pi" => Math.PI, "e" => Math.E,
                    _ => variables.TryGetValue(name, out var dependency) ? Resolve(dependency)
                        : throw new CalculationException($"Unknown variable '{name}'. Define it on another line. Names are case-sensitive; units are not supported yet.")
                };
                return values[index] = new Parser(definition.Expression, Variable).Parse();
            }
            catch (CalculationException ex) when (!ex.Message.StartsWith("Line ", StringComparison.Ordinal))
            { throw new CalculationException($"Line {definition.Line}: {ex.Message}"); }
            finally { resolving.Remove(index); }
        }
        return new Result(definitions.Select((d, i) => new Row(d.Source, d.Name, Resolve(i))).ToList());
    }
    private static string Normalize(string text) => text.Replace('−', '-').Replace('–', '-').Replace('×', '*')
        .Replace('·', '*').Replace('⋅', '*').Replace('÷', '/').Replace('＝', '=').Replace("²", "^2")
        .Replace("³", "^3").Replace("π", "pi").Replace('\u00a0', ' ');

    private sealed class Parser(string expression, Func<string, double> variable)
    {
        private int position, depth;
        private char Current { get { Skip(); return position < expression.Length ? expression[position] : '\0'; } }
        private void Skip() { while (position < expression.Length && char.IsWhiteSpace(expression[position])) position++; }
        private bool Take(char c) { if (Current != c) return false; position++; return true; }
        private CalculationException Error(string message) => new($"{message} (column {position + 1}).");
        private static double Finite(double value)
        {
            if (!double.IsFinite(value)) throw new CalculationException("The result is outside real, finite numbers. Check the input and function domain.");
            return value;
        }
        public double Parse()
        {
            var value = Sum();
            if (Current != '\0') throw Error($"Unexpected '{Current}'. Use +, -, *, /, ^, parentheses, and decimal numbers");
            return Finite(value);
        }
        private double Sum()
        {
            var value = Product();
            while (true)
                if (Take('+')) value = Finite(value + Product());
                else if (Take('-')) value = Finite(value - Product());
                else return value;
        }
        private double Product()
        {
            var value = Unary();
            while (true)
                if (Take('*')) value = Finite(value * Unary());
                else if (Take('/'))
                {
                    var divisor = Unary();
                    if (divisor == 0) throw Error("Cannot divide by zero");
                    value = Finite(value / divisor);
                }
                else if (Current == '(' || char.IsLetter(Current) || Current == '_') value = Finite(value * Unary());
                else return value;
        }
        private double Unary()
        {
            if (++depth > 48) throw Error("The expression is nested too deeply");
            try
            {
                if (Take('+')) return Unary();
                if (Take('-')) return -Unary();
                var value = Atom();
                if (Take('%')) value /= 100;
                if (Take('^')) value = Finite(Math.Pow(value, Unary())); // right associative; -2^2 = -4
                return value;
            }
            finally { depth--; }
        }
        private double Atom()
        {
            if (Take('('))
            {
                var value = Sum(); if (!Take(')')) throw Error("Missing closing parenthesis"); return value;
            }
            if (char.IsAsciiDigit(Current) || Current == '.')
            {
                var start = position;
                while (position < expression.Length && (char.IsAsciiDigit(expression[position]) || expression[position] == '.')) position++;
                if (position < expression.Length && expression[position] is 'e' or 'E')
                {
                    var exponent = position + 1;
                    if (exponent < expression.Length && expression[exponent] is '+' or '-') exponent++;
                    if (exponent < expression.Length && char.IsAsciiDigit(expression[exponent]))
                    { position = exponent + 1; while (position < expression.Length && char.IsAsciiDigit(expression[position])) position++; }
                }
                if (!double.TryParse(expression.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw Error("Invalid decimal number; use a dot for decimals");
                return Finite(value);
            }
            if (char.IsLetter(Current) || Current == '_')
            {
                var start = position++;
                while (position < expression.Length && (char.IsLetterOrDigit(expression[position]) || expression[position] == '_')) position++;
                var name = expression[start..position];
                if (!Functions.Contains(name)) return variable(name);
                if (!Take('(')) throw Error($"Use parentheses after {name}");
                var a = Sum();
                double value;
                if (name is "min" or "max")
                {
                    if (!Take(',')) throw Error($"{name} needs two arguments separated by a comma");
                    var b = Sum(); value = name == "min" ? Math.Min(a, b) : Math.Max(a, b);
                }
                else value = name switch
                {
                    "sqrt" => Math.Sqrt(a), "abs" => Math.Abs(a), "sin" => Math.Sin(a), "cos" => Math.Cos(a), "tan" => Math.Tan(a),
                    "asin" => Math.Asin(a), "acos" => Math.Acos(a), "atan" => Math.Atan(a), "ln" => Math.Log(a),
                    "log" or "log10" => Math.Log10(a), "exp" => Math.Exp(a), "floor" => Math.Floor(a), "ceil" => Math.Ceiling(a),
                    "round" => Math.Round(a, MidpointRounding.AwayFromZero), _ => throw Error("Unknown function")
                };
                if (!Take(')')) throw Error("Missing closing parenthesis or too many function arguments");
                return Finite(value);
            }
            throw Error("Expected a number, variable, or parenthesized expression");
        }
    }
}
