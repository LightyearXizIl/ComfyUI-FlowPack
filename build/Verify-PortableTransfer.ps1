[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$AppExe,
    [Parameter(Mandatory)][string]$Scope,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{32}$')][string]$RunId,
    [string]$TargetInstanceId,
    [switch]$Install,
    [switch]$SecondaryDetailsOnly
)
# Default: exercise the actual portable UI through export and automatic import preview.
# -Install additionally invokes the UI installation button against a fresh isolated target.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, WindowsBase, System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Text;
public static class PortableWindowCapture {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    delegate bool ChildCallback(IntPtr h,IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent,ChildCallback callback,IntPtr parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int maximum);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder text,int maximum);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    public static void Escape(IntPtr h,int expectedProcess) {
        uint process;GetWindowThreadProcessId(h,out process);if(process!=expectedProcess)throw new InvalidOperationException("Keyboard HWND owner mismatch");
        PostMessage(h,0x100,(IntPtr)0x1B,(IntPtr)1);PostMessage(h,0x101,(IntPtr)0x1B,new IntPtr(unchecked((int)0xC0000001)));
    }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X,Y; }
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr h,ref POINT point);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    public static void ClickText(IntPtr h,int expectedProcess,double screenX,double screenY) {
        uint process;GetWindowThreadProcessId(h,out process);if(process!=expectedProcess)throw new InvalidOperationException("Pointer HWND owner mismatch");
        SetForegroundWindow(h);
        uint foregroundOwner;GetWindowThreadProcessId(GetForegroundWindow(),out foregroundOwner);
        if(foregroundOwner!=expectedProcess)throw new InvalidOperationException("App did not become foreground for pointer acceptance");
        var screenPoint=new POINT {X=(int)screenX,Y=(int)screenY};uint hitOwner;GetWindowThreadProcessId(WindowFromPoint(screenPoint),out hitOwner);
        if(hitOwner!=expectedProcess)throw new InvalidOperationException("Pointer acceptance location is not owned by the App");
        if(!SetCursorPos(screenPoint.X,screenPoint.Y))throw new InvalidOperationException("Pointer movement failed");
        mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);
        return;
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,string text);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,StringBuilder text);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
    public sealed class ChildWindow { public long Handle; public string Name; public string Class; public int Id; }
    public static ChildWindow[] Children(IntPtr parent) {
        var children=new List<ChildWindow>();
        EnumChildWindows(parent,(h,p)=> { var name=new StringBuilder(1024);var cls=new StringBuilder(256);GetWindowText(h,name,1024);GetClassName(h,cls,256);children.Add(new ChildWindow {Handle=h.ToInt64(),Name=name.ToString(),Class=cls.ToString(),Id=GetDlgCtrlID(h)});return true; },IntPtr.Zero);
        return children.ToArray();
    }
    public static void SetDialogFilename(IntPtr edit,string path,int expectedProcess) {
        uint process;GetWindowThreadProcessId(edit,out process);int id=GetDlgCtrlID(edit);if(process!=expectedProcess || (id!=1001 && id!=1148))throw new InvalidOperationException("Filename HWND owner mismatch");
        SendMessage(edit,0x000C,IntPtr.Zero,path);
        var read=new StringBuilder(32768);SendMessage(edit,0x000D,(IntPtr)32768,read);if(read.ToString()!=path)throw new InvalidOperationException("Filename edit value did not persist");
    }
    public static void ClickDialogSubmit(IntPtr button,int expectedProcess) {
        uint process;GetWindowThreadProcessId(button,out process);if(process!=expectedProcess || GetDlgCtrlID(button)!=1)throw new InvalidOperationException("Submit HWND owner mismatch");
        SendMessage(button,0x00F5,IntPtr.Zero,IntPtr.Zero);
    }
    public static void Capture(IntPtr h, string path) {
        RECT r; if(!GetWindowRect(h,out r) || r.Right-r.Left<2 || r.Bottom-r.Top<2) throw new InvalidOperationException("No window rectangle");
        using(var image=new Bitmap(r.Right-r.Left,r.Bottom-r.Top,PixelFormat.Format32bppArgb))
        using(var graphics=Graphics.FromImage(image)) {
            IntPtr dc=graphics.GetHdc(); bool ok;
            try { ok=PrintWindow(h,dc,2); } finally { graphics.ReleaseHdc(dc); }
            if(!ok) throw new InvalidOperationException("PrintWindow failed");
            image.Save(path,ImageFormat.Png);
        }
    }
}
'@ -ReferencedAssemblies @('System.Drawing.Common','System.Drawing.Primitives','System.Runtime.InteropServices','System.Collections',
    (Join-Path $PSHOME 'System.Private.Windows.Core.dll'), (Join-Path $PSHOME 'System.Private.Windows.GdiPlus.dll'))

function Resolve-Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "An absolute path is required: $Path" }
    return [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Path))
}
function Test-Inside([string]$Path, [string]$Root) {
    $resolvedPath = Resolve-Absolute $Path
    $resolvedRoot = Resolve-Absolute $Root
    return $resolvedPath.Equals($resolvedRoot,[StringComparison]::OrdinalIgnoreCase) -or $resolvedPath.StartsWith($resolvedRoot + '\',[StringComparison]::OrdinalIgnoreCase)
}
function Assert-ScopePath([string]$Path) {
    if (-not (Test-Inside $Path $script:FixtureRoot) -and -not (Test-Inside $Path $script:ExternalRoot)) { throw "Path outside this acceptance scope: $Path" }
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Reparse path is not allowed: $cursor" }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -eq $cursor) { break }; $cursor = $parent
    }
}
function Save-Json([string]$Path, $Value) {
    Assert-ScopePath $Path
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 65), [Text.UTF8Encoding]::new($false))
}
function Get-MainWindow {
    $script:PortableApp.Refresh()
    if ($script:PortableApp.HasExited) { throw "Portable App exited: $($script:PortableApp.ExitCode)" }
    if ($script:PortableApp.MainWindowHandle -eq 0) { throw 'Portable App has no main window yet.' }
    return [Windows.Automation.AutomationElement]::FromHandle($script:PortableApp.MainWindowHandle)
}
function Get-Elements($Root, [Windows.Automation.ControlType]$Type, [string]$Name = '', [string]$Id = '') {
    $all = $Root.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)
    $items = [Collections.Generic.List[object]]::new()
    foreach ($element in $all) {
        try {
            $current = $element.Current
            if (($null -eq $Type -or $current.ControlType -eq $Type) -and (!$Name -or $current.Name -eq $Name) -and (!$Id -or $current.AutomationId -eq $Id)) { $items.Add($element) }
        } catch [Windows.Automation.ElementNotAvailableException] { }
    }
    return $items.ToArray()
}
function Get-OwnedElements([Windows.Automation.ControlType]$Type) {
    $condition = [Windows.Automation.AndCondition]::new(
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:PortableApp.Id),
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,$Type))
    # WPF ComboBox popups are sibling HWNDs, outside the main window subtree.
    $seen = [Collections.Generic.HashSet[string]]::new()
    foreach ($element in [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants,$condition)) {
        if ($seen.Add(($element.GetRuntimeId() -join ':'))) { $element }
    }
}
function Wait-Value([scriptblock]$Check, [int]$Seconds = 60, [string]$Label = 'condition') {
    $until = [DateTime]::UtcNow.AddSeconds($Seconds)
    $last = ''
    while ([DateTime]::UtcNow -lt $until) {
        try { $value = & $Check; if ($value) { return $value } } catch { $last = $_.Exception.Message }
        Start-Sleep -Milliseconds 200
    }
    throw "Timed out waiting for $Label. $last"
}
function Find-One([Windows.Automation.ControlType]$Type, [string]$Name = '', [string]$Id = '', $Root = $null, [switch]$Enabled) {
    if ($null -eq $Root) { $Root = Get-MainWindow }
    $matches = @(Get-Elements $Root $Type $Name $Id)
    if ($matches.Count -ne 1) { throw "Expected one UI control Type=$Type Name=$Name Id=$Id; found $($matches.Count)." }
    if ($Enabled -and -not $matches[0].Current.IsEnabled) { throw "UI control is not enabled: $Name $Id" }
    return $matches[0]
}
function Save-UI([string]$Label) {
    $root = Get-MainWindow
    $controls = @(Get-Elements $root $null | ForEach-Object {
        try { @{ name=$_.Current.Name; id=$_.Current.AutomationId; type=$_.Current.ControlType.ProgrammaticName; enabled=$_.Current.IsEnabled; offscreen=$_.Current.IsOffscreen } } catch { }
    })
    Save-Json (Join-Path $script:RunRoot ($Label + '-uia.json')) @{ window=$root.Current.Name; processId=$script:PortableApp.Id; controls=$controls }
    $owned = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:PortableApp.Id))
    Save-Json (Join-Path $script:RunRoot ($Label + '-owned-uia.json')) @($owned | ForEach-Object {
        try { @{name=$_.Current.Name; id=$_.Current.AutomationId; type=$_.Current.ControlType.ProgrammaticName; class=$_.Current.ClassName; offscreen=$_.Current.IsOffscreen} } catch { }
    })
    [PortableWindowCapture]::Capture($script:PortableApp.MainWindowHandle, (Join-Path $script:RunRoot ($Label + '.png')))
}
function Record-Action([string]$Kind, [string]$Name) {
    $script:Actions.Add(@{ utc=[DateTime]::UtcNow.ToString('o'); kind=$Kind; name=$Name })
    Save-Json (Join-Path $script:RunRoot 'actions.json') $script:Actions.ToArray()
}
function Invoke-Button([string]$Name, [string]$Id = '') {
    $element = Wait-Value { Find-One ([Windows.Automation.ControlType]::Button) $Name $Id -Enabled } 90 "enabled button $Name $Id"
    Record-Action 'invoke' $element.Current.Name
    ([Windows.Automation.InvokePattern]$element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
function Select-Instance([string]$Id) {
    if ($Id -notin $script:ScopeData.InstanceIds) { throw 'Selected instance is not allowlisted.' }
    $record = @($script:Registry | Where-Object id -eq $Id)
    if ($record.Count -ne 1) { throw "Instance registry record is ambiguous: $Id" }
    $combo = Wait-Value { Find-One ([Windows.Automation.ControlType]::ComboBox) '选择 Desktop 实例' -Enabled } 90 'instance selector'
    ([Windows.Automation.ExpandCollapsePattern]$combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    Save-UI ('instance-selector-' + $Id)
    $wanted = $record[0].name
    $item = Wait-Value {
        $matches = @(Get-OwnedElements ([Windows.Automation.ControlType]::ListItem) | Where-Object {
            $selectionItem = $null
            ($_.Current.Name -eq $wanted -or $_.Current.Name.StartsWith($wanted + '（') -or
                $_.Current.Name.StartsWith('InstanceDescriptor { Id = ' + $Id + ',')) -and
                $_.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern,[ref]$selectionItem)
        })
        # WPF exposes the same ComboBox data item through both popup and owner peers.
        if ($matches.Count -gt 0 -and @($matches | ForEach-Object { $_.Current.Name } | Select-Object -Unique).Count -eq 1) { $matches[0] }
    } 15 "instance item $wanted"
    Record-Action 'select-instance' $Id
    ([Windows.Automation.SelectionItemPattern]$item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Wait-Value { (Find-One ([Windows.Automation.ControlType]::ComboBox) '选择 Desktop 实例').Current.IsEnabled } 90 'instance scan completion' | Out-Null
    $selected = ([Windows.Automation.SelectionPattern](Find-One ([Windows.Automation.ControlType]::ComboBox) '选择 Desktop 实例').GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern)).Current.GetSelection()
    if ($selected.Count -ne 1 -or -not ($selected[0].Current.Name.StartsWith($wanted) -or $selected[0].Current.Name.StartsWith('InstanceDescriptor { Id = ' + $Id + ','))) { throw 'UI instance selection did not persist.' }
}
function Verify-SecondaryDetails {
    Wait-Value { (Find-One ([Windows.Automation.ControlType]::Text) '' 'Home.StatusText').Current.Name -eq '资源扫描完成' } 15 'completed home scan before layout measurement' | Out-Null
    $progressDismiss = @(Get-Elements (Get-MainWindow) ([Windows.Automation.ControlType]::Button) '收起' | Where-Object { $_.Current.IsEnabled -and !$_.Current.IsOffscreen })
    if ($progressDismiss.Count -eq 1) { Invoke-Button '收起' }
    Start-Sleep -Milliseconds 400
    $homeActions = (Find-One ([Windows.Automation.ControlType]::Button) '' 'Home.Versions').Current.BoundingRectangle
    Invoke-Button '' 'Home.Versions'
    Wait-Value { Find-One $null '' 'Secondary.Panel' } 10 'version panel' | Out-Null
    $windowBounds = (Get-MainWindow).Current.BoundingRectangle
    $dialogBounds = (Find-One $null '' 'Secondary.Panel').Current.BoundingRectangle
    if ([Math]::Abs($dialogBounds.X+$dialogBounds.Width/2-$windowBounds.X-$windowBounds.Width/2) -gt 3) { throw 'Details dialog is not horizontally centered.' }
    if ((Find-One ([Windows.Automation.ControlType]::Button) '资源库').Current.IsEnabled) { throw 'Modal backdrop did not disable background navigation.' }
    Save-UI 'secondary-01-versions'
    if (!(Get-Elements (Get-MainWindow) ([Windows.Automation.ControlType]::Text) 'Desktop 版本')) { throw 'Version fields were not displayed.' }
    $tabs = Find-One ([Windows.Automation.ControlType]::Tab) '' 'Secondary.InstanceTabs'
    $directoryTab = Find-One ([Windows.Automation.ControlType]::TabItem) '资源目录' -Root $tabs
    ([Windows.Automation.SelectionItemPattern]$directoryTab.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Wait-Value { Find-One $null '' 'Secondary.DirectoryScroll' } 10 'directory panel' | Out-Null
    Save-UI 'secondary-02-directories'
    Invoke-Button '' 'Secondary.Close'
    $homeAfter = (Find-One ([Windows.Automation.ControlType]::Button) '' 'Home.Versions').Current.BoundingRectangle
    Save-Json (Join-Path $script:RunRoot 'home-layout.json') @{before=@{x=$homeActions.X;y=$homeActions.Y;width=$homeActions.Width;height=$homeActions.Height};after=@{x=$homeAfter.X;y=$homeAfter.Y;width=$homeAfter.Width;height=$homeAfter.Height}}
    if ($homeAfter -ne $homeActions) { throw 'Closing details changed the home layout.' }
    Invoke-Button '' 'Home.Versions'
    (Find-One ([Windows.Automation.ControlType]::Button) '' 'Secondary.Close').SetFocus()
    [PortableWindowCapture]::Escape($script:PortableApp.MainWindowHandle,$script:PortableApp.Id)
    Wait-Value { @(Get-Elements (Get-MainWindow) $null '' 'Secondary.Panel').Count -eq 0 } 10 'Esc returning to home' | Out-Null
    $focused = [Windows.Automation.AutomationElement]::FocusedElement
    if ($focused.Current.ProcessId -ne $script:PortableApp.Id) { throw 'Keyboard focus escaped the App.' }
    Invoke-Button '打开模型文件夹'
    $menu = Wait-Value {
        $items = @(Get-OwnedElements ([Windows.Automation.ControlType]::MenuItem) | Where-Object { $_.Current.Name -eq '查看全部资源目录' })
        if ($items.Count -eq 1) { $items[0] }
    } 10 'actual dynamic model folder menu'
    Save-UI 'secondary-03-model-menu'
    $popups = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:PortableApp.Id))
    foreach ($popup in $popups) {
        $handle = [IntPtr]$popup.Current.NativeWindowHandle
        if ($handle -ne [IntPtr]::Zero -and $handle -ne $script:PortableApp.MainWindowHandle) {
            [PortableWindowCapture]::Capture($handle,(Join-Path $script:RunRoot ('secondary-03-popup-' + $handle.ToInt64() + '.png')))
        }
    }
    ([Windows.Automation.InvokePattern]$menu.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Wait-Value { Find-One $null '' 'Secondary.DirectoryScroll' } 10 'folder menu to full directory panel' | Out-Null
    Save-UI 'secondary-04-menu-directories'
    $windowBounds = (Get-MainWindow).Current.BoundingRectangle
    [PortableWindowCapture]::ClickText($script:PortableApp.MainWindowHandle,$script:PortableApp.Id,$windowBounds.X+20,$windowBounds.Y+120)
    Wait-Value { @(Get-Elements (Get-MainWindow) $null '' 'Secondary.Panel').Count -eq 0 } 10 'backdrop click closing the centered dialog' | Out-Null
    Invoke-Button '' 'Home.Details'
    Wait-Value { Find-One $null '' 'Secondary.StatusScroll' } 10 'status panel' | Out-Null
    Save-UI 'secondary-05-status'
    Invoke-Button '' 'Secondary.Close'
    Invoke-Button '资源库'
    Wait-Value { @(Get-Elements (Get-MainWindow) $null '' 'Secondary.Panel').Count -eq 0 } 10 'navigation closing secondary layer' | Out-Null
    Save-UI 'secondary-06-navigation'

}
function Expand-ResourceTree {
    for ($step = 0; $step -lt 24; $step++) {
        $expand = $null
        foreach ($item in @(Get-Elements (Get-MainWindow) ([Windows.Automation.ControlType]::TreeItem))) {
            $pattern = $null
            if ($item.TryGetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern,[ref]$pattern) -and $pattern.Current.ExpandCollapseState -eq [Windows.Automation.ExpandCollapseState]::Collapsed) { $expand=$item; break }
        }
        if ($null -eq $expand) { return }
        Record-Action 'expand' $expand.Current.Name
        ([Windows.Automation.ExpandCollapsePattern]$expand.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern)).Expand()
    }
    throw 'Resource tree expansion exceeded its bounded test limit.'
}
function Set-Check([string]$Name, [bool]$Checked) {
    $element = Find-One ([Windows.Automation.ControlType]::CheckBox) $Name -Enabled
    $pattern = [Windows.Automation.TogglePattern]$element.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    $wanted = if ($Checked) { [Windows.Automation.ToggleState]::On } else { [Windows.Automation.ToggleState]::Off }
    if ($pattern.Current.ToggleState -ne $wanted) { Record-Action 'toggle' $Name; $pattern.Toggle() }
    if ($pattern.Current.ToggleState -ne $wanted) { throw "Checkbox did not reach requested state: $Name" }
}
function Submit-FileDialog([string]$Path, [string]$Stage) {
    Assert-ScopePath $Path
    $dialog = Wait-Value {
        $all = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$script:PortableApp.Id))
        $dialogs = @($all | Where-Object { $_.Current.ClassName -eq '#32770' })
        if ($dialogs.Count -eq 1) { $dialogs[0] }
    } 20 'owned native file dialog'
    [PortableWindowCapture]::Capture([IntPtr]$dialog.Current.NativeWindowHandle,(Join-Path $script:RunRoot ($Stage + '-native-dialog.png')))
    Save-Json (Join-Path $script:RunRoot ($Stage + '-native-children.json')) ([PortableWindowCapture]::Children([IntPtr]$dialog.Current.NativeWindowHandle))
    Save-Json (Join-Path $script:RunRoot ($Stage + '-dialog-uia.json')) @(Get-Elements $dialog $null | ForEach-Object {
        @{name=$_.Current.Name;id=$_.Current.AutomationId;type=$_.Current.ControlType.ProgrammaticName}
    })
    $nativeFilename = Wait-Value {
        $edits = @([PortableWindowCapture]::Children([IntPtr]$dialog.Current.NativeWindowHandle) | Where-Object { $_.Class -eq 'Edit' -and $_.Id -in @(1001,1148) })
        if ($edits.Count -eq 1) { $edits[0] }
    } 30 'native filename control after Shell dialog initialization'
    Record-Action 'set-dialog-path' $Path
    $filenameEdit = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$nativeFilename.Handle)
    $valuePattern=$null
    if ($filenameEdit.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern,[ref]$valuePattern)) { $valuePattern.SetValue($Path) }
    else {
        # This Windows Shell provider exposes no ValuePattern for the native Edit.
        # Use the live enumerated HWND, still operating the actual file dialog.
        [PortableWindowCapture]::SetDialogFilename([IntPtr]$nativeFilename.Handle,$Path,$script:PortableApp.Id)
    }
    $nativeSubmit = @([PortableWindowCapture]::Children([IntPtr]$dialog.Current.NativeWindowHandle) | Where-Object { $_.Class -eq 'Button' -and $_.Id -eq 1 })
    if ($nativeSubmit.Count -ne 1) { throw 'Native file dialog submit control is ambiguous.' }
    Record-Action 'submit-file-dialog' $nativeSubmit[0].Name
    $submit = [Windows.Automation.AutomationElement]::FromHandle([IntPtr]$nativeSubmit[0].Handle)
    $invokePattern=$null
    if ($submit.TryGetCurrentPattern([Windows.Automation.InvokePattern]::Pattern,[ref]$invokePattern)) { $invokePattern.Invoke() }
    else { [PortableWindowCapture]::ClickDialogSubmit([IntPtr]$nativeSubmit[0].Handle,$script:PortableApp.Id) }
}
function Invoke-Worker([string]$Command, $Payload = @{}) {
    $sessionFile = Join-Path $script:Library 'state\worker-session.json'
    $session = Get-Content -LiteralPath $sessionFile -Raw | ConvertFrom-Json
    if (-not ([string]$session.DesktopProfile).Equals($script:ScopeData.DesktopProfile,[StringComparison]::OrdinalIgnoreCase)) { throw 'Worker session points to a different Desktop profile.' }
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $session.Pipe,[IO.Pipes.PipeDirection]::InOut)
    $reader = $null; $writer = $null
    try {
        $pipe.Connect(10000)
        $reader = [IO.StreamReader]::new($pipe,[Text.Encoding]::UTF8,$false,8192,$true)
        $writer = [IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),8192,$true); $writer.AutoFlush=$true
        $request = @{ protocolVersion='6'; requestId=[Guid]::NewGuid().ToString('N'); sessionSecret=$session.Secret; command=$Command; payload=$Payload }
        $writer.WriteLine(($request | ConvertTo-Json -Depth 35 -Compress))
        $responseTask = $reader.ReadLineAsync()
        if (-not $responseTask.Wait(15000)) { throw 'Worker response timed out.' }
        $response = $responseTask.Result | ConvertFrom-Json -Depth 65
        if (-not $response.succeeded) { throw "Worker $Command failed: $($response.error.message)" }
        return $response.payload
    } finally { if ($null -ne $writer) {$writer.Dispose()}; if ($null -ne $reader) {$reader.Dispose()}; $pipe.Dispose() }
}
function Get-Jobs { return @(Invoke-Worker 'job.list') }
function Wait-Job([string]$Operation, [string]$InstanceId = '', [int]$Seconds = 180) {
    return Wait-Value {
        # job.list intentionally omits Input/Result; read the selected original job.
        $matches = @(Get-Jobs | Where-Object { $_.Operation -eq $Operation } | Sort-Object CreatedAt -Descending)
        foreach ($summary in $matches) {
            $job = Invoke-Worker 'job.get' $summary.Id
            if ($InstanceId -and $job.Input.Instance.Id -ne $InstanceId) { continue }
            if ($job.State -notin @('Queued','Running','PauseRequested','CancelRequested')) {
                if ($job.State -ne 'Completed') { throw "Job $Operation failed: $($job.Error)" }
                return $job
            }
            break
        }
    } $Seconds "completed $Operation job"
}
function Python-Snapshot([string]$Python) {
    Assert-ScopePath $Python
    $text = & $Python -I -c "import sys,json,importlib.metadata as m,re; print(json.dumps({'prefix':sys.prefix,'packages':{re.sub(r'[-_.]+','-',d.metadata['Name']).lower():d.version for d in m.distributions() if d.metadata['Name']}}))"
    if ($LASTEXITCODE -ne 0) { throw 'Isolated Python snapshot failed.' }
    $snapshot = $text | ConvertFrom-Json -AsHashtable
    $expectedPrefix = [IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($Python))
    if (-not (Resolve-Absolute $snapshot.prefix).Equals((Resolve-Absolute $expectedPrefix),[StringComparison]::OrdinalIgnoreCase)) { throw 'Python environment prefix escaped the isolated target .venv.' }
    return $snapshot
}

$AppExe = Resolve-Absolute $AppExe
if ($Install -and $SecondaryDetailsOnly) { throw 'Secondary UI acceptance never installs resources.' }
$Scope = Resolve-Absolute $Scope
if (-not (Test-Path -LiteralPath $AppExe -PathType Leaf) -or [IO.Path]::GetExtension($AppExe) -ne '.exe') { throw 'AppExe must reference an existing portable App EXE.' }
$workerExe = Join-Path ([IO.Path]::GetDirectoryName($AppExe)) 'worker\ComfyUI.FlowPack.Worker.exe'
if (-not (Test-Path -LiteralPath $workerExe -PathType Leaf)) { throw 'The actual packaged Worker EXE was not found beside this App.' }
$script:ScopeData = Get-Content -LiteralPath $Scope -Raw | ConvertFrom-Json
$script:FixtureRoot = Resolve-Absolute $script:ScopeData.FixtureRoot
$script:ExternalRoot = Resolve-Absolute $script:ScopeData.ExternalDataRoot
$repository = Resolve-Absolute (Join-Path $PSScriptRoot '..')
if (-not (Test-Inside $script:FixtureRoot (Join-Path $repository 'artifacts\acceptance')) -or $script:FixtureRoot -eq (Join-Path $repository 'artifacts\acceptance')) { throw 'FixtureRoot is outside repository acceptance artifacts.' }
if (-not (Test-Inside $script:ExternalRoot (Join-Path $env:LOCALAPPDATA 'ComfyUI FlowPack\Acceptance'))) { throw 'ExternalDataRoot is outside the dedicated acceptance folder.' }
Assert-ScopePath $Scope; Assert-ScopePath $script:ScopeData.DesktopProfile
$script:Registry = Get-Content -LiteralPath (Join-Path $script:ScopeData.DesktopProfile 'installations.json') -Raw | ConvertFrom-Json
if (!$TargetInstanceId) { $TargetInstanceId = $script:ScopeData.NativeTargetInstanceId }
if ($TargetInstanceId -eq $script:ScopeData.NativeTargetInstanceId) { $targetData=$script:ScopeData.NativeData; $targetPython=$script:ScopeData.NativePython }
elseif ($TargetInstanceId -eq $script:ScopeData.AdoptedTargetInstanceId) { $targetData=$script:ScopeData.AdoptedData; $targetPython=$script:ScopeData.AdoptedPython }
else { throw 'Only the native or adopted target from this scope may be installed.' }
Assert-ScopePath $targetData; Assert-ScopePath $targetPython
$script:RunRoot = Join-Path $script:FixtureRoot ('portable-ui-runs\' + $RunId.ToLowerInvariant())
Assert-ScopePath $script:RunRoot
if (Test-Path -LiteralPath $script:RunRoot) { throw 'RunId already exists; existing portable state and evidence are preserved.' }
New-Item -ItemType Directory -Path $script:RunRoot | Out-Null
if ($SecondaryDetailsOnly) {
    # Clone the isolated profile, adding a second real model root for menu acceptance.
    # The existing Desktop profile, source resources and installed targets stay unchanged.
    $originalProfile = $script:ScopeData.DesktopProfile
    $clonedProfile = Join-Path $script:RunRoot 'desktop-profile'
    $secondModels = Join-Path $script:RunRoot 'additional-models'
    New-Item -ItemType Directory -Path $clonedProfile,$secondModels | Out-Null
    foreach ($file in @('installations.json','settings.json')) { Copy-Item -LiteralPath (Join-Path $originalProfile $file) -Destination $clonedProfile }
    $script:Registry = Get-Content -LiteralPath (Join-Path $clonedProfile 'installations.json') -Raw | ConvertFrom-Json
    foreach ($record in $script:Registry) {
        if ($record.id -eq $script:ScopeData.SourceInstanceId) {
            $record | Add-Member -NotePropertyName modelDirs -NotePropertyValue @($secondModels) -Force
            # A missing optional model config produces a real scan hint for status UI.
            $record.launchArgs += ' --extra-model-paths-config "' + (Join-Path $script:RunRoot 'missing-model-paths.yaml') + '"'
        }
    }
    Save-Json (Join-Path $clonedProfile 'installations.json') $script:Registry
    $script:ScopeData.DesktopProfile = $clonedProfile
    Save-Json (Join-Path $script:RunRoot 'secondary-ui-fixture.json') @{originalProfile=$originalProfile;clonedProfile=$clonedProfile;additionalModelRoot=$secondModels;changes='Only cloned source record adds modelDirs and a missing extra config reference for a real scan hint; no resource installation or Desktop launch.'}
}
$workspace = Join-Path $script:RunRoot 'workspace'
$script:Library = Join-Path $workspace 'Data\Library'
$zip = Join-Path $script:RunRoot 'exported-resources.zip'
$script:Actions = [Collections.Generic.List[object]]::new()
$script:PortableApp = $null
$reader = $null; $writer = $null
$completed = $false
try {
    if ($Install) {
        foreach ($freshTarget in @((Join-Path $targetData 'user\default\workflows\upscale.json'), (Join-Path $targetData 'models\upscale_models\RealESRGAN_x2plus.pth'),
            (Join-Path $targetData 'custom_nodes\FlowPackAcceptanceNode\__init__.py'), (Join-Path $targetData 'custom_nodes\FlowPackAcceptanceNode\requirements.txt'))) {
            if (Test-Path -LiteralPath $freshTarget) { throw "Installation acceptance needs a fresh target; this script never resets files: $freshTarget" }
        }
        $pythonBefore = Python-Snapshot $targetPython
        if ($pythonBefore.packages.ContainsKey($script:ScopeData.DependencyName)) { throw 'Target already contains the new test dependency; cannot prove a fresh install.' }
        Save-Json (Join-Path $script:RunRoot 'python-before.json') $pythonBefore
    }
    # Quote absolute paths as separate startup arguments. No user-default binding is consulted.
    $startupArguments = '--portable-root "' + $workspace + '" --desktop-profile "' + $script:ScopeData.DesktopProfile + '" --home'
    $script:PortableApp = Start-Process -FilePath $AppExe -ArgumentList $startupArguments -PassThru -WindowStyle Hidden
    Wait-Value { Get-MainWindow } 45 'actual portable App window' | Out-Null
    Wait-Value { Test-Path -LiteralPath (Join-Path $script:Library 'state\worker-session.json') } 45 'independent portable Worker session' | Out-Null
    $discovery = Wait-Job 'instance.discover'
    $discoveredIds = @($discovery.Result | ForEach-Object Id)
    if ($discoveredIds.Count -ne $script:ScopeData.InstanceIds.Count -or @($discoveredIds | Where-Object { $_ -notin $script:ScopeData.InstanceIds }).Count -ne 0) { throw 'App discovery differs from the scope allowlist.' }
    foreach ($instance in $discovery.Result) {
        if (-not ([string]$instance.ConfigurationRoot).Equals($script:ScopeData.DesktopProfile,[StringComparison]::OrdinalIgnoreCase)) { throw 'Discovery escaped the explicit profile.' }
        foreach ($path in @($instance.InstallRoot,$instance.CoreDirectory,$instance.DataDirectory,$instance.UserDirectory,$instance.WorkflowsDirectory,$instance.CustomNodesDirectory,$instance.PythonPath,$instance.ModelsWriteDirectory,$instance.InputDirectory) + @($instance.ModelRoots) + @($instance.ExtraPaths | ForEach-Object Path)) { Assert-ScopePath $path }
    }
    $workers = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $workerExe -and $_.CommandLine.Contains($script:Library) })
    if ($workers.Count -ne 1) { throw 'Expected exactly one shipped Worker process for the independent portable library.' }
    Save-Json (Join-Path $script:RunRoot 'processes.json') @{ app=@{pid=$script:PortableApp.Id; executable=$AppExe; sha256=(Get-FileHash -LiteralPath $AppExe -Algorithm SHA256).Hash; portableRoot=$workspace}; worker=@{pid=$workers[0].ProcessId;parentPid=$workers[0].ParentProcessId;executable=$workerExe;sha256=(Get-FileHash -LiteralPath $workerExe -Algorithm SHA256).Hash}; desktopProfile=$script:ScopeData.DesktopProfile; shippedQualification=$true }
    Select-Instance $script:ScopeData.SourceInstanceId
    Save-UI '01-source-home'
    if ($SecondaryDetailsOnly) {
        Verify-SecondaryDetails
        Save-Json (Join-Path $script:RunRoot 'worker-jobs.json') (Get-Jobs)
        Save-Json (Join-Path $script:RunRoot 'result.json') @{passed=$true;installed=$false;secondaryDetailsOnly=$true;appExe=$AppExe;versionPanel=$true;directoryTabs=$true;modelMenu=$true;statusPanel=$true;escapeReturned=$true;modalCentered=$true;backgroundNavigationDisabled=$true;backdropClickClosed=$true;testCapabilityInjected=$false}
        $completed=$true
        return
    }
    Invoke-Button '资源库'
    Expand-ResourceTree
    $workflowName = [IO.Path]::GetFileNameWithoutExtension($script:ScopeData.WorkflowPath)
    Set-Check $workflowName $true
    Save-UI '02-selected-workflow'
    Invoke-Button '' 'Library.Export'
    Wait-Value { Find-One ([Windows.Automation.ControlType]::Button) '导出 ZIP…' -Enabled } 90 'automatic export plan' | Out-Null
    $modelName = [IO.Path]::GetFileName($script:ScopeData.ModelPath)
    Set-Check $modelName $false
    Wait-Value { Find-One ([Windows.Automation.ControlType]::Button) '导出 ZIP…' -Enabled } 90 'manual deselection replanning' | Out-Null
    Invoke-Button '返回资源库'; Invoke-Button '' 'Library.Export'
    Wait-Value { Find-One ([Windows.Automation.ControlType]::Button) '导出 ZIP…' -Enabled } 90 'return export plan' | Out-Null
    $modelCheck = Find-One ([Windows.Automation.ControlType]::CheckBox) $modelName
    if (([Windows.Automation.TogglePattern]$modelCheck.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState -ne [Windows.Automation.ToggleState]::Off) { throw 'Manual dependency deselection was restored on return.' }
    Save-UI '03-manual-dependency-cancelled'
    Invoke-Button '重新添加依赖'
    Wait-Value { Find-One ([Windows.Automation.ControlType]::Button) '导出 ZIP…' -Enabled } 90 'dependencies restored by explicit command' | Out-Null
    Save-UI '04-final-export-list'
    Invoke-Button '导出 ZIP…'
    Submit-FileDialog $zip 'save'
    $export = Wait-Job 'export.execute'
    Save-Json (Join-Path $script:RunRoot 'ui-export-job.json') $export
    if (-not (Test-Path -LiteralPath $zip)) { throw 'Actual portable UI did not create the ZIP.' }
    Save-UI '05-export-completed'
    Select-Instance $TargetInstanceId
    Invoke-Button '导入安装'
    Invoke-Button '选择文件'
    Submit-FileDialog $zip 'open'
    $planJob = Wait-Job 'install.plan' $TargetInstanceId
    $plan = $planJob.Result
    if ($plan.Instance.Id -ne $TargetInstanceId -or $null -eq $plan.RequiresPythonDependencies) { throw 'The UI installation plan is stale or references a different target.' }
    foreach ($file in $plan.Files) { if (-not (Test-Inside $file.TargetPath $targetData)) { throw 'Planned target escaped the selected isolated instance.' }; Assert-ScopePath $file.SourcePath }
    Save-Json (Join-Path $script:RunRoot 'automatic-install-plan.json') $plan
    Save-UI '06-automatic-import-preview'
    if ($Install) {
        if ($plan.BlockingReasons.Count -gt 0 -or -not $plan.Capability.CanInstallPython -or -not $plan.RequiresPythonDependencies) { throw 'The real production plan is not qualified for the dependency installation.' }
        Invoke-Button '安装'
        $installed = Wait-Job 'install.execute' $TargetInstanceId 600
        Save-Json (Join-Path $script:RunRoot 'ui-install-job.json') $installed
        Wait-Value { Find-One ([Windows.Automation.ControlType]::ComboBox) '选择 Desktop 实例' -Enabled } 90 'post-install scan' | Out-Null
        Save-UI '07-install-completed'
        $journalPath = Join-Path $script:Library ('state\journal\' + $plan.Id + '.json')
        $journal = Get-Content -LiteralPath $journalPath -Raw | ConvertFrom-Json
        if ($journal.State -ne 'FilesDeployed' -or $journal.Error) { throw 'The real production Worker journal did not complete.' }
        Save-Json (Join-Path $script:RunRoot 'deployment-journal.json') $journal
        $hashes = @($plan.Files | ForEach-Object {
            $actual = (Get-FileHash -LiteralPath $_.TargetPath -Algorithm SHA256).Hash
            $sourceHash = (Get-FileHash -LiteralPath $_.SourcePath -Algorithm SHA256).Hash
            if ($actual -ne $_.Sha256 -or $sourceHash -ne $_.Sha256) { throw "Installed payload hash differs: $($_.TargetPath)" }
            @{source=$_.SourcePath;target=$_.TargetPath;expected=$_.Sha256;sourceSha256=$sourceHash;actual=$actual}
        })
        Save-Json (Join-Path $script:RunRoot 'file-hashes.json') $hashes
        $pythonAfter = Python-Snapshot $targetPython
        foreach ($name in $pythonBefore.packages.Keys) { if (!$pythonAfter.packages.ContainsKey($name) -or $pythonAfter.packages[$name] -ne $pythonBefore.packages[$name]) { throw "Existing Python package changed: $name" } }
        if ($pythonAfter.packages[$script:ScopeData.DependencyName] -ne $script:ScopeData.DependencyVersion) { throw 'Actual installed Python dependency does not match the requested version.' }
        Save-Json (Join-Path $script:RunRoot 'python-after.json') $pythonAfter
    }
    Save-Json (Join-Path $script:RunRoot 'worker-jobs.json') (Get-Jobs)
    Save-Json (Join-Path $script:RunRoot 'result.json') @{ passed=$true; installed=[bool]$Install; target=$TargetInstanceId; appExe=$AppExe; zip=$zip; zipSha256=(Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash; manualDependencyCancellationPreserved=$true; testCapabilityInjected=$false; desktopInferenceVerified=$false }
    $completed=$true
} catch {
    Save-Json (Join-Path $script:RunRoot 'failure.json') @{ error=$_.Exception.ToString(); stack=$_.ScriptStackTrace; installedRequested=[bool]$Install }
    if ($null -ne $script:PortableApp -and -not $script:PortableApp.HasExited) { try { Save-UI 'failure' } catch { } }
    throw
} finally {
    if ($null -ne $script:PortableApp -and -not $script:PortableApp.HasExited) {
        foreach ($modal in @(Get-OwnedElements ([Windows.Automation.ControlType]::Window) | Where-Object { $_.Current.ClassName -eq '#32770' })) {
            try { ([Windows.Automation.WindowPattern]$modal.GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)).Close() } catch { }
        }
        try { ([Windows.Automation.WindowPattern](Get-MainWindow).GetCurrentPattern([Windows.Automation.WindowPattern]::Pattern)).Close() } catch { $script:PortableApp.CloseMainWindow() | Out-Null }
        $script:PortableApp.WaitForExit(10000) | Out-Null
    }
    if (Test-Path -LiteralPath (Join-Path $script:Library 'state\worker-session.json')) {
        try { Wait-Value { Invoke-Worker 'worker.prepare-update' } 30 'this Worker becoming idle' | Out-Null } catch { Write-Warning 'The independently launched Worker did not confirm idle exit; preserve this run for inspection.' }
    }
    $workerExited=$null
    if (Test-Path -LiteralPath (Join-Path $script:RunRoot 'processes.json')) {
        $recordedProcesses=Get-Content -LiteralPath (Join-Path $script:RunRoot 'processes.json') -Raw | ConvertFrom-Json
        $workerProcess=Get-Process -Id $recordedProcesses.worker.pid -ErrorAction SilentlyContinue
        if ($null -ne $workerProcess) { $workerProcess.WaitForExit(10000) | Out-Null; $workerExited=$workerProcess.HasExited } else { $workerExited=$true }
    }
    Save-Json (Join-Path $script:RunRoot 'process-cleanup.json') @{appExited=($null -eq $script:PortableApp -or $script:PortableApp.HasExited);workerExited=$workerExited;verifiedAt=[DateTime]::UtcNow.ToString('o')}
    if ($completed) { Write-Output "EvidenceDirectory=$script:RunRoot"; Write-Output "Installed=$([bool]$Install)" }
}
