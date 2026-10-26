#!/usr/bin/env python3
"""Compatibility corpus survey for the git-only resolution cut-over.

Enumerates every (package, variant, version) that lip can install today and
checks whether it is also resolvable through pure git (tag ``v<semver>`` on
``https://<path>.git`` + root ``tooth.json`` at that tag).

Sources surveyed:
  * the lipr index (https://lipr.levimc.org/index.json) - the levimc.org/mods list;
  * LiteLDev org repositories (non-fork) with a root ``tooth.json`` on the
    default branch, with all of their ``v<semver>`` tags.

Writes ``corpus.json`` and ``README.md`` next to this script.

Requirements: Python 3.10+ (stdlib only), ``git`` and an authenticated ``gh``
CLI on PATH. HTTP responses are cached under ``--cache`` (default: a
``lip-compat-survey`` directory in the system temp dir) so re-runs are cheap
and deterministic; pass ``--refresh`` to ignore the cache.

Usage:
  python survey.py [--cache DIR] [--refresh] [--jobs N]
"""

from __future__ import annotations

import argparse
import base64
import concurrent.futures as cf
import datetime as dt
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile
from dataclasses import dataclass, field
from pathlib import Path

LIPR_INDEX = "https://lipr.levimc.org/index.json"
LIPR_MANIFEST = "https://lipr.levimc.org/packages/{path}@{version}/tooth.json"
GOPROXY = "https://proxy.golang.org"
RAW = "https://raw.githubusercontent.com"
API = "https://api.github.com"
ORG = "LiteLDev"
USER_AGENT = "lip-compat-survey/1.0"

SEMVER_RE = re.compile(
    r"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)"
    r"(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?"
    r"(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$")
GLOB_CHARS = set("*?[{")


# --------------------------------------------------------------------------- utils

def semver_key(v: str):
    """Sort key implementing SemVer 2.0 precedence (build metadata ignored)."""
    m = SEMVER_RE.match(v)
    if not m:
        return (1, (0, 0, 0), (), v)
    major, minor, patch, pre, _ = m.groups()
    if pre is None:
        pre_key: tuple = ((2,),)  # release sorts after any pre-release
    else:
        pre_key = tuple((0, int(p), "") if p.isdigit() else (1, 0, p) for p in pre.split("."))
    return (0, (int(major), int(minor), int(patch)), pre_key, v)


def go_escape(s: str) -> str:
    """golang.org/x/mod/module.EscapePath / EscapeVersion: upper-case -> '!' + lower."""
    return "".join("!" + c.lower() if "A" <= c <= "Z" else c for c in s)


def go_version(version: str) -> str:
    """lip maps major >= 2 to +incompatible when talking to the Go module proxy."""
    m = SEMVER_RE.match(version)
    if m and int(m.group(1)) >= 2:
        return f"v{version}+incompatible"
    return f"v{version}"


def pkg_key(path: str, variant: str) -> str:
    return f"{path}#{variant}" if variant else path


# --------------------------------------------------------------------------- HTTP with on-disk cache

@dataclass
class Resp:
    status: int | None  # None = network error
    body: bytes | None
    final_url: str
    error: str = ""


class Http:
    def __init__(self, cache: Path, refresh: bool, gh_token: str):
        self.cache = cache / "http"
        self.cache.mkdir(parents=True, exist_ok=True)
        self.refresh = refresh
        self.gh_token = gh_token
        self._memo: dict[str, Resp] = {}
        self._locks: dict[str, threading.Lock] = {}
        self._guard = threading.Lock()

    def _path(self, method: str, url: str) -> Path:
        h = hashlib.sha256(f"{method} {url}".encode()).hexdigest()
        return self.cache / h[:2] / f"{h}.json"

    def request(self, url: str, method: str = "GET", allow_cache: bool = True) -> Resp:
        key = f"{method} {url}"
        with self._guard:
            lock = self._locks.setdefault(key, threading.Lock())
        with lock:  # de-duplicates concurrent requests for the same URL
            if key not in self._memo:
                self._memo[key] = self._request(url, method, allow_cache)
            return self._memo[key]

    def _request(self, url: str, method: str, allow_cache: bool) -> Resp:
        p = self._path(method, url)
        if allow_cache and not self.refresh and p.exists():
            d = json.loads(p.read_text(encoding="utf-8"))
            return Resp(d["status"], base64.b64decode(d["body"]) if d["body"] is not None else None,
                        d["final_url"], d.get("error", ""))
        resp = self._fetch(url, method)
        # Only cache definitive answers: success, or "does not exist" (403 may be a rate limit, so never cached).
        if allow_cache and resp.status is not None and (resp.status < 400 or resp.status in (400, 404, 410)):
            p.parent.mkdir(parents=True, exist_ok=True)
            tmp = p.with_suffix(f".{threading.get_ident()}.tmp")
            tmp.write_text(json.dumps({
                "url": url, "status": resp.status, "final_url": resp.final_url, "error": resp.error,
                "body": base64.b64encode(resp.body).decode() if resp.body is not None else None,
            }), encoding="utf-8")
            os.replace(tmp, p)
        return resp

    def _fetch(self, url: str, method: str) -> Resp:
        headers = {"User-Agent": USER_AGENT}
        if url.startswith(API):
            headers["Accept"] = "application/vnd.github+json"
            if self.gh_token:
                headers["Authorization"] = f"Bearer {self.gh_token}"
        last = Resp(None, None, url, "unknown")
        attempts = 5
        for attempt in range(attempts):
            final = attempt == attempts - 1
            req = urllib.request.Request(url, method=method, headers=headers)
            try:
                with urllib.request.urlopen(req, timeout=60) as r:
                    body = r.read() if method == "GET" else None
                    return Resp(r.status, body, r.geturl())
            except urllib.error.HTTPError as e:
                body = None
                try:
                    body = e.read() if method == "GET" else None
                except Exception:
                    pass
                last = Resp(e.code, body, e.geturl() or url, str(e))
                if final:
                    break
                if e.code == 403 and url.startswith(API):  # primary or secondary rate limit
                    if e.headers.get("X-RateLimit-Remaining") == "0":
                        reset = int(e.headers.get("X-RateLimit-Reset", time.time() + 60))
                        time.sleep(max(1, min(900, reset - time.time() + 1)))
                    else:
                        time.sleep(int(e.headers.get("Retry-After", 2 ** (attempt + 2))))
                    continue
                if e.code in (429, 500, 502, 503, 504):
                    time.sleep(2 ** attempt)
                    continue
                return last
            except Exception as e:  # URLError, timeouts, resets
                last = Resp(None, None, url, f"{type(e).__name__}: {e}")
                if not final:
                    time.sleep(2 ** attempt)
        return last

    def download(self, url: str, dest: Path) -> int | None:
        """Streams url to dest (cached by existence). Returns HTTP status or None."""
        with self._guard:
            lock = self._locks.setdefault(f"DOWNLOAD {url}", threading.Lock())
        with lock:
            return self._download(url, dest)

    def _download(self, url: str, dest: Path) -> int | None:
        if dest.exists() and (not self.refresh or f"DOWNLOADED {url}" in self._memo):
            return 200
        dest.parent.mkdir(parents=True, exist_ok=True)
        req = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(req, timeout=600) as r, open(dest.with_suffix(".part"), "wb") as f:
                while chunk := r.read(1 << 20):
                    f.write(chunk)
            os.replace(dest.with_suffix(".part"), dest)
            self._memo[f"DOWNLOADED {url}"] = Resp(200, None, url)
            return 200
        except urllib.error.HTTPError as e:
            return e.code
        except Exception:
            return None


# --------------------------------------------------------------------------- git

@dataclass
class Tag:
    name: str
    sha: str           # object the ref points to (tag object for annotated tags)
    commit: str        # peeled commit
    annotated: bool


def ls_remote_tags(cache: Path, refresh: bool, url: str) -> tuple[bool, str, dict[str, Tag]]:
    """Returns (ok, error, tags). Cached on disk."""
    p = cache / "git" / (hashlib.sha256(url.encode()).hexdigest() + ".json")
    if p.exists() and not refresh:
        d = json.loads(p.read_text(encoding="utf-8"))
        return d["ok"], d["error"], {k: Tag(**v) for k, v in d["tags"].items()}
    env = dict(os.environ, GIT_TERMINAL_PROMPT="0", GCM_INTERACTIVE="never", GIT_ASKPASS="", SSH_ASKPASS="")
    ok, err, out = False, "", ""
    for attempt in range(3):
        try:
            r = subprocess.run(
                ["git", "-c", "credential.helper=", "-c", "core.askPass=", "ls-remote", "--tags", url],
                capture_output=True, text=True, env=env, timeout=120, stdin=subprocess.DEVNULL)
            ok, out, err = r.returncode == 0, r.stdout, r.stderr.strip()
        except subprocess.TimeoutExpired:
            ok, out, err = False, "", "timeout"
        transient = ("Could not resolve host" in err or "timed out" in err or "timeout" in err
                     or "SSL" in err or "early EOF" in err or "Connection" in err)
        if ok or not transient or attempt == 2:
            break
        time.sleep(2 ** attempt)
    raw: dict[str, str] = {}
    for line in out.splitlines():
        if "\t" not in line:
            continue
        sha, ref = line.split("\t", 1)
        if ref.startswith("refs/tags/"):
            raw[ref[len("refs/tags/"):]] = sha
    tags: dict[str, Tag] = {}
    for name, sha in raw.items():
        if name.endswith("^{}"):
            continue
        peeled = raw.get(name + "^{}")
        tags[name] = Tag(name, sha, peeled or sha, peeled is not None)
    # Cache everything except a bare timeout: after three attempts a dead host (DNS/TLS failure) is treated as
    # definitive so cached re-runs are fast and deterministic. Use --refresh to re-probe.
    if err != "timeout":
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(json.dumps({"ok": ok, "error": err, "tags": {k: v.__dict__ for k, v in tags.items()}}),
                     encoding="utf-8")
    return ok, err, tags


# --------------------------------------------------------------------------- manifest analysis

@dataclass
class ManifestInfo:
    present: bool = False
    status: int | None = None
    valid_json: bool = False
    bom: bool = False
    format_version: object = None
    tooth: str | None = None
    version: str | None = None
    labels: list[str] = field(default_factory=list)
    self_glob_srcs: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)  # v3 content lip's deserializer would reject
    deps: dict[str, set[str]] = field(default_factory=dict)  # variant label -> dependency package paths
    parsed: object = None

    def deps_for(self, variant: str) -> set[str]:
        return self.deps.get(variant, set()) if self.declares(variant) else set()

    def declares(self, variant: str) -> bool:
        if not self.valid_json or self.errors:
            return False
        if self.format_version in (1, 2):
            return variant == ""  # migration emits label "" only
        return variant in self.labels


def _dict(x: object) -> dict:
    return x if isinstance(x, dict) else {}


def _list(x: object) -> list:
    return x if isinstance(x, list) else []


def analyse_manifest(status: int | None, body: bytes | None) -> ManifestInfo:
    mi = ManifestInfo(status=status)
    if status != 200 or body is None:
        return mi
    mi.present = True
    mi.bom = body.startswith(b"\xef\xbb\xbf")
    try:
        d = json.loads(body.decode("utf-8-sig"))
    except Exception:
        return mi
    if not isinstance(d, dict):
        return mi
    mi.valid_json = True
    mi.parsed = d
    fv = d.get("format_version")
    # bool is an int subclass in Python; lip's GetInt32() rejects true/false and non-integral numbers.
    mi.format_version = fv if isinstance(fv, int) and not isinstance(fv, bool) else None
    if mi.format_version not in (1, 2, 3):
        mi.errors.append(f"unsupported format_version {fv!r}")
    mi.tooth = d.get("tooth") if isinstance(d.get("tooth"), str) else None
    mi.version = d.get("version") if isinstance(d.get("version"), str) else None
    globs: list[str] = []

    def glob_src(pl: object, key: str) -> None:
        src = pl.get(key) if isinstance(pl, dict) else None
        if isinstance(src, str) and any(c in GLOB_CHARS for c in src):
            globs.append(src)

    if mi.format_version == 3:
        model = {k: v for k, v in d.items() if isinstance(v, (str, int, float))}
        if d.get("format_uuid") != "289f771f-2c9a-4d73-9f3f-8492495a924d":
            mi.errors.append("missing/unknown format_uuid")
        if not mi.tooth:
            mi.errors.append("missing tooth")
        if not mi.version:
            mi.errors.append("missing version")
        for v in _list(d.get("variants")):
            if not isinstance(v, dict):
                mi.errors.append("variant is not an object")
                continue
            label = v.get("label", "")
            if not isinstance(label, str) or not LABEL_RE.match(label):
                mi.errors.append(f"invalid variant label {label!r}")
                continue
            mi.labels.append(label)
            deps = v.get("dependencies")
            if deps is not None and not isinstance(deps, dict):
                mi.errors.append("dependencies is not an object")
            for dep, rng in _dict(deps).items():
                dep_path, _, dep_variant = dep.partition("#")
                mi.deps.setdefault(label, set()).add(dep_path)
                if not LABEL_RE.match(dep_variant) or "/" not in dep_path or "." not in dep_path.split("/")[0]:
                    mi.errors.append(f"invalid dependency id {dep!r}")
                rendered = TEMPLATE_RE.sub(lambda m: str(model.get(m.group(1), m.group(0))), str(rng))
                if not isinstance(rng, str) or not SEMVER3_RANGE_RE.match(rendered):
                    mi.errors.append(f"invalid range {dep}: {rng!r}")
            for a in _list(v.get("assets")):
                if isinstance(a, dict) and a.get("type") == "self":
                    for pl in _list(a.get("placements")):
                        glob_src(pl, "src")
    elif mi.format_version == 2:
        places = list(_list(_dict(d.get("files")).get("place")))
        dep_maps = [_dict(d.get("dependencies"))]
        for p in _list(d.get("platforms")):
            if isinstance(p, dict):
                places += _list(_dict(p.get("files")).get("place"))
                dep_maps.append(_dict(p.get("dependencies")))
        mi.deps[""] = {k.partition("#")[0] for dm in dep_maps for k in dm}
        for pl in places:
            glob_src(pl, "src")
    elif mi.format_version == 1:
        mi.deps[""] = {k.partition("#")[0] for k in _dict(d.get("dependencies"))}
        for pl in _list(d.get("placement")):
            glob_src(pl, "source")
    mi.labels = sorted(set(mi.labels))
    mi.errors = sorted(set(mi.errors))
    mi.self_glob_srcs = sorted(set(globs))
    return mi


_NUM = r"(?:0|[1-9]\d*)"
_PRE = r"(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?"
# Approximation of Semver 3.0.0 SemVersionRange.Parse (used by lip for v3 `dependencies`), cross-checked
# against the real library on every range string found in the surveyed manifests: full M.m.p versions or
# `*`-wildcard partials, optional =,<,<=,>,>=,^,~ operator (whitespace allowed after it), space = AND,
# `||` = OR. Rejected: `1.2.x`, `1.2`, `^1`, `v1.0.0`, `a - b`, build metadata, commas.
_VER = rf"(?:\*|{_NUM}\.\*(?:\.\*)?|{_NUM}\.{_NUM}\.\*|{_NUM}\.{_NUM}\.{_NUM}{_PRE})"
_CMP = rf"(?:(?:<=|>=|<|>|=|\^|~)\s*)?{_VER}"
SEMVER3_RANGE_RE = re.compile(rf"^\s*{_CMP}(?:\s+{_CMP})*(?:\s*\|\|\s*{_CMP}(?:\s+{_CMP})*)*\s*$")
LABEL_RE = re.compile(r"^([a-z0-9_]+(/[a-z0-9_]+)?)?$")

TEMPLATE_RE = re.compile(r"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}")
UNUSED_SCRIPT_KEYS = {"pre_pack", "post_pack"}  # not consumed by `lip install`
RANGE_WS_RE = re.compile(r"([<>=~^]+)\s+")


def normalize_v3(manifest: dict) -> dict:
    """Approximates what lip sees after ParseV3: `{{var}}` rendered from top-level scalars (Scriban model),
    `info` dropped (cosmetic), empty values and pack-only scripts dropped (lipr re-serialises with defaults)."""
    model = {k: v for k, v in manifest.items() if isinstance(v, (str, int, float))}

    def render(s: str) -> str:
        return TEMPLATE_RE.sub(lambda m: str(model.get(m.group(1), m.group(0))), s)

    def walk(v):
        if isinstance(v, str):
            return render(v)
        if isinstance(v, list):
            return [walk(x) for x in v]
        if isinstance(v, dict):
            out = {}
            for k, x in v.items():
                if k in UNUSED_SCRIPT_KEYS:
                    continue
                x = walk(x)
                if k == "dependencies" and isinstance(x, dict):
                    # lipr re-serialises ranges (">= 1.0.0" -> ">=1.0.0"); Semver 3 parses both identically.
                    x = {dk: RANGE_WS_RE.sub(r"\1", dv).strip() if isinstance(dv, str) else dv for dk, dv in x.items()}
                if x in ([], {}, "", None):
                    continue
                out[k] = x
            return out
        return v

    return {k: v for k, v in walk(manifest).items() if k not in ("info", "format_uuid", "$schema")}


# --------------------------------------------------------------------------- survey

@dataclass
class Package:
    path: str
    origin: set[str] = field(default_factory=set)        # "lipr-index" / "liteldev-org"
    index_variants: dict[str, list[str]] = field(default_factory=dict)
    repo_status: str = ""        # ok / renamed / case-mismatch / not-found / not-github
    canonical: str | None = None  # owner/repo as GitHub reports it
    archived: bool = False
    git_ok: bool = False
    git_error: str = ""
    tags: dict[str, Tag] = field(default_factory=dict)
    goproxy_list_status: int | None = None
    goproxy_versions: list[str] = field(default_factory=list)
    notes: list[str] = field(default_factory=list)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--cache", type=Path, default=Path(tempfile.gettempdir()) / "lip-compat-survey")
    ap.add_argument("--refresh", action="store_true", help="ignore cached responses")
    ap.add_argument("--jobs", type=int, default=16)
    ap.add_argument("--out", type=Path, default=Path(__file__).resolve().parent)
    args = ap.parse_args()

    token = subprocess.run(["gh", "auth", "token"], capture_output=True, text=True).stdout.strip()
    if not token:
        print("warning: `gh auth token` returned nothing; api.github.com calls are unauthenticated", file=sys.stderr)
    http = Http(args.cache, args.refresh, token)
    pool = cf.ThreadPoolExecutor(max_workers=args.jobs)

    def log(msg: str) -> None:
        print(msg, file=sys.stderr, flush=True)

    # ---- 1. lipr index
    r = http.request(LIPR_INDEX, allow_cache=False)
    if r.status != 200 or r.body is None:
        raise SystemExit(f"failed to fetch {LIPR_INDEX}: {r.status} {r.error}")
    index = json.loads(r.body.decode("utf-8"))
    packages: dict[str, Package] = {}
    for path, p in index["packages"].items():
        pkg = packages.setdefault(path, Package(path))
        pkg.origin.add("lipr-index")
        for label, v in p["variants"].items():
            pkg.index_variants[label] = list(v["versions"])
    log(f"lipr index: {len(packages)} packages")

    # ---- 2. LiteLDev org repos with a root tooth.json
    org_cache = args.cache / "liteldev-repos.json"
    if org_cache.exists() and not args.refresh:
        repos = json.loads(org_cache.read_text(encoding="utf-8"))
    else:
        out = subprocess.run(
            ["gh", "api", "--paginate", f"orgs/{ORG}/repos?per_page=100",
             "--jq", ".[] | {full_name, default_branch, fork, archived}"],
            capture_output=True, text=True, check=True).stdout
        repos = sorted((json.loads(l) for l in out.splitlines() if l.strip()), key=lambda x: x["full_name"].lower())
        org_cache.write_text(json.dumps(repos), encoding="utf-8")
    non_fork = [x for x in repos if not x["fork"]]

    def org_tooth(repo: dict) -> tuple[dict, ManifestInfo]:
        url = f"{RAW}/{repo['full_name']}/refs/heads/{urllib.parse.quote(repo['default_branch'])}/tooth.json"
        rr = http.request(url)
        return repo, analyse_manifest(rr.status, rr.body)

    org_with_tooth: list[tuple[str, ManifestInfo]] = []
    index_lower = {k.lower(): k for k in packages}
    for repo, mi in pool.map(org_tooth, non_fork):
        if not mi.present:
            continue
        path = f"github.com/{repo['full_name']}"
        org_with_tooth.append((path, mi))
        existing = index_lower.get(path.lower())
        pkg = packages.setdefault(existing or path, Package(existing or path))
        pkg.origin.add("liteldev-org")
        if mi.tooth and mi.tooth != path:
            pkg.notes.append(f"default-branch tooth.json declares tooth={mi.tooth!r}")
    log(f"LiteLDev: {len(repos)} repos, {len(non_fork)} non-fork, {len(org_with_tooth)} with root tooth.json")

    # ---- 3. per-package: GitHub metadata, git tags, goproxy list
    def survey_package(pkg: Package) -> Package:
        parts = pkg.path.split("/")
        if parts[0] != "github.com" or len(parts) != 3:
            pkg.repo_status = "not-github"
            pkg.notes.append(f"host {parts[0]!r} is not github.com (typo?)" if parts[0] != "github.com"
                             else "path is not github.com/<owner>/<repo>")
        else:
            rr = http.request(f"{API}/repos/{parts[1]}/{parts[2]}")
            if rr.status == 200 and rr.body:
                meta = json.loads(rr.body)
                pkg.canonical = meta["full_name"]
                pkg.archived = bool(meta.get("archived"))
                want = f"{parts[1]}/{parts[2]}"
                if pkg.canonical == want:
                    pkg.repo_status = "ok"
                elif pkg.canonical.lower() == want.lower():
                    pkg.repo_status = "case-mismatch"
                    pkg.notes.append(f"GitHub canonical name is {pkg.canonical} (case differs)")
                else:
                    pkg.repo_status = "renamed"
                    pkg.notes.append(f"repo redirects to {pkg.canonical} (renamed/transferred)")
                if pkg.archived:
                    pkg.notes.append("repo archived")
            elif rr.status == 404:
                pkg.repo_status = "not-found"
                pkg.notes.append("GitHub repo not found (deleted or private)")
            else:
                pkg.repo_status = f"api-error-{rr.status}"
                pkg.notes.append(f"GitHub API error {rr.status} {rr.error}")
        pkg.git_ok, pkg.git_error, pkg.tags = ls_remote_tags(args.cache, args.refresh, f"https://{pkg.path}.git")
        if not pkg.git_ok:
            pkg.notes.append("git ls-remote failed: " + pkg.git_error.splitlines()[-1][:160] if pkg.git_error
                             else "git ls-remote failed")
        gr = http.request(f"{GOPROXY}/{go_escape(pkg.path)}/@v/list")
        pkg.goproxy_list_status = gr.status
        if gr.status == 200 and gr.body is not None:
            vs = []
            for line in gr.body.decode("utf-8", "replace").split("\n"):
                line = line.strip()
                if line.startswith("v"):
                    v = line[1:].removesuffix("+incompatible")
                    if SEMVER_RE.match(v):
                        vs.append(v)
            pkg.goproxy_versions = sorted(set(vs), key=semver_key)
        return pkg

    list(pool.map(survey_package, sorted(packages.values(), key=lambda p: p.path)))
    log("package survey done")

    # ---- 4. enumerate (package, variant, version) entries
    @dataclass
    class Entry:
        pkg: Package
        variant: str
        version: str
        source: str

    entries: list[Entry] = []
    for pkg in packages.values():
        covered: set[str] = set()
        for label, versions in pkg.index_variants.items():
            for v in versions:
                entries.append(Entry(pkg, label, v, "lipr-index"))
                covered.add(v)
        if "liteldev-org" in pkg.origin:
            for tname in pkg.tags:
                if tname.startswith("v") and SEMVER_RE.match(tname[1:]) and tname[1:] not in covered:
                    entries.append(Entry(pkg, "\0", tname[1:], "liteldev-org"))  # variant resolved later

    # ---- 5. per-version checks
    zip_dir = args.cache / "zips"

    def tag_alternatives(pkg: Package, version: str) -> list[str]:
        alts = []
        m = SEMVER_RE.match(version)
        cands = {version, "V" + version}
        if m and m.group(4) is None and m.group(5) is None:
            ma, mi_, pa = m.group(1), m.group(2), m.group(3)
            if pa == "0":
                cands |= {f"v{ma}.{mi_}", f"{ma}.{mi_}"}
                if mi_ == "0":
                    cands |= {f"v{ma}", ma}
        lower = {t.lower(): t for t in pkg.tags}
        for c in sorted(cands):
            if c in pkg.tags:
                alts.append(c)
            elif c.lower() in lower and lower[c.lower()] != f"v{version}":
                alts.append(lower[c.lower()])
        return sorted(set(alts))

    def check(e: Entry) -> list[dict]:
        pkg, version = e.pkg, e.version
        repo = pkg.canonical or "/".join(pkg.path.split("/")[1:3])
        tag = pkg.tags.get(f"v{version}")
        # (b) tooth.json at tag
        tooth = ManifestInfo()
        if tag is not None and pkg.path.startswith("github.com/"):
            rr = http.request(f"{RAW}/{repo}/refs/tags/{urllib.parse.quote('v' + version)}/tooth.json")
            tooth = analyse_manifest(rr.status, rr.body)
        # (c)/(d) goproxy
        esc = go_escape(pkg.path)
        gv = go_escape(go_version(version))
        zip_head = http.request(f"{GOPROXY}/{esc}/@v/{gv}.zip", method="HEAD") \
            if pkg.goproxy_list_status == 200 or pkg.path.startswith("github.com/") else Resp(None, None, "")
        info_hash = None
        if zip_head.status == 200:
            ir = http.request(f"{GOPROXY}/{esc}/@v/{gv}.info")
            if ir.status == 200 and ir.body:
                try:
                    info_hash = (json.loads(ir.body).get("Origin") or {}).get("Hash")
                except Exception:
                    pass
        # lipr-hosted manifest (first manifest source lip consults today)
        lr = http.request(LIPR_MANIFEST.format(path=pkg.path, version=version))
        lipr = analyse_manifest(lr.status, lr.body)
        # tooth.json inside the goproxy zip: lip's second manifest source when lipr has none. When goproxy's
        # cached commit is the current tag commit the zip content equals the tag's, so skip the download.
        zip_tooth = ManifestInfo()
        same_as_tag = tag is not None and info_hash is not None and info_hash == tag.commit and tooth.status == 200
        if zip_head.status == 200 and not lipr.valid_json and same_as_tag:
            zip_tooth = tooth
        elif zip_head.status == 200 and not lipr.valid_json:
            zp = zip_dir / hashlib.sha256(f"{esc}@{gv}".encode()).hexdigest()[:24] / "m.zip"
            if http.download(f"{GOPROXY}/{esc}/@v/{gv}.zip", zp) == 200:
                try:
                    with zipfile.ZipFile(zp) as z:
                        name = next((n for n in z.namelist() if re.fullmatch(r"[^@]+@v[^/]+/tooth\.json", n)), None)
                        zip_tooth = analyse_manifest(200 if name else 404, z.read(name) if name else None)
                except zipfile.BadZipFile:
                    pass

        variants = [e.variant]
        if e.variant == "\0":  # LiteLDev tag not in index: use labels declared at the tag
            src = tooth if tooth.valid_json else zip_tooth
            variants = (src.labels or [""]) if src.format_version == 3 else [""]

        out = []
        for variant in variants:
            notes: list[str] = []
            # Today's behaviour (LipClient): versions from goproxy list (fallback lipr, then git);
            # manifest from lipr, then goproxy zip, then git clone; files from goproxy zip, then git clone.
            if pkg.goproxy_list_status == 200:
                listed = version in pkg.goproxy_versions
            elif variant in pkg.index_variants:
                listed = version in pkg.index_variants[variant]
            else:
                listed = tag is not None
            source_ok = zip_head.status == 200 or tag is not None
            # lipr first; else the goproxy zip (SourceService tries goproxy before git); else the git clone.
            manifest_today = lipr if lipr.valid_json else (zip_tooth if zip_head.status == 200 else tooth)
            baseline = listed and source_ok and manifest_today.declares(variant)

            git_tag = tag is not None
            git_ok = git_tag and tooth.valid_json and tooth.declares(variant)
            # notes
            if not listed:
                notes.append("version not in goproxy @v/list" if pkg.goproxy_list_status == 200
                             else "version not listed by any registry")
            if listed and source_ok and not manifest_today.declares(variant):
                src_name = "lipr" if manifest_today is lipr else ("goproxy zip" if manifest_today is zip_tooth else "git tag")
                why = "; ".join(manifest_today.errors[:2]) if manifest_today.errors else (
                    "missing/invalid" if not manifest_today.valid_json else f"labels {manifest_today.labels}")
                notes.append(f"today's manifest ({src_name}) unusable for variant {variant!r}: {why}")
            if zip_head.status != 200:
                notes.append(f"goproxy zip HTTP {zip_head.status}" if zip_head.status
                             else "goproxy zip unavailable (" + (zip_head.error or "not a github.com path") + ")")
            if not git_tag:
                alts = tag_alternatives(pkg, version)
                notes.append(f"no tag v{version}" + (f" (similar tags: {', '.join(alts)})" if alts else ""))
            else:
                if tag.annotated:
                    notes.append("annotated tag")
                if info_hash and info_hash != tag.commit:
                    notes.append(f"tag moved: goproxy cached {info_hash[:10]}, git tag -> {tag.commit[:10]}")
                if not tooth.present:
                    notes.append(f"no root tooth.json at tag (HTTP {tooth.status})")
                elif not tooth.valid_json:
                    notes.append("tooth.json at tag is not valid JSON")
                else:
                    if tooth.bom:
                        notes.append("tooth.json has UTF-8 BOM")
                    if tooth.format_version != 3:
                        notes.append(f"format_version {tooth.format_version} (needs migration)")
                    if tooth.errors:
                        notes.append("tooth.json at tag rejected by lip's manifest parser: " + "; ".join(tooth.errors[:3]))
                    elif not tooth.declares(variant):
                        notes.append(f"variant {variant!r} not declared at tag (labels: {tooth.labels})")
                    if tooth.tooth != pkg.path:
                        if tooth.tooth and tooth.tooth.lower() == pkg.path.lower():
                            notes.append(f"tooth path case mismatch: {tooth.tooth}")
                        else:
                            notes.append(f"tooth path mismatch: {tooth.tooth}")
                    if tooth.version is not None and tooth.version.lstrip("v") != version:
                        notes.append(f"manifest version {tooth.version!r} != tag")
                    if tooth.self_glob_srcs:
                        notes.append("self placements with globs: " + ", ".join(tooth.self_glob_srcs[:3]))
            # lipr re-hosts (and sometimes rewrites) manifests; `info` edits are cosmetic, anything else
            # (variants, dependencies, assets, tooth, version) changes what gets installed.
            lipr_differs = None
            if lipr.valid_json and tooth.valid_json and lipr.format_version == 3 and tooth.format_version == 3:
                ln, tn = normalize_v3(lipr.parsed), normalize_v3(tooth.parsed)
                lipr_differs = ln != tn
                if lipr_differs and git_ok and baseline:
                    keys = sorted(k for k in set(ln) | set(tn) if ln.get(k) != tn.get(k))
                    notes.append("lipr-hosted manifest differs from tag in: " + ", ".join(keys))
            out.append({
                "package": pkg_key(pkg.path, variant),
                "version": version,
                "baseline_installable": baseline,
                "git_tag": git_tag,
                "tooth_json_at_tag": tooth.valid_json,
                "git_resolvable": git_ok,
                "risk": baseline and not git_ok,
                "notes": "; ".join(notes),
                "source": e.source,
                "detail": {
                    "goproxy_listed": pkg.goproxy_list_status == 200 and version in pkg.goproxy_versions,
                    "goproxy_zip": zip_head.status == 200,
                    "lipr_manifest": lipr.valid_json,
                    "lipr_manifest_differs": lipr_differs,
                    "annotated_tag": bool(tag and tag.annotated),
                    "tag_moved": bool(tag and info_hash and info_hash != tag.commit),
                    "format_version": tooth.format_version if tooth.valid_json else None,
                    "variant_declared_at_tag": tooth.declares(variant),
                    "manifest_errors_at_tag": tooth.errors,
                    "tooth_path_at_tag": tooth.tooth,
                },
                "_deps_today": manifest_today.deps_for(variant) if baseline else set(),
                "_deps_tag": tooth.deps_for(variant) if git_ok else set(),
            })
        return out

    done = 0
    results: list[dict] = []
    futs = [pool.submit(check, e) for e in entries]
    for f in cf.as_completed(futs):
        results.extend(f.result())
        done += 1
        if done % 200 == 0:
            log(f"  checked {done}/{len(entries)} versions")

    # ---- 5b. direct dependencies: is every dependency *path* still resolvable? (versions not checked)
    def dep_status(path: str) -> tuple[str, dict]:
        if path in packages:  # already ls-remoted in step 3
            ok, tags = packages[path].git_ok, packages[path].tags
        else:
            ok, _, tags = ls_remote_tags(args.cache, args.refresh, f"https://{path}.git")
        git = ok and any(t.startswith("v") and SEMVER_RE.match(t[1:]) for t in tags)
        gr = http.request(f"{GOPROXY}/{go_escape(path)}/@v/list")
        if gr.status == 200:  # CompositePackageRegistry stops at the first registry that answers
            listed = sorted({ln.strip()[1:].removesuffix("+incompatible")
                             for ln in (gr.body or b"").decode("utf-8", "replace").split("\n")
                             if ln.strip().startswith("v") and SEMVER_RE.match(ln.strip()[1:].removesuffix("+incompatible"))},
                            key=semver_key, reverse=True)
            # A listed version is only usable if its files can be fetched: goproxy zip, else git clone.
            today = git or any(
                http.request(f"{GOPROXY}/{go_escape(path)}/@v/{go_escape(go_version(v))}.zip",
                             method="HEAD").status == 200 for v in listed)
        else:
            today = path in index["packages"] or git
        return path, {"git": git, "today": today}

    dep_paths = sorted({d for r in results for d in r["_deps_today"] | r["_deps_tag"]})
    dep_st = dict(pool.map(dep_status, dep_paths))
    pool.shutdown()
    for r in results:
        today_bad = sorted(d for d in r.pop("_deps_today") if not dep_st[d]["today"])
        git_bad = sorted(d for d in r.pop("_deps_tag") if not dep_st[d]["git"])
        notes = [r["notes"]] if r["notes"] else []
        if today_bad:
            r["baseline_installable"] = False
            notes.append("dependency not resolvable today: " + ", ".join(today_bad))
        if git_bad:
            notes.append("dependency not resolvable via git: " + ", ".join(git_bad))
        r["notes"] = "; ".join(notes)
        r["git_deps_resolvable"] = r["git_resolvable"] and not git_bad
        r["risk"] = r["baseline_installable"] and not r["git_deps_resolvable"]
        r["detail"]["unresolvable_deps_via_git"] = git_bad
    log(f"dependency survey done ({len(dep_paths)} paths)")

    def entry_sort(x: dict):
        path, _, variant = x["package"].partition("#")
        return (path.lower(), path, variant, semver_key(x["version"]))
    results.sort(key=entry_sort)

    # ---- 6. package-level reconciliation (c)
    pkg_out = {}
    for pkg in sorted(packages.values(), key=lambda p: p.path.lower()):
        git_versions = sorted({t[1:] for t in pkg.tags if t.startswith("v") and SEMVER_RE.match(t[1:])},
                              key=semver_key)
        non_semver = sorted(t for t in pkg.tags if not (t.startswith("v") and SEMVER_RE.match(t[1:])))
        idx = sorted({v for vs in pkg.index_variants.values() for v in vs}, key=semver_key)
        pkg_out[pkg.path] = {
            "origin": sorted(pkg.origin),
            "repo_status": pkg.repo_status,
            "canonical": f"github.com/{pkg.canonical}" if pkg.canonical else None,
            "archived": pkg.archived,
            "git_ls_remote_ok": pkg.git_ok,
            "goproxy_list_status": pkg.goproxy_list_status,
            "goproxy_only_versions": [v for v in pkg.goproxy_versions if v not in git_versions],
            "git_only_versions": [v for v in git_versions if v not in pkg.goproxy_versions]
            if pkg.goproxy_list_status == 200 else [],
            "index_versions_without_tag": [v for v in idx if v not in git_versions],
            "non_semver_tags": non_semver[:20] + (["..."] if len(non_semver) > 20 else []),
            "notes": "; ".join(sorted(set(pkg.notes))),
        }

    corpus = {
        "generated_at": dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat(),
        "generator": "tests/Lip.Core.RegressionTests/Compatibility/survey.py",
        "sources": {
            "lipr_index": LIPR_INDEX,
            "liteldev_org": f"{API}/orgs/{ORG}/repos (non-fork, root tooth.json on default branch)",
            "git": "git ls-remote --tags https://<path>.git",
            "tooth_json": f"{RAW}/<owner>/<repo>/refs/tags/v<version>/tooth.json",
            "goproxy": f"{GOPROXY}/<escaped path>/@v/list, @v/<version>.zip (HEAD), @v/<version>.info",
            "lipr_manifest": LIPR_MANIFEST,
        },
        "definitions": {
            "baseline_installable": "lip today (goproxy/lipr/git composite) can resolve this version, fetch the "
                                    "package files (goproxy zip or git clone) and read a manifest declaring the "
                                    "variant (lipr-hosted, else tooth.json in the zip / at the tag), and every "
                                    "direct dependency path of that manifest resolves to some version",
            "git_tag": "refs/tags/v<version> exists on https://<path>.git",
            "tooth_json_at_tag": "root tooth.json exists at the tag and is valid JSON",
            "git_resolvable": "git_tag && tooth_json_at_tag && the manifest passes lip's parser/migration and "
                              "declares the variant",
            "git_deps_resolvable": "git_resolvable && every direct dependency path in the tag's manifest has at "
                                   "least one v<semver> tag in git (dependency version ranges are not checked)",
            "risk": "baseline_installable && !git_deps_resolvable (regression if goproxy/lipr are dropped)",
        },
        "counts": {},
        "unresolvable_dependency_paths": {d: s for d, s in dep_st.items() if not (s["git"] and s["today"])},
        "packages": pkg_out,
        "entries": results,
    }
    corpus["counts"] = counts(results, pkg_out, len(repos), len(non_fork), len(org_with_tooth))
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "corpus.json").write_text(dump_corpus(corpus), encoding="utf-8", newline="\n")
    (args.out / "README.md").write_text(render_readme(corpus), encoding="utf-8", newline="\n")
    log(f"wrote {args.out / 'corpus.json'} and README.md")
    return 0


def dump_corpus(corpus: dict) -> str:
    """Pretty top level, one line per package / entry (keeps the file small and diff-friendly)."""
    def one(v) -> str:
        return json.dumps(v, ensure_ascii=False, separators=(", ", ": "))
    out = ["{"]
    keys = list(corpus)
    for i, k in enumerate(keys):
        comma = "," if i < len(keys) - 1 else ""
        v = corpus[k]
        if k == "entries":
            body = ",\n".join("    " + one(e) for e in v)
            out.append(f'  "{k}": [\n{body}\n  ]{comma}')
        elif k == "packages":
            body = ",\n".join(f"    {one(pk)}: {one(pv)}" for pk, pv in v.items())
            out.append(f'  "{k}": {{\n{body}\n  }}{comma}')
        else:
            out.append(f'  "{k}": ' + json.dumps(v, ensure_ascii=False, indent=2).replace("\n", "\n  ") + comma)
    out.append("}")
    return "\n".join(out) + "\n"


def counts(results: list[dict], pkgs: dict, n_repos: int, n_non_fork: int, n_org_tooth: int) -> dict:
    def c(pred):
        return sum(1 for r in results if pred(r))
    return {
        "packages": len(pkgs),
        "packages_from_lipr_index": sum(1 for p in pkgs.values() if "lipr-index" in p["origin"]),
        "packages_from_liteldev_org": sum(1 for p in pkgs.values() if "liteldev-org" in p["origin"]),
        "liteldev_repos_total": n_repos,
        "liteldev_repos_non_fork": n_non_fork,
        "liteldev_repos_with_tooth_json": n_org_tooth,
        "entries": len(results),
        "entries_from_lipr_index": c(lambda r: r["source"] == "lipr-index"),
        "baseline_installable": c(lambda r: r["baseline_installable"]),
        "git_tag": c(lambda r: r["git_tag"]),
        "tooth_json_at_tag": c(lambda r: r["tooth_json_at_tag"]),
        "git_resolvable": c(lambda r: r["git_resolvable"]),
        "git_deps_resolvable": c(lambda r: r["git_deps_resolvable"]),
        "regression_candidates": c(lambda r: r["baseline_installable"] and r["git_tag"]),
        "regression_candidates_expected_to_pass": c(lambda r: r["baseline_installable"] and r["git_deps_resolvable"]),
        "risk": c(lambda r: r["risk"]),
        "preexisting_broken": c(lambda r: not r["baseline_installable"]),
        "git_only_gain": c(lambda r: r["git_deps_resolvable"] and not r["baseline_installable"]),
        "annotated_tags": c(lambda r: r["detail"]["annotated_tag"]),
        "non_v3_manifests_at_tag": c(lambda r: r["tooth_json_at_tag"] and r["detail"]["format_version"] != 3),
        "tooth_path_mismatch_at_tag": c(lambda r: r["tooth_json_at_tag"]
                                        and r["detail"]["tooth_path_at_tag"] != r["package"].split("#")[0]),
        "manifest_rejected_at_tag": c(lambda r: bool(r["detail"]["manifest_errors_at_tag"])),
        "manifest_version_mismatch_at_tag": c(lambda r: "manifest version " in r["notes"]),
        "self_placement_globs_at_tag": c(lambda r: "self placements with globs" in r["notes"]),
        "renamed_packages": sum(1 for p in pkgs.values() if p["repo_status"] == "renamed"),
        "case_mismatch_packages": sum(1 for p in pkgs.values() if p["repo_status"] == "case-mismatch"),
        "tags_moved_since_goproxy_cached": c(lambda r: r["detail"]["tag_moved"]),
        "lipr_manifest_semantically_differs": c(lambda r: bool(r["detail"]["lipr_manifest_differs"])),
    }


def md_escape(s: str) -> str:
    return s.replace("|", "\\|").replace("\n", " ")


def risk_cause(r: dict, pkg: dict) -> str:
    if pkg["repo_status"] == "not-found":
        return "repo deleted/private"
    if not r["git_tag"]:
        return "tag missing in git"
    if not r["tooth_json_at_tag"]:
        return "no valid tooth.json at tag"
    if r["detail"]["manifest_errors_at_tag"]:
        return "tooth.json at tag rejected by lip's manifest parser"
    if not r["detail"]["variant_declared_at_tag"]:
        return "variant not declared in tooth.json at tag"
    if r["detail"]["unresolvable_deps_via_git"]:
        return "direct dependency not resolvable via git: " + ", ".join(r["detail"]["unresolvable_deps_via_git"])
    return "other"


def render_readme(corpus: dict) -> str:
    c = corpus["counts"]
    pk = corpus["packages"]
    E = corpus["entries"]
    L: list[str] = []
    a = L.append
    a("# Compatibility corpus: goproxy/lipr baseline vs. pure-git resolution")
    a("")
    a("Generated by `survey.py` (this directory). Do not edit `corpus.json` by hand; re-run")
    a("`python tests/Lip.Core.RegressionTests/Compatibility/survey.py` instead.")
    a("")
    a(f"Generated at: `{corpus['generated_at']}`")
    a("")
    a("## What is checked")
    a("")
    a("For every package/variant/version in the lipr index (the levimc.org/mods list) and every `v<semver>` tag of")
    a("LiteLDev repos that carry a root `tooth.json`:")
    a("")
    a("- **baseline_installable** - lip as shipped today can install it: the version is listed (goproxy `@v/list`,")
    a("  falling back to lipr then git), package files are fetchable (goproxy `.zip` 200 or git tag), and a manifest")
    a("  declaring the variant is obtainable (lipr-hosted `tooth.json`, else the one in the zip / at the tag), and")
    a("  every direct dependency *path* of that manifest resolves to at least one version.")
    a("- **git_tag** - `refs/tags/v<version>` exists (`git ls-remote --tags https://<path>.git`).")
    a("- **tooth_json_at_tag** - root `tooth.json` at that tag exists and is valid JSON.")
    a("- **git_resolvable** - `git_tag && tooth_json_at_tag`, the manifest passes lip's parser (v3: ranges, ids,")
    a("  labels; v1/v2 are migrated) and declares the variant.")
    a("- **git_deps_resolvable** - `git_resolvable` and every direct dependency path in the tag's manifest has a")
    a("  `v<semver>` tag in git (ranges are not solved; transitive deps are covered by their own entries).")
    a("- **risk** - `baseline_installable && !git_deps_resolvable`: would regress when goproxy/lipr are dropped.")
    a("")
    a("The cut-over regression test should run every entry with `baseline_installable && git_tag`.")
    a("")
    a("## Summary")
    a("")
    a("| Metric | Count |")
    a("|---|---:|")
    for k, v in c.items():
        a(f"| {k} | {v} |")
    a("")

    risks = [r for r in E if r["risk"]]
    a(f"## RISK entries ({len(risks)})")
    a("")
    if not risks:
        a("None.")
    else:
        a("Baseline-installable today but NOT resolvable through pure git.")
        a("")
        a("### Grouped by package and root cause")
        a("")
        a("| Package (all variants) | Root cause | Entries | Versions |")
        a("|---|---|---:|---|")
        groups: dict[tuple[str, str], list[str]] = {}
        for r in risks:
            path = r["package"].split("#")[0]
            groups.setdefault((path, risk_cause(r, pk[path])), []).append(r["version"])
        for (path, cause), vs in sorted(groups.items(), key=lambda kv: (kv[0][0].lower(), kv[0][1])):
            uniq = sorted(set(vs), key=semver_key)
            a(f"| `{path}` | {md_escape(cause)} | {len(vs)} | {', '.join(uniq)} |")
        a("")
        a("### All RISK entries")
        a("")
        a("| Package | Version | Root cause | Entry notes | Repo notes |")
        a("|---|---|---|---|---|")
        for r in risks:
            p = pk[r["package"].split("#")[0]]
            a(f"| `{r['package']}` | {r['version']} | {risk_cause(r, p)} | {md_escape(r['notes'])} | "
              f"{md_escape(p['notes']) or '-'} |")
    a("")

    def table(title: str, blurb: str, rows: list[dict]) -> None:
        a(f"## {title} ({len(rows)})")
        a("")
        a(blurb)
        a("")
        if rows:
            a("| Package | Version | Notes |")
            a("|---|---|---|")
            for r in rows:
                a(f"| `{r['package']}` | {r['version']} | {md_escape(r['notes'])} |")
        else:
            a("None.")
        a("")

    ok = [r for r in E if r["baseline_installable"] and r["git_resolvable"]]
    table("Resolvable, but tooth path != package path",
          "Git-resolvable entries whose `tooth.json` at the tag declares a different `tooth` path (renamed or\n"
          "transferred repos, copy-pasted manifests). A strict `manifest.tooth == requested path` check would\n"
          "break these.",
          [r for r in ok if r["detail"]["tooth_path_at_tag"] != r["package"].split("#")[0]])
    table("Resolvable, but the tag was moved after goproxy cached it",
          "`refs/tags/v<version>` now points to a different commit than the one goproxy serves (`Origin.Hash`\n"
          "in `@v/<version>.info`). After the cut-over users get the *current* tag content, which may differ\n"
          "from what goproxy served before.",
          [r for r in ok if r["detail"]["tag_moved"]])
    table("Resolvable, but the lipr-hosted manifest differs semantically",
          "Today lip reads the manifest from lipr first. For these (format v3 on both sides) lipr's copy differs\n"
          "from the tag's `tooth.json` outside `info`; after the cut-over the tag's version is used.",
          [r for r in ok if r["detail"]["lipr_manifest_differs"]])

    broken = [r for r in E if not r["baseline_installable"] and r["source"] == "lipr-index"]
    a(f"## Pre-existing breakages in the lipr index ({len(broken)})")
    a("")
    a("Listed on levimc.org/mods but not installable with lip today (not regressions).")
    a("")
    if broken:
        a("| Package | Version | git_deps_resolvable | Notes |")
        a("|---|---|---|---|")
        for r in broken:
            a(f"| `{r['package']}` | {r['version']} | {'yes' if r['git_deps_resolvable'] else 'no'} | {md_escape(r['notes'])} |")
    else:
        a("None.")
    a("")
    org_broken = [r for r in E if not r["baseline_installable"] and r["source"] == "liteldev-org"]
    a(f"LiteLDev tags not in the index and not installable today: {len(org_broken)} "
      f"(mostly tags that predate the repo's `tooth.json`; see `corpus.json`).")
    a("")

    udp = corpus["unresolvable_dependency_paths"]
    a(f"## Dependency paths that do not resolve ({len(udp)})")
    a("")
    a("Dependency package paths referenced by surveyed manifests that have no `v<semver>` tag reachable via")
    a("git (`git`) and/or no versions via today's goproxy -> lipr -> git chain (`today`).")
    a("")
    if udp:
        a("| Dependency path | git | today |")
        a("|---|---|---|")
        for d, s in udp.items():
            a(f"| `{d}` | {'yes' if s['git'] else 'no'} | {'yes' if s['today'] else 'no'} |")
    else:
        a("None.")
    a("")

    a("## Package-level anomalies")
    a("")
    a("| Package | Repo status | goproxy-only versions | git-only versions | Index versions w/o tag | Notes |")
    a("|---|---|---|---|---|---|")
    for path, p in pk.items():
        interesting = (p["repo_status"] != "ok" or p["goproxy_only_versions"] or p["index_versions_without_tag"]
                       or p["notes"] or not p["git_ls_remote_ok"])
        if not interesting:
            continue
        a(f"| `{path}` | {p['repo_status']} | {', '.join(p['goproxy_only_versions']) or '-'} | "
          f"{', '.join(p['git_only_versions'][:8]) + (' ...' if len(p['git_only_versions']) > 8 else '') or '-'} | "
          f"{', '.join(p['index_versions_without_tag']) or '-'} | {md_escape(p['notes']) or '-'} |")
    a("")
    a(re.sub(r"<<(\w+)>>", lambda m: str(c[m.group(1)]), RECOMMENDATIONS))
    return "\n".join(L).rstrip() + "\n"


RECOMMENDATIONS = """## Recommendations for the cut-over

<!-- Hand-written; maintained in survey.py (RECOMMENDATIONS constant). Re-check after regenerating. -->

1. **Dependency aliases are the biggest regression source.** Most RISK entries are not caused by the package
   itself but by a *direct dependency* whose path no longer exists in git: self-hosted Gitea hosts that are gone
   (`gitea.litebds.com/...`, `git.levimc.org/...`) and deleted/moved GitHub repos (`github.com/GroupMountain/GMLIB`,
   `github.com/engsr6982/PLand` -> now `github.com/IceBlcokMC/PLand`, `github.com/Tooth-Hub/LiteLoaderBDS`).
   Today these work only because (a) lipr serves *rewritten* manifests (e.g. `{git.levimc.org,gitea.litebds.com}/ShrBox/7-zip`
   -> `github.com/LiteLDev/7-zip-tooth`, `gitea.litebds.com/LiteLDev/legacy-script-engine-{nodejs,quickjs}` ->
   `github.com/LiteLDev/LegacyScriptEngine` (+variant), `github.com/Tooth-Hub/LiteLoaderBDS` dropped) or (b)
   goproxy still caches the dead module (`GMLIB`, `PLand`). Ship a small, versioned **package alias table** in lip
   (old path -> new path [+ variant], or "drop") and apply it to dependency keys after manifest parsing. Without it,
   accept these as known regressions.
2. **Do not require `manifest.tooth == requested path`.** <<tooth_path_mismatch_at_tag>> entries declare a different `tooth`
   (renames/transfers: `HuHoBot/BDSAdapter`, `LiteLDev/iListenAttentively`, `LiteLDev/BarelyEnoughItems`, ...).
   Keep the *requested* path as the package identity; at most warn. Note that `{{tooth}}` in v3 asset URLs renders
   from the manifest's own `tooth`, so such URLs rely on GitHub's rename redirects.
3. **Follow GitHub redirects and be case-insensitive for `github.com`.** <<renamed_packages>> paths are renamed/transferred repos
   (e.g. `carefly/RLXWarp` -> `carefly/RLXTeleport`, the `HosihikariFramework/*Provider` -> `*Loader` renames) or
   <<case_mismatch_packages>> differ only in case from the canonical name (`dmblock/LeviBox`, `lgc-LLDev/NbsPlayer`). `git ls-remote`/smart-HTTP
   follows these redirects today, so the managed git client must follow HTTP 301/302 on `info/refs` and
   `git-upload-pack`. Key caches by the requested path (or lower-cased for github.com), never by the redirect
   target, so `lip.lock`/workspace state stays stable.
4. **Peel annotated tags.** <<annotated_tags>> entries use annotated tags; resolve `refs/tags/v<ver>^{}` to the commit before
   reading the tree.
5. **Tag syntax.** Match exactly `refs/tags/v<semver>` (SemVer 2.0, prerelease and build metadata allowed:
   `v0.6.3-alpha.1+20260605` exists and is *only* installable via git today, as goproxy rejects build metadata).
   Do not try to be clever with short tags (`v1.2`) - none of the surveyed index versions needed it. Uppercase `V`
   tags exist (`kuangby/FuckPlayerActionsAuthority` has `V26.51.2`); ignoring them matches goproxy behaviour.
6. **Moved tags change content.** <<tags_moved_since_goproxy_cached>> entries have a tag that now points to a different commit than the one goproxy
   served (`Origin.Hash`). Pure git serves the current tag. For `wo55555/OptiPiston` 0.1.3/0.1.4 the re-tag also
   renamed variants (`mc26_xx` -> `client_mc26_xx`) - the lipr index still advertises the old labels, so those
   10 entries regress; the author's new labels work.
7. **Deleted tags / restarted versioning (accept or ask authors).** `kuangby/FuckMovementAuthority` 26.40.0,
   `LordBombardir/LLTranslatorApi` 1.2.5 (repo renamed to `LLPlaceholderApi`, history restarted) and
   `MiracleForest/iListenAttentively` 0.1.0-0.2.1 (transferred to LiteLDev; early tags gone) only exist in the
   goproxy cache. No git-only fix is possible; ask the authors to restore tags or drop the versions from the index.
8. **Keep manifest migration + template rendering identical for the git source.** <<non_v3_manifests_at_tag>> tag manifests are format v1/v2
   (migration incl. `$(var)`/`{{var}}` rendering and `src: "Dir/*"` -> directory placements, <<self_placement_globs_at_tag>> entries use
   such self-placement globs/dirs), and v3 manifests use `{{tooth}}`/`{{version}}` that lip renders itself (lipr
   pre-rendered them). The git tree source must enumerate keys under a directory prefix exactly like the
   goproxy zip source did. Manifest `version` may disagree with the tag (<<manifest_version_mismatch_at_tag>> entries); use the tag's version.
9. **Strict v3 parsing.** Semver 3 rejects `1.2.x`-style ranges; <<manifest_rejected_at_tag>> tag manifests (at the time of writing
   `LiteLDev/LeviAntiCheat` 0.5.2/0.6.2/0.7.0) fail today and will keep failing - not a regression, but consider accepting `x` wildcards.
10. **Regression test selection.** Run `baseline_installable && git_tag` from `corpus.json`; expect the `risk`
    entries among them to fail unless (1) is implemented. `regression_candidates_expected_to_pass` in the summary
    is the count that should pass without aliases.
"""


if __name__ == "__main__":
    sys.exit(main())
