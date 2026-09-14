#!/usr/bin/env python3
"""Localization consistency check, run from the build (see AIImprove.csproj, CheckLocalization).

WHY THIS EXISTS (2026-09-14, 12 - 開發準則 準則 3)
-------------------------------------------------
Dead localization keys have now been swept twice - 23 keys / 207 lines on 2026-09-06, and 25
keys / 254 lines on 2026-09-14 - and both sweeps found leftovers from the same 2026-08-17
settings-page rebuild plus whatever the most recent feature removal left behind.

The reason they accumulate is specific and worth stating: a dead key never fails the build and
never shows up in game. It is invisible until somebody remembers to go looking. That is the
same shape as the README advertising a race-car feature removed a month earlier for making cars
crash - both are things the compiler cannot see, so both went stale.

準則 3 says a bug class that can be removed structurally should not be defended with discipline,
and 準則 2's evidence table already shows that remembering is not a working defence. So this is
wired into the build rather than left as a habit: it runs on every build and reports as MSBuild
warnings, which stand out because this project otherwise builds at zero warnings.

WHAT IT CHECKS
--------------
  LOC001  a key is defined but nothing references it          (dead key)
  LOC002  code references a key that no language defines      (typo / rename)
  LOC003  a language is missing keys the default language has (untranslated)
  LOC004  a language defines keys the default language does not
  LOC005  a key is defined twice in the same language

DELIBERATELY NOT AN ERROR
-------------------------
Warnings, and the build continues. Breaking the build for somebody compiling from a source zip
because their translation is one key short would be a worse failure than the one being
prevented. The maintainer sees the warnings; a contributor still gets a working DLL.

HOW "REFERENCED" IS DECIDED
---------------------------
Every string literal in every .cs file except Localization.cs counts as a reference, and so
does that literal plus ".desc" - because the settings page builds description keys by
convention: Toggle("feature.x") renders both feature.x and feature.x.desc, and
.With("tune.y", ...) does the same for tune.y.

This is deliberately generous. A key that is merely SUSPECTED of being live is left alone; the
cost of a missed dead key is 9 unused lines, and the cost of a false positive is somebody
deleting a string that players actually see. 2026-09-06 recorded exactly this lesson - "腳本化
稽核的假陽性更有說服力，報告前必須抽驗" - so this errs toward silence.

Run standalone for the full list:  python tools/check_localization.py
"""

import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
LOC = os.path.join(ROOT, "Localization.cs")

# A key definition: ["some.key"] = "value, possibly continued on the next line"
KEY_DEF = re.compile(r'\["([A-Za-z][A-Za-z0-9_.]*)"\]\s*=\s*\n?\s*"')
# Any string literal, used to decide what code references.
LITERAL = re.compile(r'"([A-Za-z][A-Za-z0-9_.]*)"')
# Start of a language dictionary: ["xx"] = new Dictionary<string, string>
LANG_START = re.compile(r'\["([a-z]{2}(?:-[a-z]{2})?)"\]\s*=\s*new Dictionary<string, string>')
DEFAULT_LANG_START = re.compile(r'\[DefaultLanguage\]\s*=\s*new Dictionary<string, string>')


def read(path):
    with open(path, encoding="utf-8-sig") as handle:
        return handle.read()


def line_of(text, index):
    return text.count("\n", 0, index) + 1


def block_end(text, start):
    """Offset just past the '}' closing the dictionary initialiser that begins at/after `start`.

    Brace-counted rather than "up to the next language", because the file does not end with the
    last language: ResolveLanguage's alias table (["zh"] = "zh-cn") is another
    Dictionary<string, string> right after it. Taking the last block as "everything to the end of
    the file" swept that table into Korean, and the very first run of this script reported it as
    LOC004, a key Korean defines and English does not.

    That false positive is the point of 2026-09-06's lesson - "腳本化稽核的假陽性更有說服力" - a
    tidy, confident, wrong line. Counting braces is exact; string literals are skipped so a brace
    inside a translated description cannot throw off the count.
    """
    i = text.find("{", start)
    if i < 0:
        return len(text)
    depth = 0
    while i < len(text):
        char = text[i]
        if char == '"':
            i += 1
            while i < len(text) and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
        elif char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return len(text)


def split_languages(text):
    """[(language name, body, offset of body)], default language first."""
    starts = []
    match = DEFAULT_LANG_START.search(text)
    if match:
        starts.append(("(default)", match.end()))
    for match in LANG_START.finditer(text):
        starts.append((match.group(1), match.end()))
    starts.sort(key=lambda pair: pair[1])

    blocks = []
    for name, start in starts:
        end = block_end(text, start)
        blocks.append((name, text[start:end], start))
    return blocks


def keys_in(body):
    """{key: offset within body}, plus a list of keys that appeared more than once."""
    found = {}
    duplicates = []
    for match in KEY_DEF.finditer(body):
        key = match.group(1)
        if key in found:
            duplicates.append((key, match.start()))
        else:
            found[key] = match.start()
    return found, duplicates


def referenced_keys():
    """Every string literal in the rest of the project, plus each literal + '.desc'."""
    used = set()
    for dirpath, dirnames, filenames in os.walk(ROOT):
        dirnames[:] = [d for d in dirnames if d not in ("bin", "obj", ".git", "tools")]
        for name in filenames:
            if not name.endswith(".cs") or name == "Localization.cs":
                continue
            for literal in LITERAL.findall(read(os.path.join(dirpath, name))):
                used.add(literal)
                used.add(literal + ".desc")
    return used


def main():
    if not os.path.exists(LOC):
        print("tools/check_localization.py: Localization.cs not found, skipping.")
        return 0

    text = read(LOC)
    blocks = split_languages(text)
    if not blocks:
        print("Localization.cs(1): warning LOC000: no language dictionaries found - "
              "the file's shape changed and this check no longer understands it.")
        return 0

    warnings = []

    def warn(code, offset, message):
        warnings.append("Localization.cs({0}): warning {1}: {2}".format(
            line_of(text, offset), code, message))

    default_name, default_body, default_offset = blocks[0]
    default_keys, default_dupes = keys_in(default_body)

    for key, offset in default_dupes:
        warn("LOC005", default_offset + offset,
             "'{0}' is defined more than once in the default language.".format(key))

    used = referenced_keys()
    for key in sorted(default_keys):
        if key not in used:
            warn("LOC001", default_offset + default_keys[key],
                 "'{0}' is defined but nothing references it. If the feature it described was "
                 "removed, remove the key from every language too.".format(key))

    defined_anywhere = set(default_keys)
    for name, body, offset in blocks[1:]:
        keys, dupes = keys_in(body)
        defined_anywhere |= set(keys)
        for key, at in dupes:
            warn("LOC005", offset + at,
                 "'{0}' is defined more than once in '{1}'.".format(key, name))
        missing = sorted(set(default_keys) - set(keys))
        extra = sorted(set(keys) - set(default_keys))
        if missing:
            warn("LOC003", offset,
                 "'{0}' is missing {1} key(s) the default language has: {2}".format(
                     name, len(missing), ", ".join(missing[:8]) + ("..." if len(missing) > 8 else "")))
        if extra:
            warn("LOC004", offset,
                 "'{0}' defines {1} key(s) the default language does not: {2}".format(
                     name, len(extra), ", ".join(extra[:8]) + ("..." if len(extra) > 8 else "")))

    # Keys the code asks for that nobody defines. Only literals that look like localization keys
    # (they contain a dot and match a known prefix) are worth reporting - every other string in
    # the project would otherwise be flagged.
    prefixes = tuple(sorted({k.split(".", 1)[0] + "." for k in default_keys if "." in k}))
    if prefixes:
        for literal in sorted(used):
            if literal.startswith(prefixes) and literal not in defined_anywhere:
                if literal.endswith(".desc"):
                    continue  # derived speculatively above; absence is normal
                warn("LOC002", 0,
                     "code references '{0}' but no language defines it.".format(literal))

    for line in warnings:
        print(line)

    if not warnings:
        print("tools/check_localization.py: {0} keys x {1} languages, all consistent.".format(
            len(default_keys), len(blocks)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
