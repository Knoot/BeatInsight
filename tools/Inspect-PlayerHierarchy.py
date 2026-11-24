"""Read serialized player-related transform links without starting Unity.

Install tools/prefab-inspection-requirements.txt into an ignored local target
and expose it through PYTHONPATH. Write machine reports under .local/.
This is asset evidence only; it never authorizes a menu capability.
"""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import UnityPy


def inspect(bundle, scripts_bundle=None, game_data=None):
    environment = UnityPy.load(str(bundle), *([str(scripts_bundle)] if scripts_bundle else []))
    matches, issues = [], []
    script_cache = {}

    def script_info(component):
        key = (component.assets_file.name, component.path_id)
        if key not in script_cache:
            try:
                script = component.read().m_Script.deref().read_typetree()
                script_cache[key] = {"class": script["m_ClassName"], "namespace": script["m_Namespace"],
                                     "assembly": script["m_AssemblyName"], "serializedExecutionOrder": script["m_ExecutionOrder"]}
            except Exception as error:
                script_cache[key] = None
                if scripts_bundle:
                    issues.append({"file": component.assets_file.name, "id": str(component.path_id), "errorType": type(error).__name__})
        return script_cache[key]

    for obj in environment.objects:
        if obj.type.name != "MonoBehaviour":
            continue
        try:
            data = obj.read_typetree()
        except Exception as error:
            issues.append({"file": obj.assets_file.name, "id": str(obj.path_id), "errorType": type(error).__name__})
            continue
        if not any(key in data for key in ("_originTransform", "_leftHandVRController", "_sceneType")):
            continue
        script = script_info(obj)
        rendering = script is not None and script["class"] == "VRRenderingParamsSetup"
        if "_originTransform" not in data and "_leftHandVRController" not in data and not rendering:
            continue

        def chain(pointer):
            result, seen = [], set()
            objects = obj.assets_file.objects
            while pointer.get("m_PathID"):
                key = pointer["m_PathID"]
                if pointer["m_FileID"] != 0:
                    issues.append({"id": str(obj.path_id), "errorType": "ExternalTransformLink"})
                    return result + [{"external": pointer}]
                if key in seen:
                    raise ValueError("Transform hierarchy contains a cycle")
                seen.add(key)
                transform = objects[key].read_typetree()
                game_object = objects[transform["m_GameObject"]["m_PathID"]].read_typetree()
                result.append({"id": str(key), "name": game_object["m_Name"],
                               "activeSelf": bool(game_object["m_IsActive"]),
                               "position": transform["m_LocalPosition"],
                               "rotation": transform["m_LocalRotation"],
                               "scale": transform["m_LocalScale"],
                               "components": [{"id": str(ref["component"]["m_PathID"]),
                                               "script": script_info(objects[ref["component"]["m_PathID"]]),
                                               "enabled": objects[ref["component"]["m_PathID"]].read_typetree().get("m_Enabled")}
                                              for ref in game_object["m_Component"]
                                              if ref["component"]["m_FileID"] == 0 and objects[ref["component"]["m_PathID"]].type.name == "MonoBehaviour"]})
                pointer = transform["m_Father"]
            return result

        def component_chain(pointer):
            if pointer["m_FileID"] != 0 or not pointer["m_PathID"]:
                issues.append({"id": str(obj.path_id), "errorType": "InvalidControllerReference"})
                return []
            objects = obj.assets_file.objects
            component = objects[pointer["m_PathID"]].read_typetree()
            game_object = objects[component["m_GameObject"]["m_PathID"]].read_typetree()
            transforms = [ref["component"] for ref in game_object["m_Component"]
                          if ref["component"]["m_FileID"] == 0
                          and objects[ref["component"]["m_PathID"]].type.name in ("Transform", "RectTransform")]
            if len(transforms) != 1:
                raise ValueError("Controller owner must have one transform")
            return chain(transforms[0])

        matches.append({"file": obj.assets_file.name, "id": str(obj.path_id),
                        "scriptReference": data.get("m_Script"), "script": script,
                        "kind": "rendering-setup" if rendering else "player-links",
                        "sceneType": data.get("_sceneType") if rendering else None,
                        "ownerHierarchy": component_chain({"m_FileID": 0, "m_PathID": obj.path_id}),
                        "links": {key: (component_chain(value) if key in ("_leftHandVRController", "_rightHandVRController") else chain(value)) for key, value in data.items()
                                  if key in ("_originTransform", "_headTransform", "_originParentTransform", "_leftHandVRController", "_rightHandVRController")
                                  and isinstance(value, dict)}})
    startup_scripts, startup_files = [], []
    if game_data:
        paths = [game_data / "globalgamemanagers", game_data / "globalgamemanagers.assets"]
        global_environment = UnityPy.load(*(str(path) for path in paths))
        startup_files = [{"name": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in paths]
        for obj in global_environment.objects:
            if obj.type.name != "MonoScript":
                continue
            data = obj.read_typetree()
            if data["m_ClassName"] in ("MonoKernel", "SceneKernel", "ProjectKernel", "DefaultGameObjectKernel", "SceneContext", "PlayerTransforms", "VRCenterAdjust", "VRRenderingParamsSetup"):
                startup_scripts.append({"class": data["m_ClassName"], "namespace": data["m_Namespace"],
                                        "assembly": data["m_AssemblyName"], "serializedExecutionOrder": data["m_ExecutionOrder"]})
    return {"schemaVersion": 5, "readerVersion": importlib.metadata.version("UnityPy"),
            "bundle": bundle.name, "sha256": hashlib.sha256(bundle.read_bytes()).hexdigest(),
            "scriptsBundle": scripts_bundle.name if scripts_bundle else None,
            "scriptsSha256": hashlib.sha256(scripts_bundle.read_bytes()).hexdigest() if scripts_bundle else None,
            "startupScripts": startup_scripts, "startupFiles": startup_files,
            "matches": matches, "issues": issues, "gameStarted": False,
            "scope": "serialized origin/head/hand links and rendering settings setup; script resolution when scripts bundle supplied; serialized execution order is not runtime initialization order; scene poses and hierarchy only, no room settings or runtime activation order"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--bundle", type=Path, required=True)
    parser.add_argument("--game-data", type=Path)
    parser.add_argument("--scripts-bundle", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    report = inspect(args.bundle, args.scripts_bundle, args.game_data)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"matches": len(report["matches"]), "issues": len(report["issues"]), "sha256": report["sha256"]}))
    raise SystemExit(2 if report["issues"] or not report["matches"] else 0)
