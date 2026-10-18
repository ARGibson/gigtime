#!/usr/bin/env bash
set -euo pipefail

usage() {
	cat <<'HELP'
Usage: scripts/install.sh [--prefix DIR] [--bin-dir DIR] [--from DIR] [--force]

Build and install gigtime on Linux, or upgrade the current installation.
  --prefix DIR   Install root (default: $HOME/.local/lib/gigtime).
  --bin-dir DIR  Directory for the gig link (default: $HOME/.local/bin).
  --from DIR     Install an already-published gig and gigtimed directory.
  --force        Reinstall even when the clean label is already current.
  --help         Show this help.
HELP
}

fail() {
	printf 'install: %s\n' "$*" >&2
	exit 1
}

prefix=${HOME:?HOME is required}/.local/lib/gigtime
bin_dir=$HOME/.local/bin
source_dir=''
force=false
while (( $# )); do
	case $1 in
		--prefix|--bin-dir|--from)
			if (( $# < 2 )) || [[ -z $2 || $2 == --* ]]; then
				printf 'install: %s requires a directory.\n' "$1" >&2
				exit 2
			fi
			case $1 in
				--prefix) prefix=$2 ;;
				--bin-dir) bin_dir=$2 ;;
				--from) source_dir=$2 ;;
			esac
			shift 2 ;;
		--force) force=true; shift ;;
		--help) usage; exit 0 ;;
		*) printf 'install: unknown argument: %s\n' "$1" >&2; usage >&2; exit 2 ;;
	esac
done

[[ $(uname -s) == Linux ]] || fail 'Linux is required.'
root=$(realpath -m -- "$prefix")
bin_dir=$(realpath -m -- "$bin_dir")
data_home=${XDG_DATA_HOME:-}
[[ $data_home == /* ]] || data_home=$HOME/.local/share
data_home=$(realpath -m -- "$data_home/gigtime")
release_root=$(realpath -m -- "$root/releases")
for directory in "$root" "$bin_dir" "$release_root"; do
	case "$directory/" in
		"$data_home/"*) fail "Install paths cannot be inside the gigtime data directory (\$XDG_DATA_HOME/gigtime): $directory" ;;
	esac
done
case "$data_home/" in
	"$release_root/"*) fail "The gigtime data directory (\$XDG_DATA_HOME/gigtime) cannot be inside releases: $data_home" ;;
esac
[[ $root != / ]] || fail 'The install root cannot be /.'
case $root in
	*'"'*|*'\'*|*'$'*) fail "The install root contains an unsupported daemon-path character: $root" ;;
esac
[[ ! -e $root/current || -L $root/current ]] || fail "$root/current is not a symlink."
[[ ! -e $bin_dir/gig || -L $bin_dir/gig ]] || fail "$bin_dir/gig exists and is not a symlink."
case :${PATH:-}: in
	*:"$bin_dir":*) ;;
	*) printf 'Warning: %s is not on PATH.\n' "$bin_dir" >&2 ;;
esac

repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd -P)
clean_label=true
label=''
already_current() {
	[[ $force == false && $clean_label == true && -x $root/current/gig && -x $root/current/gigtimed
		&& $(readlink -- "$root/current") == "releases/$label" ]]
}

if [[ -z $source_dir ]]; then
	label=$(git -C "$repo" rev-parse HEAD)
	label=${label:0:7}
	if [[ -n $(git -C "$repo" status --porcelain --untracked-files=all) ]]; then
		label="$label-dirty-$(date -u +%Y%m%d%H%M%S)"
		clean_label=false
	fi
	if already_current; then
		printf 'Already current: %s; nothing to do.\n' "$label"
		exit 0
	fi
else
	source_dir=$(realpath -e -- "$source_dir")
	[[ -x $source_dir/gig && -x $source_dir/gigtimed ]] || fail '--from requires executable gig and gigtimed files.'
fi

mkdir -p -- "$root/releases" "$bin_dir"
stage=$root/releases/.staging-$$
release=''
backup=''
temporary_current=$root/.current-$$
temporary_bin=$bin_dir/.gig-$$
staged=false
current_link=false
bin_link=false
installed=false
switched=false
build_pid=''
build_dir=''
health_log=''
cleanup() {
	local status=$?
	trap - EXIT
	if [[ -n $build_pid ]]; then
		kill -KILL -- "-$build_pid" 2>/dev/null || true
		wait "$build_pid" 2>/dev/null || true
	fi
	if [[ -n $build_dir ]]; then rm -rf -- "$build_dir"; fi
	if [[ -n $health_log ]]; then rm -f -- "$health_log"; fi
	if [[ $switched == false ]]; then
		if [[ $installed == true ]]; then rm -rf -- "$release"; fi
		if [[ -n $backup ]]; then mv -T -- "$backup" "$release"; fi
	elif [[ -n $backup ]]; then
		rm -rf -- "$backup"
	fi
	if [[ $staged == true ]]; then rm -rf -- "$stage"; fi
	if [[ $current_link == true ]]; then rm -f -- "$temporary_current"; fi
	if [[ $bin_link == true ]]; then rm -f -- "$temporary_bin"; fi
	exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
mkdir -- "$stage"
staged=true

if [[ -n $source_dir ]]; then
	cp -a -- "$source_dir/." "$stage/"
	printf 'Staged published files from %s.\n' "$source_dir"
else
	# #22: native tooling cannot publish under a prefix containing '%'.
	build_dir=$(mktemp -d /tmp/gigtime-build.XXXXXXXX)
	build_log=$build_dir/build.log
	setsid bash -c '
		cd -- "$1" || exit "$?"
		export ArtifactsPath="$2/artifacts" UseArtifactsOutput=true
		dotnet restore --locked-mode || exit "$?"
		dotnet publish src/Gigtime.Cli -c Release -r linux-x64 -p:PublishAot=true --no-restore -o "$2/published" || exit "$?"
		dotnet publish src/Gigtime.Server -c Release --no-restore -o "$2/published" || exit "$?"
	' bash "$repo" "$build_dir" >"$build_log" 2>&1 &
	build_pid=$!
	if ! wait "$build_pid"; then
		cat -- "$build_log" >&2
		fail 'Build failed; current is unchanged.'
	fi
	build_pid=''
	cp -a -- "$build_dir/published/." "$stage/"
	rm -rf -- "$build_dir"
	build_dir=''
	printf 'Built checkout %s.\n' "$label"
fi

[[ -x $stage/gig && -x $stage/gigtimed ]] || fail 'The staged release is missing an executable.'
if ! version=$("$stage/gig" --version); then
	fail 'Version check failed; current is unchanged.'
fi
if [[ -n $source_dir ]]; then
	if [[ $version =~ \+([[:xdigit:]]{7,40})$ ]]; then
		label=${BASH_REMATCH[1]:0:7}
	else
		label="version-$(date -u +%Y%m%d%H%M%S)"
		clean_label=false
	fi
	if [[ $force == false && $clean_label == true && -d $root/current ]] && diff -qr -- "$stage" "$root/current/" >/dev/null; then
		printf 'Already current: %s; nothing to do.\n' "$label"
		exit 0
	fi
	if [[ $force == false && -d $root/releases/$label ]] && ! diff -qr -- "$stage" "$root/releases/$label/" >/dev/null; then
		label="$label-$(date -u +%Y%m%d%H%M%S)"
		[[ ! -e $root/releases/$label ]] || fail "Release label already exists: $label; retry in a second."
	fi
fi
printf 'Release: %s.\n' "$label"

# Prepare both links before changing the live release. Never replace an unrelated file.
ln -s -- "$root/current/gig" "$temporary_bin"
bin_link=true
ln -s -- "releases/$label" "$temporary_current"
current_link=true
release=$root/releases/$label
if [[ -e $release || -L $release ]]; then
	candidate_backup=$root/releases/.backup-$$
	[[ ! -e $candidate_backup && ! -L $candidate_backup ]] || fail "Backup path already exists: $candidate_backup"
	mv -T -- "$release" "$candidate_backup"
	backup=$candidate_backup
fi
mv -T -- "$stage" "$release"
staged=false
installed=true
touch -- "$release"
mv -Tf -- "$temporary_current" "$root/current"
current_link=false
switched=true
mv -Tf -- "$temporary_bin" "$bin_dir/gig"
bin_link=false
printf 'Switched current to %s.\n' "$label"

# Keep current plus the newest two alternatives; current was just touched above.
kept=1
while IFS= read -r -d '' entry; do
	old_label=${entry#* }
	[[ $old_label != "$label" ]] || continue
	if (( kept < 3 )); then
		kept=$((kept + 1))
	else
		rm -rf -- "$root/releases/$old_label"
	fi
done < <(find "$root/releases" -mindepth 1 -maxdepth 1 -type d ! -name '.*' -printf '%T@ %f\0' | sort -z -nr)

"$root/current/gig" setup --gigtimed "$root/current/gigtimed"
printf 'Setup complete.\n'
systemctl --user try-restart gigtimed.service
printf 'Requested service restart.\n'
# #22: setup probes allow 250 ms; socket activation needs several probes on a cold start.
health_log=$(mktemp /tmp/gigtime-health.XXXXXXXX)
if timeout 10s bash -c '
	until "$1" setup --check >"$2" 2>&1; do sleep 0.25; done
' bash "$root/current/gig" "$health_log"; then
	cat -- "$health_log"
	printf 'Health check passed.\n'
else
	status=$?
	[[ $status != 124 ]] || status=6
	cat -- "$health_log" >&2
	printf 'Health check failed; current remains %s.\n' "$label" >&2
	exit "$status"
fi
