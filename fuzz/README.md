# Scout Fuzz Targets

`Scout.Fuzz` is the SharpFuzz harness for the design-required fuzzing layer.

Targets:

- `regex-parse` fuzzes `RegexSyntaxParser.Parse`.
- `glob-compile` fuzzes glob parsing and matching.
- `search-loop` fuzzes the literal line-search loop.

Build the harness with:

```sh
dotnet build fuzz/Scout.Fuzz/Scout.Fuzz.csproj
```

Run a single target under SharpFuzz/AFL by passing the target name as the first
argument to `Scout.Fuzz`.
