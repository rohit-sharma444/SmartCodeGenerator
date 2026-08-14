<#
.SYNOPSIS
    Checks docs/SmartCodeGenerator-User-Manual.docx is what the build was supposed to produce.

.DESCRIPTION
    A .docx is a zip. Word's converter fails quietly -- a dropped section, an index whose
    entries are not links, a heading that lost its style -- and none of it is visible from
    the file size or the page count. So the document is unzipped and its XML inspected.

    Pass -Contains to assert extra strings are present, which is how a newly documented
    feature is proved to have actually landed:

        .\tools\verify-user-manual.ps1 -Contains 'Run it now?','Build FAILED'
#>
[CmdletBinding()]
param(
    [string]$Document = (Join-Path $PSScriptRoot '..\docs\SmartCodeGenerator-User-Manual.docx'),
    [string[]]$Contains = @()
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem

$path = (Resolve-Path $Document).Path
$zip = [System.IO.Compression.ZipFile]::OpenRead($path)

try {
    $entry = $zip.Entries | Where-Object { $_.FullName -eq 'word/document.xml' }

    if (-not $entry) { throw 'No word/document.xml -- this is not a readable .docx.' }

    $reader = New-Object System.IO.StreamReader($entry.Open())
    $xml = $reader.ReadToEnd()
    $reader.Close()
}
finally {
    $zip.Dispose()
}

function Count-Of([string]$pattern) {
    ([regex]::Matches($xml, $pattern)).Count
}

$chapters   = Count-Of 'w:val="Heading2"'
$sections   = Count-Of 'w:val="Heading3"'
$tables     = Count-Of '<w:tbl>'
$hyperlinks = Count-Of '<w:hyperlink'
$bookmarks  = Count-Of '<w:bookmarkStart[^>]*w:name="_Toc'

Write-Host "Document:    $path"
Write-Host "Chapters:    $chapters (Heading 2)"
Write-Host "Sections:    $sections (Heading 3)"
Write-Host "Tables:      $tables"
Write-Host "TOC links:   $hyperlinks against $bookmarks heading bookmarks"

$problems = @()

# The index is the part that looks right when it is broken: bookmarks with no links
# means every entry is dead text.
if ($hyperlinks -eq 0) { $problems += 'The table of contents has no hyperlinks (UseHyperlinks was not passed).' }
if ($hyperlinks -lt $bookmarks) { $problems += "Dangling anchors: $bookmarks bookmarks but only $hyperlinks links." }
if ($xml -notmatch 'TOC [^<]*\\h') { $problems += 'The TOC field is missing its \h (hyperlink) switch.' }
if ($chapters -lt 15) { $problems += "Only $chapters chapter headings; expected at least 15." }
if ($tables -lt 25) { $problems += "Only $tables tables; expected at least 25." }

# Text in the XML is split across runs, so strip the tags before searching for prose.
$text = [regex]::Replace($xml, '<[^>]+>', '')

foreach ($needle in $Contains) {
    if ($text -notlike "*$needle*") { $problems += "Missing from the document: '$needle'" }
    else { Write-Host "  found: $needle" }
}

if ($problems) {
    Write-Host ''
    Write-Host 'PROBLEMS:' -ForegroundColor Red
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host ''
Write-Host 'All checks passed.' -ForegroundColor Green
