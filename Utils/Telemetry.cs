using System.Diagnostics;

namespace Sportive.API.Utils;

public static class Telemetry
{
    public static readonly ActivitySource ActivitySource = new("Sportive.API");
}
