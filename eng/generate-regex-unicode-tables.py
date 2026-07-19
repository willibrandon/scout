#!/usr/bin/env python3
import pathlib
import re
import sys


ROOT = pathlib.Path(__file__).resolve().parents[1]
TABLE_ROOT = ROOT / "upstream" / "regex-syntax-0.8.8" / "unicode_tables"


TABLES = [
    ("DecimalNumber", "perl_decimal.rs", "DECIMAL_NUMBER"),
    ("PerlWord", "perl_word.rs", "PERL_WORD"),
    ("PerlSpace", "perl_space.rs", "WHITE_SPACE"),
    ("Alphabetic", "property_bool.rs", "ALPHABETIC"),
]

BOOLEAN_PROPERTIES = [
    ("Math", "MATH"),
    ("Emoji", "EMOJI"),
    ("ExtendedPictographic", "EXTENDED_PICTOGRAPHIC"),
    ("Lowercase", "LOWERCASE"),
    ("Uppercase", "UPPERCASE"),
]

BREAK_PROPERTIES = [
    (
        "grapheme_cluster_break.rs",
        [
            ("GraphemeClusterBreakCr", "CR"),
            ("GraphemeClusterBreakLf", "LF"),
            ("GraphemeClusterBreakControl", "Control"),
            ("GraphemeClusterBreakExtend", "Extend"),
            ("GraphemeClusterBreakL", "L"),
            ("GraphemeClusterBreakLv", "LV"),
            ("GraphemeClusterBreakPrepend", "Prepend"),
            ("GraphemeClusterBreakRegionalIndicator", "Regional_Indicator"),
            ("GraphemeClusterBreakLvt", "LVT"),
            ("GraphemeClusterBreakSpacingMark", "SpacingMark"),
            ("GraphemeClusterBreakT", "T"),
            ("GraphemeClusterBreakV", "V"),
            ("GraphemeClusterBreakZwj", "ZWJ"),
        ],
    ),
    (
        "word_break.rs",
        [
            ("WordBreakHebrewLetter", "Hebrew_Letter"),
            ("WordBreakExtendNumLet", "ExtendNumLet"),
            ("WordBreakWSegSpace", "WSegSpace"),
            ("WordBreakNumeric", "Numeric"),
        ],
    ),
    (
        "sentence_break.rs",
        [
            ("SentenceBreakLower", "Lower"),
            ("SentenceBreakClose", "Close"),
            ("SentenceBreakSContinue", "SContinue"),
        ],
    ),
]

LEGACY_SCRIPT_PROPERTIES = [
    ("ScriptCyrillic", "Cyrillic"),
    ("ScriptGreek", "Greek"),
]

LEGACY_SCRIPT_EXTENSION_PROPERTIES = [
    ("ScriptExtensionCyrillic", "Cyrillic"),
    ("ScriptExtensionGreek", "Greek"),
]


CHAR_LITERAL = r"'(?:\\u\{[0-9A-Fa-f]+\}|\\.|[^'])'"
CHAR_LITERAL_PATTERN = re.compile(CHAR_LITERAL)
PAIR_PATTERN = re.compile(r"\(\s*(" + CHAR_LITERAL + r")\s*,\s*(" + CHAR_LITERAL + r")\s*\),?")
CASE_FOLD_PATTERN = re.compile(r"\(\s*(" + CHAR_LITERAL + r")\s*,\s*&\[(.*?)\]\s*\),?", re.DOTALL)


def fail(message: str) -> None:
    print(message, file=sys.stderr)
    raise SystemExit(1)


def decode_char_literal(literal: str) -> int:
    if len(literal) < 2 or literal[0] != "'" or literal[-1] != "'":
        fail(f"invalid Rust char literal: {literal}")
    body = literal[1:-1]
    if body.startswith(r"\u{") and body.endswith("}"):
        return int(body[3:-1], 16)
    if body.startswith("\\"):
        escapes = {
            r"\0": 0,
            r"\t": 0x09,
            r"\n": 0x0A,
            r"\r": 0x0D,
            r"\\": 0x5C,
            r"\'": 0x27,
            r'\"': 0x22,
        }
        if body in escapes:
            return escapes[body]
        fail(f"unsupported Rust char escape: {literal}")
    if len(body) != 1:
        fail(f"expected one Unicode scalar in {literal}")
    return ord(body)


def load_ranges(file_name: str, const_name: str) -> list[tuple[int, int]]:
    path = TABLE_ROOT / file_name
    text = path.read_text(encoding="utf-8")
    section_pattern = re.compile(
        r"pub const " + re.escape(const_name) + r":.*?=\s*&\[(.*?)\];",
        re.DOTALL,
    )
    match = section_pattern.search(text)
    if not match:
        fail(f"could not find {const_name} in {path}")

    ranges: list[tuple[int, int]] = []
    for pair in PAIR_PATTERN.finditer(match.group(1)):
        start = decode_char_literal(pair.group(1))
        end = decode_char_literal(pair.group(2))
        if start > end:
            fail(f"range start exceeds end in {path}: {pair.group(0)}")
        ranges.append((start, end))

    if not ranges:
        fail(f"no ranges found for {const_name} in {path}")
    if ranges != sorted(ranges):
        fail(f"ranges for {const_name} in {path} are not sorted")
    return ranges


def pascal_case(value: str) -> str:
    return "".join(part.title() for part in value.split("_"))


def load_general_categories() -> list[tuple[str, str, list[tuple[int, int]]]]:
    text = (TABLE_ROOT / "general_category.rs").read_text(encoding="utf-8")
    by_name_match = re.search(r"pub const BY_NAME:.*?= &\[(.*?)\];", text, re.DOTALL)
    if not by_name_match:
        fail("could not find general_category.rs BY_NAME")

    categories = []
    for name, const_name in re.findall(r'\("([^"]+)", ([A-Z_]+)\)', by_name_match.group(1)):
        categories.append((name, pascal_case(name), load_ranges("general_category.rs", const_name)))
    if not categories:
        fail("no general category tables found")
    return categories


def load_named_tables(file_name: str, selected_names: list[tuple[str, str]]) -> list[tuple[str, list[tuple[int, int]]]]:
    text = (TABLE_ROOT / file_name).read_text(encoding="utf-8")
    by_name_match = re.search(r"pub const BY_NAME:.*?= &\[(.*?)\];", text, re.DOTALL)
    if not by_name_match:
        fail(f"could not find {file_name} BY_NAME")

    consts_by_name = dict(re.findall(r'\("([^"]+)", ([A-Z_]+)\)', by_name_match.group(1)))
    tables = []
    for enum_name, table_name in selected_names:
        if table_name not in consts_by_name:
            fail(f"could not find {table_name} in {file_name} BY_NAME")
        tables.append((enum_name, load_ranges(file_name, consts_by_name[table_name])))
    return tables


def load_all_named_tables(file_name: str, prefix: str) -> list[tuple[str, list[tuple[int, int]]]]:
    text = (TABLE_ROOT / file_name).read_text(encoding="utf-8")
    by_name_match = re.search(r"pub const BY_NAME:.*?= &\[(.*?)\];", text, re.DOTALL)
    if not by_name_match:
        fail(f"could not find {file_name} BY_NAME")

    tables = []
    for table_name, const_name in re.findall(r'\("([^"]+)", ([A-Z_]+)\)', by_name_match.group(1)):
        tables.append((prefix + pascal_case(table_name), load_ranges(file_name, const_name)))
    if len(tables) != 170:
        fail(f"expected 170 {file_name} tables, found {len(tables)}")
    return tables


def load_script_names() -> list[str]:
    text = (TABLE_ROOT / "script.rs").read_text(encoding="utf-8")
    by_name_match = re.search(r"pub const BY_NAME:.*?= &\[(.*?)\];", text, re.DOTALL)
    if not by_name_match:
        fail("could not find script.rs BY_NAME")
    names = [name for name, _ in re.findall(r'\("([^"]+)", ([A-Z_]+)\)', by_name_match.group(1))]
    if len(names) != 170:
        fail(f"expected 170 script names, found {len(names)}")
    return names


def load_script_aliases() -> list[tuple[str, str]]:
    text = (TABLE_ROOT / "property_values.rs").read_text(encoding="utf-8")
    sections = dict(re.findall(
        r'\(\s*"([^"]+)",\s*&\[(.*?)\]\s*,?\s*\),',
        text,
        re.DOTALL,
    ))
    if "Script" not in sections:
        fail("could not find Script aliases in property_values.rs")
    aliases = re.findall(r'\("([^"]+)", "([^"]+)"\)', sections["Script"])
    if len(aliases) < 300:
        fail(f"expected at least 300 Script aliases, found {len(aliases)}")
    return aliases


def emit_script_kind() -> None:
    print("// <auto-generated />")
    print("// Generated by eng/generate-regex-unicode-tables.py from regex-syntax 0.8.8 Unicode tables.")
    print("// Unicode version: 16.0.0.")
    print()
    print("namespace Scout;")
    print()
    print("/// <summary>")
    print("/// Identifies a generated Unicode Script or Script_Extensions value.")
    print("/// </summary>")
    print("internal enum RegexUnicodeScriptKind")
    print("{")
    print("    /// <summary>")
    print("    /// Identifies the absence of a Unicode script value.")
    print("    /// </summary>")
    print("    None,")
    for name in load_script_names():
        enum_name = pascal_case(name)
        print()
        print("    /// <summary>")
        print(f"    /// Identifies the {enum_name} Unicode script.")
        print("    /// </summary>")
        print(f"    {enum_name},")
    print("}")


def emit_script_names() -> None:
    names = set(load_script_names())
    print("// <auto-generated />")
    print("// Generated by eng/generate-regex-unicode-tables.py from regex-syntax 0.8.8 Unicode tables.")
    print("// Unicode version: 16.0.0.")
    print()
    print("namespace Scout;")
    print()
    print("/// <summary>")
    print("/// Resolves generated Unicode Script and Script_Extensions aliases.")
    print("/// </summary>")
    print("internal static class RegexUnicodeScriptNames")
    print("{")
    print("    internal static bool TryGetKind(ReadOnlySpan<byte> name, out RegexUnicodeScriptKind kind)")
    print("    {")
    print("        kind = RegexUnicodeScriptKind.None;")
    for alias, canonical in load_script_aliases():
        if canonical not in names:
            continue
        print(f'        if (RegexUnicodePropertyNames.NameEquals(name, "{alias}"))')
        print("        {")
        print(f"            kind = RegexUnicodeScriptKind.{pascal_case(canonical)};")
        print("            return true;")
        print("        }")
        print()
    print("        return false;")
    print("    }")
    print("}")


def load_simple_case_folds() -> list[tuple[int, int]]:
    path = TABLE_ROOT / "case_folding_simple.rs"
    text = path.read_text(encoding="utf-8")
    section_pattern = re.compile(r"pub const CASE_FOLDING_SIMPLE:.*?=\s*&\[(.*?)\];", re.DOTALL)
    match = section_pattern.search(text)
    if not match:
        fail(f"could not find CASE_FOLDING_SIMPLE in {path}")

    pairs = set()
    for entry in CASE_FOLD_PATTERN.finditer(match.group(1)):
        source = decode_char_literal(entry.group(1))
        for target_literal in CHAR_LITERAL_PATTERN.findall(entry.group(2)):
            target = decode_char_literal(target_literal)
            pairs.add((source, target))

    if not pairs:
        fail(f"no simple case fold pairs found in {path}")
    return sorted(pairs)


def emit_table(name: str, ranges: list[tuple[int, int]]) -> None:
    print(f"    private static ReadOnlySpan<int> {name} =>")
    print("    [")
    for start, end in ranges:
        print(f"        0x{start:06X}, 0x{end:06X},")
    print("    ];")
    print()


def emit_pair_table(name: str, pairs: list[tuple[int, int]]) -> None:
    print(f"    private static ReadOnlySpan<int> {name} =>")
    print("    [")
    for first, second in pairs:
        print(f"        0x{first:06X}, 0x{second:06X},")
    print("    ];")
    print()


def main() -> None:
    output = sys.argv[1] if len(sys.argv) > 1 else "tables"
    if output == "script-kind":
        emit_script_kind()
        return
    if output == "script-names":
        emit_script_names()
        return
    if output != "tables":
        fail(f"unknown output: {output}")

    loaded = [(name, load_ranges(file_name, const_name)) for name, file_name, const_name in TABLES]
    general_categories = load_general_categories()
    boolean_properties = [(name, load_ranges("property_bool.rs", const_name)) for name, const_name in BOOLEAN_PROPERTIES]
    break_properties = []
    for file_name, selected_names in BREAK_PROPERTIES:
        break_properties.extend(load_named_tables(file_name, selected_names))
    legacy_script_properties = load_named_tables("script.rs", LEGACY_SCRIPT_PROPERTIES)
    legacy_script_extension_properties = load_named_tables("script_extension.rs", LEGACY_SCRIPT_EXTENSION_PROPERTIES)
    script_properties = load_all_named_tables("script.rs", "Script")
    script_extension_properties = load_all_named_tables("script_extension.rs", "ScriptExtension")
    simple_case_folds = load_simple_case_folds()
    print("// <auto-generated />")
    print("// Generated by eng/generate-regex-unicode-tables.py from regex-syntax 0.8.8 Unicode tables.")
    print("// Unicode version: 16.0.0.")
    print("using System;")
    print("using System.Collections.Generic;")
    print("using System.Text;")
    print()
    print("namespace Scout;")
    print()
    print("/// <summary>")
    print("/// Provides generated Unicode property, script, and case-fold tables.")
    print("/// </summary>")
    print("internal static class RegexUnicodeTables")
    print("{")
    print("    private static readonly Lazy<Dictionary<int, int[]>> SimpleCaseFoldEquivalents = new(BuildSimpleCaseFoldEquivalents);")
    print()
    print("    internal static bool IsDecimalNumber(Rune value)")
    print("    {")
    print("        return Contains(DecimalNumber, value.Value);")
    print("    }")
    print()
    print("    internal static bool IsPerlWord(Rune value)")
    print("    {")
    print("        return Contains(PerlWord, value.Value);")
    print("    }")
    print()
    print("    internal static bool IsPerlSpace(Rune value)")
    print("    {")
    print("        return Contains(PerlSpace, value.Value);")
    print("    }")
    print()
    print("    internal static bool IsAlphabetic(Rune value)")
    print("    {")
    print("        return Contains(Alphabetic, value.Value);")
    print("    }")
    print()
    print("    internal static bool IsGeneralCategory(RegexUnicodePropertyKind kind, Rune value)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for _, name, _ in general_categories:
        print(f"            RegexUnicodePropertyKind.{name} => Contains(GeneralCategory{name}, value.Value),")
    print("            _ => false,")
    print("        };")
    print("    }")
    print()
    print("    internal static bool IsBooleanProperty(RegexUnicodePropertyKind kind, Rune value)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in boolean_properties:
        print(f"            RegexUnicodePropertyKind.{name} => Contains(BooleanProperty{name}, value.Value),")
    print("            _ => false,")
    print("        };")
    print("    }")
    print()
    print("    internal static bool IsBreakProperty(RegexUnicodePropertyKind kind, Rune value)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in break_properties:
        print(f"            RegexUnicodePropertyKind.{name} => Contains(BreakProperty{name}, value.Value),")
    print("            _ => false,")
    print("        };")
    print("    }")
    print()
    print("    internal static bool IsScript(RegexUnicodePropertyKind kind, Rune value)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in legacy_script_properties:
        print(f"            RegexUnicodePropertyKind.{name} => Contains({name}, value.Value),")
    print("            _ => false,")
    print("        };")
    print("    }")
    print()
    print("    internal static bool IsScriptExtension(RegexUnicodePropertyKind kind, Rune value)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in legacy_script_extension_properties:
        print(f"            RegexUnicodePropertyKind.{name} => Contains({name}, value.Value),")
    print("            _ => false,")
    print("        };")
    print("    }")
    print()
    print("    internal static bool IsSimpleCaseFold(Rune left, Rune right)")
    print("    {")
    print("        return left.Value == right.Value ||")
    print("            Contains(SimpleCaseFold, left.Value, right.Value) ||")
    print("            Contains(SimpleCaseFold, right.Value, left.Value);")
    print("    }")
    print()
    print("    internal static void AddSimpleCaseFoldEquivalents(Rune value, List<Rune> equivalents)")
    print("    {")
    print("        AddDistinctRune(equivalents, value);")
    print("        if (!SimpleCaseFoldEquivalents.Value.TryGetValue(value.Value, out int[] scalarEquivalents))")
    print("        {")
    print("            return;")
    print("        }")
    print()
    print("        for (int index = 0; index < scalarEquivalents.Length; index++)")
    print("        {")
    print("            AddDistinctRune(equivalents, new Rune(scalarEquivalents[index]));")
    print("        }")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> DecimalNumberRanges => DecimalNumber;")
    print()
    print("    internal static ReadOnlySpan<int> PerlWordRanges => PerlWord;")
    print()
    print("    internal static ReadOnlySpan<int> PerlSpaceRanges => PerlSpace;")
    print()
    print("    internal static ReadOnlySpan<int> AlphabeticRanges => Alphabetic;")
    print()
    print("    internal static ReadOnlySpan<int> SimpleCaseFoldPairs => SimpleCaseFold;")
    print()
    print("    internal static ReadOnlySpan<int> GetGeneralCategoryRanges(RegexUnicodePropertyKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for _, name, _ in general_categories:
        print(f"            RegexUnicodePropertyKind.{name} => GeneralCategory{name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> GetBooleanPropertyRanges(RegexUnicodePropertyKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in boolean_properties:
        print(f"            RegexUnicodePropertyKind.{name} => BooleanProperty{name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> GetBreakPropertyRanges(RegexUnicodePropertyKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in break_properties:
        print(f"            RegexUnicodePropertyKind.{name} => BreakProperty{name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> GetScriptRanges(RegexUnicodePropertyKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in legacy_script_properties:
        print(f"            RegexUnicodePropertyKind.{name} => {name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> GetScriptExtensionRanges(RegexUnicodePropertyKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in legacy_script_extension_properties:
        print(f"            RegexUnicodePropertyKind.{name} => {name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> GetScriptRanges(RegexUnicodeScriptKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in script_properties:
        enum_name = name.removeprefix("Script")
        print(f"            RegexUnicodeScriptKind.{enum_name} => {name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static ReadOnlySpan<int> GetScriptExtensionRanges(RegexUnicodeScriptKind kind)")
    print("    {")
    print("        return kind switch")
    print("        {")
    for name, _ in script_extension_properties:
        enum_name = name.removeprefix("ScriptExtension")
        print(f"            RegexUnicodeScriptKind.{enum_name} => {name},")
    print("            _ => [],")
    print("        };")
    print("    }")
    print()
    print("    internal static void AddDecimalNumberPrefixBytes(List<byte[]> prefixes)")
    print("    {")
    print("        AddRangePrefixBytes(DecimalNumber, prefixes);")
    print("    }")
    print()
    print("    internal static void AddPerlSpacePrefixBytes(List<byte[]> prefixes)")
    print("    {")
    print("        AddRangePrefixBytes(PerlSpace, prefixes);")
    print("    }")
    print()
    print("    internal static void AddPerlWordPrefixBytes(List<byte[]> prefixes)")
    print("    {")
    print("        AddRangePrefixBytes(PerlWord, prefixes);")
    print("    }")
    print()
    print("    internal static void AddAlphabeticPrefixBytes(List<byte[]> prefixes)")
    print("    {")
    print("        AddRangePrefixBytes(Alphabetic, prefixes);")
    print("    }")
    print()
    print("    private static void AddDistinctRune(List<Rune> values, Rune value)")
    print("    {")
    print("        for (int index = 0; index < values.Count; index++)")
    print("        {")
    print("            if (values[index].Value == value.Value)")
    print("            {")
    print("                return;")
    print("            }")
    print("        }")
    print()
    print("        values.Add(value);")
    print("    }")
    print()
    print("    private static Dictionary<int, int[]> BuildSimpleCaseFoldEquivalents()")
    print("    {")
    print("        var builders = new Dictionary<int, List<int>>();")
    print("        ReadOnlySpan<int> pairs = SimpleCaseFold;")
    print("        for (int index = 0; index < pairs.Length; index += 2)")
    print("        {")
    print("            int first = pairs[index];")
    print("            int second = pairs[index + 1];")
    print("            AddCaseFoldEquivalent(builders, first, second);")
    print("            AddCaseFoldEquivalent(builders, second, first);")
    print("        }")
    print()
    print("        var equivalents = new Dictionary<int, int[]>(builders.Count);")
    print("        foreach (KeyValuePair<int, List<int>> builder in builders)")
    print("        {")
    print("            equivalents.Add(builder.Key, builder.Value.ToArray());")
    print("        }")
    print()
    print("        return equivalents;")
    print("    }")
    print()
    print("    private static void AddCaseFoldEquivalent(Dictionary<int, List<int>> equivalents, int source, int target)")
    print("    {")
    print("        if (!Rune.IsValid(source) || !Rune.IsValid(target))")
    print("        {")
    print("            return;")
    print("        }")
    print()
    print("        if (!equivalents.TryGetValue(source, out List<int> targets))")
    print("        {")
    print("            targets = [];")
    print("            equivalents.Add(source, targets);")
    print("        }")
    print()
    print("        for (int index = 0; index < targets.Count; index++)")
    print("        {")
    print("            if (targets[index] == target)")
    print("            {")
    print("                return;")
    print("            }")
    print("        }")
    print()
    print("        targets.Add(target);")
    print("    }")
    print()
    print("    private static void AddRangePrefixBytes(ReadOnlySpan<int> ranges, List<byte[]> prefixes)")
    print("    {")
    print("        for (int rangeIndex = 0; rangeIndex < ranges.Length; rangeIndex += 2)")
    print("        {")
    print("            for (int value = ranges[rangeIndex]; value <= ranges[rangeIndex + 1]; value++)")
    print("            {")
    print("                if (Rune.IsValid(value))")
    print("                {")
    print("                    AddDistinctPrefixByte(prefixes, Utf8FirstByte(value));")
    print("                }")
    print("            }")
    print("        }")
    print("    }")
    print()
    print("    private static void AddDistinctPrefixByte(List<byte[]> prefixes, byte value)")
    print("    {")
    print("        for (int index = 0; index < prefixes.Count; index++)")
    print("        {")
    print("            byte[] prefix = prefixes[index];")
    print("            if (prefix.Length == 1 && prefix[0] == value)")
    print("            {")
    print("                return;")
    print("            }")
    print("        }")
    print()
    print("        prefixes.Add([value]);")
    print("    }")
    print()
    print("    private static byte Utf8FirstByte(int value)")
    print("    {")
    print("        if (value <= 0x7F)")
    print("        {")
    print("            return (byte)value;")
    print("        }")
    print()
    print("        if (value <= 0x7FF)")
    print("        {")
    print("            return (byte)(0xC0 | (value >> 6));")
    print("        }")
    print()
    print("        if (value <= 0xFFFF)")
    print("        {")
    print("            return (byte)(0xE0 | (value >> 12));")
    print("        }")
    print()
    print("        return (byte)(0xF0 | (value >> 18));")
    print("    }")
    print()
    print("    private static bool Contains(ReadOnlySpan<int> ranges, int value)")
    print("    {")
    print("        int low = 0;")
    print("        int high = (ranges.Length / 2) - 1;")
    print("        while (low <= high)")
    print("        {")
    print("            int middle = low + ((high - low) / 2);")
    print("            int rangeStart = ranges[middle * 2];")
    print("            int rangeEnd = ranges[(middle * 2) + 1];")
    print("            if (value < rangeStart)")
    print("            {")
    print("                high = middle - 1;")
    print("                continue;")
    print("            }")
    print()
    print("            if (value > rangeEnd)")
    print("            {")
    print("                low = middle + 1;")
    print("                continue;")
    print("            }")
    print()
    print("            return true;")
    print("        }")
    print()
    print("        return false;")
    print("    }")
    print()
    print("    private static bool Contains(ReadOnlySpan<int> pairs, int first, int second)")
    print("    {")
    print("        int low = 0;")
    print("        int high = (pairs.Length / 2) - 1;")
    print("        while (low <= high)")
    print("        {")
    print("            int middle = low + ((high - low) / 2);")
    print("            int pairFirst = pairs[middle * 2];")
    print("            int pairSecond = pairs[(middle * 2) + 1];")
    print("            if (first < pairFirst || first == pairFirst && second < pairSecond)")
    print("            {")
    print("                high = middle - 1;")
    print("                continue;")
    print("            }")
    print()
    print("            if (first > pairFirst || first == pairFirst && second > pairSecond)")
    print("            {")
    print("                low = middle + 1;")
    print("                continue;")
    print("            }")
    print()
    print("            return true;")
    print("        }")
    print()
    print("        return false;")
    print("    }")
    print()
    for name, ranges in loaded:
        emit_table(name, ranges)
    emit_pair_table("SimpleCaseFold", simple_case_folds)
    for name, ranges in boolean_properties:
        emit_table("BooleanProperty" + name, ranges)
    for name, ranges in break_properties:
        emit_table("BreakProperty" + name, ranges)
    for name, ranges in script_properties:
        emit_table(name, ranges)
    for name, ranges in script_extension_properties:
        emit_table(name, ranges)
    for _, name, ranges in general_categories:
        emit_table("GeneralCategory" + name, ranges)
    print("}")


if __name__ == "__main__":
    main()
