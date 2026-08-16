using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BidRisk.Bench;

internal static class Formats
{
    public static CultureInfo Invariant => CultureInfo.InvariantCulture;

    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static double Round(double value, int digits) => Math.Round(value, digits);

    public static string Number(double value, int digits = 2) =>
        value.ToString("N" + digits.ToString(Invariant), Invariant);
}
