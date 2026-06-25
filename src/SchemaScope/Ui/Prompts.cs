using System.IO;
using Spectre.Console;

namespace SchemaScope.Ui;

internal static class Prompts
{
    private static readonly string[] CancelKeywords = ["back", "menu", "cancel"];

    private static bool IsCancel(string? input) =>
        !string.IsNullOrWhiteSpace(input)
        && CancelKeywords.Any(k => string.Equals(input.Trim(), k, StringComparison.OrdinalIgnoreCase));

    public static string AskRequired(string message, string? defaultValue = null)
    {
        while (true)
        {
            RenderPrompt(message, defaultValue);
            var answer = Console.ReadLine()?.Trim();
            if (IsCancel(answer))
            {
                throw new ReturnToMenuException();
            }
            if (string.IsNullOrEmpty(answer))
            {
                if (!string.IsNullOrWhiteSpace(defaultValue))
                {
                    return defaultValue;
                }
                AnsiConsole.MarkupLine($"[{Theme.Warning}]Value cannot be empty.[/]");
                continue;
            }
            return answer;
        }
    }

    public static string? AskOptional(string message, string? defaultValue = null)
    {
        RenderPrompt(message, defaultValue);
        var answer = Console.ReadLine()?.Trim();
        if (IsCancel(answer))
        {
            throw new ReturnToMenuException();
        }
        if (string.IsNullOrEmpty(answer))
        {
            return string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue;
        }
        return answer;
    }

    private static void RenderPrompt(string message, string? defaultValue)
    {
        AnsiConsole.Markup($"[{Theme.Prompt}]{Theme.Escape(message)}[/]");
        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            AnsiConsole.Markup($" [{Theme.Subtitle}]({Theme.Escape(defaultValue)})[/]");
        }
        AnsiConsole.Markup(": ");
    }

    public static string AskValidated(
        string message,
        string? defaultValue,
        Func<string, string?> validate,
        Func<string, string>? normalize = null)
    {
        while (true)
        {
            var answer = AskRequired(message, defaultValue);
            if (normalize is not null)
            {
                answer = normalize(answer);
            }
            var error = validate(answer);
            if (error is null)
            {
                return answer;
            }
            AnsiConsole.MarkupLine($"[{Theme.Danger}]{Theme.Escape(error)}[/]");
            defaultValue = answer;
        }
    }

    public static string? AskOptionalValidated(
        string message,
        string? defaultValue,
        Func<string, string?> validate,
        Func<string, string>? normalize = null)
    {
        while (true)
        {
            var answer = AskOptional(message, defaultValue);
            if (answer is null)
            {
                return null;
            }
            if (normalize is not null)
            {
                answer = normalize(answer);
            }
            var error = validate(answer);
            if (error is null)
            {
                return answer;
            }
            AnsiConsole.MarkupLine($"[{Theme.Danger}]{Theme.Escape(error)}[/]");
            defaultValue = answer;
        }
    }

    public static string NormalizePath(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        var s = raw.Trim();

        while (s.Length >= 2 &&
               ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
        {
            s = s[1..^1].Trim();
        }

        s = s.Trim('"', '\'').Trim();

        return s.Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
    }

    public static string AskExistingFolder(string message, string? defaultValue = null)
    {
        while (true)
        {
            var folder = AskRequired(message, defaultValue);
            if (Directory.Exists(folder))
            {
                return folder;
            }
            AnsiConsole.MarkupLine($"[{Theme.Danger}]Folder does not exist: {Theme.Escape(folder)}[/]");
            defaultValue = null;
        }
    }

    public static int AskInt(string message, int defaultValue)
    {
        while (true)
        {
            RenderPrompt(message, defaultValue.ToString());
            var raw = Console.ReadLine()?.Trim();
            if (IsCancel(raw))
            {
                throw new ReturnToMenuException();
            }
            if (string.IsNullOrEmpty(raw))
            {
                return defaultValue;
            }
            if (int.TryParse(raw, out var n))
            {
                return n;
            }
            AnsiConsole.MarkupLine($"[{Theme.Warning}]Enter a whole number (or 'back' to return to the menu).[/]");
        }
    }

    public static bool AskYesNo(string message, bool defaultYes)
    {
        var prompt = new ConfirmationPrompt($"[{Theme.Prompt}]{Theme.Escape(message)}[/]")
        {
            DefaultValue = defaultYes
        };
        return AnsiConsole.Prompt(prompt);
    }

    private static readonly (MenuAction Action, string Glyph, string Label, string Description)[] MenuItems =
    {
        (MenuAction.DetectVersion,   "▸", "Detect version",   "Probe the database to find its current applied version"),
        (MenuAction.VerifyVersion,   "✓", "Verify",           "Check whether a specific version matches the database"),
        (MenuAction.FullHeal,        "✦", "Full heal",        "Run the prepatch, then every version script in range"),
        (MenuAction.VersionsOnly,    "↻", "Versions",         "Run version scripts in range without the prepatch"),
        (MenuAction.SpecificVersion, "▹", "Specific version", "Run one version by number"),
        (MenuAction.PrepatchOnly,    "✱", "Prepatch",         "Run the prepatch SQL only"),
        (MenuAction.Backup,          "↓", "Backup",           "Back up a database to a .bak file"),
        (MenuAction.Restore,         "↑", "Restore",          "Restore a .bak file into a target database"),
        (MenuAction.Clone,           "⎘", "Clone",            "Copy a database to a new name via backup plus restore"),
        (MenuAction.TestDbScripts,   "⎈", "Test DB scripts",  "Manage test DBs and run scripts on them"),
        (MenuAction.Exit,            "✕", "Exit",             "Quit SchemaScope"),
    };

    public static MenuAction AskMenuChoice()
    {
        AnsiConsole.WriteLine();

        var prompt = new SelectionPrompt<MenuAction>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = MenuItems.Length + 2,
            WrapAround = true
        }
        .Title($"[bold {Theme.Title}]What would you like to do?[/]  [{Theme.Muted}](arrow keys to navigate, Enter to select)[/]")
        .UseConverter(action =>
        {
            var item = MenuItems.First(m => m.Action == action);
            return Theme.MenuRow(item.Glyph, item.Label, item.Description);
        });

        foreach (var item in MenuItems)
        {
            prompt.AddChoice(item.Action);
        }

        return AnsiConsole.Prompt(prompt);
    }
}
