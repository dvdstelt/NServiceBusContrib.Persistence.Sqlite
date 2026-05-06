namespace Messaging.Persistence.Sqlite;

using System.Text.RegularExpressions;

/// <summary>
/// A validated SQL identifier prefix. Internal types accept this struct rather than a raw
/// <see cref="string"/> so the regex check at the configuration boundary can't be bypassed.
/// </summary>
readonly record struct TablePrefix
{
    static readonly Regex Pattern = new("^[A-Za-z0-9_]*$", RegexOptions.Compiled);

    public string Value { get; }

    TablePrefix(string value) => Value = value;

    /// <summary>
    /// An empty (unset) prefix. Tables produced with this prefix have no leading qualifier.
    /// </summary>
    public static TablePrefix Empty { get; } = new("");

    /// <summary>
    /// Creates a validated prefix. The value must be empty or match <c>^[A-Za-z0-9_]*$</c> so it
    /// can be safely interpolated into DDL/DML.
    /// </summary>
    public static TablePrefix Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!Pattern.IsMatch(value))
        {
            throw new ArgumentException(
                "Table prefix may contain only ASCII letters, digits, and underscores.",
                nameof(value));
        }
        return new TablePrefix(value);
    }

    public static implicit operator string(TablePrefix prefix) => prefix.Value ?? "";

    public override string ToString() => Value ?? "";
}
