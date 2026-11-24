"""Inspect serialized note transform links without starting Unity or the game.

Install tools/prefab-inspection-requirements.txt into an ignored local target
and expose that target through PYTHONPATH. Reports belong in .local/.
"""

import argparse
import hashlib
import importlib.metadata
import json
import math
from pathlib import Path

import UnityPy


PREFABS = (
    "NormalGameNote", "ProModeGameNote", "BombNote",
    "BurstSliderHeadNote", "BurstSliderNote",
)


def inspect(bundle):
    environment = UnityPy.load(str(bundle))
    results = []
    for name in PREFABS:
        matches = [pointer for address, pointer in environment.container.items()
                   if address.endswith("/" + name + ".prefab")]
        if len(matches) != 1:
            raise ValueError(f"Expected exactly one prefab: {name}")
        root = matches[0].deref()
        objects = root.assets_file.objects

        def resolve(pointer):
            if pointer["m_FileID"] != 0 or not pointer["m_PathID"]:
                raise ValueError(f"Non-local or null component link: {name}")
            return objects[pointer["m_PathID"]]

        components = [resolve(item["component"])
                      for item in root.read_typetree()["m_Component"]]
        transforms = [obj for obj in components if obj.type.name == "Transform"]
        if len(transforms) != 1:
            raise ValueError(f"Expected one root transform: {name}")
        root_transform = transforms[0]
        root_data = root_transform.read_typetree()
        links = []
        controller = None
        controller_id = None
        visuals = []
        trailing = []
        for component in components:
            if component.type.name != "MonoBehaviour":
                continue
            data = component.read_typetree()
            if resolve(data["m_GameObject"]).path_id != root.path_id:
                raise ValueError(f"Component owner mismatch: {name}")
            if "_noteTransform" in data:
                controller = data
                controller_id = component.path_id
            if "_arrowMeshRenderers" in data and "_circleMeshRenderers" in data:
                visuals.append(data)
            if "_mainSmallCuttableBySaber" in data and "_trailingSmallCuttableBySaberList" in data:
                trailing.append(data)
            for field in ("_noteTransform", "_rotatedObject"):
                if field in data:
                    target = resolve(data[field])
                    if target.type.name != "Transform":
                        raise ValueError(f"Expected Transform: {name}/{field}")
                    links.append((field, target))
        note_links = [target for field, target in links if field == "_noteTransform"]
        rotated_links = [target for field, target in links if field == "_rotatedObject"]
        if len(note_links) != 1 or len(rotated_links) != 2:
            raise ValueError(f"Controller/movement link count changed: {name}")
        note = note_links[0]
        if any(target.path_id != note.path_id for target in rotated_links):
            raise ValueError(f"Cut and movement transforms differ: {name}")
        note_data = note.read_typetree()
        if resolve(note_data["m_Father"]).path_id != root_transform.path_id:
            raise ValueError(f"Cut transform is not a direct child: {name}")
        if root_data["m_Father"] != {"m_FileID": 0, "m_PathID": 0}:
            raise ValueError(f"Unexpected root parent: {name}")
        if resolve(root_data["m_GameObject"]).path_id != root.path_id:
            raise ValueError(f"Root transform owner mismatch: {name}")
        if note.path_id not in [resolve(child).path_id for child in root_data["m_Children"]]:
            raise ValueError(f"Missing reverse child link: {name}")
        for data in (root_data, note_data):
            if data["m_LocalScale"] != {"x": 1.0, "y": 1.0, "z": 1.0}:
                raise ValueError(f"Unexpected initial scale: {name}")

        def transform_record(obj, data):
            return {"id": str(obj.path_id), "position": data["m_LocalPosition"],
                    "rotation": data["m_LocalRotation"], "scale": data["m_LocalScale"]}

        def finite_vector(value, axes):
            return all(isinstance(value[axis], (int, float))
                       and math.isfinite(value[axis]) for axis in axes)

        def owner_transform(component):
            owner = resolve(component.read_typetree()["m_GameObject"])
            transforms = [resolve(item["component"])
                          for item in owner.read_typetree()["m_Component"]
                          if resolve(item["component"]).type.name == "Transform"]
            if len(transforms) != 1:
                raise ValueError(f"Expected one attachment transform: {name}")
            return transforms[0]

        def attachment(component):
            current = owner_transform(component)
            chain = []
            seen = set()
            while current.path_id != note.path_id:
                if current.path_id in seen or len(chain) >= 64:
                    raise ValueError(f"Cyclic or excessive attachment chain: {name}")
                seen.add(current.path_id)
                data = current.read_typetree()
                if not all(finite_vector(data[field], axes) for field, axes in (
                        ("m_LocalPosition", "xyz"), ("m_LocalRotation", "xyzw"),
                        ("m_LocalScale", "xyz"))):
                    raise ValueError(f"Non-finite attachment transform: {name}")
                chain.append(transform_record(current, data))
                parent = resolve(data["m_Father"])
                if parent.type.name != "Transform" or current.path_id not in [
                        resolve(child).path_id for child in parent.read_typetree()["m_Children"]]:
                    raise ValueError(f"Broken attachment parent link: {name}")
                current = parent
            chain.reverse()
            return {"componentId": str(component.path_id),
                    "transformId": str(owner_transform(component).path_id),
                    "cutTransformId": str(note.path_id), "relativeTransforms": chain}

        colliders = []
        if controller is None:
            raise ValueError(f"Missing note controller: {name}")
        trailing_ids = set()
        if len(trailing) != (1 if name == "ProModeGameNote" else 0):
            raise ValueError(f"Trailing collider controller count changed: {name}")
        for data in trailing:
            main_cuttable = resolve(data["_mainSmallCuttableBySaber"]).path_id
            trailing_ids = {resolve(pointer).path_id for pointer in data["_trailingSmallCuttableBySaberList"]}
            small_ids = {resolve(pointer).path_id for pointer in controller["_smallCuttableBySaberList"]}
            if len(trailing_ids) != 4 or main_cuttable in trailing_ids or small_ids != trailing_ids | {main_cuttable} or data["_noteMovement"] != controller["_noteMovement"]:
                raise ValueError(f"Trailing collider wiring changed: {name}")
        for field in ("_cuttableBySaber", "_bigCuttableBySaberList", "_smallCuttableBySaberList"):
            if field not in controller:
                continue
            pointers = controller[field] if isinstance(controller[field], list) else [controller[field]]
            for pointer in pointers:
                cuttable = resolve(pointer)
                cuttable_data = cuttable.read_typetree()
                collider = resolve(cuttable_data["_collider"])
                data = collider.read_typetree()
                if cuttable_data["m_GameObject"] != data["m_GameObject"]:
                    raise ValueError(f"Cuttable/collider owner mismatch: {name}")
                record = attachment(collider)
                record.update({"controllerField": field, "cuttableId": str(cuttable.path_id),
                               "shape": collider.type.name, "center": data["m_Center"],
                               "canMoveDuringJump": cuttable.path_id in trailing_ids})
                owner = resolve(data["m_GameObject"]).read_typetree()
                size_controllers = []
                for item in owner["m_Component"]:
                    component = resolve(item["component"])
                    if component.type.name != "MonoBehaviour":
                        continue
                    fields = component.read_typetree()
                    if "_boxCollider" in fields and "_noteController" in fields:
                        if resolve(fields["_boxCollider"]).path_id != collider.path_id or resolve(fields["_noteController"]).path_id != controller_id:
                            raise ValueError(f"Collider size controller wiring changed: {name}")
                        size_controllers.append(component.path_id)
                if len(size_controllers) > 1:
                    raise ValueError(f"Duplicate collider size controller: {name}")
                record["canResizeForDot"] = bool(size_controllers)
                if not finite_vector(data["m_Center"], "xyz"):
                    raise ValueError(f"Non-finite collider center: {name}")
                if collider.type.name == "BoxCollider":
                    if not finite_vector(data["m_Size"], "xyz") or any(data["m_Size"][axis] <= 0 for axis in "xyz"):
                        raise ValueError(f"Invalid box size: {name}")
                    record["size"] = data["m_Size"]
                elif collider.type.name == "SphereCollider":
                    if not math.isfinite(data["m_Radius"]) or data["m_Radius"] <= 0:
                        raise ValueError(f"Invalid sphere radius: {name}")
                    record["radius"] = data["m_Radius"]
                else:
                    raise ValueError(f"Unsupported cut collider: {name}")
                colliders.append(record)
        if len(colliders) != (1 if name == "BombNote" else 6 if name == "ProModeGameNote" else 2):
            raise ValueError(f"Cut collider count changed: {name}")
        if len({item["componentId"] for item in colliders}) != len(colliders):
            raise ValueError(f"Duplicate cut collider: {name}")
        if sum(item["canResizeForDot"] for item in colliders) != (1 if name == "NormalGameNote" else 0):
            raise ValueError(f"Adaptive collider count changed: {name}")
        renderers = []
        if len(visuals) != (0 if name == "BombNote" else 1):
            raise ValueError(f"Color visual component count changed: {name}")
        for visual in visuals:
            if resolve(visual["_noteController"]).path_id != controller_id:
                raise ValueError(f"Direction visual controller mismatch: {name}")
            for field, kind in (("_arrowMeshRenderers", "arrow"), ("_circleMeshRenderers", "circle")):
                for pointer in visual[field]:
                    renderer = resolve(pointer)
                    if renderer.type.name != "MeshRenderer":
                        raise ValueError(f"Expected visual MeshRenderer: {name}")
                    record = attachment(renderer)
                    record["kind"] = kind
                    renderers.append(record)
            if len(visual["_circleMeshRenderers"]) != 1 or len(visual["_arrowMeshRenderers"]) != (0 if name == "BurstSliderNote" else 2):
                raise ValueError(f"Direction visual count changed: {name}")
        if len({item["componentId"] for item in renderers}) != len(renderers):
            raise ValueError(f"Duplicate direction renderer: {name}")
        results.append({"prefab": name, "root": transform_record(root_transform, root_data),
                        "cutAndRotatedChild": transform_record(note, note_data),
                        "movementLinkCount": len(rotated_links),
                        "cutTransformIsDirectChild": True,
                        "cutColliders": colliders, "visualRenderers": renderers})
    with bundle.open("rb") as stream:
        bundle_hash = hashlib.file_digest(stream, "sha256").hexdigest()
    return {"schemaVersion": 2, "readerVersion": importlib.metadata.version("UnityPy"),
            "bundleSha256": bundle_hash,
            "prefabCount": len(results), "prefabs": results,
            "cutColliderCount": sum(len(item["cutColliders"]) for item in results),
            "visualRendererCount": sum(len(item["visualRenderers"]) for item in results),
            "scope": "Serialized links only; no runtime transform or cut execution"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.bundle)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {report['prefabCount']} note prefab hierarchies; "
          f"{report['cutColliderCount']} cut colliders, "
          f"{report['visualRendererCount']} direction renderers.")


if __name__ == "__main__":
    main()
