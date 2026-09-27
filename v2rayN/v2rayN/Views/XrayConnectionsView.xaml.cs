using System.ComponentModel;
using System.Windows.Controls;
using v2rayN.Base;

namespace v2rayN.Views;

/// <summary>
/// Live per-connection view for the Xray core (connstat patch).
/// Column order/width are user adjustable and persisted across restarts.
/// </summary>
public partial class XrayConnectionsView
{
    private static readonly string _tag = "XrayConnectionsView";
    private static Config _config;
    private bool _restoringColumns;

    public XrayConnectionsView()
    {
        InitializeComponent();
        _config = AppManager.Instance.Config;

        btnAutofitColumnWidth.Click += BtnAutofitColumnWidth_Click;
        foreach (var col in lstConnections.Columns)
        {
            // DataGridColumn has no public ColumnDisplayIndexChanged event;
            // watch the dependency properties instead (order + width changes)
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.DisplayIndexProperty, typeof(DataGridColumn))
                .AddValueChanged(col, ColumnPropertyChanged);
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                .AddValueChanged(col, ColumnPropertyChanged);
        }
        Unloaded += (sender, e) => StorageUI();

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ConnectionItems, v => v.lstConnections.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.HostFilter, v => v.txtHostFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.DnsFilter, v => v.cmbDnsFilter.SelectedValue).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);

            AppEvents.AppExitRequested
                .AsObservable()
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(_ => StorageUI())
                .DisposeWith(disposables);
        });

        RestoreUI();
    }

    private void ColumnPropertyChanged(object? sender, EventArgs e)
    {
        if (!_restoringColumns)
        {
            StorageUI();
        }
    }

    private void BtnAutofitColumnWidth_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        try
        {
            foreach (var it in lstConnections.Columns)
            {
                it.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private static string? ColName(DataGridColumn col) => col switch
    {
        MyDGTextColumn t => t.ExName,
        MyDGTemplateColumn m => m.ExName,
        _ => null,
    };

    private void RestoreUI()
    {
        try
        {
            var lvColumnItem = _config.ClashUIItem?.XrayConnectionsColumnItem?.OrderBy(t => t.Index).ToList();
            if (lvColumnItem == null || lvColumnItem.Count == 0)
            {
                return;
            }

            _restoringColumns = true;
            var displayIndex = 0;
            foreach (var item in lvColumnItem)
            {
                foreach (var col in lstConnections.Columns)
                {
                    if (ColName(col) == item.Name)
                    {
                        if (item.Width > 0)
                        {
                            col.Width = item.Width;
                        }
                        col.DisplayIndex = displayIndex++;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            _restoringColumns = false;
        }
    }

    private void StorageUI()
    {
        try
        {
            if (_config.ClashUIItem == null)
            {
                return;
            }

            List<ColumnItem> lvColumnItem = [];
            foreach (var col in lstConnections.Columns)
            {
                var name = ColName(col);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }
                lvColumnItem.Add(new()
                {
                    Name = name,
                    Width = (int)col.ActualWidth,
                    Index = col.DisplayIndex
                });
            }

            if (lvColumnItem.Count <= 0)
            {
                return;
            }

            _config.ClashUIItem.XrayConnectionsColumnItem = lvColumnItem;
            _ = ConfigHandler.SaveConfig(_config);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }
}
