using System;

namespace Sportive.API.Utils;

public static class StringExtensions
{
    public static string? NullIfEmpty(this string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
