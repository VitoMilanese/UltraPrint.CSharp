using UltraPrint.Core.Models;
using UltraPrint.Legacy.Configuration;

namespace UltraPrint.WinForms;

/// <summary>
/// Managed replacement for native frmPrinting. It remains modeless while the
/// synchronous batch print pumps WinForms messages, matching the VB6 Inizia /
/// Annulla cooperative workflow.
/// </summary>
internal sealed class RecordBatchPrintingForm : Form
{
    private readonly RecordBatchPrintJobController _controller;
    private readonly bool _hasChip;
    private readonly string _settingsPath;
    private readonly NumericUpDown _interval = new()
    {
        Minimum = -86400000,
        Maximum = 86400000,
        DecimalPlaces = 0,
        ThousandsSeparator = false,
        Dock = DockStyle.Fill
    };
    private readonly Label _remaining = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        AutoSize = false
    };
    private readonly Button _startCancel = new()
    {
        Text = "Inizia",
        Width = 105,
        Height = 30
    };

    private Control? _disabledOwner;
    private bool _allowClose;
    private bool _saved;

    public RecordBatchPrintingForm(
        RecordBatchPrintJobController controller,
        bool hasChip,
        int initialIntervalSeconds,
        string settingsPath)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _hasChip = hasChip;
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        _interval.Value = Math.Clamp((decimal)initialIntervalSeconds, _interval.Minimum, _interval.Maximum);

        Text = "UltraPrint - Printing";
        Width = 390;
        Height = 215;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var intervalGroup = new GroupBox { Text = "Intervallo tra 2 Carte", Dock = DockStyle.Fill };
        var intervalPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8, 5, 8, 5) };
        intervalPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        intervalPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        intervalPanel.Controls.Add(new Label
        {
            Text = _hasChip ? "Intervallo (secondi):" : "Intervallo (secondi, usato con chip):",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        intervalPanel.Controls.Add(_interval, 1, 0);
        intervalGroup.Controls.Add(intervalPanel);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        commands.Controls.Add(_startCancel);

        root.Controls.Add(intervalGroup, 0, 0);
        root.Controls.Add(_remaining, 0, 1);
        root.Controls.Add(commands, 0, 2);
        Controls.Add(root);

        _startCancel.Click += StartCancel_Click;
        _controller.StateChanged += Controller_StateChanged;
        UpdateState();
    }

    public int IntervalSeconds => decimal.ToInt32(_interval.Value);
    public bool WasShown { get; private set; }

    public bool WaitForStart(IWin32Window owner)
    {
        WasShown = true;
        if (owner is Control control)
        {
            _disabledOwner = control;
            control.Enabled = false;
        }

        Show(owner);
        while (!IsDisposed && Visible && !_controller.HasStarted && !_controller.CancelRequested)
        {
            Application.DoEvents();
            System.Threading.Thread.Sleep(15);
        }

        var started = _controller.HasStarted && !_controller.CancelRequested;
        if (!started) RestoreOwner();
        return started;
    }

    public void Finish()
    {
        if (IsDisposed) return;
        _controller.Complete();
        _allowClose = true;
        RestoreOwner();
        SaveInterval();
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && _controller.IsRunning)
        {
            _controller.RequestCancel();
            _startCancel.Enabled = false;
            e.Cancel = true;
            UpdateState();
            return;
        }

        if (!_controller.HasStarted && !_allowClose)
            _controller.RequestCancel();

        RestoreOwner();
        SaveInterval();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.StateChanged -= Controller_StateChanged;
            RestoreOwner();
        }
        base.Dispose(disposing);
    }

    private void StartCancel_Click(object? sender, EventArgs e)
    {
        if (!_controller.HasStarted)
        {
            if (_hasChip && IntervalSeconds < LegacyBatchPrintSettingsStore.MinimumChipIntervalSeconds)
                _interval.Value = LegacyBatchPrintSettingsStore.MinimumChipIntervalSeconds;

            _interval.Enabled = false;
            _startCancel.Text = "Annulla";
            _controller.Start();
            return;
        }

        if (_controller.CancelRequested) return;
        _controller.RequestCancel();
        _startCancel.Enabled = false;
    }

    private void Controller_StateChanged() => UpdateState();

    private void UpdateState()
    {
        if (IsDisposed) return;

        if (_controller.RemainingIntervalSeconds > 0)
        {
            _remaining.Text = $"Ancora {_controller.RemainingIntervalSeconds} secondi";
            return;
        }

        if (_controller.CancelRequested && _controller.IsRunning)
        {
            _remaining.Text = "Annullamento dopo la carta corrente...";
            return;
        }

        if (_controller.CurrentRecordNumber > 0)
        {
            var side = _controller.CurrentSide == LayoutSide.Back ? "Retro" : "Fronte";
            _remaining.Text = $"Carta {_controller.CurrentRecordNumber} / {_controller.TotalRecordCount} - {side}";
            return;
        }

        _remaining.Text = _hasChip
            ? "Premere Inizia. Per layout con chip l'intervallo minimo e 30 secondi."
            : "Premere Inizia.";
    }

    private void SaveInterval()
    {
        if (_saved) return;
        _saved = true;
        try
        {
            LegacyBatchPrintSettingsStore.SaveIntervalSeconds(_settingsPath, IntervalSeconds);
        }
        catch
        {
            // Native frmPrinting persists on unload without making the print job
            // dependent on the settings write succeeding.
        }
    }

    private void RestoreOwner()
    {
        if (_disabledOwner is null) return;
        if (!_disabledOwner.IsDisposed) _disabledOwner.Enabled = true;
        _disabledOwner = null;
    }
}
