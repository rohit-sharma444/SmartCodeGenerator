<#
.SYNOPSIS
    Builds docs/SmartCodeGenerator-User-Manual.docx from docs/user-manual-source.html.

.DESCRIPTION
    The HTML is the source; the .docx is a build artefact that happens to be committed.
    Word converts it over COM, which is what gives real heading styles, a live table of
    contents and page numbers with nothing to install (this machine has neither pandoc
    nor python-docx).

    Edit the HTML, run this, commit both.

    Two silent traps this script exists to encode, both of which have bitten before:

    * TablesOfContents.Add takes UseHyperlinks as its TENTH argument. Omit it and you
      get an index of 59 bookmarks with not one link to them -- it looks perfect and
      clicking an entry does nothing.
    * Add() over an UNCOLLAPSED range REPLACES that range. That is deliberate here (the
      placeholder paragraph is meant to be consumed) but it is why the placeholder is a
      paragraph of its own rather than chapter 1's heading, which it would otherwise eat.

.NOTES
    Verify the result rather than trusting it -- see tools/verify-user-manual.ps1.
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\docs\user-manual-source.html'),
    [string]$Output = (Join-Path $PSScriptRoot '..\docs\SmartCodeGenerator-User-Manual.docx')
)

$ErrorActionPreference = 'Stop'

# Word demands absolute paths and will not tell you why if it does not get them.
$sourcePath = (Resolve-Path $Source).Path
$outputPath = [System.IO.Path]::GetFullPath($Output)

if (-not (Test-Path $sourcePath)) { throw "No such source: $sourcePath" }

# Word constants, spelled out because the enum names are not available over late binding.
$wdPaperA4          = 7
$wdFieldPage        = 33
$wdAlignCenter      = 1
$wdFormatDocumentDefault = 16

Write-Host "Converting $sourcePath"

$word = New-Object -ComObject Word.Application
$word.Visible = $false
$word.DisplayAlerts = 0

try {
    $doc = $word.Documents.Open($sourcePath, [ref]$false, [ref]$false)

    $doc.PageSetup.PaperSize = $wdPaperA4

    # --- The contents page -------------------------------------------------------
    # Inserted here rather than in the HTML because a <ul> of links is not a Word TOC:
    # it would not carry page numbers and would not update.

    $chapterOne = 0
    for ($i = 1; $i -le $doc.Paragraphs.Count; $i++) {
        if ($doc.Paragraphs.Item($i).Range.Text -match '^\s*1\.\s+About this manual') {
            $chapterOne = $i
            break
        }
    }

    if ($chapterOne -eq 0) { throw 'Could not find the chapter 1 heading to insert the contents before.' }

    # Two new paragraphs ahead of chapter 1: the label, and the one the TOC consumes.
    $doc.Paragraphs.Add($doc.Paragraphs.Item($chapterOne).Range) | Out-Null
    $doc.Paragraphs.Add($doc.Paragraphs.Item($chapterOne).Range) | Out-Null

    $label = $doc.Paragraphs.Item($chapterOne)

    # InsertBefore rather than assigning Range.Text: assigning replaces the paragraph
    # mark, and the paragraph then re-inherits the FOLLOWING paragraph's style -- which
    # silently made this label a Heading 2, so the index listed itself.
    $label.Range.InsertBefore('Contents')
    $label.Range.Style = 'Heading 1'
    $label.Format.PageBreakBefore = $true

    # Heading 1 is deliberate: the index covers levels 2-3, so the label cannot list
    # itself, and the cover title is excluded for the same reason.
    $null = $doc.TablesOfContents.Add(
        $doc.Paragraphs.Item($chapterOne + 1).Range,
        $true,   # UseHeadingStyles
        2,       # UpperHeadingLevel  -- chapters
        3,       # LowerHeadingLevel  -- their sections
        $false,  # UseFields
        '',      # TableID
        $true,   # RightAlignPageNumbers
        $true,   # IncludePageNumbers
        '',      # AddedStyles
        $true,   # UseHyperlinks      <- the one that is easy to omit
        $true,   # HidePageNumbersInWeb
        $false)  # UseOutlineLevels

    # --- Page numbers ------------------------------------------------------------

    $footer = $doc.Sections.Item(1).Footers.Item(1).Range
    $null = $footer.Fields.Add($footer, $wdFieldPage)
    $footer.ParagraphFormat.Alignment = $wdAlignCenter

    # --- Save --------------------------------------------------------------------

    $doc.Repaginate()
    $doc.TablesOfContents.Item(1).Update()

    $doc.SaveAs2($outputPath, $wdFormatDocumentDefault)

    $pages = $doc.ComputeStatistics(2)
    Write-Host "Wrote $outputPath ($pages pages)"

    $doc.Close($false)
}
finally {
    $word.Quit()
    [System.Runtime.InteropServices.Marshal]::ReleaseComObject($word) | Out-Null
}
