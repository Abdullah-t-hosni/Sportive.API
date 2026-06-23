using System;
using System.Linq;

namespace Sportive.API.Utils;

public static class PasswordGenerator
{
    public static string GenerateSecurePassword(int length = 12)
    {
        const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lower = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string specials = "!@#$?_-";

        var random = new Random();

        // Ensure at least one character from each pool to satisfy ASP.NET Core Identity defaults
        var password = new char[length];
        password[0] = upper[random.Next(upper.Length)];
        password[1] = lower[random.Next(lower.Length)];
        password[2] = digits[random.Next(digits.Length)];
        password[3] = specials[random.Next(specials.Length)];

        const string allChars = upper + lower + digits + specials;

        for (int i = 4; i < length; i++)
        {
            password[i] = allChars[random.Next(allChars.Length)];
        }

        // Shuffle the characters
        return new string(password.OrderBy(x => random.Next()).ToArray());
    }
}
