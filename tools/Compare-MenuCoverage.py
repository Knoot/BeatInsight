"""Compare bounded menu snapshots only when source hashes and settings agree."""
import argparse
import copy
import json
import math
import re
from pathlib import Path


FIELDS = ("status", "metrics", "medianLeft", "medianRight", "squats",
          "squatError", "noteError", "independentDensity", "densityError",
          "densitySegments", "densitySha256", "densityValues", "error")
SHA256 = re.compile(r"[0-9A-Fa-f]{64}\Z")
METRICS = ("leftSwings", "rightSwings", "LeftAverage", "RightAverage", "TotalAverage",
           "LeftPeak", "RightPeak", "TotalPeak", "FirstLeft", "FirstRight", "windows", "segments")


def index(rows):
    if not isinstance(rows, list) or not rows:
        raise ValueError("Expected a nonempty coverage array")
    result = {}
    for row in rows:
        if not isinstance(row, dict):
            raise ValueError("Invalid coverage row")
        source = row.get("source")
        if not isinstance(source, str) or not source or source in result:
            raise ValueError("Missing or duplicate source identity")
        if any(field not in row for field in FIELDS):
            raise ValueError("Incomplete coverage snapshot: " + source)
        if row["status"] not in ("ready", "partial", "unsupported", "unavailable", "error"):
            raise ValueError("Unknown coverage status: " + source)
        if type(row["independentDensity"]) is not bool or type(row["densitySegments"]) is not int or row["densitySegments"] < 0:
            raise ValueError("Invalid density metadata: " + source)
        if row["squats"] is not None and (type(row["squats"]) is not int or row["squats"] < 0):
            raise ValueError("Invalid squat count: " + source)
        for field in ("medianLeft", "medianRight"):
            if row[field] is not None and (type(row[field]) not in (float, int) or not math.isfinite(row[field])):
                raise ValueError("Invalid median: " + source)
        if row["metrics"] is not None and (not isinstance(row["metrics"], dict)
                or any(key not in row["metrics"] for key in METRICS)
                or any(type(v) not in (float, int) or not math.isfinite(v) for v in row["metrics"].values())):
            raise ValueError("Invalid note metrics: " + source)
        if any(row[field] is not None and not isinstance(row[field], str)
               for field in ("squatError", "noteError", "densityError", "error")):
            raise ValueError("Invalid diagnostic: " + source)
        context = row.get("analysisContext")
        if (not isinstance(context, dict) or type(context.get("features")) is not int
                or not isinstance(context.get("jump"), str) or not context["jump"]):
            raise ValueError("Missing analysis context; regenerate coverage: " + source)
        if any(not isinstance(context.get(field), str) or not context[field]
               for field in ("Characteristic", "Difficulty", "File")):
            raise ValueError("Missing exact selection identity: " + source)
        info_hash = context.get("infoSha256")
        if row["status"] not in ("unavailable", "error") and (
                not isinstance(info_hash, str) or not SHA256.fullmatch(info_hash)):
            raise ValueError("Missing Info fingerprint; regenerate coverage: " + source)
        duration = context.get("duration")
        if row["status"] in ("ready", "partial") and (
                type(duration) not in (float, int) or not math.isfinite(duration) or duration < 0):
            raise ValueError("Missing or invalid timeline context: " + source)
        digest = row.get("mapSha256")
        if row["status"] not in ("unavailable", "error"):
            if not isinstance(digest, str) or not SHA256.fullmatch(digest):
                raise ValueError("Missing source hash; regenerate coverage: " + source)
        elif digest is not None and (not isinstance(digest, str) or not SHA256.fullmatch(digest)):
            raise ValueError("Invalid source hash: " + source)
        values = row["densityValues"]
        graph_hash = row["densitySha256"]
        if graph_hash is not None:
            if (not isinstance(graph_hash, str) or not SHA256.fullmatch(graph_hash)
                    or not isinstance(values, list)
                    or type(row["densitySegments"]) is not int
                    or len(values) != row["densitySegments"]
                    or any(type(v) not in (float, int) or not math.isfinite(v) or v < 0 for v in values)):
                raise ValueError("Invalid density snapshot: " + source)
        elif values is not None:
            raise ValueError("Density values without digest: " + source)
        if row["status"] in ("unsupported", "unavailable", "error") and (
                row["metrics"] is not None or row["squats"] is not None
                or graph_hash is not None or row["densitySegments"] != 0):
            raise ValueError("Failed selection retains stale metrics: " + source)
        # Reject NaN/Infinity anywhere, including values nested in note metrics.
        json.dumps(row, allow_nan=False)
        result[source] = row
    return result


def compare(before, after):
    old, new = index(before), index(after)
    changes, uncompared, unavailable, errors = [], [], [], []
    compared = 0
    for source in sorted(old.keys() | new.keys()):
        a, b = old.get(source), new.get(source)
        if a is None or b is None:
            uncompared.append({"source": source, "reason": "new-source" if a is None else "removed-source"})
            continue
        if a["status"] == "error" or b["status"] == "error":
            errors.append({"source": source, "reason": "analysis-error"})
            continue
        if a["status"] == b["status"] == "unavailable":
            unavailable.append(source)
            continue
        if a["status"] == "unavailable" or b["status"] == "unavailable":
            uncompared.append({"source": source, "reason": "source-availability-changed"})
            continue
        if a["mapSha256"].upper() != b["mapSha256"].upper():
            uncompared.append({"source": source, "reason": "source-content-changed"})
            continue
        if a["analysisContext"] != b["analysisContext"]:
            uncompared.append({"source": source, "reason": "analysis-context-changed"})
            continue
        compared += 1
        fields = [field for field in FIELDS if a[field] != b[field]]
        if fields:
            changes.append({"source": source, "fields": fields})
    status = "match" if compared and not (changes or uncompared or errors) else "needs-review"
    if errors:
        status = "analysis-error"
    return {"schemaVersion": 1, "status": status, "baselineCases": len(old), "currentCases": len(new),
            "comparedCases": compared, "changes": changes, "uncompared": uncompared,
            "unavailable": unavailable, "errors": errors,
            "scope": "Menu snapshots at identical beatmap JSON/Info hashes, exact selection, duration and analysis settings only; "
                     "changed output requires review, including newly supported metrics. "
                     "Unavailable files are not validated. No independent map correctness or game execution."}


def self_test():
    row = dict(source="one.result.json", status="partial", mapSha256="A" * 64,
               analysisContext=dict(features=255, jump="normal", Characteristic="Standard",
                                    Difficulty="Expert", File="map.dat", infoSha256="D" * 64, duration=10), metrics=None,
               medianLeft=None, medianRight=None, squats=3, squatError=None, noteError="rotation",
               independentDensity=True, densityError=None, densitySegments=2,
               densitySha256="B" * 64, densityValues=[1.0, 2.0], error=None)
    checks = 0
    note_metrics = dict.fromkeys(METRICS, 0) | {"LeftPeak": 7}

    def check(condition):
        nonlocal checks
        if not condition:
            raise AssertionError("Coverage comparison control failed")
        checks += 1

    check(compare([row], [copy.deepcopy(row)])["status"] == "match")
    empty_graph = row | dict(densityValues=[], densitySegments=0)
    check(compare([empty_graph], [empty_graph])["status"] == "match")
    for field, value in (("squats", 4), ("densityValues", [2.0, 1.0]), ("status", "ready"),
                         ("noteError", None), ("independentDensity", False), ("metrics", note_metrics)):
        changed = copy.deepcopy(row)
        changed[field] = value
        result = compare([row], [changed])
        check(result["status"] == "needs-review" and field in result["changes"][0]["fields"])
    for field, value, reason in (("mapSha256", "C" * 64, "source-content-changed"),
                                 ("analysisContext", row["analysisContext"] | dict(features=31, jump="unknown"), "analysis-context-changed"),
                                 ("status", "unavailable", "source-availability-changed")):
        changed = copy.deepcopy(row)
        changed[field] = value
        if value == "unavailable":
            changed.update(metrics=None, squats=None, densitySha256=None, densityValues=None, densitySegments=0)
        result = compare([row], [changed])
        check(result["comparedCases"] == 0 and result["uncompared"][0]["reason"] == reason)
    other = dict(row, source="two.result.json")
    check(compare([row], [row, other])["uncompared"][0]["reason"] == "new-source")
    check(compare([row, other], [row])["uncompared"][0]["reason"] == "removed-source")
    absent = dict(row, status="unavailable", mapSha256=None, metrics=None, squats=None,
                  densitySha256=None, densityValues=None, densitySegments=0)
    check(compare([absent], [absent])["status"] == "needs-review")
    check(compare([row, absent | {"source": "missing"}], [row, absent | {"source": "missing"}])["unavailable"] == ["missing"])
    check(compare([row], [dict(absent, status="error")])["status"] == "analysis-error")
    for field, value in (("infoSha256", "E" * 64), ("Characteristic", "Lawless"), ("duration", 12)):
        changed = row | {"analysisContext": row["analysisContext"] | {field: value}}
        check(compare([row], [changed])["uncompared"][0]["reason"] == "analysis-context-changed")
    for field, value in (("mapSha256", None), ("analysisContext", None),
                         ("densitySegments", 99), ("densityValues", [float("nan"), 0]),
                         ("densitySha256", None), ("metrics", {"LeftPeak": float("inf")}),
                         ("status", "unsupported"), ("squats", True), ("medianLeft", True),
                         ("independentDensity", 1), ("noteError", {"unexpected": 1}),
                         ("metrics", note_metrics | {"LeftPeak": True}), ("metrics", {})):
        try:
            compare([row], [row | {field: value}])
        except ValueError:
            check(True)
        else:
            check(False)
    for field, value in (("infoSha256", None), ("File", ""), ("duration", float("nan"))):
        try:
            compare([row], [row | {"analysisContext": row["analysisContext"] | {field: value}}])
        except ValueError:
            check(True)
        else:
            check(False)
    for invalid in ([], [row, row], [dict(row, source="")]):
        try:
            compare(invalid, [row])
        except ValueError:
            check(True)
        else:
            check(False)
    return {"status": "pass", "checks": checks, "scope": "Synthetic snapshot comparison controls"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("baseline", nargs="?")
    parser.add_argument("current", nargs="?")
    parser.add_argument("--output")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        print(json.dumps(self_test(), indent=2))
        return 0
    if not args.baseline or not args.current:
        parser.error("Supply baseline and current coverage arrays")
    try:
        result = compare(json.loads(Path(args.baseline).read_text(encoding="utf-8-sig")),
                         json.loads(Path(args.current).read_text(encoding="utf-8-sig")))
        code = 0 if result["status"] == "match" else 2 if result["errors"] else 3
    except (ValueError, OSError) as error:
        result, code = {"schemaVersion": 1, "status": "invalid-input", "error": str(error)}, 2
    text = json.dumps(result, indent=2, allow_nan=False)
    if args.output:
        Path(args.output).write_text(text + "\n", encoding="utf-8")
    print(text)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
