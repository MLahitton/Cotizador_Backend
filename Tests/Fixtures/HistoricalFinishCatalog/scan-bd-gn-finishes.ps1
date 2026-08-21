param(
    [string]$CorpusPath = 'C:\Users\mlahi\Desktop\Cotizaciones',
    [string]$OutputPath = 'Tests\Fixtures\HistoricalFinishCatalog\bd-gn-finishes-inventory.json'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Normalize([string]$value) {
    if ($null -eq $value) { return '' }
    $text = $value.Trim().ToUpperInvariant().Normalize(
        [Text.NormalizationForm]::FormD)
    $builder = [Text.StringBuilder]::new()
    foreach ($character in $text.ToCharArray()) {
        if ([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) `
            -ne [Globalization.UnicodeCategory]::NonSpacingMark) {
            [void]$builder.Append($character)
        }
    }
    return (($builder.ToString().Normalize([Text.NormalizationForm]::FormC)) `
        -replace '\s+', ' ').Trim()
}

function ColumnIndex([string]$reference) {
    $letters = ([regex]::Match($reference, '^[A-Z]+')).Value
    $value = 0
    foreach ($character in $letters.ToCharArray()) {
        $value = $value * 26 + ([int][char]$character - [int][char]'A' + 1)
    }
    return $value
}

function RowIndex([string]$reference) {
    return [int]([regex]::Match($reference, '\d+').Value)
}

function ColumnName([int]$index) {
    $value = ''
    while ($index -gt 0) {
        $remainder = ($index - 1) % 26
        $value = [char]([int][char]'A' + $remainder) + $value
        $index = [math]::Floor(($index - 1) / 26)
    }
    return $value
}

function CellText($cell, $sharedStrings) {
    if ($null -eq $cell) { return '' }
    $type = $cell.GetAttribute('t')
    $valueNode = $cell.GetElementsByTagName('v') | Select-Object -First 1
    if ($type -eq 'inlineStr') {
        $textNode = $cell.GetElementsByTagName('t') | Select-Object -First 1
        if ($null -ne $textNode) { return $textNode.InnerText }
        return ''
    }
    if ($null -eq $valueNode) { return '' }
    $raw = $valueNode.InnerText
    if ($type -eq 's') {
        $index = 0
        if ([int]::TryParse($raw, [ref]$index) `
            -and $index -ge 0 `
            -and $index -lt $sharedStrings.Count) {
            return $sharedStrings[$index]
        }
    }
    return $raw
}

$files = @(Get-ChildItem -Path $CorpusPath -Filter *.xlsx -File)
$records = @()
$fileSummaries = @()
$headerHits = @()

foreach ($file in $files) {
    $status = 'Unknown'
    $rows = 0
    try {
        $stream = [IO.File]::OpenRead($file.FullName)
        try {
            $signature = New-Object byte[] 8
            [void]$stream.Read($signature, 0, 8)
            $stream.Position = 0
            $isZip = $signature[0] -eq 0x50 -and $signature[1] -eq 0x4B
            $isOle = $signature[0] -eq 0xD0 -and $signature[1] -eq 0xCF `
                -and $signature[2] -eq 0x11 -and $signature[3] -eq 0xE0
            if (-not $isZip) {
                $status = if ($isOle) { 'OleSkipped' } else { 'UnknownSkipped' }
                continue
            }

            $zip = [IO.Compression.ZipArchive]::new(
                $stream,
                [IO.Compression.ZipArchiveMode]::Read)
            try {
                $shared = [Collections.Generic.List[string]]::new()
                $sharedEntry = $zip.GetEntry('xl/sharedStrings.xml')
                if ($null -ne $sharedEntry) {
                    $reader = [IO.StreamReader]::new($sharedEntry.Open())
                    try { [xml]$sharedXml = $reader.ReadToEnd() }
                    finally { $reader.Dispose() }
                    foreach ($item in $sharedXml.GetElementsByTagName('si')) {
                        $shared.Add(($item.GetElementsByTagName('t') |
                            ForEach-Object { $_.InnerText }) -join '')
                    }
                }

                $workbookEntry = $zip.GetEntry('xl/workbook.xml')
                $relsEntry = $zip.GetEntry('xl/_rels/workbook.xml.rels')
                if ($null -eq $workbookEntry -or $null -eq $relsEntry) {
                    $status = 'InvalidOoxml'
                    continue
                }

                $reader = [IO.StreamReader]::new($workbookEntry.Open())
                try { [xml]$workbook = $reader.ReadToEnd() }
                finally { $reader.Dispose() }
                $reader = [IO.StreamReader]::new($relsEntry.Open())
                try { [xml]$rels = $reader.ReadToEnd() }
                finally { $reader.Dispose() }

                $relMap = @{}
                foreach ($rel in $rels.Relationships.Relationship) {
                    $relMap[$rel.Id] = $rel.Target
                }

                $bd = $null
                foreach ($sheet in $workbook.GetElementsByTagName('sheet')) {
                    if ($sheet.GetAttribute('name').Trim().ToUpperInvariant() `
                        -eq 'BD GN') {
                        $bd = $sheet
                        break
                    }
                }
                if ($null -eq $bd) {
                    $status = 'NoBdGn'
                    continue
                }

                $rid = $bd.GetAttribute(
                    'id',
                    'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
                $target = $relMap[$rid]
                $sheetPath = if ($target.StartsWith('/')) {
                    $target.TrimStart('/')
                } else {
                    'xl/' + $target
                }
                $sheetEntry = $zip.GetEntry($sheetPath)
                if ($null -eq $sheetEntry) {
                    $status = 'NoBdGnSheet'
                    continue
                }

                $reader = [IO.StreamReader]::new($sheetEntry.Open())
                try { [xml]$sheetXml = $reader.ReadToEnd() }
                finally { $reader.Dispose() }

                $cells = @{}
                foreach ($cell in $sheetXml.GetElementsByTagName('c')) {
                    $reference = $cell.GetAttribute('r')
                    if ($reference) {
                        $cells[$reference] = CellText $cell $shared
                    }
                }

                $candidateHeaders = @()
                foreach ($entry in $cells.GetEnumerator()) {
                    $normalized = Normalize ([string]$entry.Value)
                    if ($normalized -in @(
                            'ACABADO',
                            'ACABADOS',
                            'ALUMINIO',
                            'COLOR',
                            'COLOR ALUMINIO',
                            'ACABADO ALUMINIO')) {
                        $candidateHeaders += [pscustomobject]@{
                            cell = $entry.Key
                            raw = [string]$entry.Value
                            normalized = $normalized
                        }
                    }
                }

                $finishHeader = $candidateHeaders |
                    Where-Object { $_.normalized -eq 'ACABADO' } |
                    Select-Object -First 1
                if ($null -eq $finishHeader) {
                    $finishHeader = $candidateHeaders | Select-Object -First 1
                }
                if ($null -eq $finishHeader) {
                    $status = 'NoFinishHeader'
                    continue
                }

                $headerHits += [pscustomobject]@{
                    file_name = $file.Name
                    sheet = 'BD GN'
                    cell = $finishHeader.cell
                    raw_header = $finishHeader.raw
                    normalized_header = $finishHeader.normalized
                }

                $column = ColumnIndex $finishHeader.cell
                $headerRow = RowIndex $finishHeader.cell
                $maxRow = 0
                foreach ($reference in $cells.Keys) {
                    $row = RowIndex $reference
                    if ($row -gt $maxRow) { $maxRow = $row }
                }

                for ($row = $headerRow + 1; $row -le $maxRow; $row++) {
                    $cellRef = (ColumnName $column) + $row
                    $text = if ($cells.ContainsKey($cellRef)) {
                        [string]$cells[$cellRef]
                    } else {
                        ''
                    }
                    if (-not [string]::IsNullOrWhiteSpace($text)) {
                        $records += [pscustomobject]@{
                            file_name = $file.Name
                            sheet = 'BD GN'
                            header = $finishHeader.raw
                            row = $row
                            cell = $cellRef
                            raw_value = $text
                            normalized_value = Normalize $text
                        }
                        $rows++
                    }
                }

                $status = 'OoxmlRead'
            }
            finally {
                if ($zip) { $zip.Dispose() }
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        $status = 'Error: ' + $_.Exception.Message
    }
    finally {
        $fileSummaries += [pscustomobject]@{
            file_name = $file.Name
            status = $status
            rows = $rows
        }
    }
}

$groups = @($records | Group-Object normalized_value | Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            normalized_value = $_.Name
            occurrences = $_.Count
            workbooks = @($_.Group |
                Select-Object -ExpandProperty file_name -Unique)
            raw_values = @($_.Group |
                Select-Object -ExpandProperty raw_value -Unique)
        }
    })

$result = [pscustomobject]@{
    generated_at_utc = [DateTime]::UtcNow.ToString('O')
    corpus_path = $CorpusPath
    summary = [pscustomobject]@{
        workbooks_found = $files.Count
        workbooks_ooxml_read = @($fileSummaries |
            Where-Object status -eq 'OoxmlRead').Count
        workbooks_ole_skipped = @($fileSummaries |
            Where-Object status -eq 'OleSkipped').Count
        workbooks_other_skipped = @($fileSummaries |
            Where-Object {
                $_.status -ne 'OoxmlRead' -and $_.status -ne 'OleSkipped'
            }).Count
        rows_read = $records.Count
        distinct_raw_values = @($records |
            Select-Object -ExpandProperty raw_value -Unique).Count
        distinct_normalized_values = $groups.Count
    }
    headers = @($headerHits | Group-Object normalized_header |
        Sort-Object Name |
        ForEach-Object {
            [pscustomobject]@{
                normalized_header = $_.Name
                occurrences = $_.Count
                raw_headers = @($_.Group |
                    Select-Object -ExpandProperty raw_header -Unique)
                cells = @($_.Group |
                    Select-Object -ExpandProperty cell -Unique)
            }
        })
    files = @($fileSummaries)
    groups = @($groups)
    rows = @($records)
}

New-Item -ItemType Directory -Force -Path (Split-Path $OutputPath) | Out-Null
$result | ConvertTo-Json -Depth 20 | Set-Content -Path $OutputPath -Encoding UTF8
$result.summary
