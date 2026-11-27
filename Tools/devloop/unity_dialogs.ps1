# Lists (and optionally answers) modal dialogs owned by the running Unity Editor, so a remote session is never stuck behind one.
#   powershell -NoProfile -File Tools/devloop/unity_dialogs.ps1                 -> list dialogs, their text and buttons
#   powershell -NoProfile -File Tools/devloop/unity_dialogs.ps1 -Click "Reload" -> press the first button whose name contains "Reload"
param([string]$Click = "")

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$unityProcesses = Get-Process -Name Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 }
if (-not $unityProcesses) { "NO UNITY"; exit 2 }

$root = [System.Windows.Automation.AutomationElement]::RootElement
$found = $false
foreach ($unityProcess in $unityProcesses) {
    $processCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $unityProcess.Id)
    $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition)
    foreach ($window in $windows) {
        $buttonCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
        $buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)
        $isMainWindow = $window.Current.NativeWindowHandle -eq [int]$unityProcess.MainWindowHandle
        if ($isMainWindow -or $buttons.Count -eq 0) { continue }

        $found = $true
        "DIALOG: " + $window.Current.Name
        $textCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
        foreach ($textElement in $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition)) { "  text: " + $textElement.Current.Name }
        foreach ($button in $buttons) { "  button: " + $button.Current.Name }

        if ($Click -ne "") {
            foreach ($button in $buttons) {
                if ($button.Current.Name -like "*$Click*") {
                    $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    $invoke.Invoke()
                    "  CLICKED: " + $button.Current.Name
                    exit 0
                }
            }
            "  NO BUTTON MATCHING: $Click"
            exit 1
        }
    }
}
if (-not $found) { "NO DIALOG" }
exit 0
