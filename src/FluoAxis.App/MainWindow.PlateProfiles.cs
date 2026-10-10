namespace FluoAxis.App;

using System.Globalization;
using FluoAxis.Contracts;
using FluoAxis.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

public sealed partial class MainWindow
{
    private async void OnManagePlateProfileClick(object sender, RoutedEventArgs args) => await EditPlateProfileAsync(false);
    private async void OnSaveCurrentPlateProfileClick(object sender, RoutedEventArgs args) => await EditPlateProfileAsync(true);

    private async Task EditPlateProfileAsync(bool fromCurrent)
    {
        if (IsWorkbenchBusy || !workspaceAvailable) return;
        activeOperationTask = EditAsync();
        await activeOperationTask;

        async Task EditAsync()
        {
            var panel = new StackPanel { Spacing = 12, MinWidth = 340 };
            var name = new TextBox { Header = "方案名称", MaxLength = 64, Text = SelectedPlateProfile?.Definition.Name ?? "固定孔位方案" };
            panel.Children.Add(name);
            IReadOnlyList<PlateProfileWellDto> existing = fromCurrent
                ? plateDraft.Rows.Where(row => row.Role is not null).Select(row => new PlateProfileWellDto(row.Well, row.Role!)).ToArray()
                : SelectedPlateProfile?.Definition.Wells ?? [];
            var fields = new List<(string Role, TextBox Input)>();
            foreach ((string role, string label) in new[]
            {
                (WellRoleNames.Positive100, "100% 阳性对照"), (WellRoleNames.Ntc, "NTC"),
                (WellRoleNames.WildType, "WT"), (WellRoleNames.Lqc01Percent, "LQC"),
                (WellRoleNames.Sample, "样本区（例 B2-G11；也可列出 B2,B3）"), (WellRoleNames.Ignore, "忽略孔（可空）"),
            })
            {
                string value = PlateProfileDisplay.FormatWells(existing.Where(well => well.Role == role).Select(well => well.Well));
                var input = new TextBox { Header = label, Text = value, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxLength = 800, MaxHeight = 95 };
                panel.Children.Add(input); fields.Add((role, input));
            }
            var remaining = new CheckBox { Content = "未列出的已测孔也作为样本", IsChecked = SelectedPlateProfile?.Definition.RemainingWellsAreSamples ?? false };
            var makeDefault = new CheckBox { Content = "保存为日常导入的默认方案", IsChecked = true };
            panel.Children.Add(remaining);
            var useOptions = new CheckBox { Content = "文件未标注解析规则时，使用以下明确设置", IsChecked = SelectedPlateProfile?.Definition.Options is not null };
            panel.Children.Add(useOptions);
            ImportOptionsDto initial = SelectedPlateProfile?.Definition.Options ?? ReadImportOptions();
            ComboBox encoding = ProfileChoice("文本编码", ["utf-8", "utf-16", "utf-16BE", "gb18030", "windows-1252"], initial.EncodingName);
            ComboBox delimiter = ProfileChoice("文本分隔符", ["comma", "tab", "semicolon"], initial.Delimiter);
            ComboBox culture = ProfileChoice("小数格式", ["invariant", "zh-CN", "de-DE", "fr-FR"], initial.DecimalCulture);
            ComboBox time = ProfileChoice("数值时间单位", ["minutes", "seconds", "hours", "days"], initial.TimeUnit);
            var parsing = new StackPanel { Spacing = 8, Visibility = useOptions.IsChecked == true ? Visibility.Visible : Visibility.Collapsed };
            parsing.Children.Add(encoding); parsing.Children.Add(delimiter); parsing.Children.Add(culture); parsing.Children.Add(time);
            useOptions.Checked += (_, _) => parsing.Visibility = Visibility.Visible;
            useOptions.Unchecked += (_, _) => parsing.Visibility = Visibility.Collapsed;
            panel.Children.Add(parsing);
            panel.Children.Add(new TextBlock { Text = "方案保存角色和解析规则，不复制旧实验的样本编号。无当前文件样本标记时使用孔号技术ID；重复孔关系可在后续设置中调整。", TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(makeDefault);
            var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = WorkbenchBrush("Danger") };
            panel.Children.Add(validation);
            IReadOnlyList<WellAssignmentDto>? assignments = null;
            ImportOptionsDto? configured = null;
            var dialog = new ContentDialog
            {
                XamlRoot = RootSurface.XamlRoot, Title = "固定孔位方案", PrimaryButtonText = "保存方案", CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary, Content = new ScrollViewer { Content = panel, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            };
            dialog.PrimaryButtonClick += (dialogSender, args) =>
            {
                try
                {
                    var values = new Dictionary<string, WellAssignmentDto>(StringComparer.Ordinal);
                    foreach ((string role, TextBox input) in fields)
                        foreach (string well in ParseProfileWells(input.Text))
                            if (!values.TryAdd(well, new WellAssignmentDto(well, role, role == WellRoleNames.Sample ? well : null, replicateIndex: role == WellRoleNames.Sample ? 1 : null)))
                                throw new ArgumentException($"孔 {well} 被重复分配。请删除重叠角色。");
                    configured = useOptions.IsChecked == true ? new ImportOptionsDto((string)encoding.SelectedItem, (string)delimiter.SelectedItem, (string)culture.SelectedItem, (string)time.SelectedItem) : null;
                    _ = new PlateProfileDefinitionDto(name.Text, values.Values.Select(item => new PlateProfileWellDto(item.Well, item.Role)).ToArray(), remaining.IsChecked == true, configured);
                    assignments = values.Values.ToArray();
                }
                catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
                {
                    args.Cancel = true;
                    validation.Text = "请完整填写四类对照，孔位不得重叠。孔号范围 A1-H12，矩形写作 B2-G11。";
                }
            };
            draftDialogOpen = true; UpdateWorkbenchFromViewModel();
            try
            {
                if (await dialog.ShowAsync() != ContentDialogResult.Primary || assignments is null) return;
                var result = await workspace.SavePlateProfileAsync(name.Text, assignments, remaining.IsChecked == true, makeDefault.IsChecked == true, workbenchLifetime.Token, configured);
                if (!ShowDeliveryFailure(result)) return;
                await RefreshPlateProfilesAsync(workbenchLifetime.Token, result.Value!.ProfileId);
                OperationText.Text = "固定孔位方案已保存，下次可直接导入并自动判读。已有 Run 保持原分配。";
            }
            catch (OperationCanceledException) when (workbenchLifetime.IsCancellationRequested) { }
            catch (Exception) { ShowWorkbenchError("PLATE_PROFILE_SAVE_FAILED", "无法保存孔位方案，请检查填写内容并重试。"); }
            finally { draftDialogOpen = false; UpdateWorkbenchFromViewModel(); }
        }
    }

    private static ComboBox ProfileChoice(string label, string[] values, string selected)
    {
        var choice = new ComboBox { Header = label, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (string value in values) choice.Items.Add(value);
        choice.SelectedItem = selected;
        return choice;
    }

    private static IEnumerable<string> ParseProfileWells(string text)
    {
        foreach (string raw in text.Replace('，', ',').Replace('；', ',').Replace('–', '-').Split([',', ';', ' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] range = raw.ToUpperInvariant().Split('-');
            if (range.Length == 1) { yield return new PlateProfileWellDto(range[0], WellRoleNames.Ignore).Well; continue; }
            if (range.Length != 2) throw new ArgumentException("Invalid range.");
            string first = new PlateProfileWellDto(range[0], WellRoleNames.Ignore).Well;
            string last = new PlateProfileWellDto(range[1], WellRoleNames.Ignore).Well;
            int from = int.Parse(first.AsSpan(1), CultureInfo.InvariantCulture), to = int.Parse(last.AsSpan(1), CultureInfo.InvariantCulture);
            if (first[0] > last[0] || from > to) throw new ArgumentException("Range endpoints are reversed.");
            for (char row = first[0]; row <= last[0]; row++)
                for (int column = from; column <= to; column++) yield return string.Create(CultureInfo.InvariantCulture, $"{row}{column}");
        }
    }
}
