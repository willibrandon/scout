#!/bin/sh

validate_performance_metadata_value() {
    metadata_value="$1"
    metadata_label="$2"
    carriage_return="$(printf '\r_')"
    carriage_return="${carriage_return%_}"

    if [ -z "$metadata_value" ]; then
        printf 'The performance metadata value for %s must not be empty.\n' \
            "$metadata_label" >&2
        return 1
    fi
    case "$metadata_value" in
        *'
'*|*"$carriage_return"*)
            printf 'The performance metadata value for %s must be a single line.\n' \
                "$metadata_label" >&2
            return 1
            ;;
    esac

    printf '%s\n' "$metadata_value"
}

exec_clean_performance_gate() {
    tool_environment="$1"
    driver="$2"
    shift 2

    metadata_runner_name="$(validate_performance_metadata_value \
        "${RUNNER_NAME:-local}" "RUNNER_NAME")" || return 1
    metadata_image_os="$(validate_performance_metadata_value \
        "${ImageOS:-local}" "ImageOS")" || return 1
    metadata_image_version="$(validate_performance_metadata_value \
        "${ImageVersion:-local}" "ImageVersion")" || return 1

    exec /usr/bin/env -i \
        LANG="C" \
        LC_ALL="C" \
        PATH="/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin" \
        SCOUT_PERFORMANCE_GATE_BOOTSTRAPPED="1" \
        SCOUT_PERFORMANCE_GATE_IMAGE_OS="$metadata_image_os" \
        SCOUT_PERFORMANCE_GATE_IMAGE_VERSION="$metadata_image_version" \
        SCOUT_PERFORMANCE_GATE_RUNNER_NAME="$metadata_runner_name" \
        SCOUT_PERFORMANCE_GATE_TOOL_ENVIRONMENT="$tool_environment" \
        TZ="UTC" \
        "$driver" "$@"
}

sanitize_performance_environment() {
    dotnet_root="$1"
    if [ -z "$dotnet_root" ]; then
        printf 'The performance environment requires an isolated .NET root.\n' >&2
        return 1
    fi

    metadata_runner_name="$(validate_performance_metadata_value \
        "${SCOUT_PERFORMANCE_GATE_RUNNER_NAME:-local}" \
        "SCOUT_PERFORMANCE_GATE_RUNNER_NAME")" || return 1
    metadata_image_os="$(validate_performance_metadata_value \
        "${SCOUT_PERFORMANCE_GATE_IMAGE_OS:-local}" \
        "SCOUT_PERFORMANCE_GATE_IMAGE_OS")" || return 1
    metadata_image_version="$(validate_performance_metadata_value \
        "${SCOUT_PERFORMANCE_GATE_IMAGE_VERSION:-local}" \
        "SCOUT_PERFORMANCE_GATE_IMAGE_VERSION")" || return 1

    for variable in $(/usr/bin/env | /usr/bin/sed -n \
        -e 's/^\(ACTIONS_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(COMPlus_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(COREHOST_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(DOTNET_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(GIT_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(GITHUB_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(HOMEBREW_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(MSBUILD[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(Malloc[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(NUGET_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(PYTHON[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(RUNNER_[A-Za-z0-9_]*\)=.*/\1/p' \
        -e 's/^\(SCOUT_[A-Za-z0-9_]*\)=.*/\1/p'); do
        unset "$variable"
    done

    unset \
        AR \
        AS \
        BASH_ENV \
        CC \
        CFLAGS \
        C_INCLUDE_PATH \
        CDPATH \
        CI \
        CONFIG_SITE \
        CPATH \
        CPPFLAGS \
        CPLUS_INCLUDE_PATH \
        CURL_HOME \
        CXX \
        CXXFLAGS \
        DEVELOPER_DIR \
        DYLD_INSERT_LIBRARIES \
        DYLD_LIBRARY_PATH \
        DYLD_PRINT_STATISTICS \
        ENV \
        ImageOS \
        ImageVersion \
        LD \
        LDFLAGS \
        LIBRARY_PATH \
        MACOSX_DEPLOYMENT_TARGET \
        MAKEFLAGS \
        MFLAGS \
        MSBuildSDKsPath \
        NM \
        NuGetPackageRoot \
        OBJCOPY \
        OBJDUMP \
        RANLIB \
        RIPGREP_CONFIG_PATH \
        RestoreConfigFile \
        RestorePackagesPath \
        SDKROOT \
        STRIP \
        VIRTUAL_ENV

    export DOTNET_ROOT="$dotnet_root"
    export PATH="$DOTNET_ROOT:/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin"
    export DOTNET_CLI_TELEMETRY_OPTOUT="1"
    export DOTNET_CLI_HOME="$HOME"
    export DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1"
    export DOTNET_MULTILEVEL_LOOKUP="0"
    export DOTNET_NOLOGO="1"
    export DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
    export GIT_CONFIG_GLOBAL="/dev/null"
    export GIT_CONFIG_NOSYSTEM="1"
    export HOMEBREW_NO_ANALYTICS="1"
    export HOMEBREW_NO_AUTO_UPDATE="1"
    export HOMEBREW_NO_ENV_HINTS="1"
    export LANG="C"
    export LC_ALL="C"
    export NuGetAudit="false"
    export PYTHONDONTWRITEBYTECODE="1"
    export PYTHONNOUSERSITE="1"
    export PYTHONSAFEPATH="1"
    export SCOUT_PERFORMANCE_GATE_IMAGE_OS="$metadata_image_os"
    export SCOUT_PERFORMANCE_GATE_IMAGE_VERSION="$metadata_image_version"
    export SCOUT_PERFORMANCE_GATE_RUNNER_NAME="$metadata_runner_name"
    export TZ="UTC"
}
