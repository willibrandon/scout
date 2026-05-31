# Unicode Character Database

Scout vendors the exact Unicode Character Database archive used to generate the
pinned `regex-syntax` 0.8.8 Unicode tables.

```text
version = "16.0.0"
archive = "UCD-16.0.0.zip"
source_url = "https://www.unicode.org/Public/16.0.0/ucd/UCD.zip"
sha256 = "c86dd81f2b14a43b0cc064aa5f89aa7241386801e35c59c7984e579832634eb2"
generator = "ucd-generate 0.3.1"
```

The checked-in regex table files under
`upstream/regex-syntax-0.8.8/unicode_tables/` record the exact
`ucd-generate` command used for each generated output.
