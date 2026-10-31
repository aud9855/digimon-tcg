# 디지몬 카드 게임 공식 영어 카드 리스트(world.digimoncard.com/cards)에서 진화 조건을 받아 온다.
# 개인 로컬 사용 전용 — 받은 파일은 data/en/ (git 제외)에만 저장한다. 카드 문구는 코드나 커밋에 옮기지 않는다.
#
#   .\collect_en.ps1             # 받은 적 없는 카테고리 페이지만 받고, data/en/card_rules.json 을 다시 만든다
#   .\collect_en.ps1 -Refresh    # 모든 카테고리 페이지를 다시 받는다
#
# 영어 API 데이터에는 진화 조건이 하나(레벨 · 색 1개)뿐이라, 여러 색 · 두 번째 진화 조건과
# 한글 데이터가 없는 카드의 [Special Digivolution Condition]을 이 파일에서 읽는다 (엔진: Data.EnglishCardRules).
# 요청은 한 번에 하나씩, 요청 사이 -DelayMs(기본 1500ms)만큼 쉰다.
param(
  [string]$DataDir = (Join-Path $PSScriptRoot "..\..\data"),
  [int]$DelayMs = 1500,
  [switch]$Refresh
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.Web
$Base = "https://world.digimoncard.com/cards/"
$DataDir = [System.IO.Path]::GetFullPath($DataDir)
$EnDir = Join-Path $DataDir "en"
$PageDir = Join-Path $EnDir "pages"
New-Item -ItemType Directory -Force -Path $PageDir | Out-Null
$http = New-Object System.Net.Http.HttpClient
$http.Timeout = [TimeSpan]::FromSeconds(120)

function Get-Page([string]$url) {
  Start-Sleep -Milliseconds $DelayMs
  return $http.GetStringAsync($url).GetAwaiter().GetResult()
}

# 1. 카테고리 목록 (카드 리스트 첫 페이지의 category= 링크)
$first = Get-Page ($Base + "?search=true&category=522001")
$cats = [regex]::Matches($first, 'category=(52\d{4})') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
Write-Host "카테고리 $($cats.Count)개"
foreach ($c in $cats) {
  $file = Join-Path $PageDir "c$c.html"
  if ((Test-Path $file) -and -not $Refresh) { continue }
  Write-Host "  받기 $c"
  $html = if ($c -eq "522001") { $first } else { Get-Page ($Base + "?search=true&category=$c") }
  [System.IO.File]::WriteAllText($file, $html, [System.Text.Encoding]::UTF8)
}

# 2. 카드마다 진화 조건 (Digivolve Cost N) · [Special Digivolution Condition] 의 [Digivolve] 줄
function Clean([string]$s) {
  $s = [regex]::Replace($s, '<br\s*/?>', "`n")
  $s = [regex]::Replace($s, '<[^>]+>', ' ')
  $s = [System.Web.HttpUtility]::HtmlDecode($s)
  return (($s -split "`n") | ForEach-Object { ($_ -replace '\s+', ' ').Trim() } | Where-Object { $_ }) -join "`n"
}
$cards = [ordered]@{}
foreach ($f in (Get-ChildItem $PageDir -Filter "c*.html" | Sort-Object Name)) {
  $html = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
  foreach ($part in ($html -split '<li class="image_lists_item') | Select-Object -Skip 1) {
    $idm = [regex]::Match($part, 'data-src="#([^"]+)"')
    if (-not $idm.Success) { continue }
    $id = ($idm.Groups[1].Value -replace '_P\d+$', '').Trim()
    if ($cards.Contains($id)) { continue }   # 먼저 나온 카테고리(부스터 · 스타터)가 기준 — 프로모 재수록 페이지는 진화 조건 칸이 빠진 것이 있다
    $evo = @()
    foreach ($m in [regex]::Matches($part, 'cardInfoTit">Digivolve Cost (\d)</dt>\s*<dd[^>]*>([\s\S]*?)</dd>')) {
      $colors = @([regex]::Matches($m.Groups[2].Value, "cardColor_([a-z]+)") | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ })
      $t = Clean $m.Groups[2].Value
      $cm = [regex]::Match($t, '(\d+)\s+from\s+(.+)$')
      if (-not $cm.Success) { continue }
      $from = $cm.Groups[2].Value.Trim()
      $lv = 0
      $lm = [regex]::Match($from, '^Lv\.(\d+)$')
      if ($lm.Success) { $lv = [int]$lm.Groups[1].Value }
      $evo += [ordered]@{ Colors = $colors; Cost = [int]$cm.Groups[1].Value; Level = $lv; From = $from }
    }
    $special = @()
    foreach ($m in [regex]::Matches($part, 'cardInfoTitSmall">\[Special Digivolution Condition\]</dt>\s*<dd[^>]*>([\s\S]*?)</dd>')) {
      foreach ($line in ((Clean $m.Groups[1].Value) -split "`n")) { if ($line.StartsWith("[Digivolve]")) { $special += $line } }
    }
    $cards[$id] = [ordered]@{ CardId = $id; Evo = $evo; SpecialDigivolve = $special }
  }
}
$out = Join-Path $EnDir "card_rules.json"
ConvertTo-Json @($cards.Values) -Depth 5 | Set-Content -Path $out -Encoding utf8
Write-Host "카드 $($cards.Count)장 → $out"
