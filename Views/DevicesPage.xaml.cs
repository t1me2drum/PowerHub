using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerHub.Models;
using PowerHub.Services;

namespace PowerHub.Views;

public sealed partial class DevicesPage : Page
{
    public ObservableCollection<Device> Devices { get; } = new();

    private bool _syncing;

    public DevicesPage()
    {
        InitializeComponent();
        Loaded += DevicesPage_Loaded;
        Unloaded += DevicesPage_Unloaded;
    }

    private void DevicesPage_Loaded(object sender, RoutedEventArgs e)
    {
        App.Repository.DevicesChanged += OnDevicesChanged;
        MonitorService.StatusChanged += OnStatusChanged;
        OnStatusChanged(MonitorService.Status);
        LoadDevices();
    }

    private void DevicesPage_Unloaded(object sender, RoutedEventArgs e)
    {
        App.Repository.DevicesChanged -= OnDevicesChanged;
        MonitorService.StatusChanged -= OnStatusChanged;
    }

    /// <summary>
    /// DevicesChanged може прийти з фонового потоку
    /// </summary>
    private void OnDevicesChanged() => DispatcherQueue.TryEnqueue(LoadDevices);

    private void OnStatusChanged(string status)
    {
        StatusText.Text = status;

        // Банер лише коли з'єднання немає (як ConnectionBanner в Android-версії)
        var state = MonitorService.State;
        ConnectionBar.IsOpen = state is ConnectionState.Connecting or ConnectionState.Reconnecting or ConnectionState.Failed;
        ConnectionBar.Severity = state == ConnectionState.Failed ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        ConnectionBar.Message = state switch
        {
            ConnectionState.Connecting => "Підключення до хмари…",
            ConnectionState.Failed => $"{status}. Перевірте логін у налаштуваннях.",
            _ => status
        };
    }

    private void LoadDevices()
    {
        Devices.Clear();
        foreach (var device in App.Repository.ActiveDevices)
        {
            Devices.Add(device);
        }

        var empty = Devices.Count == 0;
        var dashboard = App.Repository.Settings.Layout == HomeLayout.Dashboard;
        EmptyPanel.Visibility = Ui.VisibleIf(empty);
        DevicesList.Visibility = Ui.VisibleIf(!empty && !dashboard);
        TilesGrid.Visibility = Ui.VisibleIf(!empty && dashboard);
        SyncButton.Visibility = Ui.VisibleIf(App.Repository.Credentials?.HasDeveloperKeys == true);
        UpdateTileSize();
    }

    #region Розмір плиток

    private const double TileGap = 12;          // Відступ контейнера плитки (ItemContainerStyle)
    private const double TileBorder = 2;        // Рамка картки
    private const double MinTileScale = 0.6;    // Дрібніше текст уже погано читається — тоді прокрутка

    private void TilesGrid_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTileSize();

    /// <summary>
    /// Підібрати найбільший масштаб (до 100%), за якого всі плитки вміщаються у вікні без прокрутки
    /// </summary>
    private void UpdateTileSize()
    {
        if (TilesGrid.ItemsPanelRoot is not ItemsWrapGrid panel)
            return;

        var width = TilesGrid.ActualWidth - TilesGrid.Padding.Left - TilesGrid.Padding.Right;
        var height = TilesGrid.ActualHeight - TilesGrid.Padding.Top - TilesGrid.Padding.Bottom;
        var count = Math.Max(Devices.Count, 1);
        var tileW = StationTile.TileWidth + TileBorder;
        var tileH = StationTile.TileHeight + TileBorder;

        var scale = MinTileScale;
        if (width > 0 && height > 0)
        {
            for (var s = 1.0; s >= MinTileScale; s -= 0.02)
            {
                var columns = (int)Math.Floor(width / (tileW * s + TileGap));
                if (columns < 1)
                    continue;
                var rows = (int)Math.Ceiling(count / (double)columns);
                if (rows * (tileH * s + TileGap) <= height)
                {
                    scale = s;
                    break;
                }
            }
        }

        panel.ItemWidth = Math.Floor(tileW * scale + TileGap);
        panel.ItemHeight = Math.Floor(tileH * scale + TileGap);
    }

    #endregion

    private void DevicesList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Device device)
            Frame.Navigate(typeof(DeviceDetailsPage), device.SerialNumber);
    }

    /// <summary>
    /// Порядок після перетягування зберігається (і для списку, і для сітки плиток)
    /// </summary>
    private void DevicesList_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => SaveOrder();

    private void SaveOrder() => App.Repository.ReorderDevices(Devices.Select(d => d.SerialNumber).ToList());

    private void ApplyOrder(IEnumerable<Device> ordered)
    {
        var list = ordered.ToList();
        Devices.Clear();
        foreach (var device in list)
            Devices.Add(device);
        SaveOrder();
    }

    // Сортування стабільне: відносний порядок усередині групи зберігається
    private void SortOnline_Click(object sender, RoutedEventArgs e) =>
        ApplyOrder(Devices.OrderByDescending(d => d.IsOnline));

    private void SortName_Click(object sender, RoutedEventArgs e) =>
        ApplyOrder(Devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase));

    private void SortSoc_Click(object sender, RoutedEventArgs e) =>
        ApplyOrder(Devices.OrderByDescending(d => d.BatteryLevel ?? -1));

    private async void SyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        SyncButton.IsEnabled = false;
        SyncProgress.IsActive = true;
        string text;
        try
        {
            text = Ui.DescribeSync(await App.Repository.SyncStationsAsync());
        }
        catch (Exception ex)
        {
            text = $"Не вдалося оновити список: {ex.Message}";
        }
        finally
        {
            _syncing = false;
            SyncButton.IsEnabled = true;
            SyncProgress.IsActive = false;
        }
        await Ui.ShowMessageAsync(XamlRoot, "Список станцій", text);
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var snBox = new TextBox { Header = "Серійний номер", PlaceholderText = "R331ZEB4ZE8M0000", CharacterCasing = CharacterCasing.Upper };
        var nameBox = new TextBox { Header = "Назва (необов'язково)" };
        var models = Enum.GetValues<DeviceModel>().Where(m => m != DeviceModel.Unknown).ToList();
        var modelBox = new ComboBox { Header = "Модель", HorizontalAlignment = HorizontalAlignment.Stretch };
        modelBox.Items.Add("Визначити за серійним номером");
        foreach (var m in models)
            modelBox.Items.Add(m.GetDisplayName());
        modelBox.SelectedIndex = 0;
        var hint = new TextBlock
        {
            Text = "Щоб підтягнути всі станції акаунта автоматично, введіть ключі Developer API в налаштуваннях.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.7
        };

        var dialog = new ContentDialog
        {
            Title = "Нова станція",
            Content = new StackPanel { Spacing = 12, Children = { snBox, nameBox, modelBox, hint } },
            PrimaryButtonText = "Додати",
            CloseButtonText = "Скасувати",
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot
        };
        snBox.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = snBox.Text.Trim().Length >= 8;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        DeviceModel? model = modelBox.SelectedIndex > 0 ? models[modelBox.SelectedIndex - 1] : null;
        var device = App.Repository.AddDevice(snBox.Text, nameBox.Text, model);
        if (device.Model == DeviceModel.Unknown)
        {
            await Ui.ShowMessageAsync(XamlRoot, "Невідома модель",
                "Модель станції не визначено за серійним номером. Видаліть станцію й додайте її знову, вибравши модель зі списку.");
        }
    }

    private async void RenameItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string sn } || App.Repository.GetDevice(sn) is not Device device)
            return;
        if (await Ui.AskNameAsync(XamlRoot, device.Name) is string name)
            App.Repository.RenameDevice(sn, name);
    }

    private async void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string sn } || App.Repository.GetDevice(sn) is not Device device)
            return;
        if (await Ui.ConfirmDeleteAsync(XamlRoot, device))
            App.Repository.RemoveDevice(sn);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Frame.Navigate(typeof(SettingsPage));
    }
}
