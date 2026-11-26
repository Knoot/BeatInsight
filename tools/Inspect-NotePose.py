"""Check captured active collider AABBs against transformed local box corners."""
import argparse
import copy
import gzip
import itertools
import json
import math
from pathlib import Path


def vector(value, size):
    if not isinstance(value, list) or len(value) != size or any(
        isinstance(x, bool) or not isinstance(x, (float, int)) or not math.isfinite(x)
        for x in value
    ):
        raise ValueError("Invalid finite vector")
    return value


def check_box(box):
    bounds = box.get("bounds")
    if bounds is None:
        return None
    if not isinstance(bounds, dict) or not all(isinstance(bounds.get(k), bool) for k in ("enabled", "active")):
        raise ValueError("Invalid collider flags")
    # Unity can return empty bounds for disabled or inactive colliders.
    if not bounds["enabled"] or not bounds["active"]:
        return None
    center, size = vector(box["center"], 3), vector(box["size"], 3)
    matrix = vector(box["pose"]["world"], 16)
    actual_center, actual_size = vector(bounds["center"], 3), vector(bounds["size"], 3)
    if any(x < 0 for x in size + actual_size):
        raise ValueError("Negative box size")
    if max(abs(matrix[12 + i] - x) for i, x in enumerate((0, 0, 0, 1))) > 1e-6:
        raise ValueError("Non-affine world matrix")
    corners = []
    for signs in itertools.product((-1, 1), repeat=3):
        point = [center[i] + signs[i] * size[i] / 2 for i in range(3)] + [1]
        corners.append([sum(matrix[4*i+j] * point[j] for j in range(4)) for i in range(3)])
    low = [min(p[i] for p in corners) for i in range(3)]
    high = [max(p[i] for p in corners) for i in range(3)]
    if not all(math.isfinite(x) for point in corners for x in point):
        raise ValueError("Non-finite transformed corner")
    return max(max(abs((low[i]+high[i])/2 - actual_center[i]),
                   abs(high[i]-low[i] - actual_size[i])) for i in range(3))


def review(events):
    poses = checked = skipped = 0
    maximum = 0
    issues = []
    for event in events:
        if event.get("kind") != "note.pose":
            continue
        poses += 1
        try:
            snapshot = event["data"]["snapshot"]
            for group in ("bigBoxes", "smallBoxes"):
                boxes = snapshot[group]
                if boxes is None:
                    continue
                if not isinstance(boxes, list):
                    raise ValueError("Invalid box collection")
                for index, box in enumerate(boxes):
                    error = check_box(box)
                    if error is None:
                        skipped += 1
                        continue
                    checked += 1
                    maximum = max(maximum, error)
                    if error > 2e-5:
                        issues.append({"pose": poses, "group": group, "box": index, "error": error})
        except (KeyError, TypeError, ValueError, AttributeError) as error:
            issues.append({"pose": poses, "error": str(error)})
    return {"schemaVersion": 1,
            "status": "needs-review" if issues else "verified-samples" if checked else "missing-active-samples",
            "poses": poses, "checkedBoxes": checked, "skippedBoxes": skipped,
            "maximumAbsoluteError": maximum, "issues": issues,
            "scope": "Captured enabled+active collider AABB only; no final patch order or arbitrary Noodle pose proof"}


def records(path):
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", encoding="utf-8-sig", errors="replace") as stream:
        for number, line in enumerate(stream, 1):
            if "BI-DIAG " not in line:
                continue
            try:
                event = json.loads(line.split("BI-DIAG ", 1)[1])
                if not isinstance(event, dict):
                    raise ValueError("Expected diagnostic object")
                yield event
            except ValueError as error:
                raise ValueError("Malformed diagnostic at line " + str(number)) from error


def self_test():
    # Explicit gold: translation, quarter-turn, nonuniform signed scale.
    box = {"center": [1, 2, 3], "size": [2, 4, 6],
           "pose": {"world": [0, -3, 0, 10, 2, 0, 0, 20, 0, 0, -4, 30, 0, 0, 0, 1]},
           "bounds": {"enabled": True, "active": True, "center": [4, 22, 18], "size": [12, 4, 24]}}
    assert check_box(box) == 0
    checks = 1
    for flag in ("enabled", "active"):
        changed = copy.deepcopy(box); changed["bounds"][flag] = False
        changed["bounds"]["size"] = [0, 0, 0]
        assert check_box(changed) is None
        checks += 1
    event = {"kind": "note.pose", "data": {"snapshot": {"bigBoxes": [box], "smallBoxes": []}}}
    assert review([event])["status"] == "verified-samples"; checks += 1
    for target, field, value in (("bounds", "center", [0, 0, 0]), ("pose", "world", [0]*16),
                                 ("bounds", "size", [float("nan"), 1, 1]), ("bounds", "enabled", 1)):
        changed = copy.deepcopy(event)
        changed["data"]["snapshot"]["bigBoxes"][0][target][field] = value
        assert review([changed])["status"] == "needs-review"
        checks += 1
    assert review([])["status"] == "missing-active-samples"; checks += 1
    return {"status": "pass", "checks": checks, "scope": "Synthetic controls; no Unity execution"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("inputs", nargs="*", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if not args.self_test and not args.inputs:
        parser.error("Provide logs or --self-test")
    try:
        result = self_test() if args.self_test else review(e for p in args.inputs for e in records(p))
    except (OSError, ValueError) as error:
        result = {"status": "invalid-input", "error": str(error)}
    text = json.dumps(result, indent=2, allow_nan=False)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)
    return 0 if result["status"] in ("pass", "verified-samples") else 3 if result["status"] == "missing-active-samples" else 2


if __name__ == "__main__":
    raise SystemExit(main())
