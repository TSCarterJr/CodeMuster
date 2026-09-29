#!/usr/bin/env python3
"""Measures whether Jev (TypeSafe's decision model) could screen CodeMuster work.

Reads a repository's .codemuster/ledger.db read-only and asks Jev, through a
Decisions API endpoint, two things it can be checked on:

  verify  For findings an LLM already confirmed or refuted: does the code shown
          prove the reported defect? Compared with the LLM verdict.
  screen  For analysed units: could this code hold a defect a reviewer would
          report? Compared with whether the unit got any finding not refuted. Also
          asks whether each unit is test, product, generated or tooling code.
  queue   The same screen questions over pending units, to show how much of the
          queue each threshold would skip.

Standard library only. The API key comes from an environment variable and is
never written anywhere. Files whose content changed since the ledger recorded
them are skipped, so Jev sees the code the LLM saw.

  OPENROUTER_API_KEY=... python scripts/jev-measure.py --repo ../ToolbagCRM
  python scripts/jev-measure.py --repo ../ToolbagCRM --dry-run
"""

import argparse
import concurrent.futures
import hashlib
import json
import os
import random
import sqlite3
import sys
import threading
import time
import urllib.error
import urllib.request

DEFAULT_URL = "https://openrouter.ai/api/alpha/decisions"
DEFAULT_MODEL = "typesafe/jev-1.13"
STATE_CHAR_BUDGET = 60_000  # code tokenizes denser than four characters a token; 100k overran Jev's 32k limit
THRESHOLDS = [0.05, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9]

DEFECT_QUESTION = {
    "type": "noul",
    "instructions": "Does this code contain at least one defect a careful reviewer would report: incorrect logic, an unhandled failure path, missing authorization or tenant scoping, injection or unsafe rendering, a leaked secret, or a concurrency mistake?",
    "criteria": {
        "true": "The code shown contains a concrete defect of that kind.",
        "false": "The code shown is free of such defects, or is too simple to contain one.",
    },
}
ROLE_QUESTION = {
    "type": "choice",
    "instructions": "What role does this code play in the repository?",
    "criteria": {
        "test": "Automated tests, end-to-end specs, test helpers, fixtures or mocks.",
        "product": "Code that runs as part of the shipped product: API, business logic, UI, data access.",
        "generated": "Generated or declarative code such as migrations, DTO or contract records, designer files.",
        "tooling": "Build, deployment, CI or developer scripts that do not ship with the product.",
    },
}
VERIFY_QUESTION = {
    "type": "noul",
    "instructions": "Does the code shown prove that the reported defect is real and can actually happen?",
    "criteria": {
        "true": "The code shown demonstrates the defect exactly as reported.",
        "false": "The code shown contradicts the report, the defect cannot happen, or the report misreads the code.",
    },
}


def git_blob_sha(data: bytes) -> str:
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


class Code:
    """Reads files once and only when their content still matches the ledger."""

    def __init__(self, repo, ledger):
        self.repo = repo
        self.hashes = dict(ledger.execute("select path, content_hash from files"))
        self.cache = {}

    def lines(self, path):
        if path not in self.cache:
            try:
                with open(os.path.join(self.repo, path), "rb") as f:
                    data = f.read()
            except OSError:
                self.cache[path] = None
            else:
                fresh = git_blob_sha(data) == self.hashes.get(path) or git_blob_sha(data.replace(b"\r\n", b"\n")) == self.hashes.get(path)
                self.cache[path] = data.decode("utf-8", "replace").splitlines() if fresh else None
        return self.cache[path]

    def render(self, members):
        """Numbered code for unit members, or None when any file changed since the ledger saw it."""
        parts = []
        for path, start, end in members:
            lines = self.lines(path)
            if lines is None:
                return None
            lo, hi = (start or 1), (end or len(lines))
            body = "\n".join(f"{n}: {lines[n - 1]}" for n in range(lo, min(hi, len(lines)) + 1))
            parts.append(f"// {path}\n{body}")
        text = "\n\n".join(parts)
        return text if len(text) <= STATE_CHAR_BUDGET else text[:STATE_CHAR_BUDGET] + "\n// truncated"


def members_of(ledger, unit_id):
    rows = ledger.execute(
        "select path, start_line, end_line, distance from unit_members where unit_id = ? order by distance, path, start_line",
        (unit_id,),
    ).fetchall()
    return [(p, s, e) for p, s, e, _ in rows]


class Jev:
    def __init__(self, url, model, key, max_cost, dry_run):
        self.url, self.model, self.key = url, model, key
        self.max_cost, self.dry_run = max_cost, dry_run
        self.cost = 0.0
        self.chars = 0
        self.lock = threading.Lock()

    def ask(self, state, questions):
        body = json.dumps({"model": self.model, "state": state, "questions": questions}).encode()
        with self.lock:
            self.chars += len(body)
            if self.dry_run:
                return None
            if self.cost >= self.max_cost:
                raise RuntimeError(f"stopped at the --max-cost limit of ${self.max_cost}")
        for attempt in range(5):
            request = urllib.request.Request(
                self.url, data=body, method="POST",
                headers={"Authorization": f"Bearer {self.key}", "Content-Type": "application/json"},
            )
            try:
                with urllib.request.urlopen(request, timeout=60) as response:
                    reply = json.load(response)
                with self.lock:
                    self.cost += float(reply.get("usage", {}).get("cost") or 0)
                return reply
            except urllib.error.HTTPError as e:
                detail = e.read().decode("utf-8", "replace")[:300]
                if e.code in (429, 500, 502, 503, 504) and attempt < 4:
                    time.sleep(2 ** attempt)
                    continue
                raise RuntimeError(f"HTTP {e.code}: {detail}") from None
            except urllib.error.URLError:
                if attempt < 4:
                    time.sleep(2 ** attempt)
                    continue
                raise


def verify_items(ledger, code, rng, confirmed_limit):
    rows = ledger.execute(
        """select f.id, f.path, f.line_start, f.line_end, f.severity, f.category, f.claim, f.evidence, f.verify_status, a.unit_id
           from findings f join analyses a on a.id = f.analysis_id
           where f.verify_status in ('confirmed', 'refuted')"""
    ).fetchall()
    refuted = [r for r in rows if r[8] == "refuted"]
    confirmed = [r for r in rows if r[8] == "confirmed"]
    rng.shuffle(confirmed)
    items = []
    for fid, path, ls, le, sev, cat, claim, evidence, verdict, unit_id in refuted + confirmed[:confirmed_limit]:
        text = code.render(members_of(ledger, unit_id))
        if text is None:
            continue
        state = {
            "finding": {"path": path, "lines": f"{ls}-{le}", "severity": sev, "category": cat, "claim": claim, "evidence": evidence},
            "code": text,
        }
        items.append({"id": fid, "label": verdict == "confirmed", "state": state})
    return items


def unit_items(ledger, code, status, limit, rng):
    units = ledger.execute(
        "select id, kind, key from units where status = ? and kind in ('file', 'orphan', 'slice')", (status,)
    ).fetchall()
    rng.shuffle(units)
    items = []
    for unit_id, kind, key in units:
        if limit and len(items) >= limit:
            break
        text = code.render(members_of(ledger, unit_id))
        if text is None:
            continue
        found, severities = ledger.execute(
            """select count(*), group_concat(distinct f.severity) from findings f join analyses a on a.id = f.analysis_id
               where a.unit_id = ? and coalesce(f.verify_status, '') <> 'refuted'""",
            (unit_id,),
        ).fetchone()
        items.append({"id": unit_id, "kind": kind, "path": key, "label": found > 0, "severities": severities,
                      "state": {"path": key, "code": text}})
    return items


def run_all(jev, items, questions, workers, out, experiment):
    results = []

    def one(item):
        reply = jev.ask(item["state"], questions)
        return item, reply

    with concurrent.futures.ThreadPoolExecutor(workers) as pool:
        for n, future in enumerate(concurrent.futures.as_completed([pool.submit(one, i) for i in items]), 1):
            try:
                item, reply = future.result()
            except RuntimeError as e:
                print(f"  {experiment}: {e}", file=sys.stderr)
                if "max-cost" in str(e):
                    pool.shutdown(cancel_futures=True)
                    break
                continue
            if reply is None:
                continue
            answers = reply.get("answers", {})
            record = {k: v for k, v in item.items() if k != "state"}
            record["experiment"] = experiment
            record["answers"] = answers
            results.append(record)
            out.write(json.dumps(record) + "\n")
            if n % 25 == 0:
                print(f"  {experiment}: {n}/{len(items)}  spent ${jev.cost:.4f}", file=sys.stderr)
    return results


def table(results, key, positive_name, negative_name):
    """At each threshold, how many positives and negatives fall below it (would be screened out)."""
    scored = [(r["answers"][key]["noul"], r["label"]) for r in results if key in r["answers"]]
    pos = [p for p, label in scored if label]
    neg = [p for p, label in scored if not label]
    print(f"    {len(pos)} {positive_name}, {len(neg)} {negative_name}")
    print(f"    {'below':>6}  {positive_name + ' below':>20}  {negative_name + ' below':>20}")
    for t in THRESHOLDS:
        pb = sum(p < t for p in pos)
        nb = sum(p < t for p in neg)
        print(f"    {t:>6.2f}  {pb:>5}/{len(pos):<5} {pb / max(len(pos), 1):>7.0%}  {nb:>5}/{len(neg):<5} {nb / max(len(neg), 1):>7.0%}")


def roles(results):
    counts = {}
    for r in results:
        role = r["answers"].get("role", {}).get("choice")
        counts[role] = counts.get(role, 0) + 1
    return counts


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--repo", default=".")
    parser.add_argument("--url", default=DEFAULT_URL)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--key-env", default="OPENROUTER_API_KEY")
    parser.add_argument("--experiments", default="verify,screen,queue")
    parser.add_argument("--confirmed", type=int, default=120, help="confirmed findings sampled beside every refuted one")
    parser.add_argument("--queue", type=int, default=400, help="pending units sampled for the queue experiment, 0 for all")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--max-cost", type=float, default=2.0, help="stop once this many USD are spent")
    parser.add_argument("--seed", type=int, default=7)
    parser.add_argument("--out", default="jev-measure.jsonl")
    parser.add_argument("--dry-run", action="store_true", help="build every request and report its size, call nothing")
    args = parser.parse_args()

    key = os.environ.get(args.key_env, "")
    if not key and not args.dry_run:
        sys.exit(f"set {args.key_env} to the API key, or pass --dry-run")
    ledger = sqlite3.connect(f"file:{os.path.join(args.repo, '.codemuster', 'ledger.db')}?mode=ro", uri=True)
    code = Code(args.repo, ledger)
    rng = random.Random(args.seed)
    jev = Jev(args.url, args.model, key, args.max_cost, args.dry_run)
    wanted = set(args.experiments.split(","))

    with open(args.out, "w", encoding="utf-8") as out:
        if "verify" in wanted:
            items = verify_items(ledger, code, rng, args.confirmed)
            print(f"verify: {len(items)} findings with unchanged code", file=sys.stderr)
            results = run_all(jev, items, {"real": VERIFY_QUESTION}, args.workers, out, "verify")
            if results:
                print("\nverify: Jev's probability that the finding is real, against the LLM verdict")
                table(results, "real", "confirmed", "refuted")
                print("    a screen that auto-refutes below a threshold wrongly drops the 'confirmed below' share")

        questions = {"defect": DEFECT_QUESTION, "role": ROLE_QUESTION}
        if "screen" in wanted:
            items = unit_items(ledger, code, "done", 0, rng)
            print(f"screen: {len(items)} analysed units with unchanged code", file=sys.stderr)
            results = run_all(jev, items, questions, args.workers, out, "screen")
            if results:
                print("\nscreen: Jev's probability of a defect, against whether the unit got any finding not refuted")
                table(results, "defect", "with-finding", "clean")
                high = sorted(r["answers"]["defect"]["noul"] for r in results if "high" in (r.get("severities") or "") or "critical" in (r.get("severities") or ""))
                print(f"    units with a high or critical finding scored: {[round(x, 2) for x in high]}")
                print("    skipping below a threshold misses the 'with-finding below' share and saves the 'clean below' share")
                print(f"    roles: {roles(results)}")

        if "queue" in wanted:
            items = unit_items(ledger, code, "pending", args.queue, rng)
            print(f"queue: {len(items)} pending units with unchanged code", file=sys.stderr)
            results = run_all(jev, items, questions, args.workers, out, "queue")
            if results:
                scores = [r["answers"]["defect"]["noul"] for r in results if "defect" in r["answers"]]
                print(f"\nqueue: share of {len(scores)} sampled pending units each threshold would skip")
                for t in THRESHOLDS:
                    print(f"    below {t:.2f}: {sum(s < t for s in scores) / max(len(scores), 1):.0%}")
                print(f"    roles: {roles(results)}")
                tests = [r["path"] for r in results if r["answers"].get("role", {}).get("choice") == "test"]
                print(f"    sampled as test: {tests[:15]}")

    if args.dry_run:
        print(f"dry run: {jev.chars:,} request characters, about {jev.chars // 4:,} tokens, about ${jev.chars / 4 * 0.042 / 1e6:.4f} at $0.042 per million")
    else:
        print(f"\nspent ${jev.cost:.4f}; per-item answers in {args.out}")


if __name__ == "__main__":
    main()
