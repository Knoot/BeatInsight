"""Validate bounded rotation.math diagnostics without importing Unity."""
import argparse
import copy
import gzip
import itertools
import json
import math
from pathlib import Path


TOLERANCE = 2e-5


def close(a, b):
    return max(abs(x - y) for x, y in zip(a, b)) <= TOLERANCE


def same_rotation(a, b):
    return close(a, b) or close(a, [-x for x in b])


def product(a, b):
    x, y, z, w = a
    u, v, t, s = b
    return [w*u + x*s + y*t - z*v, w*v - x*t + y*s + z*u,
            w*t + x*v - y*u + z*s, w*s - x*u - y*v - z*t]


def euler_product(degrees, order):
    axes = {}
    for index, axis in enumerate("XYZ"):
        half = math.radians(degrees[index]) / 2
        value = [0.0, 0.0, 0.0, math.cos(half)]
        value[index] = math.sin(half)
        axes[axis] = value
    result = [0.0, 0.0, 0.0, 1.0]
    for axis in order:
        result = product(result, axes[axis])
    return result


def vector(row, name, size):
    value = row.get(name)
    if not isinstance(value, list) or len(value) != size:
        raise ValueError("Invalid vector shape: " + name)
    if any(isinstance(x, bool) or not isinstance(x, (int, float)) or not math.isfinite(x) for x in value):
        raise ValueError("Non-finite or invalid vector component: " + name)
    return value


def inspect(data):
    issues = []
    if not isinstance(data, dict):
        return {"status": "invalid", "issues": ["Invalid diagnostic payload"]}
    rows = data.get("rows")
    if type(data.get("schema")) is not int or data["schema"] != 1 or not isinstance(rows, list) or not 1 <= len(rows) <= 32:
        return {"status": "invalid", "issues": ["Invalid schema or bounded row count"]}
    if type(data.get("count")) is not int or data.get("count") != len(rows):
        issues.append("Declared row count differs from payload")
    orders = list(itertools.permutations("XYZ"))
    for index, row in enumerate(rows):
        try:
            angles = vector(row, "degrees", 3)
            q = vector(row, "vector", 4)
            other = vector(row, "scalar", 4)
            inverse = vector(row, "inverse", 4)
            norm = sum(x*x for x in q)
            if abs(norm - 1) > TOLERANCE:
                issues.append(f"row {index}: quaternion is not unit length")
            if not same_rotation(q, other):
                issues.append(f"row {index}: Euler overloads differ")
            if norm == 0 or not close(inverse, [-q[0]/norm, -q[1]/norm, -q[2]/norm, q[3]/norm]):
                issues.append(f"row {index}: inverse differs")
            x, y, z, w = q
            basis = {
                "right": [1-2*(y*y+z*z), 2*(x*y+z*w), 2*(x*z-y*w)],
                "up": [2*(x*y-z*w), 1-2*(x*x+z*z), 2*(y*z+x*w)],
                "forward": [2*(x*z+y*w), 2*(y*z-x*w), 1-2*(x*x+y*y)],
            }
            for name, expected in basis.items():
                if not close(vector(row, name, 3), expected):
                    issues.append(f"row {index}: {name} basis differs")
            orders = [order for order in orders if same_rotation(q, euler_product(angles, order))]
        except (ValueError, TypeError, AttributeError, OverflowError) as error:
            issues.append(f"row {index}: {error}")
    if not orders:
        issues.append("No axis product order matches all rows")
    return {"status": "invalid" if issues else "verified-samples" if len(orders) == 1 else "ambiguous",
            "rows": len(rows), "unity": data.get("unity") if isinstance(data.get("unity"), str) else None, "issues": issues,
            "quaternionProductOrders": ["*".join(order) for order in orders],
            "scope": "Captured samples only; axis product order, not an intrinsic/extrinsic naming convention. No full scene/player pose or map admission."}


def records(path):
    if path.suffix.lower() == ".json":
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(data, (dict, list)):
            raise ValueError("Expected diagnostic event/list or math snapshot")
        if isinstance(data, dict) and "rows" in data:
            return [{"kind": "rotation.math", "data": data}]
        return data if isinstance(data, list) else [data]
    opener = gzip.open if path.suffix.lower() == ".gz" else open
    found = []
    with opener(path, "rt", encoding="utf-8-sig", errors="replace") as stream:
        for line in stream:
            if "BI-DIAG " not in line:
                continue
            event = json.loads(line.split("BI-DIAG ", 1)[1])
            if event.get("kind") in ("rotation.math", "rotation.math.unavailable"):
                found.append(event)
    return found


def review(events):
    selected = [e for e in events if isinstance(e, dict) and e.get("kind") in ("rotation.math", "rotation.math.unavailable")]
    results = []
    for event in selected:
        payload = event.get("data")
        if event["kind"] == "rotation.math":
            result = inspect(payload)
        elif isinstance(payload, dict):
            result = {"status": "unavailable", "error": str(payload.get("error"))}
        else:
            result = {"status": "invalid", "issues": ["Invalid unavailable diagnostic payload"]}
        if event["kind"] == "rotation.math" and result.get("rows") != 24:
            result = {**result, "status": "invalid", "issues": result.get("issues", []) + ["Runtime capture requires the complete 24-input matrix"]}
        if event["kind"] == "rotation.math" and not result.get("unity"):
            result = {**result, "status": "invalid", "issues": result.get("issues", []) + ["Runtime version is missing"]}
        results.append({"session": event.get("session") if isinstance(event.get("session"), str) else None,
                        "seq": event.get("seq") if type(event.get("seq")) is int else None, **result})
    status = "missing" if not results else "verified-samples" if all(r["status"] == "verified-samples" for r in results) else "unavailable" if all(r["status"] == "unavailable" for r in results) else "needs-review"
    return {"schemaVersion": 1, "status": status, "captures": results}


def self_test():
    # Independent exact quarter-turn gold: quaternion and basis entries are
    # declared explicitly, not generated by the inspected product implementation.
    gold = [
        ([0, 0, 0], [0, 0, 0, 1], [1, 0, 0], [0, 1, 0], [0, 0, 1]),
        ([90, 90, 0], [.5, .5, -.5, .5], [0, 0, -1], [1, 0, 0], [0, -1, 0]),
        ([0, 90, 90], [.5, .5, .5, .5], [0, 1, 0], [0, 0, 1], [1, 0, 0]),
        ([90, 0, 90], [.5, -.5, .5, .5], [0, 0, 1], [-1, 0, 0], [0, -1, 0]),
    ]
    rows = [{"degrees": a, "vector": q, "scalar": list(q), "inverse": [-q[0], -q[1], -q[2], q[3]],
             "right": r, "up": u, "forward": f} for a, q, r, u, f in gold]
    data = {"schema": 1, "count": len(rows), "rows": rows}
    checks = 0
    def check(condition):
        nonlocal checks
        if not condition:
            raise AssertionError("Rotation diagnostic self-test failed")
        checks += 1
    result = inspect(data)
    check(result["status"] == "verified-samples" and result["quaternionProductOrders"] == ["Y*X*Z"])
    changed = copy.deepcopy(data); changed["rows"][1]["scalar"] = [-x for x in rows[1]["scalar"]]
    check(inspect(changed)["status"] == "verified-samples")
    for field, value in [("vector", [0, 0, 0, 2]), ("scalar", [0, 0, 0, 1]),
                         ("inverse", [0, 0, 0, 1]), ("right", [1, 0, 0]), ("degrees", [float("nan"), 0, 0])]:
        changed = copy.deepcopy(data); changed["rows"][1][field] = value
        check(inspect(changed)["status"] == "invalid")
    changed = copy.deepcopy(data); changed["count"] += 1
    check(inspect(changed)["status"] == "invalid")
    check(inspect({"schema": 1, "count": 1, "rows": rows[:1]})["status"] == "ambiguous")
    check(review([])["status"] == "missing")
    check(review([{"kind": "rotation.math.unavailable", "data": {"error": "control"}}])["status"] == "unavailable")
    check(inspect(None)["status"] == "invalid")
    check(review([{"kind": "rotation.math", "data": data}])["status"] == "needs-review")
    capture = {"schema": 1, "unity": "synthetic-control", "count": 24,
               "rows": [copy.deepcopy(row) for row in rows for _ in range(6)]}
    check(review([{"kind": "rotation.math", "data": capture}])["status"] == "verified-samples")
    check(review([{"kind": "rotation.math.unavailable", "data": None}])["status"] == "needs-review")
    return {"checks": checks, "status": "pass", "scope": "Synthetic mathematical controls only; no native Unity evidence"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("input", nargs="?", type=Path)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    if args.self_test:
        result = self_test(); code = 0
    else:
        if args.input is None:
            parser.error("input is required unless --self-test is selected")
        try:
            result = review(records(args.input))
        except (OSError, ValueError, TypeError) as error:
            result = {"schemaVersion": 1, "status": "invalid-input", "error": str(error), "captures": []}
        code = 0 if result["status"] == "verified-samples" else 3 if result["status"] in ("missing", "unavailable") else 2
    text = json.dumps(result, ensure_ascii=True, indent=2, allow_nan=False)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text + "\n", encoding="utf-8")
    print(text)
    return code


if __name__ == "__main__":
    raise SystemExit(main())
