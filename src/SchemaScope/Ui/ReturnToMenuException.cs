namespace SchemaScope.Ui;

public sealed class ReturnToMenuException : Exception
{
    public ReturnToMenuException() : base("User cancelled; returning to main menu.") { }
}
