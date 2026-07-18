#!/usr/bin/env sh

# Decodes a SHA-256 value read from PREREQS.lock. Scalar values are expected to
# have already had their TOML quotes removed. Arrays retain their TOML syntax.
# Prints one approved hash per line and returns 2 when the value is malformed.
decode_sha256_set() (
    [ "$#" -eq 1 ] || exit 2

    SCOUT_SHA256_SET_VALUE="$1" LC_ALL=C awk '
        function invalid() {
            exit 2
        }

        function is_sha256(value) {
            return length(value) == 64 && value ~ /^[0-9a-f]+$/
        }

        function skip_blanks() {
            while (position <= value_length &&
                   substr(value, position, 1) ~ /[[:blank:]]/) {
                position++
            }
        }

        BEGIN {
            value = ENVIRON["SCOUT_SHA256_SET_VALUE"]
            if (index(value, "\n") != 0 || index(value, "\r") != 0) {
                invalid()
            }

            sub(/^[[:blank:]]*/, "", value)
            sub(/[[:blank:]]*$/, "", value)

            if (substr(value, 1, 1) != "[") {
                if (!is_sha256(value)) {
                    invalid()
                }

                print value
                exit 0
            }

            if (substr(value, length(value), 1) != "]") {
                invalid()
            }

            value = substr(value, 2, length(value) - 2)
            value_length = length(value)
            position = 1
            skip_blanks()
            if (position > value_length) {
                invalid()
            }

            while (position <= value_length) {
                if (substr(value, position, 1) != "\"") {
                    invalid()
                }

                position++
                start = position
                while (position <= value_length &&
                       substr(value, position, 1) != "\"") {
                    position++
                }
                if (position > value_length) {
                    invalid()
                }

                hash = substr(value, start, position - start)
                if (!is_sha256(hash) || seen[hash]) {
                    invalid()
                }

                seen[hash] = 1
                hashes[++hash_count] = hash
                position++
                skip_blanks()

                if (position > value_length) {
                    break
                }
                if (substr(value, position, 1) != ",") {
                    invalid()
                }

                position++
                skip_blanks()
                if (position > value_length) {
                    invalid()
                }
            }

            for (item_index = 1; item_index <= hash_count; item_index++) {
                print hashes[item_index]
            }
            exit 0
        }
    ' </dev/null
)

# Returns 0 when an exact hash is approved, 1 when a valid hash is not
# approved, and 2 when either argument is malformed.
sha256_set_contains() (
    [ "$#" -eq 2 ] || exit 2

    actual_sha256="$2"
    decoded_actual="$(decode_sha256_set "$actual_sha256")" || exit 2
    [ "$decoded_actual" = "$actual_sha256" ] || exit 2

    decoded_set="$(decode_sha256_set "$1")" || exit 2
    while IFS= read -r expected_sha256; do
        if [ "$actual_sha256" = "$expected_sha256" ]; then
            exit 0
        fi
    done <<EOF
$decoded_set
EOF

    exit 1
)
