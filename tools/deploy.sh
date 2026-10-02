#!/usr/bin/env bash
set -euo pipefail

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
fail() { echo "$*" >&2; exit 1; }
for command in git gh sha256sum cmp make; do
    command -v "$command" >/dev/null || fail "Required command missing: $command"
done
dotnet="${DOTNET:-dotnet}"
version="$("$dotnet" msbuild src/HudOverhaul.csproj -nologo -getProperty:Version)"
export RELEASE_TAG="${RELEASE_TAG:-$version}"
bash tools/check_release.sh
[[ -z "$(git status --porcelain)" ]] || fail 'Commit all source changes before deploying.'
head="$(git rev-parse HEAD)"
tag_commit="$(git rev-parse --verify "refs/tags/$RELEASE_TAG^{commit}")" || fail 'Create the release tag on the intended commit first.'
[[ "$tag_commit" == "$head" ]] || fail 'The release tag must point to the checked-out commit.'

# Resolve the destination from origin, not from a caller-supplied GH_REPO.
unset GH_REPO
remote="$(git remote get-url origin)"
repo="$(gh repo view "$remote" --json nameWithOwner --jq .nameWithOwner)"
[[ -n "$repo" ]] || fail 'Could not identify the origin repository.'
remote_refs="$(git ls-remote origin "refs/tags/$RELEASE_TAG" "refs/tags/$RELEASE_TAG^{}")"
remote_tag="$(awk '$2 !~ /\^\{\}$/ {print $1}' <<< "$remote_refs")"
if [[ -n "$remote_tag" ]]; then
    [[ "$remote_tag" == "$(git rev-parse "refs/tags/$RELEASE_TAG")" ]] || fail 'Remote tag differs from the local tag; refusing to overwrite it.'
fi
# Listing must succeed: authentication/network failures are not absent releases.
draft="$(gh api --paginate "repos/$repo/releases" --jq ".[] | select(.tag_name == \"$RELEASE_TAG\") | .draft")"
[[ "$draft" != false ]] || fail 'This version is already published. Use a new version and tag.'

make test
make package
[[ -z "$(git status --porcelain)" && "$(git rev-parse HEAD)" == "$head" ]] || fail 'Source changed during the build; refusing to publish.'
archive="$(cd "${PACKAGE_DIR:-bin}" && pwd)/Nivalis.HudOverhaul-$RELEASE_TAG.zip"
checksum="$archive.sha256"
(cd "$(dirname "$archive")" && sha256sum "$(basename "$archive")" > "$(basename "$checksum")")

if [[ -z "$remote_tag" ]]; then git push origin "refs/tags/$RELEASE_TAG"; fi
if [[ "$draft" == true ]]; then
    gh release upload "$RELEASE_TAG" "$archive" "$checksum" --repo "$repo" --clobber
else
    gh release create "$RELEASE_TAG" "$archive" "$checksum" --repo "$repo" \
        --draft --verify-tag --title "HUD Overhaul $RELEASE_TAG" --generate-notes
fi

# Verify the actual uploaded bytes before making the release public.
verify_dir="$(mktemp -d)"
trap 'rm -rf -- "$verify_dir"' EXIT
gh release download "$RELEASE_TAG" --repo "$repo" --dir "$verify_dir" \
    --pattern "$(basename "$archive")" --pattern "$(basename "$checksum")"
cmp "$checksum" "$verify_dir/$(basename "$checksum")"
(cd "$verify_dir" && sha256sum --check "$(basename "$checksum")")
gh release edit "$RELEASE_TAG" --repo "$repo" --draft=false
gh release view "$RELEASE_TAG" --repo "$repo" --json url --jq .url
