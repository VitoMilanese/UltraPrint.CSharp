using System.Data;
using System.Data.OleDb;
using System.Text;

namespace UltraPrint.Legacy.Data;

public enum LegacyDataSourceKind
{
    Access,
    DBase,
    Excel,
    Csv,
    Text
}

public interface ILegacyRecordSource : IDisposable
{
    string SourcePath { get; }
    LegacyDataSourceKind Kind { get; }
    bool IsReadOnly { get; }
    string ProviderDescription { get; }

    IReadOnlyList<string> GetTableNames();
    DataTable OpenTable(string tableName, int maxRows = 0);
    DataTable ExecuteQuery(string sql);
    int ExecuteNonQuery(string sql);
}

/// <summary>
/// Opens the data formats exposed by the original UltraPrint database UI:
/// Access/Jet databases, DBF, Excel, CSV and text files.  The supplied 2003
/// installer contains DAO 3.5/3.6 and Jet 3.5/4.0, so .mdb and Operatori.FFM
/// are treated as Jet/Access databases.  On modern Windows we prefer ACE when
/// installed and fall back to Jet 4.0 (normally available only to a 32-bit process).
/// </summary>
public static class LegacyRecordSourceFactory
{
    public static LegacyDataSourceKind DetectKind(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mdb" or ".accdb" or ".ffm" => LegacyDataSourceKind.Access,
            ".dbf" => LegacyDataSourceKind.DBase,
            ".xls" or ".xlsx" => LegacyDataSourceKind.Excel,
            ".csv" => LegacyDataSourceKind.Csv,
            ".txt" or ".dat" => LegacyDataSourceKind.Text,
            _ => throw new NotSupportedException($"Unsupported UltraPrint data file: {Path.GetExtension(path)}")
        };
    }

    public static ILegacyRecordSource Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Database/data file not found.", fullPath);

        var kind = DetectKind(fullPath);
        return kind switch
        {
            LegacyDataSourceKind.Csv or LegacyDataSourceKind.Text => new DelimitedTextRecordSource(fullPath, kind),
            _ => OleDbRecordSource.Open(fullPath, kind)
        };
    }
}

public sealed class OleDbRecordSource : ILegacyRecordSource
{
    private readonly OleDbConnection _connection;

    private OleDbRecordSource(string sourcePath, LegacyDataSourceKind kind, OleDbConnection connection)
    {
        SourcePath = sourcePath;
        Kind = kind;
        _connection = connection;
        ProviderDescription = connection.Provider;
    }

    public string SourcePath { get; }
    public LegacyDataSourceKind Kind { get; }
    public bool IsReadOnly => false;
    public string ProviderDescription { get; }

    public static OleDbRecordSource Open(string path, LegacyDataSourceKind kind)
    {
        var errors = new List<string>();
        foreach (var connectionString in BuildCandidateConnectionStrings(path, kind))
        {
            try
            {
                var connection = new OleDbConnection(connectionString);
                connection.Open();
                return new OleDbRecordSource(path, kind, connection);
            }
            catch (Exception ex)
            {
                errors.Add($"{ProviderName(connectionString)}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "UltraPrint could not open the legacy data source. Install a matching Microsoft Access Database Engine " +
            "(ACE 16/12) or run the application as x86 to use Jet 4.0.\r\n\r\n" + string.Join("\r\n", errors));
    }

    public IReadOnlyList<string> GetTableNames()
    {
        var schema = _connection.GetSchema("Tables");
        var result = new List<string>();
        foreach (DataRow row in schema.Rows)
        {
            var name = Convert.ToString(row["TABLE_NAME"])?.Trim();
            var type = Convert.ToString(row["TABLE_TYPE"])?.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)) continue;
            if (type is not null && !type.Equals("TABLE", StringComparison.OrdinalIgnoreCase) &&
                !type.Equals("VIEW", StringComparison.OrdinalIgnoreCase)) continue;
            if (!result.Contains(name, StringComparer.OrdinalIgnoreCase)) result.Add(name);
        }
        result.Sort(StringComparer.CurrentCultureIgnoreCase);
        return result;
    }

    public DataTable OpenTable(string tableName, int maxRows = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
        var top = maxRows > 0 ? $"TOP {maxRows} " : string.Empty;
        return ExecuteQuery($"SELECT {top}* FROM {QuoteIdentifier(tableName)}");
    }

    public DataTable ExecuteQuery(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        using var command = new OleDbCommand(sql, _connection);
        using var adapter = new OleDbDataAdapter(command);
        var table = new DataTable { Locale = System.Globalization.CultureInfo.CurrentCulture };
        adapter.Fill(table);
        return table;
    }

    public int ExecuteNonQuery(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        using var command = new OleDbCommand(sql, _connection);
        return command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    public static string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";

    private static IEnumerable<string> BuildCandidateConnectionStrings(string path, LegacyDataSourceKind kind)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var providers = extension == ".accdb" || extension == ".xlsx"
            ? new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0" }
            : new[] { "Microsoft.ACE.OLEDB.16.0", "Microsoft.ACE.OLEDB.12.0", "Microsoft.Jet.OLEDB.4.0" };

        foreach (var provider in providers)
        {
            var builder = new OleDbConnectionStringBuilder { Provider = provider };
            switch (kind)
            {
                case LegacyDataSourceKind.Access:
                    builder.DataSource = path;
                    builder["Persist Security Info"] = false;
                    break;

                case LegacyDataSourceKind.DBase:
                    builder.DataSource = Path.GetDirectoryName(path)!;
                    builder["Extended Properties"] = "dBASE IV";
                    break;

                case LegacyDataSourceKind.Excel:
                    builder.DataSource = path;
                    builder["Extended Properties"] = extension == ".xlsx"
                        ? "Excel 12.0 Xml;HDR=YES;IMEX=0"
                        : "Excel 8.0;HDR=YES;IMEX=0";
                    break;

                default:
                    continue;
            }
            yield return builder.ConnectionString;
        }
    }

    private static string ProviderName(string connectionString)
    {
        try { return new OleDbConnectionStringBuilder(connectionString).Provider; }
        catch { return "OLE DB"; }
    }
}

public sealed class DelimitedTextRecordSource : ILegacyRecordSource
{
    private readonly char _delimiter;
    private readonly string _tableName;

    public DelimitedTextRecordSource(string sourcePath, LegacyDataSourceKind kind)
    {
        SourcePath = sourcePath;
        Kind = kind;
        _tableName = Path.GetFileName(sourcePath);
        _delimiter = DetectDelimiter(ReadText(sourcePath));
    }

    public string SourcePath { get; }
    public LegacyDataSourceKind Kind { get; }
    public bool IsReadOnly => true;
    public string ProviderDescription => _delimiter == '\t' ? "Delimited text (tab)" : $"Delimited text ('{_delimiter}')";

    public IReadOnlyList<string> GetTableNames() => new[] { _tableName };

    public DataTable OpenTable(string tableName, int maxRows = 0)
    {
        if (!string.Equals(tableName, _tableName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown text table '{tableName}'.", nameof(tableName));
        return Parse(ReadText(SourcePath), _delimiter, maxRows);
    }

    public DataTable ExecuteQuery(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return OpenTable(_tableName);
        var normalized = string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Equals("SELECT *", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("SELECT * FROM", StringComparison.OrdinalIgnoreCase))
            return OpenTable(_tableName);
        throw new NotSupportedException("CSV/text sources support table browsing; arbitrary SQL requires an OLE DB text driver.");
    }

    public int ExecuteNonQuery(string sql) =>
        throw new NotSupportedException("CSV/text sources are read-only in the managed compatibility layer.");

    public void Dispose() { }

    public static DataTable Parse(string text, char delimiter, int maxRows = 0)
    {
        var rows = ParseRows(text, delimiter).Where(row => row.Any(cell => cell.Length > 0)).ToList();
        var table = new DataTable();
        if (rows.Count == 0) return table;

        var header = rows[0];
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var baseName = string.IsNullOrWhiteSpace(header[i]) ? $"Field{i + 1}" : header[i].Trim();
            var name = baseName;
            var suffix = 2;
            while (!used.Add(name)) name = baseName + "_" + suffix++;
            table.Columns.Add(name, typeof(string));
        }

        var count = 0;
        foreach (var sourceRow in rows.Skip(1))
        {
            if (maxRows > 0 && count >= maxRows) break;
            while (table.Columns.Count < sourceRow.Count)
                table.Columns.Add($"Field{table.Columns.Count + 1}", typeof(string));

            var row = table.NewRow();
            for (var i = 0; i < sourceRow.Count; i++) row[i] = sourceRow[i];
            table.Rows.Add(row);
            count++;
        }
        return table;
    }

    private static string ReadText(string path) => Encoding.Latin1.GetString(File.ReadAllBytes(path));

    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
        var candidates = new[] { ';', ',', '\t', '|' };
        return candidates
            .Select(candidate => new { Delimiter = candidate, Count = CountDelimiterOutsideQuotes(firstLine, candidate) })
            .OrderByDescending(x => x.Count)
            .FirstOrDefault(x => x.Count > 0)?.Delimiter ?? ';';
    }

    private static int CountDelimiterOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { i++; continue; }
                quoted = !quoted;
            }
            else if (!quoted && line[i] == delimiter) count++;
        }
        return count;
    }

    private static IEnumerable<List<string>> ParseRows(string text, char delimiter)
    {
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else quoted = !quoted;
                continue;
            }

            if (!quoted && ch == delimiter)
            {
                row.Add(cell.ToString());
                cell.Clear();
                continue;
            }

            if (!quoted && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                yield return row;
                row = new List<string>();
                continue;
            }

            cell.Append(ch);
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            yield return row;
        }
    }
}
