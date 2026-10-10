namespace FluoAxis.App;

using FluoAxis.Contracts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

public sealed partial class MainWindow
{
    private ImportOptionsDto ReadImportOptions() => new(Selected(ImportEncodingBox), Selected(ImportDelimiterBox), Selected(ImportCultureBox), Selected(ImportTimeUnitBox));
    private static string Selected(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? throw new ArgumentException("Select an explicit import option.");

    private void InitializeImportControls()
    {
        foreach (ComboBox box in new[] { ImportEncodingBox, ImportDelimiterBox, ImportCultureBox, ImportTimeUnitBox })
            box.SelectionChanged += (_, _) => { UpdateImportOptionSummary(); UpdateDraftStatus(); };
        FamSheetBox.TextChanged += OnMappingDraftChanged;
        RoxSheetBox.TextChanged += OnMappingDraftChanged;
        FamEndRowBox.ValueChanged += OnMappingNumberChanged;
        RoxEndRowBox.ValueChanged += OnMappingNumberChanged;
    }

    private void RestoreImportOptions(ImportOptionsDto? options)
    {
        ImportOptionsDto stored = options ?? new ImportOptionsDto();
        foreach ((ComboBox Box, string Value) choice in new[]
        {
            (ImportEncodingBox, stored.EncodingName), (ImportDelimiterBox, stored.Delimiter),
            (ImportCultureBox, stored.DecimalCulture), (ImportTimeUnitBox, stored.TimeUnit),
        }) choice.Box.SelectedItem = choice.Box.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, choice.Value));
        MappingOptionsText.Text = options is null
            ? "旧映射未保存显式解析选项，保留旧编码/分隔符识别行为和分钟单位；只读复核时不更改。"
            : DescribeOptions(stored);
    }

    private void UpdateImportOptionSummary()
    {
        if (!uiInitialized) return;
        MappingOptionsText.Text = DescribeOptions(ReadImportOptions());
    }

    private static string DescribeOptions(ImportOptionsDto options) =>
        $"已选解析规则：编码 {options.EncodingName}；分隔符 {options.Delimiter}；小数文化 {options.DecimalCulture}；时间单位 {options.TimeUnit}。XLS/XLSX 数字单元格使用文件内数值，编码/分隔符只适用于文本。";

    private void OnImportDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = !IsWorkbenchBusy && workspaceAvailable && args.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy : DataPackageOperation.None;
        args.Handled = true;
    }

    private async void OnImportDrop(object sender, DragEventArgs args)
    {
        if (IsWorkbenchBusy || !workspaceAvailable) return;
        args.Handled = true;
        await StartSmartImportAsync(true, args.DataView);
    }

}
