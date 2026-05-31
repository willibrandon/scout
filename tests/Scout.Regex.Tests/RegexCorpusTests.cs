using System;
using System.Collections.Generic;
using System.Text;

namespace Scout;

/// <summary>
/// Runs Scout's regex engine against the pinned regex crate corpus.
/// </summary>
public sealed class RegexCorpusTests
{
    internal static readonly (string RelativePath, string[] Names)[] CorpusGroups =
        [
            ("misc.toml",
            [
                "ascii-literal",
                "ascii-literal-not",
                "ascii-literal-anchored",
                "ascii-literal-anchored-not",
                "anchor-start-end-line",
                "prefix-literal-match",
                "prefix-literal-match-ascii",
                "prefix-literal-no-match",
                "one-literal-edge",
                "terminates",
                "suffix-100",
                "suffix-200",
                "suffix-300",
                "suffix-400",
                "suffix-500",
                "suffix-600",
            ]),
            ("flags.toml",
            [
                "1",
                "2",
                "3",
                "4",
                "5",
                "6",
                "7",
                "8",
                "9",
                "10",
                "11",
            ]),
            ("iter.toml",
            [
                "1",
                "2",
                "empty1",
                "empty2",
                "empty3",
                "empty4",
                "empty5",
                "empty6",
                "empty7",
                "empty8",
                "empty9",
                "empty10",
                "empty11",
                "start1",
                "start2",
                "anchored1",
                "anchored2",
                "anchored3",
                "nonempty-followedby-empty",
                "nonempty-followedby-oneempty",
                "nonempty-followedby-onemixed",
                "nonempty-followedby-twomixed",
            ]),
            ("empty.toml",
            [
                "100",
                "110",
                "120",
                "130",
                "200",
                "210",
                "220",
                "230",
                "240",
                "300",
                "310",
                "320",
                "330",
                "400",
                "500",
                "510",
                "520",
                "600",
                "610",
            ]),
            ("earliest.toml",
            [
                "no-greedy-100",
                "no-greedy-200",
                "is-ungreedy",
                "look-start-test",
                "look-end-test",
                "no-leftmost-first-100",
                "no-leftmost-first-200",
            ]),
            ("expensive.toml",
            [
                "regression-many-repeat-no-stack-overflow",
                "backtrack-blow-visited-capacity",
            ]),
            ("crazy.toml",
            [
                "nothing-empty",
                "nothing-something",
                "ranges",
                "ranges-not",
                "float1",
                "float2",
                "float3",
                "float4",
                "float5",
                "email",
                "email-not",
                "email-big",
                "date1",
                "date2",
                "date3",
                "start-end-empty",
                "start-end-empty-rev",
                "start-end-empty-many-1",
                "start-end-empty-many-2",
                "start-end-empty-rep",
                "start-end-empty-rep-rev",
                "neg-class-letter",
                "neg-class-letter-comma",
                "neg-class-letter-space",
                "neg-class-comma",
                "neg-class-space",
                "neg-class-space-comma",
                "neg-class-comma-space",
                "neg-class-ascii",
                "lazy-many-many",
                "lazy-many-optional",
                "lazy-one-many-many",
                "lazy-one-many-optional",
                "lazy-range-min-many",
                "lazy-range-many",
                "greedy-many-many",
                "greedy-many-optional",
                "greedy-one-many-many",
                "greedy-one-many-optional",
                "greedy-range-min-many",
                "greedy-range-many",
                "empty1",
                "empty2",
                "empty3",
                "empty4",
                "empty5",
                "empty6",
                "empty7",
                "empty8",
                "empty9",
                "empty10",
                "empty11",
            ]),
            ("multiline.toml",
            [
                "basic1",
                "basic1-crlf",
                "basic1-crlf-cr",
                "basic2",
                "basic2-crlf",
                "basic2-crlf-cr",
                "basic3",
                "basic3-crlf",
                "basic3-crlf-cr",
                "basic4",
                "basic4-crlf",
                "basic4-crlf-cr",
                "basic5",
                "basic5-crlf",
                "basic5-crlf-cr",
                "basic6",
                "basic6-crlf",
                "basic6-crlf-cr",
                "basic7",
                "basic7-crlf",
                "basic7-crlf-cr",
                "basic8",
                "basic8-crlf",
                "basic8-crlf-cr",
                "basic9",
                "basic9-crlf",
                "repeat1",
                "repeat1-crlf",
                "repeat1-crlf-cr",
                "repeat1-no-multi",
                "repeat1-no-multi-crlf",
                "repeat1-no-multi-crlf-cr",
                "repeat2",
                "repeat2-crlf",
                "repeat2-crlf-cr",
                "repeat2-no-multi",
                "repeat2-no-multi-crlf",
                "repeat2-no-multi-crlf-cr",
                "repeat3",
                "repeat3-crlf",
                "repeat3-crlf-cr",
                "repeat3-no-multi",
                "repeat3-no-multi-crlf",
                "repeat3-no-multi-crlf-cr",
                "repeat4",
                "repeat4-crlf",
                "repeat4-crlf-cr",
                "repeat4-no-multi",
                "repeat4-no-multi-crlf",
                "repeat4-no-multi-crlf-cr",
                "repeat5",
                "repeat5-crlf",
                "repeat5-crlf-cr",
                "repeat5-no-multi",
                "repeat5-no-multi-crlf",
                "repeat5-no-multi-crlf-cr",
                "repeat6",
                "repeat6-crlf",
                "repeat6-crlf-cr",
                "repeat6-no-multi",
                "repeat6-no-multi-crlf",
                "repeat6-no-multi-crlf-cr",
                "repeat7",
                "repeat7-crlf",
                "repeat7-crlf-cr",
                "repeat7-no-multi",
                "repeat7-no-multi-crlf",
                "repeat7-no-multi-crlf-cr",
                "repeat8",
                "repeat8-crlf",
                "repeat8-crlf-cr",
                "repeat8-no-multi",
                "repeat8-no-multi-crlf",
                "repeat8-no-multi-crlf-cr",
                "repeat9",
                "repeat9-crlf",
                "repeat9-crlf-cr",
                "repeat9-no-multi",
                "repeat9-no-multi-crlf",
                "repeat9-no-multi-crlf-cr",
                "repeat10",
                "repeat10-crlf",
                "repeat10-crlf-cr",
                "repeat10-no-multi",
                "repeat10-no-multi-crlf",
                "repeat10-no-multi-crlf-cr",
                "repeat11",
                "repeat11-crlf",
                "repeat11-crlf-cr",
                "repeat11-no-multi",
                "repeat11-no-multi-crlf",
                "repeat11-no-multi-crlf-cr",
                "repeat12",
                "repeat12-crlf",
                "repeat12-crlf-cr",
                "repeat12-no-multi",
                "repeat12-no-multi-crlf",
                "repeat12-no-multi-crlf-cr",
                "repeat13",
                "repeat13-crlf",
                "repeat13-crlf-cr",
                "repeat13-no-multi",
                "repeat13-no-multi-crlf",
                "repeat13-no-multi-crlf-cr",
                "repeat14",
                "repeat14-crlf",
                "repeat14-crlf-cr",
                "repeat14-no-multi",
                "repeat14-no-multi-crlf",
                "repeat14-no-multi-crlf-cr",
                "repeat15",
                "repeat15-crlf",
                "repeat15-crlf-cr",
                "repeat15-no-multi",
                "repeat15-no-multi-crlf",
                "repeat15-no-multi-crlf-cr",
                "repeat16",
                "repeat16-crlf",
                "repeat16-crlf-cr",
                "repeat16-no-multi",
                "repeat16-no-multi-crlf",
                "repeat16-no-multi-crlf-cr",
                "repeat17",
                "repeat17-crlf",
                "repeat17-crlf-cr",
                "repeat17-no-multi",
                "repeat17-no-multi-crlf",
                "repeat17-no-multi-crlf-cr",
                "repeat18",
                "repeat18-crlf",
                "repeat18-crlf-cr",
                "repeat18-no-multi",
                "repeat18-no-multi-crlf",
                "repeat18-no-multi-crlf-cr",
                "match-line-100",
                "match-line-100-crlf",
                "match-line-100-crlf-cr",
                "match-line-200",
                "match-line-200-crlf",
                "match-line-200-crlf-cr",
            ]),
            ("line-terminator.toml",
            [
                "nul",
                "dot-changes-with-line-terminator",
                "not-line-feed",
                "non-ascii",
                "carriage",
                "word-byte",
                "non-word-byte",
                "word-boundary",
                "word-boundary-at",
                "not-word-boundary-at",
            ]),
            ("bytes.toml",
            [
                "word-boundary-ascii",
                "word-boundary-unicode",
                "word-boundary-ascii-not",
                "word-boundary-unicode-not",
                "perl-word-ascii",
                "perl-word-unicode",
                "perl-decimal-ascii",
                "perl-decimal-unicode",
                "perl-whitespace-ascii",
                "perl-whitespace-unicode",
                "case-one-ascii",
                "case-one-unicode",
                "case-class-simple-ascii",
                "case-class-ascii",
                "case-class-unicode",
                "dotstar-prefix-ascii",
                "dotstar-prefix-unicode",
                "invalid-utf8-anchor-100",
                "invalid-utf8-anchor-200",
                "invalid-utf8-anchor-300",
                "negate-ascii",
                "negate-unicode",
                "null-bytes",
                "mixed-dot",
                "word-boundary-ascii-100",
                "word-boundary-ascii-200",
            ]),
            ("anchored.toml",
            [
                "greedy",
                "greedy-earliest",
                "nongreedy",
                "nongreedy-all",
                "word-boundary-unicode-01",
                "word-boundary-nounicode-01",
                "no-match-at-start",
                "no-match-at-start-bounds",
                "no-match-at-start-reverse-inner",
                "no-match-at-start-reverse-inner-bounds",
                "no-match-at-start-reverse-anchored",
                "no-match-at-start-reverse-anchored-bounds",
            ]),
            ("substring.toml",
            [
                "unicode-word-start",
                "unicode-word-end",
                "ascii-word-start",
                "ascii-word-end",
            ]),
            ("leftmost-all.toml",
            [
                "alt",
                "multi",
                "dotall",
            ]),
            ("overlapping.toml",
            [
                "ungreedy-dotstar-matches-everything-100",
                "greedy-dotstar-matches-everything-100",
                "repetition-plus-leftmost-first-100",
                "repetition-plus-leftmost-first-110",
                "repetition-plus-all-100",
                "repetition-plus-all-110",
                "repetition-plus-leftmost-first-200",
                "repetition-plus-all-200",
                "repetition-star-leftmost-first-100",
                "repetition-star-all-100",
                "repetition-star-leftmost-first-200",
                "repetition-star-all-200",
                "start-end-rep-leftmost-first",
                "start-end-rep-all",
                "alt-leftmost-first-100",
                "alt-all-100",
                "empty-000",
                "empty-alt-000",
                "empty-alt-010",
                "iter1-bytes",
                "iter1-utf8",
                "iter1-incomplete-utf8",
                "scratch",
            ]),
            ("crlf.toml",
            [
                "basic",
                "start-end-non-empty",
                "start-end-empty",
                "start-end-before-after",
                "start-no-split",
                "start-no-split-adjacent",
                "start-no-split-adjacent-cr",
                "start-no-split-adjacent-lf",
                "end-no-split",
                "end-no-split-adjacent",
                "end-no-split-adjacent-cr",
                "end-no-split-adjacent-lf",
                "dot-no-crlf",
                "onepass-wrong-crlf-with-capture",
                "onepass-wrong-crlf-anchored",
            ]),
            ("regex-lite.toml",
            [
                "perl-class-decimal",
                "perl-class-space",
                "perl-class-word",
                "word-boundary",
                "word-boundary-negated",
                "empty-no-split-codepoint",
                "dot-always-matches-codepoint",
                "negated-class-always-matches-codepoint",
                "case-insensitive-is-ascii-only",
            ]),
            ("unicode.toml",
            [
                "literal1",
                "literal2",
                "literal3",
                "literal4",
                "wb-100",
                "wb-200",
                "wb-300",
                "wb-400",
                "class1",
                "class2",
                "class3",
                "class4",
                "class5",
                "class6",
                "class7",
                "class8",
                "class9",
                "class10",
                "perl1",
                "perl2",
                "perl3",
                "perl4",
                "perl5",
                "perl6",
                "perl7",
                "perl8",
                "perl9",
                "class-gencat1",
                "class-gencat2",
                "class-gencat3",
                "class-gencat4",
                "class-gencat5",
                "class-gencat6",
                "class-gencat7",
                "class-gencat8",
                "class-gencat9",
                "class-gencat10",
                "class-gencat11",
                "class-gencat12",
                "class-gencat13",
                "class-gencat14",
                "class-gencat15",
                "class-gencat16",
                "class-gencat17",
                "class-gencat18",
                "class-gencat19",
                "class-gencat20",
                "class-gencat21",
                "class-gencat22",
                "class-gencat23",
                "class-gencat24",
                "class-gencat25",
                "class-gencat26",
                "class-gencat27",
                "class-gencat28",
                "class-gencat29",
                "class-gencat30",
                "class-gencat31",
                "class-gencat32",
                "class-gencat33",
                "class-gencat34",
                "class-gencat35",
                "class-gencat36",
                "class-gencat37",
                "class-emoji1",
                "class-emoji2",
                "class-emoji3",
                "class-emoji4",
                "class-gcb1",
                "class-gcb2",
                "class-gcb3",
                "class-gcb4",
                "class-gcb5",
                "class-gcb6",
                "class-word-break1",
                "class-word-break2",
                "class-word-break3",
                "class-word-break4",
                "class-word-break5",
                "class-sentence-break1",
                "class-sentence-break2",
                "class-sentence-break3",
                "class-sentence-break4",
                "class-sentence-break5",
            ]),
            ("no-unicode.toml",
            [
                "invalid-utf8-literal1",
                "mixed",
                "case1",
                "case2",
                "case3",
                "negate1",
                "case4",
                "negate2",
                "dotstar-prefix1",
                "dotstar-prefix2",
                "null-bytes1",
                "word-ascii",
                "word-unicode",
                "decimal-ascii",
                "decimal-unicode",
                "space-ascii",
                "space-unicode",
                "iter1-bytes",
                "iter1-utf8",
                "iter2-bytes",
                "unanchored-invalid-utf8-match-100",
                "unanchored-invalid-utf8-nomatch",
                "anchored-iter-empty-utf8",
            ]),
            ("regression.toml",
            [
                "unicode-case-lower-nocase-flag",
                "captures-repeat",
                "invalid-regex-no-crash-100",
                "invalid-regex-no-crash-200",
                "invalid-regex-no-crash-300",
                "invalid-regex-no-crash-400",
                "endl-or-word-boundary",
                "zero-or-end",
                "y-or-endl",
                "word-boundary-start-x",
                "word-boundary-ascii-start-x",
                "unsorted-binary-search-100",
                "unsorted-binary-search-200",
                "negated-char-class-100",
                "negated-char-class-200",
                "ascii-word-underscore",
                "alt-in-alt-100",
                "alt-in-alt-200",
                "leftmost-first-prefix",
                "many-alternates",
                "word-boundary-alone-100",
                "word-boundary-alone-200",
                "word-boundary-ascii-no-capture",
                "word-boundary-ascii-capture",
                "end-not-word-boundary",
                "partial-anchor",
                "partial-anchor-alternate-begin",
                "partial-anchor-alternate-end",
                "lits-unambiguous-100",
                "lits-unambiguous-200",
                "negated-full-byte-range",
                "strange-anchor-non-complete-prefix",
                "strange-anchor-non-complete-suffix",
                "captures-after-dfa-premature-end-100",
                "captures-after-dfa-premature-end-200",
                "captures-after-dfa-premature-end-300",
                "captures-after-dfa-premature-end-400",
                "literal-panic",
                "empty-flag-expr",
                "invalid-repetition",
                "empty-group-with-unicode",
                "word-boundary-weird",
                "word-boundary-weird-ascii",
                "word-boundary-weird-minimal-ascii",
                "flags-are-unset",
                "reverse-suffix-100",
                "reverse-suffix-200",
                "reverse-suffix-300",
                "stops",
                "stops-ascii",
                "adjacent-line-boundary-100",
                "adjacent-line-boundary-200",
                "anchored-prefix-100",
                "anchored-prefix-200",
                "anchored-prefix-300",
                "aho-corasick-100",
                "interior-anchor-capture",
                "ruff-whitespace-around-keywords",
                "fowler-basic154-unanchored",
                "impossible-branch",
                "captures-wrong-order",
                "missed-match",
                "regex-to-glob",
                "i429-0",
                "i429-1",
                "i429-2",
                "i429-3",
                "i429-3-utf8",
                "i429-4",
                "i429-5",
                "i429-6",
                "i429-7",
                "i429-8",
                "i429-9",
                "i429-10",
                "i429-11",
                "i429-12",
                "i969",
                "word-boundary-interact-poorly-with-literal-optimizations",
                "negated-unicode-word-boundary-dfa-fail",
                "reverse-inner-plus-shorter-than-expected",
                "reverse-inner-short",
                "improper-reverse-suffix-optimization",
                "prefilter-with-aho-corasick-standard-semantics",
                "non-prefix-literal-quit-state",
                "hir-optimization-out-of-order-class",
            ]),
            ("set.toml",
            [
                "basic10",
                "basic20",
                "basic30",
                "basic40",
                "basic50",
                "basic60",
                "basic10-leftmost-first",
                "basic60-leftmost-first",
                "basic61",
                "basic61-leftmost-first",
                "basic70",
                "basic71",
                "basic80",
                "basic81",
                "basic82",
                "basic90",
                "basic91",
                "basic100",
                "basic101",
                "basic102",
                "basic110",
                "basic111",
                "basic120",
                "basic121",
                "basic122",
                "basic130",
                "empty10",
                "empty10-leftmost-first",
                "empty11",
                "empty11-leftmost-first",
                "empty20",
                "empty20-leftmost-first",
                "empty21",
                "empty21-leftmost-first",
                "empty22",
                "empty23",
                "empty30",
                "empty30-leftmost-first",
                "empty31",
                "empty31-leftmost-first",
                "empty40",
                "empty40-leftmost-first",
                "nomatch10",
                "nomatch20",
                "nomatch30",
                "nomatch40",
                "caps-010",
                "caps-020",
                "caps-030",
                "caps-110",
                "caps-120",
                "caps-121",
            ]),
            ("word-boundary-special.toml",
            [
                "word-start-ascii-010",
                "word-start-ascii-020",
                "word-start-ascii-030",
                "word-start-ascii-040",
                "word-start-ascii-050",
                "word-start-ascii-060",
                "word-start-ascii-060-bounds",
                "word-start-ascii-070",
                "word-start-ascii-080",
                "word-start-ascii-090",
                "word-start-ascii-110",
                "word-start-unicode-010",
                "word-start-unicode-020",
                "word-start-unicode-030",
                "word-start-unicode-040",
                "word-start-unicode-050",
                "word-start-unicode-060",
                "word-start-unicode-060-bounds",
                "word-start-unicode-070",
                "word-start-unicode-080",
                "word-start-unicode-090",
                "word-start-unicode-110",
                "word-end-ascii-010",
                "word-end-ascii-020",
                "word-end-ascii-030",
                "word-end-ascii-040",
                "word-end-ascii-050",
                "word-end-ascii-060",
                "word-end-ascii-060-bounds",
                "word-end-ascii-070",
                "word-end-ascii-080",
                "word-end-ascii-090",
                "word-end-ascii-110",
                "word-end-unicode-010",
                "word-end-unicode-020",
                "word-end-unicode-030",
                "word-end-unicode-040",
                "word-end-unicode-050",
                "word-end-unicode-060",
                "word-end-unicode-060-bounds",
                "word-end-unicode-070",
                "word-end-unicode-080",
                "word-end-unicode-090",
                "word-end-unicode-110",
                "word-start-half-ascii-010",
                "word-start-half-ascii-020",
                "word-start-half-ascii-030",
                "word-start-half-ascii-040",
                "word-start-half-ascii-050",
                "word-start-half-ascii-060",
                "word-start-half-ascii-060-noutf8",
                "word-start-half-ascii-060-bounds",
                "word-start-half-ascii-070",
                "word-start-half-ascii-080",
                "word-start-half-ascii-090",
                "word-start-half-ascii-110",
                "word-start-half-unicode-010",
                "word-start-half-unicode-020",
                "word-start-half-unicode-030",
                "word-start-half-unicode-040",
                "word-start-half-unicode-050",
                "word-start-half-unicode-060",
                "word-start-half-unicode-060-bounds",
                "word-start-half-unicode-070",
                "word-start-half-unicode-080",
                "word-start-half-unicode-090",
                "word-start-half-unicode-110",
                "word-end-half-ascii-010",
                "word-end-half-ascii-020",
                "word-end-half-ascii-030",
                "word-end-half-ascii-040",
                "word-end-half-ascii-050",
                "word-end-half-ascii-060",
                "word-end-half-ascii-060-bounds",
                "word-end-half-ascii-070",
                "word-end-half-ascii-080",
                "word-end-half-ascii-090",
                "word-end-half-ascii-110",
                "word-end-half-unicode-010",
                "word-end-half-unicode-020",
                "word-end-half-unicode-030",
                "word-end-half-unicode-040",
                "word-end-half-unicode-050",
                "word-end-half-unicode-060",
                "word-end-half-unicode-060-bounds",
                "word-end-half-unicode-070",
                "word-end-half-unicode-080",
                "word-end-half-unicode-090",
                "word-end-half-unicode-110",
                "word-start-half-ascii-carriage",
                "word-start-half-ascii-linefeed",
                "word-start-half-ascii-customlineterm",
            ]),
            ("word-boundary.toml",
            [
                "wb1",
                "wb2",
                "wb3",
                "wb4",
                "wb5",
                "wb6",
                "wb7",
                "wb8",
                "wb9",
                "wb10",
                "wb11",
                "wb12",
                "wb13",
                "wb14",
                "wb15",
                "wb16",
                "wb17",
                "wb18",
                "wb19",
                "wb20",
                "wb21",
                "wb22",
                "wb23",
                "wb24",
                "wb25",
                "wb26",
                "wb27",
                "wb28",
                "wb29",
                "wb30",
                "wb31",
                "wb32",
                "wb33",
                "wb34",
                "wb35",
                "wb36",
                "wb37",
                "wb38",
                "wb39",
                "wb40",
                "wb41",
                "wb42",
                "wb43",
                "wb44",
                "nb1",
                "nb2",
                "nb3",
                "nb4",
                "nb5",
                "nb6",
                "nb7",
                "nb8",
                "nb9",
                "nb10",
                "nb11",
                "nb12",
                "nb13",
                "nb14",
                "nb15",
                "nb16",
                "nb17",
                "nb18",
                "nb19",
                "nb20",
                "nb21",
                "nb22",
                "nb23",
                "nb24",
                "nb25",
                "nb26",
                "nb27",
                "nb28",
                "nb29",
                "nb30",
                "nb31",
                "nb32",
                "nb33",
                "nb34",
                "nb35",
                "nb36",
                "nb37",
                "nb38",
                "nb39",
                "unicode1",
                "unicode1-only-ascii",
                "unicode2",
                "unicode2-only-ascii",
                "unicode3",
                "unicode3-only-ascii",
                "unicode4",
                "unicode4-only-ascii",
                "unicode5",
                "unicode5-only-ascii",
                "unicode5-noutf8",
                "unicode5-noutf8-only-ascii",
                "unicode5-not",
                "unicode5-not-only-ascii",
                "unicode5-not-noutf8",
                "unicode5-not-noutf8-only-ascii",
                "unicode6",
                "unicode7",
                "unicode8",
                "alt-with-assertion-repetition",
            ]),
            ("utf8.toml",
            [
                "empty-utf8yes",
                "empty-utf8yes-overlapping",
                "empty-utf8no",
                "empty-utf8no-overlapping",
                "empty-utf8yes-bounds",
                "empty-utf8yes-bounds-overlapping",
                "empty-utf8no-bounds",
                "empty-utf8no-bounds-overlapping",
                "empty-utf8yes-anchored",
                "empty-utf8yes-anchored-overlapping",
                "empty-utf8no-anchored",
                "empty-utf8no-anchored-overlapping",
                "empty-utf8yes-anchored-bounds",
                "empty-utf8yes-anchored-bounds-overlapping",
                "empty-utf8no-anchored-bounds",
                "empty-utf8no-anchored-bounds-overlapping",
                "empty-utf8yes-startbound",
                "empty-utf8yes-startbound-overlapping",
                "empty-utf8no-startbound",
                "empty-utf8no-startbound-overlapping",
                "empty-utf8yes-anchored-startbound",
                "empty-utf8yes-anchored-startbound-overlapping",
                "empty-utf8no-anchored-startbound",
                "empty-utf8no-anchored-startbound-overlapping",
                "empty-utf8yes-anchored-endbound",
                "empty-utf8yes-anchored-endbound-overlapping",
                "empty-utf8no-anchored-endbound",
                "empty-utf8no-anchored-endbound-overlapping",
            ]),
        ];

    /// <summary>
    /// Verifies one supported <c>regex</c> 1.12.2 TOML corpus case.
    /// </summary>
    /// <param name="relativePath">The corpus TOML file.</param>
    /// <param name="name">The corpus case name.</param>
    [Theory]
    [MemberData(nameof(CorpusCases))]
    public void CorpusCaseMatchesExpectedSpans(string relativePath, string name)
    {
        RegexCorpusCase testCase = RegexCorpusLoader.Load(relativePath, name);
        if (!testCase.Compiles)
        {
            AssertCompileFails(testCase.Patterns, testCase.LineTerminator, testCase.CaseInsensitive, testCase.Utf8, testCase.UnicodeClasses);
            return;
        }

        RegexAutomaton[] automata = CompileAll(testCase.Patterns, testCase.LineTerminator, testCase.CaseInsensitive, testCase.Utf8, testCase.UnicodeClasses);
        RegexMatch[] actual = FindAll(
            automata,
            testCase.Haystack,
            testCase.MatchLimit,
            testCase.BoundsStart,
            testCase.BoundsEnd,
            testCase.Anchored,
            testCase.Earliest,
            testCase.Overlapping,
            testCase.MatchKindAll);

        Assert.True(
            MatchesEqual(testCase.ExpectedMatches, actual),
            relativePath + "::" + testCase.Name + " expected [" + FormatMatches(testCase.ExpectedMatches) + "] actual [" + FormatMatches(actual) + "]");
    }

    /// <summary>
    /// Gets every supported regex corpus case as xUnit data.
    /// </summary>
    /// <returns>The corpus case parameters.</returns>
    public static IEnumerable<object[]> CorpusCases()
    {
        for (int groupIndex = 0; groupIndex < CorpusGroups.Length; groupIndex++)
        {
            (string relativePath, string[] names) = CorpusGroups[groupIndex];
            for (int index = 0; index < names.Length; index++)
            {
                yield return [relativePath, names[index]];
            }
        }
    }

    internal static string[] CorpusCaseKeys()
    {
        var keys = new List<string>();
        for (int groupIndex = 0; groupIndex < CorpusGroups.Length; groupIndex++)
        {
            (string relativePath, string[] names) = CorpusGroups[groupIndex];
            for (int index = 0; index < names.Length; index++)
            {
                keys.Add(relativePath + "|" + names[index]);
            }
        }

        return keys.ToArray();
    }

    private static RegexAutomaton[] CompileAll(IReadOnlyList<byte[]> patterns, byte lineTerminator, bool caseInsensitive, bool utf8, bool unicodeClasses)
    {
        var automata = new RegexAutomaton[patterns.Count];
        for (int index = 0; index < patterns.Count; index++)
        {
            automata[index] = RegexAutomaton.Compile(patterns[index], caseInsensitive, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator, utf8, unicodeClasses);
        }

        return automata;
    }

    private static void AssertCompileFails(IReadOnlyList<byte[]> patterns, byte lineTerminator, bool caseInsensitive, bool utf8, bool unicodeClasses)
    {
        for (int index = 0; index < patterns.Count; index++)
        {
            Assert.Throws<FormatException>(() => RegexAutomaton.Compile(patterns[index], caseInsensitive, multiLine: false, dotMatchesNewline: false, crlf: false, lineTerminator, utf8, unicodeClasses));
        }
    }

    private static RegexMatch[] FindAll(
        IReadOnlyList<RegexAutomaton> automata,
        byte[] haystack,
        int? matchLimit,
        int boundsStart,
        int boundsEnd,
        bool anchored,
        bool earliest,
        bool overlapping,
        bool matchKindAll)
    {
        if (overlapping)
        {
            return FindOverlapping(automata, haystack, matchLimit, boundsStart, boundsEnd, anchored, earliest, matchKindAll);
        }

        var matches = new List<RegexMatch>();
        int startAt = boundsStart;
        int suppressedEmptyStart = -1;
        while (startAt <= boundsEnd)
        {
            if (matchLimit is int limit && matches.Count >= limit)
            {
                break;
            }

            RegexMatch? match = Find(automata, haystack, startAt, boundsEnd, anchored, earliest, matchKindAll);
            if (!match.HasValue)
            {
                break;
            }

            if (match.Value.Length == 0 && match.Value.Start == suppressedEmptyStart)
            {
                startAt++;
                suppressedEmptyStart = -1;
                continue;
            }

            matches.Add(match.Value);
            if (anchored && overlapping)
            {
                break;
            }

            if (match.Value.Length == 0)
            {
                startAt = match.Value.Start + 1;
                suppressedEmptyStart = -1;
            }
            else
            {
                startAt = match.Value.Start + match.Value.Length;
                suppressedEmptyStart = startAt;
            }
        }

        return matches.ToArray();
    }

    private static RegexMatch[] FindOverlapping(
        IReadOnlyList<RegexAutomaton> automata,
        byte[] haystack,
        int? matchLimit,
        int boundsStart,
        int boundsEnd,
        bool anchored,
        bool earliest,
        bool matchKindAll)
    {
        if (earliest)
        {
            return [];
        }

        RegexMatch? envelope = matchKindAll
            ? null
            : FindLeftmostFirst(automata, haystack, boundsStart, boundsEnd, anchored, earliest: false);
        if (!matchKindAll && !envelope.HasValue)
        {
            return [];
        }

        int firstStart = matchKindAll ? boundsStart : envelope!.Value.Start;
        int lastStart = anchored ? firstStart : matchKindAll ? boundsEnd : envelope!.Value.End;
        int lastEnd = matchKindAll ? boundsEnd : envelope!.Value.End;
        var candidates = new List<OverlappingCandidate>();
        for (int start = firstStart; start <= lastStart; start++)
        {
            for (int patternIndex = 0; patternIndex < automata.Count; patternIndex++)
            {
                IReadOnlyList<RegexMatch> matches = automata[patternIndex].FindOverlappingAt(haystack, start);
                for (int matchIndex = 0; matchIndex < matches.Count; matchIndex++)
                {
                    RegexMatch match = matches[matchIndex];
                    if (match.Start < boundsStart || match.End > lastEnd)
                    {
                        continue;
                    }

                    if (!matchKindAll && ContainsSpan(candidates, match))
                    {
                        continue;
                    }

                    candidates.Add(new OverlappingCandidate(match, patternIndex));
                }
            }
        }

        SortOverlappingCandidates(candidates, matchKindAll && automata.Count > 1);
        int count = matchLimit is int limit ? Math.Min(limit, candidates.Count) : candidates.Count;
        var result = new RegexMatch[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = candidates[index].Match;
        }

        return result;
    }

    private static RegexMatch? Find(IReadOnlyList<RegexAutomaton> automata, byte[] haystack, int startAt, int boundsEnd, bool anchored, bool earliest, bool matchKindAll)
    {
        return matchKindAll
            ? FindAllKind(automata, haystack, startAt, boundsEnd, anchored)
            : FindLeftmostFirst(automata, haystack, startAt, boundsEnd, anchored, earliest);
    }

    private static RegexMatch? FindLeftmostFirst(IReadOnlyList<RegexAutomaton> automata, byte[] haystack, int startAt, int boundsEnd, bool anchored, bool earliest)
    {
        RegexMatch? best = null;
        int bestPatternIndex = int.MaxValue;
        for (int index = 0; index < automata.Count; index++)
        {
            RegexMatch? match = earliest
                ? automata[index].FindEarliest(haystack, startAt)
                : automata[index].Find(haystack, startAt);
            if (!match.HasValue)
            {
                continue;
            }

            if ((anchored && match.Value.Start != startAt) ||
                match.Value.Start > boundsEnd ||
                match.Value.Start + match.Value.Length > boundsEnd)
            {
                continue;
            }

            if (!best.HasValue ||
                match.Value.Start < best.Value.Start ||
                match.Value.Start == best.Value.Start && index < bestPatternIndex)
            {
                best = match.Value;
                bestPatternIndex = index;
            }
        }

        return best;
    }

    private static RegexMatch? FindAllKind(IReadOnlyList<RegexAutomaton> automata, byte[] haystack, int startAt, int boundsEnd, bool anchored)
    {
        RegexMatch? best = null;
        int bestPatternIndex = int.MaxValue;
        int lastStart = anchored ? startAt : boundsEnd;
        for (int start = startAt; start <= lastStart; start++)
        {
            for (int index = 0; index < automata.Count; index++)
            {
                RegexMatch? match = automata[index].FindAllKindAt(haystack, start);
                if (!match.HasValue ||
                    match.Value.Start > boundsEnd ||
                    match.Value.Start + match.Value.Length > boundsEnd)
                {
                    continue;
                }

                if (!best.HasValue ||
                    match.Value.Start + match.Value.Length > best.Value.Start + best.Value.Length ||
                    match.Value.Start + match.Value.Length == best.Value.Start + best.Value.Length && match.Value.Start < best.Value.Start ||
                    match.Value.Equals(best.Value) && index < bestPatternIndex)
                {
                    best = match.Value;
                    bestPatternIndex = index;
                }
            }
        }

        return best;
    }

    private static bool ContainsSpan(List<OverlappingCandidate> candidates, RegexMatch match)
    {
        for (int index = 0; index < candidates.Count; index++)
        {
            if (candidates[index].Match.Equals(match))
            {
                return true;
            }
        }

        return false;
    }

    private static void SortOverlappingCandidates(List<OverlappingCandidate> candidates, bool regexSetAll)
    {
        if (regexSetAll)
        {
            candidates.Sort((left, right) => CompareRegexSetAllOverlappingCandidates(candidates, left, right));
            return;
        }

        candidates.Sort(CompareOverlappingCandidates);
    }

    private static int CompareOverlappingCandidates(OverlappingCandidate left, OverlappingCandidate right)
    {
        int end = left.Match.End.CompareTo(right.Match.End);
        if (end != 0)
        {
            return end;
        }

        int start = right.Match.Start.CompareTo(left.Match.Start);
        if (start != 0)
        {
            return start;
        }

        return left.PatternIndex.CompareTo(right.PatternIndex);
    }

    private static int CompareRegexSetAllOverlappingCandidates(List<OverlappingCandidate> candidates, OverlappingCandidate left, OverlappingCandidate right)
    {
        int end = left.Match.End.CompareTo(right.Match.End);
        if (end != 0)
        {
            return end;
        }

        int leftPatternStart = MinStartForPatternAtEnd(candidates, left.PatternIndex, left.Match.End);
        int rightPatternStart = MinStartForPatternAtEnd(candidates, right.PatternIndex, right.Match.End);
        int patternStart = leftPatternStart.CompareTo(rightPatternStart);
        if (patternStart != 0)
        {
            return patternStart;
        }

        int pattern = left.PatternIndex.CompareTo(right.PatternIndex);
        if (pattern != 0)
        {
            return pattern;
        }

        return right.Match.Start.CompareTo(left.Match.Start);
    }

    private static int MinStartForPatternAtEnd(List<OverlappingCandidate> candidates, int patternIndex, int end)
    {
        int start = int.MaxValue;
        for (int index = 0; index < candidates.Count; index++)
        {
            OverlappingCandidate candidate = candidates[index];
            if (candidate.PatternIndex == patternIndex && candidate.Match.End == end)
            {
                start = Math.Min(start, candidate.Match.Start);
            }
        }

        return start;
    }

    private static bool MatchesEqual(IReadOnlyList<RegexMatch> expected, RegexMatch[] actual)
    {
        if (expected.Count != actual.Length)
        {
            return false;
        }

        for (int index = 0; index < expected.Count; index++)
        {
            if (!expected[index].Equals(actual[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatMatches(IReadOnlyList<RegexMatch> matches)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < matches.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(", ");
            }

            RegexMatch match = matches[index];
            builder.Append('[');
            builder.Append(match.Start);
            builder.Append(", ");
            builder.Append(match.Start + match.Length);
            builder.Append(']');
        }

        return builder.ToString();
    }
}
