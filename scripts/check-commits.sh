#!/usr/bin/env bash
set -euo pipefail

if [[ $# == 3 ]]; then
	base=$1
	head=$2
	branch=$3
elif [[ $# == 2 && $1 == *..* && $1 != *...* ]]; then
	base=${1%%..*}
	head=${1#*..}
	branch=$2
else
	printf 'Usage: %s <base> <head> <branch> (or <base>..<head> <branch>)\n' "$0" >&2
	exit 2
fi

base_sha=$(git rev-parse --verify --end-of-options "${base}^{commit}")
head_sha=$(git rev-parse --verify --end-of-options "${head}^{commit}")
types='feat|fix|docs|test|refactor|perf|build|ci|chore|style|revert|deps'
scopes='domain|contracts|platform|server|cli|tests|repo'
branch_pattern="^($types)/[1-9][0-9]*-[a-z0-9]+(-[a-z0-9]+)*$"
subject_pattern="^($types)(\(($scopes)\))?(!)?: ([a-z].*)$"
failures=0

fail() {
	printf 'FAIL %s: %s\n' "$1" "$2" >&2
	failures=$((failures + 1))
}

if [[ ! $branch =~ $branch_pattern ]]; then
	fail "$head_sha" "branch '$branch' must match <type>/<issue>-<slug>"
fi

while IFS= read -r commit; do
	message=$(git show -s --format=%B "$commit")
	subject=${message%%$'\n'*}
	breaking=false

	if [[ $subject =~ $subject_pattern ]]; then
		[[ ${BASH_REMATCH[4]} != '!' ]] || breaking=true
	else
		fail "$commit" 'subject must have an allowed type, optional scope/!, and a lowercase description'
	fi

	if (( ${#subject} > 72 )); then
		fail "$commit" 'subject exceeds 72 characters including its prefix'
	fi

	if [[ $subject == *. ]]; then
		fail "$commit" 'subject must not end with a full stop'
	fi

	# Footers form the final paragraph, separated from the subject/body by a blank line.
	footer=''
	[[ $message != *$'\n\n'* ]] || footer=${message##*$'\n\n'}
	refs=false
	breaking_footer=false
	while IFS= read -r line; do
		[[ ! $line =~ ^Refs\ \#[1-9][0-9]*$ ]] || refs=true
		[[ ! $line =~ ^BREAKING\ CHANGE:\ .+ ]] || breaking_footer=true
	done <<< "$footer"

	if [[ $refs != true ]]; then
		fail "$commit" 'final footer paragraph must contain Refs #<issue>'
	fi

	if [[ $breaking != "$breaking_footer" ]]; then
		fail "$commit" 'breaking changes require both ! and a BREAKING CHANGE: footer'
	fi
done < <(git rev-list --reverse "$base_sha..$head_sha")

if (( failures > 0 )); then
	exit 1
fi

printf 'Commit range and branch conventions passed.\n'
