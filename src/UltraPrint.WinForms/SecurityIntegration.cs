using UltraPrint.Legacy.Security;

namespace UltraPrint.WinForms;

internal static class SecurityIntegration
{
    public static void Attach(MainForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var menu = form.MainMenuStrip;
        if (menu is null) return;
        if (menu.Items.OfType<ToolStripMenuItem>().Any(item => item.Name == "managedSecurityMenu")) return;

        LegacyOperatorStore? store = null;
        LegacyOperatorSession? session = null;

        var security = new ToolStripMenuItem("Security") { Name = "managedSecurityMenu" };
        var loginItem = new ToolStripMenuItem("Login...") { ShortcutKeys = Keys.Control | Keys.L };
        var logoutItem = new ToolStripMenuItem("Logout");
        var changePasswordItem = new ToolStripMenuItem("Change password...");
        var operatorsItem = new ToolStripMenuItem("Operators...");
        var privilegesItem = new ToolStripMenuItem("Privileges...");
        var openDbItem = new ToolStripMenuItem("Open operator database...");
        var createDbItem = new ToolStripMenuItem("Create operator database...");
        var databaseInfoItem = new ToolStripMenuItem("No operator database loaded") { Enabled = false };

        security.DropDownItems.Add(loginItem);
        security.DropDownItems.Add(logoutItem);
        security.DropDownItems.Add(changePasswordItem);
        security.DropDownItems.Add(new ToolStripSeparator());
        security.DropDownItems.Add(operatorsItem);
        security.DropDownItems.Add(privilegesItem);
        security.DropDownItems.Add(new ToolStripSeparator());
        security.DropDownItems.Add(openDbItem);
        security.DropDownItems.Add(createDbItem);
        security.DropDownItems.Add(new ToolStripSeparator());
        security.DropDownItems.Add(databaseInfoItem);

        var helpIndex = menu.Items.OfType<ToolStripItem>()
            .Select((item, index) => new { item, index })
            .FirstOrDefault(x => string.Equals(x.item.Text, "Help", StringComparison.OrdinalIgnoreCase))?.index ?? menu.Items.Count;
        menu.Items.Insert(helpIndex, security);

        ToolStripStatusLabel? operatorStatus = null;
        var statusStrip = FindControl<StatusStrip>(form);
        if (statusStrip is not null)
        {
            var existingStatus = statusStrip.Items.OfType<ToolStripStatusLabel>().FirstOrDefault();
            if (existingStatus is not null) existingStatus.Spring = true;
            operatorStatus = new ToolStripStatusLabel
            {
                Name = "managedOperatorStatus",
                TextAlign = ContentAlignment.MiddleRight
            };
            statusStrip.Items.Add(operatorStatus);
        }

        loginItem.Click += (_, _) => Login();
        logoutItem.Click += (_, _) =>
        {
            session = null;
            UpdateUi();
        };
        changePasswordItem.Click += (_, _) => ChangeCurrentPassword();
        operatorsItem.Click += (_, _) => ManageOperators();
        privilegesItem.Click += (_, _) => ManagePrivileges();
        openDbItem.Click += (_, _) => OpenDatabase();
        createDbItem.Click += (_, _) => CreateDatabase();

        form.Shown += (_, _) =>
        {
            var path = FindDefaultDatabase();
            if (path is not null) TryOpenStore(path, showError: false);
            UpdateUi();
        };
        form.FormClosed += (_, _) => store?.Dispose();

        void UpdateUi()
        {
            var hasStore = store is not null;
            loginItem.Enabled = hasStore && session is null;
            logoutItem.Enabled = session is not null;
            changePasswordItem.Enabled = hasStore && session is not null;
            operatorsItem.Enabled = hasStore;
            privilegesItem.Enabled = hasStore;

            databaseInfoItem.Text = store is null
                ? "No operator database loaded"
                : $"Database: {store.DatabasePath}";

            if (operatorStatus is not null)
            {
                operatorStatus.Text = store is null
                    ? "Operator DB: not loaded"
                    : session is null
                        ? "Operator: <not logged in>"
                        : $"Operator: {session.Name}   Livello={session.Level}   Privilegio={session.Privilege}";
                operatorStatus.ToolTipText = store is null
                    ? string.Empty
                    : session is null
                        ? $"{store.DatabasePath}\r\n{store.ProviderDescription}"
                        : $"Gruppo: {session.Group}\r\n{store.DatabasePath}\r\n{store.ProviderDescription}";
            }
        }

        bool Login()
        {
            if (!EnsureStore()) return false;
            if (store!.OperatorCount() == 0)
            {
                MessageBox.Show(
                    form,
                    "The operator database contains no operators. Create the first operator from Security -> Operators.",
                    "UltraPrint login",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            using var dialog = new LoginForm(store);
            if (dialog.ShowDialog(form) != DialogResult.OK || dialog.Session is null) return false;
            session = dialog.Session;
            UpdateUi();
            return true;
        }

        void ChangeCurrentPassword()
        {
            if (store is null || session is null) return;
            using var dialog = new PasswordDialog(session.Name);
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            try
            {
                store.ChangePassword(session.Name, dialog.PasswordText);
                MessageBox.Show(form, "Password updated.", "UltraPrint password", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, ex.Message, "Change password", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool EnsureManagementAccess()
        {
            if (!EnsureStore()) return false;
            if (store!.OperatorCount() == 0) return true;
            if (session is not null) return true;
            return Login();
        }

        bool EnsureStore()
        {
            if (store is not null) return true;
            var defaultPath = FindDefaultDatabase();
            if (defaultPath is not null && TryOpenStore(defaultPath, showError: true)) return true;

            MessageBox.Show(
                form,
                "No Operatori.FFM is loaded. Use Security -> Open operator database or Create operator database.",
                "UltraPrint security",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        void ManageOperators()
        {
            if (!EnsureManagementAccess()) return;
            using var dialog = new OperatorsForm(store!);
            dialog.ShowDialog(form);

            if (session is not null && !store!.GetOperators().Any(x =>
                    string.Equals(x.Name, session.Name, StringComparison.OrdinalIgnoreCase)))
                session = null;
            UpdateUi();
        }

        void ManagePrivileges()
        {
            if (!EnsureManagementAccess()) return;
            using var dialog = new PrivilegesForm(store!);
            dialog.ShowDialog(form);
            UpdateUi();
        }

        void OpenDatabase()
        {
            using var dialog = new OpenFileDialog
            {
                Filter = "UltraPrint operator database (Operatori.FFM)|*.ffm|Access database (*.mdb;*.accdb)|*.mdb;*.accdb|All files (*.*)|*.*",
                Title = "Open UltraPrint operator database",
                FileName = "Operatori.FFM"
            };
            if (dialog.ShowDialog(form) != DialogResult.OK) return;
            if (TryOpenStore(dialog.FileName, showError: true)) UpdateUi();
        }

        void CreateDatabase()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "UltraPrint operator database (Operatori.FFM)|*.ffm|All files (*.*)|*.*",
                Title = "Create UltraPrint operator database",
                FileName = "Operatori.FFM",
                AddExtension = true,
                DefaultExt = "ffm"
            };
            if (dialog.ShowDialog(form) != DialogResult.OK) return;

            try
            {
                var created = LegacyOperatorStore.Create(dialog.FileName);
                store?.Dispose();
                store = created;
                session = null;
                UpdateUi();

                using var operators = new OperatorsForm(store);
                operators.ShowDialog(form);
                UpdateUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(form, ex.Message, "Create operator database", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        bool TryOpenStore(string path, bool showError)
        {
            try
            {
                var opened = LegacyOperatorStore.Open(path);
                store?.Dispose();
                store = opened;
                session = null;
                UpdateUi();
                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                    MessageBox.Show(form, ex.Message, "Open operator database", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }
    }

    private static string? FindDefaultDatabase()
    {
        var applicationDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var found = LegacyOperatorDatabaseLocator.FindExisting(applicationDirectory);
        if (found is not null) return found;

        var current = Path.GetFullPath(Environment.CurrentDirectory);
        if (!string.Equals(current.TrimEnd(Path.DirectorySeparatorChar),
                applicationDirectory.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
            return LegacyOperatorDatabaseLocator.FindExisting(current);

        return null;
    }

    private static T? FindControl<T>(Control parent) where T : Control
    {
        if (parent is T direct) return direct;
        foreach (Control child in parent.Controls)
        {
            var found = FindControl<T>(child);
            if (found is not null) return found;
        }
        return null;
    }
}
