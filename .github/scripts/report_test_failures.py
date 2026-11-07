"""Publish the failing tests of a CI run as check-run annotations.

GitHub caps a run's log visibility in awkward places - the job log scrolls, the uploaded artifact is
not readable through the API from every environment - while annotations are. This script reads every
``.trx`` under ``TestResults``, turns each failed test into one ``::error`` command, and also writes
the full list to ``$RUNNER_TEMP/failures.txt`` for the pull-request comment step.

It is deliberately forgiving: a missing directory, an unreadable file or a crash must never be the
reason a job turns red, because the job is already red from the tests themselves. The calling step
falls back to the console logs when this script produces nothing.
"""

from __future__ import annotations

import glob
import os
import sys
import xml.etree.ElementTree as ET

NAMESPACE = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

# GitHub's per-step cap for one annotation level; anything beyond it stays in failures.txt.
ANNOTATION_LIMIT = 10

MESSAGE_LIMIT = 900
STACK_LIMIT = 400


def escape_title(text: str) -> str:
    """Escapes the characters a workflow-command *property* may not carry literally."""
    return (
        text.replace("%", "%25")
        .replace("\r", "%0D")
        .replace("\n", "%0A")
        .replace(":", "%3A")
        .replace(",", "%2C")
    )


def escape_body(text: str) -> str:
    """Escapes the characters a workflow-command *message* may not carry literally."""
    return text.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")


def failures(path: str) -> list[str]:
    """Returns one annotation block per failed test in a single trx file."""
    try:
        root = ET.parse(path).getroot()
    except Exception as error:  # a truncated or non-XML file must not stop the report
        print(f"::warning::could not read {path}: {error}")
        return []

    blocks = []
    for result in root.findall(".//t:UnitTestResult", NAMESPACE):
        if result.get("outcome") != "Failed":
            continue

        info = result.find(".//t:ErrorInfo", NAMESPACE)
        message = (info.findtext("t:Message", "", NAMESPACE) or "").strip() if info is not None else ""
        stack = (info.findtext("t:StackTrace", "", NAMESPACE) or "").strip() if info is not None else ""
        blocks.append(
            f"FAILED {os.path.basename(path)} :: {result.get('testName')}\n"
            f"{message[:MESSAGE_LIMIT]}\n"
            f"{stack[:STACK_LIMIT]}"
        )

    return blocks


def main() -> int:
    print(f"python {sys.version.split()[0]} on {sys.platform}, cwd={os.getcwd()}")

    paths = sorted(glob.glob("TestResults/**/*.trx", recursive=True))
    print(f"trx files: {len(paths)}")

    blocks: list[str] = []
    for path in paths:
        blocks.extend(failures(path))

    for block in blocks[:ANNOTATION_LIMIT]:
        parts = block.split("\n")
        print(f"::error title={escape_title(parts[0])}::{escape_body(chr(10).join(parts[1:]))}")

    if len(blocks) > ANNOTATION_LIMIT:
        print(f"::warning::{len(blocks) - ANNOTATION_LIMIT} more failing test(s) in the job summary")

    temp = os.environ.get("RUNNER_TEMP")
    if temp:
        with open(os.path.join(temp, "failures.txt"), "w", encoding="utf-8", newline="\n") as handle:
            handle.write("\n\n".join(blocks))

    print(f"{len(blocks)} failing test(s)")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # never let the reporter be the reason the job is red
        print(f"::warning::the trx report did not finish: {error}")
        sys.exit(0)
