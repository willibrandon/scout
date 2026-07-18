#!/bin/bash

performance_process_state_fail() {
    printf '%s\n' "$1" >&2
    return 1
}

parse_performance_nice_output() {
    performance_nice_output="$1"
    performance_nice="$(
        printf '%s\n' "$performance_nice_output" | /usr/bin/awk '
            {
                gsub(/^[[:space:]]+/, "")
                gsub(/[[:space:]]+$/, "")
                if ($0 !~ /^-?[0-9]+$/ || found) {
                    exit 1
                }
                value = $0
                found = 1
            }
            END {
                if (!found) {
                    exit 1
                }
                print value
            }
        '
    )" || {
        performance_process_state_fail \
            "Could not parse the process nice priority as an integer."
        return 1
    }

    printf '%s\n' "$performance_nice"
}

verify_performance_nice_output() {
    performance_nice="$(parse_performance_nice_output "$1")" ||
        return 1

    [ "$performance_nice" = "0" ] || {
        performance_process_state_fail \
            "The performance gate requires process nice priority 0; found $performance_nice."
        return 1
    }

    printf '%s\n' "$performance_nice"
}

set_performance_soft_nofile() {
    ulimit -S -n "$1"
}

read_performance_soft_nofile() {
    ulimit -S -n
}

read_performance_nice_output() {
    /bin/ps -o nice= -p "$$"
}

set_performance_nice() {
    /usr/bin/renice "$1" -p "$$" >/dev/null
}

configure_performance_process_state() {
    umask 0022
    performance_umask="$(umask)"
    case "$performance_umask" in
        0022|022)
            ;;
        *)
            performance_process_state_fail \
                "The performance gate could not set umask 0022; found $performance_umask."
            return 1
            ;;
    esac

    if ! set_performance_soft_nofile 1024; then
        performance_process_state_fail \
            "The performance gate could not set the soft nofile limit to 1024."
        return 1
    fi
    performance_soft_nofile="$(read_performance_soft_nofile)"
    [ "$performance_soft_nofile" = "1024" ] || {
        performance_process_state_fail \
            "The performance gate requires soft nofile 1024; found $performance_soft_nofile."
        return 1
    }

    performance_nice_output="$(read_performance_nice_output)" || {
        performance_process_state_fail \
            "The performance gate could not read its process nice priority with /bin/ps."
        return 1
    }
    performance_nice="$(parse_performance_nice_output "$performance_nice_output")" ||
        return 1

    if [ "$performance_nice" -lt 0 ]; then
        if ! set_performance_nice 0; then
            performance_process_state_fail \
                "The performance gate could not normalize process nice priority $performance_nice to 0."
            return 1
        fi
    elif [ "$performance_nice" -gt 0 ]; then
        performance_process_state_fail \
            "The performance gate cannot normalize inherited process nice priority $performance_nice to 0 without raising priority."
        return 1
    fi

    performance_nice_output="$(read_performance_nice_output)" || {
        performance_process_state_fail \
            "The performance gate could not verify its normalized process nice priority with /bin/ps."
        return 1
    }
    performance_nice="$(verify_performance_nice_output "$performance_nice_output")" ||
        return 1

    PERFORMANCE_PROCESS_UMASK="0022"
    PERFORMANCE_PROCESS_SOFT_NOFILE="$performance_soft_nofile"
    PERFORMANCE_PROCESS_NICE="$performance_nice"
    export \
        PERFORMANCE_PROCESS_UMASK \
        PERFORMANCE_PROCESS_SOFT_NOFILE \
        PERFORMANCE_PROCESS_NICE
}
