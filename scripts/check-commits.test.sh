#!/usr/bin/env bash
set -euo pipefail

checker=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/check-commits.sh
temp_repo=$(mktemp -d)
trap 'rm -rf -- "$temp_repo"' EXIT
git init --quiet "$temp_repo"
cd -- "$temp_repo"
git config user.name 'Commit checker tests'
git config user.email 'tests@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null
git -c core.commentChar=';' commit --quiet --allow-empty -m 'test: initialize fixture'
base=$(git rev-parse HEAD)
branch=ci/6-github-actions

commit() {
	git -c core.commentChar=';' commit --quiet --allow-empty --cleanup=verbatim -m "$1"
}

expect() {
	local expected=$1 label=$2 output status=0
	shift 2
	output=$(bash "$checker" "$@" 2>&1) || status=$?
	if [[ $status != "$expected" ]]; then
		printf 'FAIL %s: expected %s, got %s\n%s\n' "$label" "$expected" "$status" "$output" >&2
		exit 1
	fi
	printf 'ok %s\n' "$label"
}

for type in feat fix docs test refactor perf build ci chore style revert deps; do
	commit "$type: exercise valid type"$'\n\nRefs #6'
done
for scope in domain contracts platform server cli tests repo; do
	commit "refactor($scope): exercise valid scope"$'\n\nRefs #6'
done
commit $'feat(domain)!: change the contract\n\nExplain the change.\n\nBREAKING CHANGE: clients use the new contract\nRefs #6'
commit $'fix!: change another contract\n\nRefs #6\nBREAKING CHANGE: callers use the new contract'
commit $'docs: describe ci.yml\n\nRefs #6'
commit $'fix: 32-character keys\n\nRefs #6'
commit $'fix: accept crlf messages\r\n\r\nDescribe the change.\r\n\r\nRefs #6\r\nCo-Authored-By: Test <tests@example.invalid>\r\n'
printf -v unicode_padding '%066d' 0
commit "ci: a$unicode_padding"$'\xe2\x80\x94\n\nRefs #6'
valid_head=$(git rev-parse HEAD)
expect 0 'all types, scopes, breaking forms and internal periods' "$base" "$valid_head" "$branch"
LC_ALL=C expect 0 'digit start, CRLF trailers and a 72-character em-dash subject under a byte-counting caller locale' "$base" "$valid_head" "$branch"
expect 0 'range argument' "$base..$valid_head" "$branch"
expect 0 'empty range' "$valid_head" "$valid_head" "$branch"

bad() {
	local label=$1 message=$2 previous
	previous=$(git rev-parse HEAD)
	commit "$message"
	expect 1 "$label" "$previous" HEAD "$branch"
}

bad 'unknown type' $'unknown: add workflow\n\nRefs #6'
bad 'unknown scope' $'ci(other): add workflow\n\nRefs #6'
bad 'uppercase subject' $'ci: Add workflow\n\nRefs #6'
bad 'trailing full stop' $'ci: add workflow.\n\nRefs #6'
bad 'missing separator space' $'ci:add workflow\n\nRefs #6'
bad 'empty description' $'ci: \n\nRefs #6'
bad 'empty scope' $'ci(): add workflow\n\nRefs #6'
bad 'missing footer' 'ci: add workflow'
bad 'refs in body only' $'ci: add workflow\n\nRefs #6\n\nThis is the final paragraph.'
bad 'trailers separated from refs by a blank line' $'ci: add workflow\n\nRefs #6\n\nCo-Authored-By: Test <tests@example.invalid>'
bad 'malformed footer' $'ci: add workflow\n\nRefs #nope'
bad 'missing breaking footer' $'ci!: add workflow\n\nRefs #6'
bad 'missing breaking marker' $'ci: add workflow\n\nBREAKING CHANGE: new interface\nRefs #6'
printf -v long '%069d' 0
bad 'subject over 72 characters' "ci: a$long"$'\n\nRefs #6'
bad 'em-dash subject over 72 characters' "ci: a${unicode_padding}x"$'\xe2\x80\x94\n\nRefs #6'
printf -v boundary '%067d' 0
previous=$(git rev-parse HEAD)
commit "ci: a$boundary"$'\n\nRefs #6'
expect 0 'subject exactly 72 characters' "$previous" HEAD "$branch"

for bad_branch in main ci/no-issue ci/6-UPPER ci/6-double--dash unknown/6-name ci/0-name ci/6-name/extra; do
	expect 1 "branch $bad_branch" "$base" "$valid_head" "$bad_branch"
done
expect 2 'usage'
expect 128 'invalid base' not-a-ref HEAD "$branch"

# The checker reports every bad commit, and every violated rule on each commit.
output=$(bash "$checker" "$valid_head" HEAD "$branch" 2>&1) && status=0 || status=$?
[[ $status == 1 ]]
while IFS= read -r hash; do
	message=$(git show -s --format=%s "$hash")
	[[ $message == "ci: a$boundary" ]] && continue
	[[ $output == *"FAIL $hash:"* ]] || { printf 'Missing failure for %s\n' "$hash" >&2; exit 1; }
done < <(git rev-list "$valid_head..HEAD")
previous=$(git rev-parse HEAD)
commit $'wrong: Uppercase.\n\nNo reference footer'
hash=$(git rev-parse HEAD)
output=$(bash "$checker" "$previous" HEAD "$branch" 2>&1) && status=0 || status=$?
[[ $status == 1 && $output == *"FAIL $hash: subject must have"* && $output == *"FAIL $hash: subject must not end"* && $output == *"FAIL $hash: final paragraph"* ]]
printf 'ok all failures include their commit hash and are collected\n'
