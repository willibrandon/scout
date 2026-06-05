use strict;
use warnings;

my $kind = $ENV{"KIND"} // die "missing KIND\n";
my $version = $ENV{"SCOUT_VERSION"} // die "missing SCOUT_VERSION\n";
my $rg_version = $ENV{"SCOUT_RIPGREP_VERSION"} // die "missing SCOUT_RIPGREP_VERSION\n";
my $rg_rev = $ENV{"SCOUT_RIPGREP_REVISION_SHORT"} // die "missing SCOUT_RIPGREP_REVISION_SHORT\n";
my $repo_url = $ENV{"SCOUT_REPOSITORY_URL"} // die "missing SCOUT_REPOSITORY_URL\n";
my $short_version = "scout $version (ripgrep $rg_version compatible, rev $rg_rev)";
my $man_version = "$version (ripgrep $rg_version compatible, rev $rg_rev)";

local $/;
my $text = <STDIN>;
$text =~ s/\r\n/\n/g;

sub replace_product_words {
    my ($value) = @_;
    $value =~ s/\bripgrep's\b/Scout's/g;
    $value =~ s/\bripgrep\b/Scout/g;
    $value =~ s/\bRipgrep\b/Scout/g;
    $value =~ s/\\fBrg\\fP/\\fBscout\\fP/g;
    $value =~ s/\brg's\b/scout's/g;
    $value =~ s/(?<![A-Za-z0-9_])rg(?![A-Za-z0-9_])/scout/g;
    return $value;
}

sub replace_ignore_docs {
    my ($value) = @_;
    $value =~ s/\.ignore or \.rgignore files/.ignore, .rgignore, or .scoutignore files/g;
    $value =~ s/\.ignore and \.rgignore files/.ignore, .rgignore, and .scoutignore files/g;
    $value =~ s/\.ignore, \.rgignore/.ignore, .rgignore, .scoutignore/g;
    $value =~ s/\.gitignore, \.rgignore and \.ignore/.gitignore, .ignore, .rgignore, and .scoutignore/g;
    $value =~ s/\\fB\.ignore\\fP or \\fB\.rgignore\\fP files/\\fB.ignore\\fP, \\fB.rgignore\\fP or \\fB.scoutignore\\fP files/g;
    $value =~ s/\\fB\.ignore\\fP and \\fB\.rgignore\\fP/\\fB.ignore\\fP, \\fB.rgignore\\fP and \\fB.scoutignore\\fP/g;
    $value =~ s/\\fB\.rgignore\\fP and \\fB\.ignore\\fP/\\fB.ignore\\fP, \\fB.rgignore\\fP and \\fB.scoutignore\\fP/g;
    $value =~ s/\\fB\.gitignore\\fP, \\fB\.rgignore\\fP and \\fB\.ignore\\fP/\\fB.gitignore\\fP, \\fB.ignore\\fP, \\fB.rgignore\\fP and \\fB.scoutignore\\fP/g;
    $value =~ s/\.ignore, \.rgignore, \.scoutignore, or \.scoutignore files/.ignore, .rgignore, or .scoutignore files/g;
    $value =~ s/\.ignore, \.rgignore, \.scoutignore, and \.scoutignore files/.ignore, .rgignore, and .scoutignore files/g;
    $value =~ s/\\fB\.ignore\\fP, \\fB\.rgignore\\fP, \\fB\.scoutignore\\fP or \\fB\.scoutignore\\fP files/\\fB.ignore\\fP, \\fB.rgignore\\fP or \\fB.scoutignore\\fP files/g;
    $value =~ s/\\fB\.ignore\\fP, \\fB\.rgignore\\fP, \\fB\.scoutignore\\fP and \\fB\.scoutignore\\fP/\\fB.ignore\\fP, \\fB.rgignore\\fP and \\fB.scoutignore\\fP/g;
    return $value;
}

sub replace_config_docs {
    my ($value) = @_;
    my $compat_placeholder = "__SCOUT_COMPAT_CONFIG_PATH__";
    $value =~ s/the RIPGREP_CONFIG_PATH environment variable/the SCOUT_CONFIG_PATH or $compat_placeholder environment variables/g;
    $value =~ s/Set \\fBRIPGREP_CONFIG_PATH\\fP to a\nconfiguration file\./Set \\fBSCOUT_CONFIG_PATH\\fP to a configuration file. If \\fBSCOUT_CONFIG_PATH\\fP is unset or empty, Scout reads \\fB$compat_placeholder\\fP as a compatibility fallback./g;
    $value =~ s/Scout will look for a single configuration file if and only if the\n\\fBRIPGREP_CONFIG_PATH\\fP environment variable is set and is non-empty\./Scout will look for a single configuration file. It uses the\n\\fBSCOUT_CONFIG_PATH\\fP environment variable when set and non-empty. If\n\\fBSCOUT_CONFIG_PATH\\fP is unset or empty, Scout reads\n\\fB$compat_placeholder\\fP as a compatibility fallback./g;
    $value =~ s/RIPGREP_CONFIG_PATH/SCOUT_CONFIG_PATH/g;
    $value =~ s/SCOUT_CONFIG_PATH or SCOUT_CONFIG_PATH/SCOUT_CONFIG_PATH or RIPGREP_CONFIG_PATH/g;
    $value =~ s/not respect the SCOUT_CONFIG_PATH\n        environment variable/not respect the SCOUT_CONFIG_PATH or RIPGREP_CONFIG_PATH\n        environment variables/g;
    $value =~ s/not respect the \\fBSCOUT_CONFIG_PATH\\fP environment\nvariable/not respect the \\fBSCOUT_CONFIG_PATH\\fP or \\fBRIPGREP_CONFIG_PATH\\fP environment\nvariables/g;
    $value =~ s/$compat_placeholder/RIPGREP_CONFIG_PATH/g;
    return $value;
}

sub transform_document {
    my ($value) = @_;
    my $version_placeholder = "__SCOUT_VERSION_LINE__";
    my $man_placeholder = "__SCOUT_MAN_HEADER__";
    my $attribution_placeholder = "__SCOUT_ATTRIBUTION__";
    my $url_placeholder = "__SCOUT_PROJECT_URL__";
    my $raw_version = quotemeta("ripgrep $rg_version (rev $rg_rev)");

    $value =~ s/^$raw_version$/$version_placeholder/m;
    $value =~ s/^\.TH RG 1 ([^\n]*) "$raw_version" "User Commands"$/$man_placeholder $1/m;
    $value =~ s/^Andrew Gallant <jamslam\@gmail\.com>$/$attribution_placeholder/m;
    $value =~ s#Project home page: https://github\.com/BurntSushi/ripgrep#Project home page: $url_placeholder#g;
    $value =~ s#https://github\.com/BurntSushi/ripgrep/blob/master/GUIDE\.md#__SCOUT_UPSTREAM_GUIDE_URL__#g;

    $value = replace_product_words($value);
    $value = replace_ignore_docs($value);
    $value = replace_config_docs($value);
    $value =~ s/ripgreprc/scoutrc/g;
    $value =~ s/\\fBrg\.bash\\fP/\\fBscout.bash\\fP/g;
    $value =~ s/\\fBrg\.fish\\fP/\\fBscout.fish\\fP/g;
    $value =~ s/\\fB_rg\\fP/\\fB_scout\\fP/g;

    $value =~ s/$version_placeholder/$short_version/g;
    $value =~ s/$man_placeholder ([^\n]*)/.TH SCOUT 1 $1 "$man_version" "User Commands"/g;
    $value =~ s/$attribution_placeholder/Scout ports ripgrep, originally authored by Andrew Gallant./g;
    $value =~ s/$url_placeholder/$repo_url/g;
    $value =~ s#__SCOUT_UPSTREAM_GUIDE_URL__#https://github.com/BurntSushi/ripgrep/blob/master/GUIDE.md#g;
    $value =~ s/^15\.1\.0 \(rev \Q$rg_rev\E\)$/$man_version/m;
    $value =~ s#\\fIhttps://github\.com/BurntSushi/Scout\\fP#\\fI$repo_url\\fP#g;
    $value =~ s#https://github\.com/BurntSushi/Scout/discussions#$repo_url/discussions#g;
    $value =~ s/^Andrew Gallant <\\fIjamslam\@gmail\.com\\fP>$/Scout ports ripgrep, originally authored by Andrew Gallant./m;
    return $value;
}

sub transform_bash {
    my ($value) = @_;
    $value =~ s/_rg/_scout/g;
    $value =~ s/(?<![A-Za-z0-9_])rg(?![A-Za-z0-9_])/scout/g;
    $value = replace_product_words($value);
    $value = replace_ignore_docs($value);
    $value = replace_config_docs($value);
    return $value;
}

sub transform_zsh {
    my ($value) = @_;
    $value =~ s#https://github\.com/BurntSushi/ripgrep/issues/2956#__SCOUT_UPSTREAM_ZSH_ISSUE__#g;
    $value =~ s#https://github\.com/BurntSushi/ripgrep/pull/2957#__SCOUT_UPSTREAM_ZSH_PULL__#g;
    $value =~ s/_rg/_scout/g;
    $value =~ s/_RG_COMPLETE_LIST_ARGS/_SCOUT_COMPLETE_LIST_ARGS/g;
    $value =~ s/(?<![A-Za-z0-9_])rg(?![A-Za-z0-9_])/scout/g;
    $value = replace_product_words($value);
    $value = replace_ignore_docs($value);
    $value = replace_config_docs($value);
    $value =~ s#__SCOUT_UPSTREAM_ZSH_ISSUE__#https://github.com/BurntSushi/ripgrep/issues/2956#g;
    $value =~ s#__SCOUT_UPSTREAM_ZSH_PULL__#https://github.com/BurntSushi/ripgrep/pull/2957#g;
    return $value;
}

sub transform_fish {
    my ($value) = @_;
    $value =~ s/if set -qx RIPGREP_CONFIG_PATH\n            set __rg_config \(\n                cat -- \$RIPGREP_CONFIG_PATH 2>\/dev\/null \\\n                \| string trim \\\n                \| string match -rv '\^\$\|\^#'\n            \)\n        end/if set -qx SCOUT_CONFIG_PATH\n            set __rg_config (\n                cat -- \$SCOUT_CONFIG_PATH 2>\/dev\/null \\\n                | string trim \\\n                | string match -rv '^\$|^#'\n            )\n        else if set -qx RIPGREP_CONFIG_PATH\n            set __rg_config (\n                cat -- \$RIPGREP_CONFIG_PATH 2>\/dev\/null \\\n                | string trim \\\n                | string match -rv '^\$|^#'\n            )\n        end/g;
    $value =~ s/__rg/__scout/g;
    $value =~ s/(?<![A-Za-z0-9_])rg(?![A-Za-z0-9_])/scout/g;
    $value = replace_product_words($value);
    $value = replace_ignore_docs($value);
    $value = replace_config_docs($value);
    $value =~ s/else if set -qx SCOUT_CONFIG_PATH\n            set __scout_config \(\n                cat -- \$SCOUT_CONFIG_PATH/else if set -qx RIPGREP_CONFIG_PATH\n            set __scout_config (\n                cat -- \$RIPGREP_CONFIG_PATH/g;
    return $value;
}

sub transform_powershell {
    my ($value) = @_;
    $value =~ s/CommandName 'rg'/CommandName 'scout'/g;
    $value =~ s/(?<![A-Za-z0-9_])rg(?![A-Za-z0-9_])/scout/g;
    $value = replace_product_words($value);
    $value = replace_ignore_docs($value);
    $value = replace_config_docs($value);
    return $value;
}

if ($kind eq "complete-bash") {
    $text = transform_bash($text);
} elsif ($kind eq "complete-zsh") {
    $text = transform_zsh($text);
} elsif ($kind eq "complete-fish") {
    $text = transform_fish($text);
} elsif ($kind eq "complete-powershell") {
    $text = transform_powershell($text);
} elsif ($kind eq "help-short" || $kind eq "help-long" || $kind eq "man") {
    $text = transform_document($text);
} else {
    die "unsupported artifact kind: $kind\n";
}

print $text;
