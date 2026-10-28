using System.Globalization;
using SurfaceNotebook.Services;

var checks = 0;
void Value(string source, double expected)
{
    var result = CalculationEngine.Evaluate(source).Rows[^1].Value;
    if (Math.Abs(result - expected) > Math.Max(1e-12, Math.Abs(expected) * 1e-12))
        throw new InvalidOperationException($"{source}: expected {expected}, got {result}.");
    checks++;
}
void Reject(string source, string reason)
{
    try { CalculationEngine.Evaluate(source); }
    catch (CalculationEngine.CalculationException ex)
    {
        if (!ex.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Expected '{reason}', got '{ex.Message}'.");
        checks++; return;
    }
    throw new InvalidOperationException($"Invalid calculation was accepted: {source}");
}
Value("F = m * a\nm = 2\na = 3\nF =", 6);
Value("x = 2; y = 3; total = x*y + (x+y)^2", 31);
Value("2 + 3 * 4", 14);
Value("-2^2", -4); Value("(-2)^2", 4); Value("2^-3", .125); Value("2^3^2", 512);
Value("x = 4\n2x + 3(x + 1)", 23);
Value("sqrt(9) + abs(-2) + min(7,3) + max(1,2)", 10);
Value("sin(pi/2) + cos(0) + ln(e) + log(100)", 5);
Value("floor(1.9) + ceil(1.1) + round(2.5)", 6);
Value("3 × 4 − 6 ÷ 2", 9); Value("3² + 2³", 17); Value("2π", 2 * Math.PI);
Value(".5 + 1e-3 + 2E2", 200.501); Value("50% * 80", 40);
Value("mass = 2\nMass = 3\nmass + Mass", 5);
Value("α = 3\nα^2", 9);
Reject("x = y\ny = x", "circular"); Reject("x = x + 1", "circular");
Reject("x = 2\nx = 3", "twice"); Reject("pi = 3", "reserved"); Reject("sqrt = 3", "reserved");
Reject("x = 2\nX + 1", "Unknown variable"); Reject("2kg", "units");
Reject("1/0", "zero"); Reject("sqrt(-1)", "finite"); Reject("1e999", "finite"); Reject("10^1000", "finite");
Reject("1 +", "Expected"); Reject("1.2.3", "Invalid decimal"); Reject("2 3", "Unexpected");
Reject("x=1=2", "Unexpected"); Reject("(2+3", "parenthesis"); Reject("min(1)", "two arguments");
Reject("x=2==", "Unexpected");
Reject("sqrt(1,2)", "arguments"); Reject("System.IO.File.Delete(1)", "Unknown variable");
Reject("", "first"); Reject(new string('1', 12001), "12,000");
Reject(string.Join('\n', Enumerable.Repeat("1", 65)), "64 lines");
Reject(new string('(', 60) + "1" + new string(')', 60), "deeply");
var culture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
    Value("1.5+2.5", 4);
    if (CalculationEngine.Evaluate("1/8").Rows[0].FormattedValue != "0.125") throw new InvalidOperationException("Result formatting depends on system locale.");
}
finally { CultureInfo.CurrentCulture = culture; }
Console.WriteLine($"PASS: {checks} calculation checks; variables, precedence, functions, recognition symbols, limits, and error handling.");
