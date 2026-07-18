#!/usr/bin/env sh

evaluate_native_runtime_framework_version() {
    project="$1"
    rid="$2"
    version="$3"

    if ! evaluated_runtime_framework_version="$(
        dotnet msbuild "$project" \
            -nologo \
            -getProperty:RuntimeFrameworkVersion \
            -p:RuntimeIdentifier="$rid" \
            -p:Configuration=Release \
            -p:NativeLib=Static \
            -p:VersionPrefix="$version" \
            -p:Version="$version" \
            --disable-build-servers |
            awk '
                NF {
                    value = $0
                    sub(/\r$/, "", value)
                    print value
                    count++
                }
                END {
                    if (count != 1) {
                        exit 1
                    }
                }
            '
    )"; then
        printf 'Expected one evaluated RuntimeFrameworkVersion for %s.\n' "$project" >&2
        return 1
    fi
    case "$evaluated_runtime_framework_version" in
        ''|[!0-9]*|*[!0-9A-Za-z.+-]*)
            printf 'Invalid evaluated RuntimeFrameworkVersion: %s\n' \
                "$evaluated_runtime_framework_version" >&2
            return 1
            ;;
    esac

    printf '%s\n' "$evaluated_runtime_framework_version"
}

validate_nativeaot_runtime_pack() {
    project="$1"
    rid="$2"
    runtime_framework_version="$3"
    assets_file="$(dirname "$project")/obj/project.assets.json"
    package_name="Microsoft.NETCore.App.Runtime.NativeAOT.$rid"
    package_version="[$runtime_framework_version, $runtime_framework_version]"

    if [ ! -f "$assets_file" ]; then
        printf 'Missing restore assets file: %s\n' "$assets_file" >&2
        return 1
    fi

    if ! awk -v package_name="$package_name" -v package_version="$package_version" '
        index($0, "\"name\": \"" package_name "\"") {
            found_name = 1
            next
        }
        found_name && index($0, "\"version\": \"" package_version "\"") {
            found = 1
            exit
        }
        found_name && index($0, "\"name\": \"") {
            found_name = 0
        }
        END {
            if (!found) {
                exit 1
            }
        }
    ' "$assets_file"; then
        printf 'Restore assets do not request %s %s.\n' \
            "$package_name" "$package_version" >&2
        return 1
    fi
}

publish_native_app() {
    root="$1"
    rid="$2"
    version="$3"
    output="$4"
    project="$root/src/Scout.App/Scout.App.csproj"

    dotnet publish "$project" \
        -r "$rid" \
        -c Release \
        -p:NativeLib=Static \
        -p:RestoreDisableParallel=true \
        -p:VersionPrefix="$version" \
        -p:Version="$version" \
        -o "$output" \
        -m:1 \
        --disable-build-servers || return 1

    # shellcheck disable=SC2034 # Returned to the sourcing native build script.
    NATIVEAOT_RUNTIME_FRAMEWORK_VERSION="$(
        evaluate_native_runtime_framework_version "$project" "$rid" "$version"
    )" || return 1

    validate_nativeaot_runtime_pack \
        "$project" \
        "$rid" \
        "$NATIVEAOT_RUNTIME_FRAMEWORK_VERSION"
}
