#!/usr/bin/env bash
set -euo pipefail

notes=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/release-notes.sh
temp_repo=$(mktemp -d)
trap 'rm -rf -- "$temp_repo"' EXIT
git init --quiet "$temp_repo"
cd -- "$temp_repo"
git config user.name 'Release notes tests'
git config user.email 'tests@example.invalid'
git config commit.gpgsign false
git config core.hooksPath /dev/null

commit() {
	git -c core.commentChar=';' commit --quiet --allow-empty --cleanup=verbatim -m "$1"
}

expect() {
	local expected=$1 label=$2 output
	shift 2
	output=$(bash "$notes" "$@")
	if [[ $output != "$expected" ]]; then
		printf 'FAIL %s\nExpected:\n%s\nActual:\n%s\n' "$label" "$expected" "$output" >&2
		exit 1
	fi
	printf 'ok %s\n' "$label"
}

commit 'chore: initialize'
base=$(git rev-parse HEAD)
commit $'feat(cli): add exports (#12)\n\nRefs #7'
feature=$(git rev-parse HEAD)
commit $'docs: quote $HOME and `gig` literally (#13)\n\nRefs #7'
commit $'fix(server): preserve intervals (#14)\n\nRefs #7'
commit $'feat(cli)!: replace output (#15)\n\nBREAKING CHANGE: a new format\nRefs #7'
commit $'fix: change defaults (#16)\n\nBREAKING CHANGE: new defaults\nRefs #7'
commit $'refactor: change protocol (#17)\n\nBREAKING-CHANGE: new protocol\nRefs #7'
last=$(git rev-parse HEAD)
expected=$'## Breaking changes\n\n- feat(cli)!: replace output (#15)\n- fix: change defaults (#16)\n- refactor: change protocol (#17)\n\n## Features\n\n- feat(cli): add exports (#12)\n\n## Fixes\n\n- fix(server): preserve intervals (#14)\n\n## Other changes\n\n- docs: quote $HOME and `gig` literally (#13)'
expect "$expected" 'range, breaking forms, ordering, scopes, PR numbers and literal shell characters' "$base" "$last"
expect $'## Features\n\n- feat(cli): add exports (#12)\n\n## Other changes\n\n- chore: initialize' 'no previous tag includes the whole history' '' "$feature"
expect $'## Features\n\n- feat(cli): add exports (#12)' 'exclude the previous commit' "$base" "$feature"
expect 'No changes.' 'empty range' "$last" "$last"

# #7: exercise real tag arguments without creating tags in the working checkout.
git tag v0.1.0 "$base"
git tag -a v0.2.0 "$last" -m 'Version fixture'
expect "$expected" 'lightweight and annotated tags' v0.1.0 v0.2.0

for arguments in missing invalid-current invalid-previous option-like; do
	status=0
	case $arguments in
		missing) bash "$notes" >/dev/null 2>&1 || status=$?; expected_status=2 ;;
		invalid-current) bash "$notes" '' no-such-tag >/dev/null 2>&1 || status=$?; expected_status=128 ;;
		invalid-previous) bash "$notes" no-such-tag HEAD >/dev/null 2>&1 || status=$?; expected_status=128 ;;
		option-like) bash "$notes" '' --help >/dev/null 2>&1 || status=$?; expected_status=128 ;;
	esac
	[[ $status == "$expected_status" ]] || { printf 'FAIL %s: exit %s\n' "$arguments" "$status" >&2; exit 1; }
	printf 'ok %s\n' "$arguments"
done

# #7: merged branches contribute their reviewed title once, not their development commits.
main_branch=$(git symbolic-ref --short HEAD)
merge_base=$(git rev-parse HEAD)
git checkout --quiet -b merged-topic
commit 'fix: intermediate branch change'
commit 'feat: another intermediate branch change'
git checkout --quiet "$main_branch"
git merge --quiet --no-ff merged-topic -m $'Merge pull request #42 from tests/merged-topic\n\nfeat(cli): ship the reviewed change\n\nRefs #7'
expect $'## Features\n\n- feat(cli): ship the reviewed change (#42)' 'first-parent merge title, PR number and no branch noise' "$merge_base" HEAD

# A squash has one parent; even if its source branch remains locally, only its public subject appears.
squash_base=$(git rev-parse HEAD)
git checkout --quiet -b squash-topic
printf 'first step\n' > change
git add change
commit 'feat: private pre-squash step'
printf 'second step\n' >> change
git add change
commit 'fix: private pre-squash repair'
git checkout --quiet "$main_branch"
git merge --quiet --squash squash-topic
commit $'fix(cli): ship the squashed change (#43)\n\nRefs #7'
expect $'## Fixes\n\n- fix(cli): ship the squashed change (#43)' 'pre-squash branch history stays out of published notes' "$squash_base" HEAD
