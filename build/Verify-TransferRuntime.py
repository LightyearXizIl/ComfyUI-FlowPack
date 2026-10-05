"""Read-only checks and one real API execution in a Desktop-launched isolated Core."""
import argparse
import copy
import hashlib
import json
import struct
import time
import urllib.request
import uuid
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("scope")
parser.add_argument("instance", choices=["source", "native", "adopted"])
parser.add_argument("--port", type=int, default=8188)
args = parser.parse_args()
scope = json.loads(Path(args.scope).read_text(encoding="utf-8-sig"))
root = Path(scope["FixtureRoot"]).resolve()
data = Path(scope[{"source": "SourceData", "native": "NativeData", "adopted": "AdoptedData"}[args.instance]]).resolve()
assert root in data.parents and root in Path(args.scope).resolve().parents
workflow = data / "user/default/workflows/upscale.json"
assert workflow.is_file()
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest().upper()
assert sha(workflow) == sha(Path(scope["WorkflowPath"]))
assert sha(data / "models/upscale_models/RealESRGAN_x2plus.pth") == scope["ModelSha256"]
base = f"http://127.0.0.1:{args.port}"

def request(path, payload=None):
    body = None if payload is None else json.dumps(payload).encode()
    req = urllib.request.Request(base + path, data=body, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=10) as response:
        return json.load(response)

objects = request("/object_info")
for node in ["UpscaleModelLoader", "ImageUpscaleWithModel", "FlowPackAcceptanceFixture", "FlowPackAcceptancePass", "SaveImage"]:
    assert node in objects, node
model_input = objects["UpscaleModelLoader"]["input"]["required"]["model_name"]
model_options = model_input[1]["options"] if model_input[0] == "COMBO" else model_input[0]
assert "RealESRGAN_x2plus.pth" in model_options
queue = request("/queue")
assert not queue["queue_running"] and not queue["queue_pending"], "Existing work must finish first"
run = uuid.uuid4().hex
evidence = root / "desktop-runtime" / f"{args.instance}-{run}"
evidence.mkdir(parents=True)
graph = json.loads(workflow.read_text(encoding="utf-8-sig"))
graph = copy.deepcopy(graph)
graph["5"]["inputs"]["filename_prefix"] = f"flowpack_{args.instance}_{run}"
payload = {"prompt": graph, "client_id": run, "extra_data": {"source": "flowpack-isolated-acceptance-api"}}
prompt = request("/prompt", payload)
assert not prompt.get("node_errors"), prompt
prompt_id = prompt["prompt_id"]
deadline = time.monotonic() + 180
while True:
    history = request("/history/" + prompt_id)
    if prompt_id in history:
        entry = history[prompt_id]
        assert entry["status"]["status_str"] == "success", entry["status"]
        assert entry["status"]["completed"]
        break
    assert time.monotonic() < deadline, "Real output timed out"
    time.sleep(0.5)
cached = [m[1].get("nodes", []) for m in entry["status"]["messages"] if m[0] == "execution_cached"]
assert not any(nodes for nodes in cached), "Use a fresh Desktop process for this verification"
image = entry["outputs"]["5"]["images"][0]
assert image["type"] == "output"
output = data / "output" / image["subfolder"] / image["filename"]
assert output.resolve().is_relative_to(data / "output")
pixels = output.read_bytes()
assert pixels[:8] == b"\x89PNG\r\n\x1a\n"
dimensions = struct.unpack(">II", pixels[16:24])
assert dimensions == (16, 16), dimensions
proof = json.loads((data / "output/dependency-proof.json").read_text())
assert proof["version"] == "4.16.0" and proof["result"] == "12,345"
assert Path(proof["pythonPrefix"]).resolve() == (data / ".venv").resolve()
assert Path(proof["packageFile"]).resolve().is_relative_to(data / ".venv")
result = {"passed": True, "instance": args.instance, "port": args.port, "promptId": prompt_id,
          "executionSource": "API against official Desktop-launched isolated Core", "guiSubmission": False,
          "noCachedNodes": True, "workflowSha256": sha(workflow), "output": str(output),
          "outputSha256": sha(output), "dimensions": dimensions, "dependencyProof": proof}
for name, value in [("result.json", result), ("history.json", history), ("submission.json", payload),
                    ("object-info-selected.json", {node["class_type"]: objects[node["class_type"]] for node in graph.values()})]:
    (evidence / name).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"evidence": str(evidence), **result}, ensure_ascii=False))
