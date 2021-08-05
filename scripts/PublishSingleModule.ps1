
param (
  [Parameter(Mandatory = $true)]
  [string]
  $ModulePath,

  [Parameter(Mandatory = $true)]
  [string]
  $Repository,

  [Parameter(Mandatory = $true)]
  [string]
  $Tag
)

$ErrorActionPreference = 'Stop';

$savedLocation = Get-Location;

$ModuleDir = Get-Item -Path $ModulePath;

$bicep = Join-Path -Path $ModuleDir.Fullname -ChildPath 'main.bicep';
$json = Join-Path -Path $ModuleDir.Fullname -ChildPath 'main.json';

if((Test-Path $bicep) -and (Test-Path $json))
{
  $artifactRef = "majastrzoci.azurecr.io/$($Repository):$($Tag)"
  Write-Output $artifactRef

  Set-Location "$($ModuleDir.Fullname)";
  oras push $artifactRef --manifest-config NUL:application/vnd.unknown.config.v1 ".\main.bicep:application/vnd.ms.bicep" ".\main.json:application/vnd.ms.arm-template+json"
  Set-Location -Path $savedLocation;
}
else
{
  Write-Error "Module directory does not contain a Bicep module.";
}