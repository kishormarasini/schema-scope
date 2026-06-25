namespace SchemaScope.Cli;

public enum CliCommand
{
    Auto,
    Backup,
    Restore,
    Clone,
    Verify,
    Detect
}

public sealed class CliOptions
{
    public CliCommand Command { get; set; } = CliCommand.Auto;

    public string? DatabaseName { get; set; }
    public string? Server { get; set; }
    public string? VersionFolder { get; set; }
    public string? PrepatchFile { get; set; }
    public string? ConfigPath { get; set; }
    public int StartFrom { get; set; }
    public int EndAt { get; set; }
    public bool SkipPrepatch { get; set; }
    public bool PrepatchOnly { get; set; }
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }

    public string? SourceDatabase { get; set; }
    public string? TargetDatabase { get; set; }
    public string? BackupPath { get; set; }
    public string? DataDir { get; set; }
    public string? LogDir { get; set; }

    public int VerifyTarget { get; set; }

    public bool IsInteractive =>
        Command == CliCommand.Auto && string.IsNullOrWhiteSpace(DatabaseName);

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--database":
                case "-d":
                    options.DatabaseName = ReadValue(args, ref i, arg);
                    break;
                case "--server":
                case "-s":
                    options.Server = ReadValue(args, ref i, arg);
                    break;
                case "--version-folder":
                case "-f":
                    options.VersionFolder = ReadValue(args, ref i, arg);
                    break;
                case "--prepatch-file":
                    options.PrepatchFile = ReadValue(args, ref i, arg);
                    break;
                case "--config":
                case "-c":
                    options.ConfigPath = ReadValue(args, ref i, arg);
                    break;
                case "--start-from":
                    options.StartFrom = ParseInt(args, ref i, arg);
                    break;
                case "--end-at":
                    options.EndAt = ParseInt(args, ref i, arg);
                    break;
                case "--skip-prepatch":
                    options.SkipPrepatch = true;
                    break;
                case "--prepatch-only":
                    options.PrepatchOnly = true;
                    break;

                case "--backup":
                    options.SetCommand(CliCommand.Backup);
                    break;
                case "--restore":
                    options.SetCommand(CliCommand.Restore);
                    break;
                case "--clone":
                    options.SetCommand(CliCommand.Clone);
                    break;
                case "--detect":
                    options.SetCommand(CliCommand.Detect);
                    break;
                case "--verify":
                    options.SetCommand(CliCommand.Verify);
                    options.VerifyTarget = ParseInt(args, ref i, arg);
                    break;
                case "--source":
                    options.SourceDatabase = ReadValue(args, ref i, arg);
                    break;
                case "--target":
                    options.TargetDatabase = ReadValue(args, ref i, arg);
                    break;
                case "--backup-path":
                    options.BackupPath = ReadValue(args, ref i, arg);
                    break;
                case "--data-dir":
                    options.DataDir = ReadValue(args, ref i, arg);
                    break;
                case "--log-dir":
                    options.LogDir = ReadValue(args, ref i, arg);
                    break;

                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "--version":
                case "-v":
                    options.ShowVersion = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return options;
    }

    private void SetCommand(CliCommand command)
    {
        if (Command != CliCommand.Auto && Command != command)
        {
            throw new ArgumentException(
                "Only one operation can be run at a time (--backup, --restore, --clone, --verify, --detect).");
        }
        Command = command;
    }

    private static string ReadValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for {flag}");
        }
        i++;
        return args[i];
    }

    private static int ParseInt(string[] args, ref int i, string flag)
    {
        var raw = ReadValue(args, ref i, flag);
        if (!int.TryParse(raw, out var result))
        {
            throw new ArgumentException($"Value for {flag} must be an integer; got '{raw}'.");
        }
        return result;
    }
}
