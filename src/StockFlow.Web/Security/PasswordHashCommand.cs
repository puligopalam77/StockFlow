using Microsoft.AspNetCore.Identity;
using StockFlow.Domain.Entities;

namespace StockFlow.Web.Security;

public static class PasswordHashCommand
{
    public static void Run()
    {
        Console.Write("New account password (hidden): ");
        var password = ReadPassword();
        Console.Write("Confirm password (hidden): ");
        var confirmation = ReadPassword();
        if (password.Length < 8 || password != confirmation)
        {
            Console.Error.WriteLine("Passwords must match and contain at least 8 characters.");
            Environment.ExitCode = 1;
            return;
        }

        Console.WriteLine("Paste this hash into database/inserts/003_insert_admin.sql:");
        Console.WriteLine(new PasswordHasher<User>().HashPassword(new User(), password));
    }

    private static string ReadPassword()
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return value.ToString();
    }
}
