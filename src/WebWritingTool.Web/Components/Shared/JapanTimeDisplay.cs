using System.Globalization;

namespace WebWritingTool.Web.Components.Shared;

public static class JapanTimeDisplay
{
    private static readonly TimeSpan JapanOffset = TimeSpan.FromHours(9);

    public static string Format(DateTimeOffset value, string format)
        => value.ToOffset(JapanOffset).ToString(format, CultureInfo.InvariantCulture);
}
