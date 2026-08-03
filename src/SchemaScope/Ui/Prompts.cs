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
        AnsiConsole.Markup($"  [{Theme.Accent}]❯[/] [{Theme.Prompt}]{Theme.Escape(message)}[/]");
        if (!string.IsNullOrWhiteSpace(defaultValue))
        {
            AnsiConsole.Markup($" [{Theme.Subtle}]‹{Theme.Escape(defaultValue)}›[/]");
        }
        AnsiConsole.Markup($" [{Theme.Muted}]·[/] ");
    }

    public static void PauseBeforeMenu()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [{Theme.Subtle}]Press any key to return to the menu …[/]");
        Console.ReadKey(intercept: true);
    }

    public static string AskSecret(string message)
    {
        while (true)
        {
            AnsiConsole.Markup($"  [{Theme.Accent}]❯[/] [{Theme.Prompt}]{Theme.Escape(message)}[/] [{Theme.Muted}]·[/] ");
            var answer = ReadSecretLine();
            if (IsCancel(answer))
            {
                throw new ReturnToMenuException();
            }
            if (!string.IsNullOrEmpty(answer))
            {
                return answer;
            }
            AnsiConsole.MarkupLine($"[{Theme.Warning}]Value cannot be empty.[/]");
        }
    }

    private static string ReadSecretLine()
    {
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                    Console.Write("\b \b");
                }
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
                Console.Write('•');
            }
        }
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

    private sealed record MenuEntry(MenuAction? Action, string Glyph, string Label, string Description);

    private static readonly (string Group, MenuEntry[] Entries)[] MenuGroups =
    {
        ("Inspect", new[]
        {
            new MenuEntry(MenuAction.DetectVersion,   "◉", "Detect version",   "Probe the database to find its current applied version"),
            new MenuEntry(MenuAction.VerifyVersion,   "✓", "Verify",           "Check whether a specific version matches the database"),
        }),
        ("Migrate", new[]
        {
            new MenuEntry(MenuAction.FullHeal,        "✦", "Full heal",        "Run the prepatch, then every version script in range"),
            new MenuEntry(MenuAction.VersionsOnly,    "↻", "Versions",         "Run version scripts in range without the prepatch"),
            new MenuEntry(MenuAction.SpecificVersion, "▹", "Specific version", "Run one version by number"),
            new MenuEntry(MenuAction.PrepatchOnly,    "✱", "Prepatch",         "Run the prepatch SQL only"),
        }),
        ("Database", new[]
        {
            new MenuEntry(MenuAction.Backup,          "↓", "Backup",           "Back up a database to a .bak file"),
            new MenuEntry(MenuAction.Restore,         "↑", "Restore",          "Restore a .bak file into a target database"),
            new MenuEntry(MenuAction.Clone,           "⧉", "Clone",            "Copy a database to a new name via backup plus restore"),
        }),
        ("Workspace", new[]
        {
            new MenuEntry(MenuAction.TestDbScripts,   "⚑", "Test DB scripts",  "Manage test DBs and run scripts on them"),
            new MenuEntry(MenuAction.Settings,        "⚙", "Settings",         "View and edit the connection and script configuration"),
            new MenuEntry(MenuAction.Exit,            "✕", "Exit",             "Quit SchemaScope"),
        }),
    };

    public static MenuAction AskMenuChoice()
    {
        AnsiConsole.WriteLine();

        var prompt = new SelectionPrompt<MenuEntry>
        {
            HighlightStyle = Theme.HighlightStyle,
            PageSize = MenuGroups.Sum(g => g.Entries.Length) + MenuGroups.Length + 2,
            WrapAround = true,
            SearchEnabled = true
        }
        .Title($"[bold {Theme.Title}]What would you like to do?[/]  [{Theme.Muted}](↑↓ move · type to filter · Enter selects)[/]")
        .UseConverter(entry => entry.Action is null
            ? $"[{Theme.Subtle}]{Markup.Escape(entry.Label.ToUpperInvariant())}[/]"
            : Theme.MenuRow(entry.Glyph, entry.Label, entry.Description));

        foreach (var (group, entries) in MenuGroups)
        {
            prompt.AddChoiceGroup(new MenuEntry(null, string.Empty, group, string.Empty), entries);
        }

        while (true)
        {
            var picked = AnsiConsole.Prompt(prompt);
            if (picked.Action is { } action)
            {
                return action;
            }
        }
    }
}
