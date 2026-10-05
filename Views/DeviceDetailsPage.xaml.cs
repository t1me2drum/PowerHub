using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using PowerHub.Models;
using PowerHub.Protocol;
using PowerHub.Services;

namespace PowerHub.Views;

public sealed partial class DeviceDetailsPage : Page
{
    /// <summary>
    /// Слайдер надсилає команду, коли значення не змінювалось стільки часу
    /// (замінює onValueChangeFinished з Android: працює і для миші, і для клавіатури)
    /// </summary>
    private static readonly TimeSpan SliderCommitDelay = TimeSpan.FromMilliseconds(700);

    private const int TabOverview = 0, TabControls = 1, TabHistory = 2, TabRaw = 3;

    private Device? _device;

    /// <summary>
    /// Оновлювачі елементів керування: переносять значення з параметрів станції в UI
    /// </summary>
    private readonly List<Action<DeviceParams>> _controlRefreshers = new();

    /// <summary>
    /// Значення змінюється програмно — обробники подій не мають надсилати команди
    /// </summary>
    private bool _refreshing;

    public string? SerialNumber { get; private set; }

    public DeviceDetailsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string serialNumber)
        {
            SerialNumber = serialNumber;
            LoadDevice();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        if (_device != null)
        {
            _device.PropertyChanged -= Device_PropertyChanged;
        }
        MonitorService.ParamsUpdated -= OnParamsUpdated;
        History.Stop();
    }

    private void LoadDevice()
    {
        if (string.IsNullOrEmpty(SerialNumber))
            return;

        _device = App.Repository.GetDevice(SerialNumber);

        if (_device == null)
        {
            // Пристрій не знайдено
            if (Frame.CanGoBack)
                Frame.GoBack();
            return;
        }

        DeviceModelText.Text = _device.Model.GetDisplayName();
        ModelText.Text = _device.Model.GetDisplayName();
        SerialNumberText.Text = _device.SerialNumber;

        // Станції з акаунта перейменовують в офіційному застосунку, назва звідти підтягується сама
        RenameButton.Visibility = Ui.VisibleIf(_device.CanRename);

        // Телеметрія оновлюється MonitorService в UI-потоці
        _device.PropertyChanged += Device_PropertyChanged;
        UpdateOverview();

        BuildControls(_device);
        MonitorService.ParamsUpdated += OnParamsUpdated;
        OnParamsUpdated();

        // Рідкісні поля приходять лише в повному стані — запитуємо його одразу (як LaunchedEffect в Android)
        _ = MonitorService.RequestQuotaAsync(_device);
    }

    private void Device_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateOverview();

    private void OnParamsUpdated()
    {
        RefreshControls();
        if (Tabs.SelectedIndex == TabRaw)
            UpdateRawData();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_device == null)
            return;

        if (Tabs.SelectedIndex == TabHistory)
            History.Start(_device.SerialNumber);
        else
            History.Stop();

        if (Tabs.SelectedIndex == TabRaw)
            UpdateRawData();
    }

    /// <summary>
    /// Вкладка «Огляд» (як Overview в Android-версії)
    /// </summary>
    private void UpdateOverview()
    {
        if (_device == null)
            return;

        var d = _device;
        var s = d.State;
        var online = d.IsOnline;

        DeviceNameText.Text = d.Name;
        OnlineIndicator.Visibility = Ui.VisibleIf(online);
        OfflineWarningText.Visibility = Ui.VisibleIf(!online);

        Ring.Soc = s.Soc;
        Ring.IsOnline = online;
        Ring.IsCharging = d.IsChargingFromGrid;

        var weak = online && d.GridStatus == GridStatus.Weak;
        WeakGridCard.Visibility = Ui.VisibleIf(weak);
        if (weak)
            WeakGridText.Text = $"⚠ Слабка мережа: {s.AcInVolt} В (поріг {d.WeakGridVolt} В). Станція не заряджається від мережі.";

        FlowText.Visibility = Ui.VisibleIf(online);
        FlowText.Text = Format.FlowText(d.Flow);

        InputText.Text = Format.Watts(s.InputW);
        OutputText.Text = Format.Watts(s.OutputW);
        AcInText.Text = Format.Watts(s.AcInW);
        SolarText.Text = Format.Watts(s.SolarW);
        GridStateText.Text = Format.GridLong(d.GridStatus, s.AcInVolt);
        AcOutText.Text = Format.Watts(s.AcOutW);
        DcOutText.Text = Format.Watts(s.DcOutW);
        UsbOutText.Text = Format.Watts(s.UsbOutW);

        TemperatureText.Text = s.BatteryTempC.HasValue ? $"{s.BatteryTempC} °C" : "—";
        SohText.Text = s.Soh.HasValue ? $"{s.Soh}%" : "—";
        CyclesText.Text = s.Cycles?.ToString() ?? "—";
    }

    /// <summary>
    /// Вкладка «Дані»: усі сирі поля від станції, щоб знайти ті, яких ще немає в інтерфейсі
    /// </summary>
    private void UpdateRawData()
    {
        if (_device == null)
            return;

        var parameters = MonitorService.GetParams(_device.SerialNumber);
        RawHeaderText.Text = $"Сирі значення від станції ({parameters.Count}). Корисно, щоб знайти поля, яких ще немає в інтерфейсі.";

        var width = parameters.Count == 0 ? 0 : parameters.Keys.Max(k => k.Length);
        var text = new StringBuilder();
        foreach (var (key, value) in parameters.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var shown = value switch
            {
                null => "null",
                string str => str,
                System.Collections.IEnumerable list => "[" + string.Join(", ", list.Cast<object?>()) + "]",
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            };
            if (shown.Length > 40)
                shown = shown[..40] + "…";
            text.Append(key.PadRight(width + 2)).Append(shown).Append('\n');
        }
        RawDataText.Text = text.ToString();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
    }

    private async void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (_device == null) return;
        if (await Ui.AskNameAsync(XamlRoot, _device.Name) is string name)
            App.Repository.RenameDevice(_device.SerialNumber, name);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_device != null)
            Report(MonitorService.RequestQuotaAsync(_device));
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_device == null) return;
        if (!await Ui.ConfirmDeleteAsync(XamlRoot, _device))
            return;

        var sn = _device.SerialNumber;
        if (Frame.CanGoBack)
            Frame.GoBack();
        App.Repository.RemoveDevice(sn);
    }

    #region Керування

    private static string SectionTitle(ControlSection section) => section switch
    {
        ControlSection.Outputs => "Виходи",
        ControlSection.Charging => "Заряджання",
        ControlSection.Backup => "Резерв",
        ControlSection.System => "Система",
        _ => section.ToString()
    };

    /// <summary>
    /// Картка на кожну секцію, як Controls() в Android-версії
    /// </summary>
    private void BuildControls(Device device)
    {
        ControlsPanel.Children.Clear();
        _controlRefreshers.Clear();

        var controls = Protocols.For(device.Model).GetControls(device.SerialNumber);
        foreach (var group in controls.GroupBy(c => c.Section))
        {
            var rows = new StackPanel { Spacing = 12 };
            rows.Children.Add(new TextBlock
            {
                Text = SectionTitle(group.Key),
                Style = (Style)Resources["CardTitleStyle"]
            });

            foreach (var control in group)
            {
                FrameworkElement? row = control switch
                {
                    ToggleControl t => ToggleRow(device, t),
                    SliderControl s => SliderRow(device, s),
                    ChoiceControl c => ChoiceRow(device, c),
                    _ => null
                };
                if (row != null)
                    rows.Children.Add(row);
            }

            // Стиль зі сторінки: ThemeResource у ньому стежить за темою сторінки
            ControlsPanel.Children.Add(new Border { Style = (Style)Resources["CardStyle"], Child = rows });
        }
    }

    private void RefreshControls()
    {
        if (_device == null)
            return;

        var parameters = MonitorService.GetParams(_device.SerialNumber);
        _refreshing = true;
        try
        {
            foreach (var refresh in _controlRefreshers)
            {
                refresh(parameters);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Показати помилку і повернути елементи керування до фактичного стану
    /// </summary>
    private async void Report(Task<bool> send)
    {
        bool ok;
        try
        {
            ok = await send;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DeviceDetailsPage: command failed - {ex.Message}");
            ok = false;
        }

        CommandErrorBar.IsOpen = !ok;
        if (!ok)
            RefreshControls();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap
    };

    private static TextBlock ValueText() => new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Right,
        Opacity = 0.7
    };

    private FrameworkElement ToggleRow(Device device, ToggleControl control)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = Label(control.Label);
        var unknown = ValueText();
        unknown.Text = "—";
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", MinWidth = 0 };

        Grid.SetColumn(unknown, 1);
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(label);
        grid.Children.Add(unknown);
        grid.Children.Add(toggle);

        toggle.Toggled += (_, _) =>
        {
            if (!_refreshing)
                Report(MonitorService.ToggleAsync(device, control, toggle.IsOn));
        };

        _controlRefreshers.Add(p =>
        {
            var value = control.Read(p);
            unknown.Visibility = value.HasValue ? Visibility.Collapsed : Visibility.Visible;
            toggle.IsOn = value == true;
        });

        return grid;
    }

    private FrameworkElement SliderRow(Device device, SliderControl control)
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var valueText = ValueText();
        Grid.SetColumn(valueText, 1);
        header.Children.Add(Label(control.Label));
        header.Children.Add(valueText);

        var slider = new Slider
        {
            Minimum = control.Min,
            Maximum = control.Max,
            StepFrequency = control.Step,
            SnapsTo = SliderSnapsTo.StepValues,
            TickFrequency = control.Step,
            TickPlacement = TickPlacement.None,
            IsThumbToolTipEnabled = true
        };

        // Поки користувач рухає повзунок, значення від станції його не перебивають
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = SliderCommitDelay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            var snapped = SnapToStep(slider.Value, control);
            Report(MonitorService.SetValueAsync(device, control, snapped));
        };

        slider.ValueChanged += (_, e) =>
        {
            if (_refreshing)
                return;
            valueText.Text = $"{SnapToStep(e.NewValue, control)} {control.Unit}";
            timer.Stop();
            timer.Start();
        };

        _controlRefreshers.Add(p =>
        {
            if (timer.IsRunning)
                return;
            var value = control.Read(p);
            valueText.Text = value.HasValue ? $"{value} {control.Unit}" : "—";
            slider.Value = Math.Clamp(value ?? control.Min, control.Min, control.Max);
        });

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(header);
        panel.Children.Add(slider);
        return panel;
    }

    private static int SnapToStep(double value, SliderControl control)
    {
        var step = Math.Max(control.Step, 1);
        var snapped = control.Min + (int)Math.Round((value - control.Min) / step) * step;
        return Math.Clamp(snapped, control.Min, control.Max);
    }

    private FrameworkElement ChoiceRow(Device device, ChoiceControl control)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var combo = new ComboBox { MinWidth = 140, PlaceholderText = "—" };
        foreach (var (label, _) in control.Options)
        {
            combo.Items.Add(label);
        }
        Grid.SetColumn(combo, 1);
        grid.Children.Add(Label(control.Label));
        grid.Children.Add(combo);

        combo.SelectionChanged += (_, _) =>
        {
            if (_refreshing || combo.SelectedIndex < 0)
                return;
            Report(MonitorService.ChooseAsync(device, control, control.Options[combo.SelectedIndex].Value));
        };

        _controlRefreshers.Add(p =>
        {
            var value = control.Read(p);
            combo.SelectedIndex = value.HasValue ? control.Options.FindIndex(o => o.Value == value.Value) : -1;
        });

        return grid;
    }

    #endregion
}
