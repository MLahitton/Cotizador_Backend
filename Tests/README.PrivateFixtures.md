# Private historical PDF fixtures

The following files are intentionally ignored and must not be force-added:

- `Tests/Fixtures/FpPro/S&G1043.PDF`
- `Tests/Fixtures/FpPro/S&G1049.PDF`
- `Tests/Fixtures/FpPro/S&G1085.PDF`
- `Tests/Fixtures/FpPro/S&G648_t2.PDF`

`FpProReportParserTests` and `QuotationWorkbookGeneratorTests` consume these
historical inputs. `CotizadorBackend.Tests.csproj` copies the fixture directory to
test output. The parser helper tries output/repository paths, then opens the file;
absence is a failing prerequisite, not a passing or silently skipped test.

A clean checkout can restore/build without them, but cannot reproduce the cases
that open them. Request the exact approved fixture set from its data custodian
through a private channel, verify its provenance/integrity there, and place it at
the paths above before running those tests. Do not obtain customer documents from
a production upload directory or substitute made-up files to get green results.
No redistribution permission is implied by a historical test name.

Other tracked historical JSON fixtures have real test consumers. Before wider
distribution, the data custodian must confirm authorization/anonymization for
`Fixtures/DocumentProcessing/ai2-real-ventaneria-puertas.json` and the inventory
files under `Fixtures/HistoricalFinishCatalog`, `Fixtures/HistoricalGlassCatalog`
and `Fixtures/HistoricalSystemCatalog`. They contain provenance from historical
documents, including identifiers/path metadata. These are not authentication
secrets, but being tracked is not evidence of distribution permission. Specifically,
confirm the provenance of `requirement.project_id` and `sources[].file_name` in
the extraction fixture, and `corpus_path`, `files[].file_name`, `rows[].file_name`
and `target_row_samples[].file_name` (where present) in the inventories. They can
link data to a source corpus or project even without an email or phone field. Do not
delete them without an approved equivalent replacement and affected test review.
