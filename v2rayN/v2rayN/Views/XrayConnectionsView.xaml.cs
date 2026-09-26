using System.Windows.Controls;
using v2rayN.Base;

namespace v2rayN.Views;

/// <summary>
/// Live per-connection view for the Xray core (connstat patch).
/// </summary>
public partial class XrayConnectionsView
{
    private static readonly string _tag = "XrayConnectionsView";

    public XrayConnectionsView()
    {
        InitializeComponent();

        btnAutofitColumnWidth.Click += BtnAutofitColumnWidth_Click;

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.ConnectionItems, v => v.lstConnections.ItemsSource).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.HostFilter, v => v.txtHostFilter.Text).DisposeWith(disposables);
            this.Bind(ViewModel, vm => vm.AutoRefresh, v => v.togAutoRefresh.IsChecked).DisposeWith(disposables);
        });
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
}
