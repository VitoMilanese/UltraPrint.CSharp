using System.Data;
using System.Data.OleDb;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace UltraPrint.Legacy.Security;

public sealed record LegacyOperatorInfo(string Name, int Level, int Privilege, string Group);
public sealed record LegacyPrivilegeInfo(int Privilege, string Description);
public sealed record LegacyOperatorSession(string Name, int Level, int Privilege, string Group);

public static class LegacyOperatorDatabaseLocator
{
    public static IReadOnlyList<string> Candidates(string applicationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDirectory);
        var root = Path.GetFullPath(applicationDirectory);
        return new[]
        {
            Path.Combine(root, "Db", "Operatori.FFM"),
            Path.Combine(root, "Operatori.FFM")
        };
    }

    public static string? FindExisting(string applicationDirectory) =>
        Candidates(applicationDirectory).FirstOrDefault(File.Exists);
}

/// <summary>
/// Jet/ACE compatibility store for UltraPrint's recovered Operatori.FFM database.
///
/// Confirmed native fields: Operatore, Password, Livello, Privilegio and Gruppo.
/// Confirmed startup migrations: add Privilegio LONG and Gruppo TEXT(50) when absent.
/// Existing databases are otherwise opened conservatively; this class does not invent
/// privilege meanings or silently rebuild an unknown legacy schema.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LegacyOperatorStore : IDisposable
{
    private static readonly string[] Providers =
    {
        "Microsoft.ACE.OLEDB.16.0",
        "Microsoft.ACE.OLEDB.12.0",
        "Microsoft.Jet.OLEDB.4.0"
    };

    private readonly OleDbConnection _connection;

    private LegacyOperatorStore(string path, OleDbConnection connection)
    {
        DatabasePath = path;
        _connection = connection;
        ProviderDescription = connection.Provider;
    }

    public string DatabasePath { get; }
    public string ProviderDescription { get; }

    public static LegacyOperatorStore Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("UltraPrint operator database not found.", fullPath);

        var errors = new List<string>();
        foreach (var provider in Providers)
        {
            OleDbConnection? connection = null;
            try
            {
                connection = new OleDbConnection(ConnectionString(provider, fullPath, create: false));
                connection.Open();
                var store = new LegacyOperatorStore(fullPath, connection);
                connection = null; // ownership transferred to store
                store.EnsureExistingSchemaCompatibility();
                return store;
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                errors.Add($"{provider}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "UltraPrint could not open Operatori.FFM. Install a matching Microsoft Access Database Engine " +
            "(ACE 16/12), or use an x86 build when Jet 4.0 is required.\r\n\r\n" + string.Join("\r\n", errors));
    }

    public static LegacyOperatorStore Create(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)) throw new IOException($"The operator database already exists: {fullPath}");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var adoxType = Type.GetTypeFromProgID("ADOX.Catalog", throwOnError: false)
                       ?? throw new InvalidOperationException(
                           "ADOX is not registered. Install Microsoft Access Database Engine/MDAC to create a new Operatori.FFM.");
        var errors = new List<string>();

        foreach (var provider in Providers)
        {
            object? catalog = null;
            try
            {
                if (File.Exists(fullPath)) File.Delete(fullPath);
                catalog = Activator.CreateInstance(adoxType)
                          ?? throw new InvalidOperationException("Could not instantiate ADOX.Catalog.");
                dynamic dynamicCatalog = catalog;
                dynamicCatalog.Create(ConnectionString(provider, fullPath, create: true));
            }
            catch (Exception ex)
            {
                errors.Add($"{provider} create: {ex.Message}");
                continue;
            }
            finally
            {
                if (catalog is not null && Marshal.IsComObject(catalog))
                {
                    try { Marshal.FinalReleaseComObject(catalog); }
                    catch { }
                }
            }

            OleDbConnection? connection = null;
            try
            {
                connection = new OleDbConnection(ConnectionString(provider, fullPath, create: false));
                connection.Open();
                var store = new LegacyOperatorStore(fullPath, connection);
                connection = null; // ownership transferred to store
                store.CreateManagedSchema();
                return store;
            }
            catch (Exception ex)
            {
                connection?.Dispose();
                errors.Add($"{provider} open-after-create: {ex.Message}");
            }
        }

        if (File.Exists(fullPath))
        {
            try { File.Delete(fullPath); }
            catch { }
        }

        throw new InvalidOperationException(
            "UltraPrint could not create Operatori.FFM with the installed Jet/ACE providers.\r\n\r\n" +
            string.Join("\r\n", errors));
    }

    public int OperatorCount()
    {
        using var command = new OleDbCommand("SELECT COUNT(*) FROM [Operatori]", _connection);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public IReadOnlyList<LegacyOperatorInfo> GetOperators()
    {
        using var command = new OleDbCommand(
            "SELECT [Operatore],[Livello],[Privilegio],[Gruppo] FROM [Operatori] ORDER BY [Operatore]", _connection);
        using var reader = command.ExecuteReader();
        var result = new List<LegacyOperatorInfo>();
        if (reader is null) return result;
        while (reader.Read())
        {
            var name = ReadString(reader, 0);
            if (name.Length == 0) continue;
            result.Add(new LegacyOperatorInfo(name, ReadInt32(reader, 1), ReadInt32(reader, 2), ReadString(reader, 3)));
        }
        return result;
    }

    public LegacyOperatorSession? Authenticate(string operatorName, string password)
    {
        operatorName = NormalizeName(operatorName);
        password ??= string.Empty;
        using var command = new OleDbCommand(
            "SELECT TOP 1 [Operatore],[Password],[Livello],[Privilegio],[Gruppo] FROM [Operatori] WHERE [Operatore]=?",
            _connection);
        AddText(command, operatorName);
        using var reader = command.ExecuteReader();
        if (reader is null || !reader.Read()) return null;

        // Native code reads Password and compares it in VB6. Exact case/encryption semantics still need a real FFM fixture.
        if (!string.Equals(ReadString(reader, 1), password, StringComparison.Ordinal)) return null;
        return new LegacyOperatorSession(ReadString(reader, 0), ReadInt32(reader, 2), ReadInt32(reader, 3), ReadString(reader, 4));
    }

    public void CreateOperator(string name, string password, int level, int privilege, string? group)
    {
        name = NormalizeName(name);
        if (OperatorExists(name)) throw new InvalidOperationException($"Operator '{name}' already exists.");
        using var command = new OleDbCommand(
            "INSERT INTO [Operatori] ([Operatore],[Password],[Livello],[Privilegio],[Gruppo]) VALUES (?,?,?,?,?)", _connection);
        AddText(command, name);
        AddText(command, password ?? string.Empty);
        AddInt(command, level);
        AddInt(command, privilege);
        AddText(command, group ?? string.Empty);
        command.ExecuteNonQuery();
    }

    public void UpdateOperator(string originalName, string newName, int level, int privilege, string? group, string? newPassword = null)
    {
        originalName = NormalizeName(originalName);
        newName = NormalizeName(newName);
        if (!string.Equals(originalName, newName, StringComparison.OrdinalIgnoreCase) && OperatorExists(newName))
            throw new InvalidOperationException($"Operator '{newName}' already exists.");

        var sql = newPassword is null
            ? "UPDATE [Operatori] SET [Operatore]=?,[Livello]=?,[Privilegio]=?,[Gruppo]=? WHERE [Operatore]=?"
            : "UPDATE [Operatori] SET [Operatore]=?,[Password]=?,[Livello]=?,[Privilegio]=?,[Gruppo]=? WHERE [Operatore]=?";
        using var command = new OleDbCommand(sql, _connection);
        AddText(command, newName);
        if (newPassword is not null) AddText(command, newPassword);
        AddInt(command, level);
        AddInt(command, privilege);
        AddText(command, group ?? string.Empty);
        AddText(command, originalName);
        if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException($"Operator '{originalName}' was not found.");
    }

    public void ChangePassword(string operatorName, string newPassword)
    {
        operatorName = NormalizeName(operatorName);
        using var command = new OleDbCommand("UPDATE [Operatori] SET [Password]=? WHERE [Operatore]=?", _connection);
        AddText(command, newPassword ?? string.Empty);
        AddText(command, operatorName);
        if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException($"Operator '{operatorName}' was not found.");
    }

    public void DeleteOperator(string operatorName)
    {
        using var command = new OleDbCommand("DELETE FROM [Operatori] WHERE [Operatore]=?", _connection);
        AddText(command, NormalizeName(operatorName));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<LegacyPrivilegeInfo> GetPrivileges()
    {
        if (!TableExists("Privilegi")) return Array.Empty<LegacyPrivilegeInfo>();
        var columns = GetColumns("Privilegi");
        if (!columns.Contains("Privilegio") || !columns.Contains("Descrizione")) return Array.Empty<LegacyPrivilegeInfo>();

        using var command = new OleDbCommand("SELECT [Privilegio],[Descrizione] FROM [Privilegi] ORDER BY [Privilegio]", _connection);
        using var reader = command.ExecuteReader();
        var result = new List<LegacyPrivilegeInfo>();
        if (reader is null) return result;
        while (reader.Read()) result.Add(new LegacyPrivilegeInfo(ReadInt32(reader, 0), ReadString(reader, 1)));
        return result;
    }

    public void SavePrivilege(int privilege, string? description)
    {
        EnsureManagedPrivilegeTable();
        using var exists = new OleDbCommand("SELECT COUNT(*) FROM [Privilegi] WHERE [Privilegio]=?", _connection);
        AddInt(exists, privilege);
        var found = Convert.ToInt32(exists.ExecuteScalar() ?? 0) > 0;
        using var command = new OleDbCommand(
            found ? "UPDATE [Privilegi] SET [Descrizione]=? WHERE [Privilegio]=?"
                  : "INSERT INTO [Privilegi] ([Descrizione],[Privilegio]) VALUES (?,?)", _connection);
        AddText(command, description ?? string.Empty);
        AddInt(command, privilege);
        command.ExecuteNonQuery();
    }

    public void DeletePrivilege(int privilege)
    {
        if (!TableExists("Privilegi")) return;
        using var command = new OleDbCommand("DELETE FROM [Privilegi] WHERE [Privilegio]=?", _connection);
        AddInt(command, privilege);
        command.ExecuteNonQuery();
    }

    public void Dispose() => _connection.Dispose();

    private void EnsureExistingSchemaCompatibility()
    {
        if (!TableExists("Operatori"))
            throw new InvalidDataException("This Access/Jet file does not contain the recovered UltraPrint [Operatori] table.");

        var columns = GetColumns("Operatori");
        foreach (var required in new[] { "Operatore", "Password", "Livello" })
            if (!columns.Contains(required))
                throw new InvalidDataException($"The [Operatori] table is missing required legacy column [{required}].");

        // These two ALTER statements were recovered directly from the original native strings.
        if (!columns.Contains("Privilegio")) Execute("ALTER TABLE [Operatori] ADD COLUMN [Privilegio] LONG");
        if (!columns.Contains("Gruppo")) Execute("ALTER TABLE [Operatori] ADD COLUMN [Gruppo] TEXT(50)");
    }

    private void CreateManagedSchema()
    {
        Execute("CREATE TABLE [Operatori] ([Operatore] TEXT(50) NOT NULL, [Password] TEXT(255), [Livello] LONG, [Privilegio] LONG, [Gruppo] TEXT(50))");
        TryExecute("CREATE UNIQUE INDEX [IX_Operatori_Operatore] ON [Operatori] ([Operatore])");
        EnsureManagedPrivilegeTable();
    }

    private void EnsureManagedPrivilegeTable()
    {
        if (TableExists("Privilegi")) return;
        Execute("CREATE TABLE [Privilegi] ([Privilegio] LONG, [Descrizione] TEXT(255))");
        TryExecute("CREATE UNIQUE INDEX [IX_Privilegi_Privilegio] ON [Privilegi] ([Privilegio])");
    }

    private bool OperatorExists(string name)
    {
        using var command = new OleDbCommand("SELECT COUNT(*) FROM [Operatori] WHERE [Operatore]=?", _connection);
        AddText(command, name);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0) > 0;
    }

    private bool TableExists(string tableName)
    {
        var schema = _connection.GetSchema("Tables");
        foreach (DataRow row in schema.Rows)
            if (string.Equals(Convert.ToString(row["TABLE_NAME"]), tableName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private HashSet<string> GetColumns(string tableName)
    {
        var quoted = tableName.Replace("]", "]]", StringComparison.Ordinal);
        using var command = new OleDbCommand($"SELECT TOP 0 * FROM [{quoted}]", _connection);
        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (reader is null) return result;
        for (var i = 0; i < reader.FieldCount; i++) result.Add(reader.GetName(i));
        return result;
    }

    private void Execute(string sql)
    {
        using var command = new OleDbCommand(sql, _connection);
        command.ExecuteNonQuery();
    }

    private void TryExecute(string sql)
    {
        try { Execute(sql); }
        catch { }
    }

    private static string ConnectionString(string provider, string path, bool create)
    {
        var builder = new OleDbConnectionStringBuilder { Provider = provider, DataSource = path };
        builder["Persist Security Info"] = false;
        if (create) builder["Jet OLEDB:Engine Type"] = 5;
        return builder.ConnectionString;
    }

    private static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var value = name.Trim();
        if (value.Length > 50) throw new ArgumentOutOfRangeException(nameof(name), "Operator name is limited to 50 characters.");
        return value;
    }

    private static void AddText(OleDbCommand command, string value) =>
        command.Parameters.Add("@p", OleDbType.VarWChar, Math.Max(1, Math.Min(255, value.Length == 0 ? 1 : value.Length))).Value = value;

    private static void AddInt(OleDbCommand command, int value) =>
        command.Parameters.Add("@p", OleDbType.Integer).Value = value;

    private static string ReadString(OleDbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;

    private static int ReadInt32(OleDbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return 0;
        try { return Convert.ToInt32(reader.GetValue(ordinal)); }
        catch { return 0; }
    }
}
