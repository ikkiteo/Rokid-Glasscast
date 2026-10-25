param(
    [Parameter(Mandatory = $true)][string]$Dir
)
$ErrorActionPreference = "Stop"
$Dir = (Resolve-Path $Dir).Path
$dll = Join-Path $Dir "IddSampleDriver.dll"
$inf = Join-Path $Dir "IddSampleDriver.inf"
if (-not (Test-Path $dll)) { throw "нет $dll" }
if (-not (Test-Path $inf)) { throw "нет $inf" }

$subject = "CN=GlassCast Test Driver"
$existing = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $existing) {
    $existing = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject -CertStoreLocation Cert:\LocalMachine\My -HashAlgorithm SHA256 -KeyAlgorithm RSA -KeyLength 2048 -NotAfter (Get-Date).AddYears(5)
}
foreach ($loc in @("Cert:\LocalMachine\Root", "Cert:\LocalMachine\TrustedPublisher")) {
    $already = Get-ChildItem $loc -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $existing.Thumbprint }
    if (-not $already) {
        $cer = Join-Path $Dir "GlassCastTest.cer"
        Export-Certificate -Cert $existing -FilePath $cer | Out-Null
        Import-Certificate -FilePath $cer -CertStoreLocation $loc | Out-Null
    }
}

$kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
if (-not (Test-Path $kits)) { throw "нет Windows SDK: $kits" }
$inf2cat = Get-ChildItem $kits -Recurse -Filter inf2cat.exe | Sort-Object FullName -Descending | Select-Object -First 1
$signtool = Get-ChildItem $kits -Recurse -Filter signtool.exe | Where-Object { $_.FullName -match '\\x64\\signtool.exe$' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $inf2cat) { throw "нет inf2cat.exe (поставь Windows SDK или WDK)" }
if (-not $signtool) { throw "нет signtool.exe" }

$cat = Join-Path $Dir "GlassCastIdd.cat"
if (Test-Path $cat) { Remove-Item $cat -Force }
$made = $false
foreach ($os in @("10_VB_X64", "10_X64", "10_NI_X64")) {
    & $inf2cat.FullName /driver:$Dir /os:$os /uselocaltime
    if ($LASTEXITCODE -eq 0 -and (Test-Path $cat)) { $made = $true; break }
}
if (-not $made) { throw "inf2cat не собрал каталог" }

& $signtool.FullName sign /fd SHA256 /sha1 $existing.Thumbprint /sm /s My $dll
if ($LASTEXITCODE -ne 0) { throw "подпись dll не прошла" }
& $signtool.FullName sign /fd SHA256 /sha1 $existing.Thumbprint /sm /s My $cat
if ($LASTEXITCODE -ne 0) { throw "подпись каталога не прошла" }
Write-Output "signed $cat"
