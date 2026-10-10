param(
    [Parameter(Mandatory = $true)][string] $PublishDirectory,
    [Parameter(Mandatory = $true)][string] $SourceEntry,
    [Parameter(Mandatory = $true)][string] $EvidenceDirectory
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).Path
function Resolve-Entry([string] $entry) {
    $normal = $entry.Replace('\','/')
    if ([IO.Path]::IsPathRooted($entry) -or $normal.Contains(':') -or @($normal.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count) { throw 'Use relative entries without traversal.' }
    return Join-Path $repository $normal
}
if (-not $EvidenceDirectory.StartsWith('artifacts/desktop-validation/', [StringComparison]::Ordinal)) { throw 'Use the desktop validation artifact root.' }
if (-not $SourceEntry.StartsWith('artifacts/ui-evidence/', [StringComparison]::Ordinal)) { throw 'Use a synthetic UI evidence source.' }
$evidence = Resolve-Entry $EvidenceDirectory
if (Test-Path -LiteralPath $evidence) { throw 'Use a unique evidence directory.' }
$exe = Resolve-Entry ($PublishDirectory.TrimEnd('/') + '/FluoAxis.App.exe')
$source = Resolve-Entry $SourceEntry
New-Item -ItemType Directory -Path $evidence | Out-Null
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DesktopAcceptanceWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
'@
$steps = [Collections.Generic.List[object]]::new()
$owned = [Collections.Generic.List[Diagnostics.Process]]::new()
$phase = 'launch'
$runTimer = [Diagnostics.Stopwatch]::StartNew()
function Condition([string] $id) { return [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id) }
function Wait-For([scriptblock] $read, [string] $failure, [int] $seconds = 25) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $seconds) {
        $value = & $read
        if ($value) { return $value }
        Start-Sleep -Milliseconds 200
    }
    throw $failure
}
function Root-For([Diagnostics.Process] $process) {
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id))
}
function Item([string] $id) { return $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(Condition $id)) }
function Text-Of([string] $id) { $element = Item $id; if ($element) { return $element.Current.Name }; return '' }
function Invoke-Item([string] $id) {
    $element = Wait-For { $item = Item $id; if ($item -and $item.Current.IsEnabled) { $item } } 'Enabled control not available.'
    ([System.Windows.Automation.InvokePattern]$element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 250
}
function Picker {
    return $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ClassNameProperty,'#32770'))
}
function Queue-DialogButton($dialog, [int] $id) {
    $button = Wait-For { $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants,(Condition $id.ToString())) } 'Native dialog button unavailable.' 10
    $handle = $button.Current.NativeWindowHandle
    $queued = if ($handle) { [DesktopAcceptanceWindow]::PostMessage([IntPtr]$handle,0x00F5,[IntPtr]::Zero,[IntPtr]::Zero) }
        else { [DesktopAcceptanceWindow]::PostMessage([IntPtr]$dialog.Current.NativeWindowHandle,0x0111,[IntPtr]$id,[IntPtr]::Zero) }
    if (-not $queued) { throw 'Unable to queue native picker button.' }
}
function Select-File([string] $path) {
    $dialog = Wait-For { Picker } 'Native picker unavailable.'
    $fieldCondition = [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.OrCondition]::new((Condition '1148'),(Condition '1001')),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::Edit))
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $field = Wait-For { $dialog.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$fieldCondition) } 'Native filename edit unavailable.' 10
        if (-not $field) { throw 'Native filename edit unavailable.' }
        try { $field.SetFocus() } catch { }
        $value = [System.Windows.Automation.ValuePattern]$field.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $value.SetValue($path)
        Start-Sleep -Milliseconds 400
        if ($value.Current.Value -ne $path) { throw 'Native filename was not accepted.' }
        # InvokePattern synchronously re-enters PickerHost during an input call.
        # Queue the native button click so the RPC can complete before saving.
        Queue-DialogButton $dialog 1
        Start-Sleep -Milliseconds 600
        $dialog = Picker
        if (-not $dialog) { return }
    }
    throw 'Native picker did not submit the selected filename.'
}
function Expand-Projects {
    $expander = Item 'ProjectToolsExpander'
    ([System.Windows.Automation.ExpandCollapsePattern]$expander.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Start-Sleep -Milliseconds 250
}
function Navigate([string] $name) {
    $navigation = $script:window.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new([System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty,$name),
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)))
    ([System.Windows.Automation.SelectionItemPattern]$navigation.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 250
}
function Start-Instance([string] $workspaceEntry) {
    $process = Start-Process -FilePath $exe -ArgumentList @('--verify-desktop-interaction',$workspaceEntry) -WindowStyle Hidden -PassThru
    $owned.Add($process)
    $script:window = Wait-For { Root-For $process } 'Application window unavailable.'
    [void][DesktopAcceptanceWindow]::ShowWindow([IntPtr]$script:window.Current.NativeWindowHandle,5)
    [void][DesktopAcceptanceWindow]::SetForegroundWindow([IntPtr]$script:window.Current.NativeWindowHandle)
    [void](Wait-For { $button = Item 'SmartImportButton'; $button -and $button.Current.IsEnabled } 'Workbench did not become ready.')
    return $process
}
function Record([string] $name) {
    $steps.Add([ordered]@{step=$name;status='passed';elapsedMs=[Math]::Round($runTimer.Elapsed.TotalMilliseconds,1)})
    Write-Output "Desktop interaction passed: $name"
}
try {
    $process = Start-Instance ($EvidenceDirectory + '/session')
    $phase = 'cancel-picker'
    Invoke-Item 'SmartImportButton'
    $dialog = Wait-For { Picker } 'Native picker unavailable.'
    Queue-DialogButton $dialog 2
    [void](Wait-For { (Text-Of 'OperationText') -match '已取消选择文件' } 'Picker cancellation not reflected.')
    Record $phase
    $phase = 'native-import-analysis'
    Invoke-Item 'SmartImportButton'
    Select-File $source
    [void](Wait-For { (Text-Of 'RunStateText') -match '分析完成.*revision 5' } 'Analysis did not complete.')
    if ((Text-Of 'SmartOutcomeTitle') -ne '自动分析完成') { throw 'Analysis outcome mismatch.' }
    Record $phase
    $phase = 'explorer-save-view'
    Navigate '数据与图形'
    [void](Wait-For { (Text-Of 'StatusText') -match '当前页 3 点' } 'Raw plot did not render.')
    $phase = 'focus-and-restore-plot'
    Invoke-Item 'ExplorerFocusButton'
    if ((Text-Of 'ExplorerFocusButton') -ne '还原布局') { throw 'Plot focus mode did not activate.' }
    Invoke-Item 'ExplorerFocusButton'
    if ((Text-Of 'ExplorerFocusButton') -ne '放大图形') { throw 'Plot layout did not restore.' }
    Record $phase
    $phase = 'explorer-save-view'
    Invoke-Item 'ExplorerSettingsButton'
    Invoke-Item 'SaveExplorerButton'
    [void](Wait-For { (Text-Of 'StatusText') -match '视图定义已保存' } 'Explorer view was not saved.')
    Invoke-Item 'CloseExplorerSettingsButton'
    Record $phase
    foreach ($format in @('Png','Svg','HtmlPlot')) {
        $phase = 'export-plot-' + $format.ToLowerInvariant()
        Invoke-Item 'MoreButton'
        Invoke-Item ('Export' + $format + 'Button')
        $extension = if ($format -eq 'HtmlPlot') { 'html' } else { $format.ToLowerInvariant() }
        $destination = Join-Path $evidence ('plot.' + $extension)
        Select-File $destination
        [void](Wait-For { (Test-Path -LiteralPath $destination) -and (Text-Of 'StatusText') -match '已保存' } 'Plot export did not complete.' 40)
        if ((Get-Item -LiteralPath $destination).Length -le 0) { throw 'Empty plot export.' }
        Record $phase
    }
    $phase = 'save-project-package'
    Navigate '分析工作台'
    Expand-Projects
    Invoke-Item 'SavePackageButton'
    $package = Join-Path $evidence 'interaction.fluoaxisproj'
    Select-File $package
    [void](Wait-For { (Test-Path -LiteralPath $package) -and (Text-Of 'OperationText') -eq '已完成保存内容的输出。' } 'Package save did not complete.')
    Record $phase
    $phase = 'report-snapshot'
    Navigate '报告与导出'
    Invoke-Item 'PreviewReportButton'
    [void](Wait-For { (Text-Of 'ReportPreviewSummary') -match '已读取 revision 5' } 'Report snapshot unavailable.')
    Record $phase
    foreach ($format in @('Html','Xlsx','Pdf')) {
        $phase = 'export-' + $format.ToLowerInvariant()
        Invoke-Item ('Export' + $format + 'Button')
        $destination = Join-Path $evidence ('interaction.' + $format.ToLowerInvariant())
        Select-File $destination
        [void](Wait-For { (Test-Path -LiteralPath $destination) -and (Text-Of 'DeliveryStatusText') -match '技术报告已保存' } 'Report export did not complete.' 40)
        if ((Get-Item -LiteralPath $destination).Length -le 0) { throw 'Empty report export.' }
        Record $phase
    }
    $phase = 'restart-preserves-results'
    ([System.Windows.Automation.WindowPattern]$script:window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    if (-not $process.WaitForExit(15000)) { throw 'Application close did not release process.' }
    $process = Start-Instance ($EvidenceDirectory + '/session')
    [void](Wait-For { (Text-Of 'RunStateText') -match '分析完成.*revision 5' } 'Stored results were not restored.')
    Record $phase
    $phase = 'restore-explorer-view'
    Navigate '数据与图形'
    Invoke-Item 'ExplorerSettingsButton'
    Invoke-Item 'RestoreExplorerButton'
    Invoke-Item 'CloseExplorerSettingsButton'
    [void](Wait-For { (Text-Of 'StatusText') -match '当前页 3 点' } 'Stored Explorer view did not render.')
    Record $phase
    ([System.Windows.Automation.WindowPattern]$script:window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)).Close()
    if (-not $process.WaitForExit(15000)) { throw 'Application close did not release process.' }
    $phase = 'open-portable-project'
    $process = Start-Instance ($EvidenceDirectory + '/portable-session')
    Expand-Projects
    Invoke-Item 'OpenPackageButton'
    Select-File $package
    [void](Wait-For { (Text-Of 'RunStateText') -match '分析完成.*revision 5' } 'Portable result was not restored.')
    Record $phase
    $status = 'passed'
} catch {
    $status = 'failed'
    $steps.Add([ordered]@{step=$phase;status='failed';code='DESKTOP_INTERACTION_INCOMPLETE';exception=$_.Exception.GetType().Name;scriptLine=$_.InvocationInfo.ScriptLineNumber;errorId=$_.FullyQualifiedErrorId.Split(',')[0]})
    Write-Output "Desktop interaction incomplete: $phase"
} finally {
    foreach ($process in $owned) {
        if (-not $process.HasExited) {
            [void]$process.CloseMainWindow()
            if (-not $process.WaitForExit(5000) -and $process.Path -eq $exe) { Stop-Process -Id $process.Id }
        }
    }
    [ordered]@{schemaVersion=1;status=$status;sourceSha256=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant();applicationSha256=(Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant();applicationAssemblySha256=(Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $exe) 'FluoAxis.App.dll')).Hash.ToLowerInvariant();input='synthetic';driver='Windows UI Automation';steps=$steps.ToArray();scope='Native picker and controls; not drag/drop, keyboard, Narrator, High Contrast or clean-machine installation.'} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $evidence 'interaction.json') -Encoding utf8NoBOM
}
if ($status -ne 'passed') { throw 'Desktop acceptance did not pass all selected scenarios.' }
