using System.Windows;
using System.Windows.Controls;

namespace v2rayN.Views;

/// <summary>
/// Interaction logic for XrayConnectionsView.xaml
/// </summary>
public partial class XrayConnectionsView
{
    private static readonly string _tag = "XrayConnectionsView";

    public XrayConnectionsView()
    {
        InitializeComponent();

        btnAutofitColumnWidth.Click += BtnAutofitColumnWidth_Click;
        btnExpandAll.Click += (_, _) => ViewModel?.ExpandAll();
        btnCollapseAll.Click += (_, _) => ViewModel?.CollapseAll();
        lstRows.MouseDoubleClick += LstRows_MouseDoubleClick;
        rbTabActive.Checked += (_, _) => ViewModel?.SetClosed(false);
        rbTabClosed.Checked += (_, _) => ViewModel?.SetClosed(true);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Rows, v => v.lstRows.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.GroupKind, v => v.cmbGroupKind.SelectedIndex).DisposeWith(disposables);

            this.Bind(ViewModel, vm => vm.TextFilter, v => v.txtFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.Summary, v => v.txtSummary.Text).DisposeWith(disposables);

            // 程序分类下程序与域名/IP 已合并进同一列，主机列只在其余分组和已关闭页出现
            this.WhenAnyValue(x => x.ViewModel.GroupKind, x => x.ViewModel.ShowClosed)
                .Subscribe(t =>
                {
                    var (kind, closed) = t;
                    colTargetHost.Visibility = closed || kind != 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                })
                .DisposeWith(disposables);
        });
    }

    private void LstRows_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not DependencyObject source)
            {
                return;
            }

            if (ItemsControl.ContainerFromElement(lstRows, source) is not DataGridRow row)
            {
                return;
            }

            ViewModel?.Toggle(row.Item as XrayRow);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }

    private void BtnAutofitColumnWidth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var column in lstRows.Columns)
            {
                column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
    }
}
