"""Build the backend image with immutable, Git-derived release metadata."""
import argparse
import base64
from datetime import datetime, timedelta, timezone
import json
import os
from pathlib import Path
import subprocess
import uuid

ROOT = Path(__file__).resolve().parent.parent


def git(*args):
    return subprocess.check_output(
        ["git", *args], cwd=ROOT, encoding="utf-8", errors="strict"
    ).strip()


def create_metadata():
    now = datetime.now(timezone.utc)
    local_time = now.astimezone(timezone(timedelta(hours=8)))
    commit = git("rev-parse", "HEAD")
    # Read each message separately: commit messages may contain any delimiter.
    commits = []
    for sha in git("rev-list", "--max-count=2", "HEAD").splitlines():
        commits.append({
            "sha": sha,
            "author": git("show", "-s", "--format=%an", sha),
            "committedAt": git("show", "-s", "--format=%cI", sha),
            "message": git("show", "-s", "--format=%B", sha),
        })
    return {
        "schemaVersion": 1,
        "version": f"{local_time:%Y%m%d.%H%M%S}-{commit[:8]}-{uuid.uuid4().hex[:8]}",
        "builtAtUtc": now.isoformat(timespec="seconds"),
        "builtAtShanghai": local_time.isoformat(timespec="seconds"),
        "git": {
            "commit": commit,
            "branch": os.environ.get("GITHUB_REF_NAME") or git("branch", "--show-current") or "detached",
            "dirty": bool(git("status", "--porcelain")),
            "commits": commits,
        },
        "build": {
            "source": "github-actions" if os.environ.get("GITHUB_ACTIONS") == "true" else "local",
            "runId": os.environ.get("GITHUB_RUN_ID"),
            "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT"),
        },
    }


def encode_metadata(metadata):
    return base64.b64encode(
        json.dumps(metadata, ensure_ascii=False, indent=2).encode("utf-8")
    ).decode("ascii")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--metadata-only", action="store_true", help="Generate metadata without building Docker")
    parser.add_argument("--github-output", type=Path, help="Append build outputs for GitHub Actions")
    parser.add_argument("--output", type=Path, help="Save a local copy of version.json")
    parser.add_argument("--tag", help="Docker image tag (default: myblogs/app:<generated version>)")
    args = parser.parse_args()
    metadata = create_metadata()
    encoded = encode_metadata(metadata)
    if args.output:
        args.output.write_text(json.dumps(metadata, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.github_output:
        with args.github_output.open("a", encoding="utf-8") as output:
            output.write(f"version={metadata['version']}\n")
            output.write(f"commit={metadata['git']['commit']}\n")
            output.write(f"built-at={metadata['builtAtUtc']}\n")
            output.write(f"metadata-base64={encoded}\n")
    print(f"Release: {metadata['version']} (dirty={metadata['git']['dirty']})", flush=True)
    if not args.metadata_only:
        subprocess.run([
            "docker", "build", "--progress=plain", "-t", args.tag or f"myblogs/app:{metadata['version']}",
            "--build-arg", f"RELEASE_METADATA_BASE64={encoded}",
            "--build-arg", f"RELEASE_VERSION={metadata['version']}",
            "--build-arg", f"RELEASE_COMMIT={metadata['git']['commit']}",
            "--build-arg", f"RELEASE_BUILT_AT={metadata['builtAtUtc']}",
            ".",
        ], cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
