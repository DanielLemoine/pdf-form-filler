#requires -Version 7.4
# SOURCE ONLY / NOT_RUN. Future call in the SAME root-admitted persistent session.
# Root supplies final exact pins and live native admission; no imports or child shell.
[CmdletBinding()]
param([Parameter(Mandatory)]$ReadOnlyAdmission)
if($ReadOnlyAdmission.workflowModule -isnot [Management.Automation.PSModuleInfo]){throw 'E_GA_WORKFLOW_MODULE_REQUIRED'}
$terminal=@(& $ReadOnlyAdmission.workflowModule {param($c) Invoke-GenericOnboardingWorkflow -ReadOnlyAdmission $c} $ReadOnlyAdmission)
if($terminal.Count -ne 1 -or $terminal[0].schema -cne 'pdfform.generic-onboarding-console/1'){throw 'E_GA_CONSOLE_TERMINAL_SHAPE'}
# In-memory serialization only; no filesystem write after final domain observation.
# Serialization failure never creates a success record or releases live custody.
$json=ConvertTo-Json -InputObject $terminal[0] -Depth 16 -Compress
$bytes=[Text.UTF8Encoding]::new($false,$true).GetBytes($json+"`n")
if($bytes.Length -gt 65536){throw 'E_GA_CONSOLE_SERIALIZATION_CAP'}
return [Text.UTF8Encoding]::new($false,$true).GetString($bytes)
