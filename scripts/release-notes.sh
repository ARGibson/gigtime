#!/usr/bin/env bash
set -euo pipefail

if (( $# != 2 )) || [[ -z $2 ]]; then
	printf 'Usage: scripts/release-notes.sh <previous-tag-or-empty> <tag>\n' >&2
	exit 2
fi

current=$(git rev-parse --verify --end-of-options "$2^{commit}")
range=$current
if [[ -n $1 ]]; then
	previous=$(git rev-parse --verify --end-of-options "$1^{commit}")
	range="$previous..$current"
fi

breaking=()
features=()
fixes=()
other=()
while IFS= read -r commit; do
	subject=$(git show -s --format=%s "$commit")
	body=$(git show -s --format=%b "$commit")
	if [[ $subject =~ ^[a-z]+(\([^\)]+\))?!: || $body =~ (^|$'\n')BREAKING[\ -]CHANGE: ]]; then
		breaking+=("$subject")
	elif [[ $subject =~ ^feat(\([^\)]+\))?: ]]; then
		features+=("$subject")
	elif [[ $subject =~ ^fix(\([^\)]+\))?: ]]; then
		fixes+=("$subject")
	else
		other+=("$subject")
	fi
done < <(git rev-list --reverse "$range")

section() {
	local heading=$1
	shift
	(( $# > 0 )) || return 0
	printf '## %s\n\n' "$heading"
	printf -- '- %s\n' "$@"
	printf '\n'
}

section 'Breaking changes' "${breaking[@]}"
section 'Features' "${features[@]}"
section 'Fixes' "${fixes[@]}"
section 'Other changes' "${other[@]}"
if (( ${#breaking[@]} + ${#features[@]} + ${#fixes[@]} + ${#other[@]} == 0 )); then
	printf 'No changes.\n'
fi
