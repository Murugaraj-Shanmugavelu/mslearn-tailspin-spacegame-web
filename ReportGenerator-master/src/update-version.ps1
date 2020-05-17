param(
[string]$From,
[string]$To)

Write-Host $From
Write-Host $To

((Get-Content -path build.proj -Raw) -replace $From, $To) | Set-Content -Path build.proj -NoNewline
((Get-Content -path ..\azure-pipelines.yml -Raw) -replace $From, $To) | Set-Content -Path ..\azure-pipelines.yml -NoNewline
((Get-Content -path ReportGenerator.Console\Properties\AssemblyInfo.cs -Raw) -replace $From, $To) | Out-File ReportGenerator.Console\Properties\AssemblyInfo.cs -Encoding UTF8 -NoNewline
(Get-ChildItem -Recurse -Filter ReportGenerator*.csproj | Select-String $From) | ForEach-Object { ((Get-Content -path $_.Path -Raw) -replace $From, $To) | Out-File $_.Path -Encoding UTF8 -NoNewline }
((Get-Content -path AzureDevopsTask\vss-extension.json -Raw) -replace $From, $To) | Set-Content -Path AzureDevopsTask\vss-extension.json -NoNewline

$FromVersions = $From.Split(".")
$ToVersions = $To.Split(".")

((((Get-Content -path AzureDevopsTask\ReportGenerator\task.json -Raw) -replace ("""Major"": " + $FromVersions[0]), ("""Major"": " + $ToVersions[0])) -replace ("""Minor"": " + $FromVersions[1]), ("""Minor"": " + $ToVersions[1])) -replace ("""Patch"": " + $FromVersions[2]), ("""Patch"": " + $ToVersions[2])) | Set-Content -Path AzureDevopsTask\ReportGenerator\task.json -NoNewline