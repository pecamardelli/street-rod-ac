#!/usr/bin/env python3
"""Token-based clone finder for the tech-audit `duplication` section.

Deterministic and dependency-free. Tokenizes C# (and Lua), drops comments and
whitespace, replaces string/number literals with placeholders (so copies with
changed constants still match), and reports regions of at least --min-tokens
consecutive identical tokens that occur in more than one place.

    python find_duplicates.py [--root .] [--min-tokens 80] [--rename] [--json out.json]

--rename also replaces identifiers with a placeholder (type-2 clones: same shape,
renamed variables). Noisier; use it to find copy-paste-then-edit families.
"""
import argparse
import json
import os
import re
import sys
from collections import defaultdict

SKIP_DIRS = {"bin", "obj", ".git", ".vs", "node_modules", ".claude", "packages"}
SKIP_SUFFIXES = (".g.cs", ".g.i.cs", ".designer.cs", "assemblyinfo.cs")
EXTS = {".cs": "cs", ".lua": "lua"}

KEYWORDS = set("""
abstract as base bool break byte case catch char checked class const continue decimal default delegate do
double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface
internal is lock long namespace new null object operator out override params private protected public readonly
ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong
unchecked unsafe ushort using virtual void volatile while var async await get set init value yield record when
and or not local function end then elseif nil repeat until
""".split())

TOKEN_RE = re.compile(
    r'''(?P<ws>\s+)
      |(?P<lc>//[^\n]*|--(?!\[\[)[^\n]*)
      |(?P<bc>/\*.*?\*/|--\[\[.*?\]\])
      |(?P<str>@?\$?"(?:[^"\\]|\\.|"")*"|'(?:[^'\\]|\\.)*'|\[\[.*?\]\])
      |(?P<num>\b\d[\d_]*(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdDmMuUlL]*\b|\b0x[0-9a-fA-F]+\b)
      |(?P<id>[A-Za-z_]\w*)
      |(?P<op>=>|\?\?=?|\?\.|&&|\|\||[=!<>]=|\+\+|--|[-+*/%&|^]=|<<|>>|::|\S)
    ''', re.X | re.S)


def tokenize(text, rename):
    toks, line = [], 1
    for m in TOKEN_RE.finditer(text):
        kind, val = m.lastgroup, m.group()
        start_line = line
        line += val.count("\n")
        if kind in ("ws", "lc", "bc"):
            continue
        if kind == "str":
            val = "$S"
        elif kind == "num":
            val = "$N"
        elif kind == "id" and rename and val not in KEYWORDS:
            val = "$I"
        toks.append((val, start_line))
    return toks


def is_boilerplate(toks):
    """Skip regions that are only using-directives, attributes or auto-properties."""
    vals = [t for t, _ in toks]
    if vals.count("using") * 3 >= len(vals):
        return True
    distinct = set(vals) - {";", "{", "}", "(", ")", ",", ".", "=", "$S", "$N", "$I"}
    return len(distinct) < 8


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=".")
    ap.add_argument("--min-tokens", type=int, default=80)
    ap.add_argument("--rename", action="store_true")
    ap.add_argument("--json")
    ap.add_argument("--top", type=int, default=60)
    args = ap.parse_args()

    files, tokens = [], []
    for dirpath, dirnames, filenames in os.walk(args.root):
        dirnames[:] = sorted(d for d in dirnames if d not in SKIP_DIRS)
        for name in sorted(filenames):
            ext = os.path.splitext(name)[1].lower()
            if ext not in EXTS or name.lower().endswith(SKIP_SUFFIXES):
                continue
            path = os.path.join(dirpath, name)
            try:
                with open(path, encoding="utf-8-sig", errors="replace") as fh:
                    toks = tokenize(fh.read(), args.rename)
            except OSError:
                continue
            if len(toks) >= args.min_tokens:
                files.append(os.path.relpath(path, args.root).replace("\\", "/"))
                tokens.append(toks)

    w = args.min_tokens
    index = defaultdict(list)
    for fid, toks in enumerate(tokens):
        vals = [t for t, _ in toks]
        for i in range(len(vals) - w + 1):
            index[hash(tuple(vals[i:i + w]))].append((fid, i))

    # Matches grouped by diagonal (file pair + offset); consecutive windows merge into one region.
    diagonals = defaultdict(set)
    for occ in index.values():
        if len(occ) < 2:
            continue
        occ = occ[:12]  # cap pathological families (e.g. identical property blocks)
        for a in range(len(occ)):
            for b in range(a + 1, len(occ)):
                (fa, ia), (fb, ib) = occ[a], occ[b]
                if fa == fb and abs(ib - ia) < w:
                    continue
                diagonals[(fa, fb, ib - ia)].add(ia)

    clones = []
    for (fa, fb, off), starts in diagonals.items():
        starts = sorted(starts)
        run_start = prev = starts[0]
        for s in starts[1:] + [None]:
            if s is not None and s == prev + 1:
                prev = s
                continue
            a0, a1 = run_start, prev + w - 1
            ta = tokens[fa][a0:a1 + 1]
            if not is_boilerplate(ta):
                tb0, tb1 = a0 + off, a1 + off
                clones.append({
                    "tokens": a1 - a0 + 1,
                    "a": {"file": files[fa], "lines": [ta[0][1], ta[-1][1]]},
                    "b": {"file": files[fb], "lines": [tokens[fb][tb0][1], tokens[fb][tb1][1]]},
                })
            if s is not None:
                run_start = prev = s

    clones.sort(key=lambda c: (-c["tokens"], c["a"]["file"], c["a"]["lines"][0]))

    # Different diagonals can report overlapping slices of one copy; keep the largest.
    def overlaps(x, y):
        return x["file"] == y["file"] and x["lines"][0] <= y["lines"][1] and y["lines"][0] <= x["lines"][1]

    kept = []
    for c in clones:
        if not any(overlaps(c["a"], k["a"]) and overlaps(c["b"], k["b"]) for k in kept):
            kept.append(c)
    clones = kept
    total_lines = sum(c["a"]["lines"][1] - c["a"]["lines"][0] + 1 for c in clones)

    if args.json:
        with open(args.json, "w", encoding="utf-8") as fh:
            json.dump({"files_scanned": len(files), "min_tokens": w, "rename": args.rename,
                       "clones": clones}, fh, indent=1)

    print(f"Scanned {len(files)} files; {len(clones)} clone pairs >= {w} tokens "
          f"(~{total_lines} duplicated lines){' [identifiers normalized]' if args.rename else ''}")
    for c in clones[:args.top]:
        a, b = c["a"], c["b"]
        print(f"{c['tokens']:5d} tok  {a['file']}:{a['lines'][0]}-{a['lines'][1]}"
              f"  <->  {b['file']}:{b['lines'][0]}-{b['lines'][1]}")
    if len(clones) > args.top:
        print(f"... {len(clones) - args.top} more (use --json for all)")


if __name__ == "__main__":
    sys.exit(main())
